using PitMedic.Models;

namespace PitMedic.Services;

public sealed class MonitoringCoordinator : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly HardwareMonitorService _hardware = new();
    private readonly TelemetryBuffer _buffer = new(3600);
    private readonly IncidentRecorder _incidents = new();
    private readonly RepairService _repairs;
    private readonly GameWatchService _games;
    private readonly CompanionSoftwareWatchService _companions;
    private readonly SettingsService _settings;
    private readonly UsageStatsService _usage = new();
    private readonly SimulatorDistanceTelemetryService _distanceTelemetry = new();
    private readonly SimulatorLapTelemetryService _lapTelemetry = new();
    private DateTimeOffset _lastUsageFlush = DateTimeOffset.UtcNow;
    private Task? _loop;

    public event Action<TelemetrySample>? TelemetryUpdated;
    public event Action<GameKind, bool>? GameStatusChanged;
    public event Action<DistanceTelemetryStatus>? DistanceTelemetryStatusChanged;
    public event Action<CompanionSoftwareStatus>? CompanionSoftwareStatusChanged;
    public event Action<LiveFaultEvidence>? LiveFaultDetected;
    public event Action<IncidentSummary>? IncidentCreated;
    public event Action<RepairStatus>? RepairStatusChanged;
    public event Action<AppSettings>? SettingsChanged;

    public MonitoringCoordinator(SettingsService settings)
    {
        _settings = settings;
        _repairs = new RepairService(_usage);
        _games = new GameWatchService(_buffer, _incidents, _settings);
        _companions = new CompanionSoftwareWatchService(_buffer, _incidents, _settings);
        _games.GameStatusChanged += (g, running) =>
        {
            if (running) _usage.RecordSessionStarted(g);
            else
            {
                var finalMiles = _distanceTelemetry.Stop(g);
                if (double.IsFinite(finalMiles) && finalMiles > 0)
                    _usage.RecordMiles(g, finalMiles, persist: false);
                if (_lapTelemetry.Stop(g) is { } finalLap)
                    _usage.RecordBestLap(finalLap, persist: false);
                _usage.Flush();
            }
            GameStatusChanged?.Invoke(g, running);
        };
        _games.SessionCompleted += (g, ended, clean) => _usage.RecordSessionEnded(g, ended, clean);
        _distanceTelemetry.StatusChanged += status => DistanceTelemetryStatusChanged?.Invoke(status);
        _games.LiveFaultDetected += fault => LiveFaultDetected?.Invoke(fault);
        _companions.StatusChanged += status => CompanionSoftwareStatusChanged?.Invoke(status);
        _incidents.IncidentCreated += i =>
        {
            var game = GameDefinition.Supported.FirstOrDefault(g =>
                g.DisplayName.Equals(i.Game, StringComparison.OrdinalIgnoreCase));
            if (game is not null) _usage.RecordFinding(game.Kind);
            IncidentCreated?.Invoke(i);
        };
        _repairs.StatusChanged += s => RepairStatusChanged?.Invoke(s);
        _settings.SettingsChanged += s => SettingsChanged?.Invoke(s);
    }

    public SettingsService Settings => _settings;
    public RepairStatus? CurrentRepair => _repairs.Current;

    public void Start()
    {
        if (_loop is not null) return;
        _loop = Task.Run(RunAsync);
    }

    private async Task RunAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var settings = _settings.Current;
                var sample = _hardware.Read(settings);
                _buffer.Add(sample);
                TelemetryUpdated?.Invoke(sample);
                _games.Scan();
                _companions.Scan();
                foreach (var (game, miles) in _distanceTelemetry.Poll(_games.IsRunning))
                    _usage.RecordMiles(game, miles, persist: false);
                foreach (var lap in _lapTelemetry.Poll(_games.IsRunning))
                    _usage.RecordBestLap(lap, persist: false);
                if (DateTimeOffset.UtcNow - _lastUsageFlush >= TimeSpan.FromMinutes(1))
                {
                    _usage.Flush();
                    _lastUsageFlush = DateTimeOffset.UtcNow;
                }
                await Task.Delay(TimeSpan.FromSeconds(settings.SamplingSeconds), _cts.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AppLog.Write($"Monitoring loop stopped unexpectedly: {ex}");
        }
    }

    public bool IsGameRunning(GameKind kind) => _games.IsRunning(kind);
    public IReadOnlyList<CompanionSoftwareStatus> CompanionSoftwareStatuses() => _companions.StatusSnapshot();
    public IReadOnlyList<IncidentSummary> RecentIncidents() => _incidents.LoadRecent();
    public IReadOnlyList<IncidentSummary> IncidentHistory() => _incidents.LoadHistory();
    public IncidentRecord? GetIncident(string folder) => _incidents.LoadRecord(folder);
    public IncidentDetailsData? GetIncidentDetails(string folder)
    {
        var incident = _incidents.LoadRecord(folder);
        return incident is null ? null : IncidentDetailsService.Build(incident);
    }
    public bool AcknowledgeIncident(string folder) => _incidents.Acknowledge(folder);

    public void CancelRepair() => _repairs.Cancel();

    public bool BeginRepair(string incidentFolder, bool automatic = false)
    {
        var incident = _incidents.LoadRecord(incidentFolder);
        if (incident is null) return false;
        var plan = incident.RecommendedRepair ?? RepairPlanner.TryCreateFromIncident(incident);
        if (plan is null) return false;
        return _repairs.Begin(incident, plan, _settings.Current, automatic);
    }

    public CapabilitiesSnapshot CapabilitiesStats() => _usage.Snapshot();
    public SimulatorActivitySnapshot SimulatorActivity(GameKind game) => _usage.SimulatorSnapshot(game);

    public Task<IncidentSummary> CaptureSnapshotAsync()
    {
        var settings = _settings.Current;
        return _incidents.CaptureSnapshotAsync(_buffer.Snapshot(settings.BufferMinutes));
    }

    public string WriteSensorReport() => _hardware.WriteSensorReport();

    public void Dispose()
    {
        _cts.Cancel();
        try { _loop?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _distanceTelemetry.Dispose();
        _lapTelemetry.Dispose();
        _usage.StopMonitoring();
        _repairs.Dispose();
        _companions.Dispose();
        _games.Dispose();
        _hardware.Dispose();
        _cts.Dispose();
    }
}
