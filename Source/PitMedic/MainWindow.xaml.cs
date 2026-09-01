using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PitMedic.Models;
using PitMedic.Services;

namespace PitMedic;

public partial class MainWindow : Window
{
    private static readonly TimeSpan LatestFindingWindow = TimeSpan.FromHours(48);
    private readonly MonitoringCoordinator _monitoring;
    private readonly AnonymousUsageService _anonymousUsage;
    private readonly UpdateService _updates;
    private readonly LapBenchmarkService _lapBenchmarks = new();
    private readonly ObservableCollection<IncidentSummary> _incidents = new();
    private readonly Queue<TelemetrySample> _chart = new();
    private readonly Dictionary<GameKind, bool> _gameRunning = Enum.GetValues<GameKind>().ToDictionary(game => game, _ => false);
    private readonly HashSet<GameKind> _liveFaultGames = new();
    private readonly Dictionary<GameKind, RadioButton> _navButtons = new();
    private readonly Dictionary<GameKind, TextBlock> _navStatuses = new();
    private readonly Dictionary<GameKind, System.Windows.Shapes.Ellipse> _navDots = new();
    private readonly Dictionary<GameKind, DistanceTelemetryStatus> _distanceTelemetryStatuses = new();
    private bool _allowClose;
    private AppSettings _settings;
    private RepairProgressWindow? _repairProgressWindow;
    private bool _repairWasActive;
    private bool _repairBannerDismissed;
    private GameKind _selectedGame = GameKind.LeMansUltimate;
    private IncidentSummary? _selectedPrimaryIncident;
    private IncidentSummary? _homeLatestIncident;
    private AvailableUpdate? _availableUpdate;
    private TelemetrySample? _latestTelemetry;
    private CancellationTokenSource? _benchmarkLookupCancellation;
    private string? _displayedLapCombination;
    private string? _benchmarkSourceUrl;

    public MainWindow(MonitoringCoordinator monitoring, AnonymousUsageService anonymousUsage, UpdateService updates)
    {
        InitializeComponent();
        _monitoring = monitoring;
        _anonymousUsage = anonymousUsage;
        _updates = updates;
        _settings = monitoring.Settings.Current;
        ConfigureSimulatorNavigation();
        RefreshIncidents();

        _monitoring.TelemetryUpdated += sample => Dispatcher.BeginInvoke(() => UpdateTelemetry(sample));
        _monitoring.GameStatusChanged += (game, running) => Dispatcher.BeginInvoke(() => UpdateGame(game, running));
        _monitoring.DistanceTelemetryStatusChanged += status => Dispatcher.BeginInvoke(() => UpdateDistanceTelemetryStatus(status));
        _monitoring.CompanionSoftwareStatusChanged += _ => Dispatcher.BeginInvoke(() => RefreshHomePage());
        _monitoring.LiveFaultDetected += fault => Dispatcher.BeginInvoke(() => UpdateLiveFault(fault));
        _monitoring.IncidentCreated += incident => Dispatcher.BeginInvoke(() => AddIncident(incident));
        _monitoring.RepairStatusChanged += status => Dispatcher.BeginInvoke(() => UpdateRepair(status));
        _monitoring.SettingsChanged += settings => Dispatcher.BeginInvoke(() => ApplySettings(settings));
        _updates.UpdateAvailable += update => Dispatcher.BeginInvoke(() => ShowAvailableUpdate(update));

        if (_monitoring.CurrentRepair is RepairStatus current) UpdateRepair(current);

        foreach (var game in GameDefinition.Supported)
            UpdateGame(game.Kind, _monitoring.IsGameRunning(game.Kind));

        Closing += (_, e) =>
        {
            if (_allowClose) return;
            e.Cancel = true;
            Hide();
        };
        StateChanged += (_, _) =>
        {
            UpdateMaximizeGlyph();
            if (!_allowClose && WindowState == WindowState.Minimized) Hide();
        };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible && _latestTelemetry is not null)
                UpdateTelemetry(_latestTelemetry, record: false);
        };
        Closed += (_, _) =>
        {
            _benchmarkLookupCancellation?.Cancel();
            _benchmarkLookupCancellation?.Dispose();
            _lapBenchmarks.Dispose();
        };
    }

    public void AllowCloseAndClose()
    {
        _allowClose = true;
        Close();
    }

    private void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        RecorderStatus.Text = $"Monitoring · {settings.SamplingSeconds}s";
        RefreshSimulatorViews();
        if (_latestTelemetry is not null && IsVisible)
            UpdateTelemetry(_latestTelemetry, record: false);
        DrawChart();
    }

    private void UpdateTelemetry(TelemetrySample s, bool record = true)
    {
        _latestTelemetry = s;
        if (record)
        {
            _chart.Enqueue(s);
            var thermalCutoff = s.Timestamp.AddMinutes(-60);
            while (_chart.Count > 0 && _chart.Peek().Timestamp < thermalCutoff) _chart.Dequeue();
        }

        // Keep capturing evidence while hidden, but do not continuously rebuild WPF controls
        // and chart geometry that the user cannot see.
        if (!IsVisible) return;

        CpuTemp.Text = Temp(s.CpuTempC);
        CpuSub.Text = s.CpuTempC.HasValue
            ? SubLine(s.CpuLoadPct, s.CpuClockMhz)
            : "—";

        GpuTemp.Text = Temp(s.GpuTempC);
        GpuSub.Text = SubLine(s.GpuLoadPct, s.GpuClockMhz);
        GpuPowerValue.Text = Power(s.GpuPowerW);
        GpuPowerSub.Text = s.GpuFanRpm.HasValue ? $"Fan {s.GpuFanRpm.Value:0} RPM" : "—";

        GraphCpuValue.Text = Temp(s.CpuTempC);
        GraphGpuValue.Text = Temp(s.GpuTempC);
        GraphPowerValue.Text = Power(s.GpuPowerW);

        SetBar(CpuLoadBar, CpuLoadLabel, s.CpuLoadPct);
        SetBar(GpuLoadBar, GpuLoadLabel, s.GpuLoadPct);
        SetBar(RamLoadBar, RamLoadLabel, s.MemoryLoadPct);

        float? vramPct = null;
        if (s.GpuMemoryUsedMb.HasValue && s.GpuMemoryTotalMb.HasValue && s.GpuMemoryTotalMb.Value > 0)
            vramPct = s.GpuMemoryUsedMb.Value / s.GpuMemoryTotalMb.Value * 100f;
        SetBar(VramLoadBar, VramLoadLabel, vramPct);
        var vramDetail = s.GpuMemoryUsedMb.HasValue
            ? (s.GpuMemoryTotalMb.HasValue
                ? $"{s.GpuMemoryUsedMb.Value / 1024f:0.0} / {s.GpuMemoryTotalMb.Value / 1024f:0.0} GB used"
                : $"{s.GpuMemoryUsedMb.Value / 1024f:0.0} GB used · total unavailable")
            : "VRAM usage unavailable";
        VramLoadBar.ToolTip = vramDetail;
        VramLoadLabel.ToolTip = vramDetail;

        HomeCpuTemp.Text = Temp(s.CpuTempC);
        HomeCpuDetail.Text = s.CpuLoadPct.HasValue ? $"{s.CpuLoadPct.Value:0}% load" : "Sensor active";
        HomeGpuTemp.Text = Temp(s.GpuTempC);
        HomeGpuDetail.Text = s.GpuLoadPct.HasValue ? $"{s.GpuLoadPct.Value:0}% load" : "Sensor active";
        HomeMemoryValue.Text = s.MemoryLoadPct.HasValue ? $"{s.MemoryLoadPct.Value:0}%" : "--%";
        HomeGpuPower.Text = Power(s.GpuPowerW);
        RefreshSelectedActivity();

        DrawChart();
    }

    private string SubLine(float? load, float? clock)
    {
        if (load.HasValue && clock.HasValue) return $"{load:0}% · {clock:0} MHz";
        if (load.HasValue) return $"{load:0}% load";
        if (clock.HasValue) return $"{clock:0} MHz";
        return "—";
    }

    private void ConfigureSimulatorNavigation()
    {
        _navButtons[GameKind.LeMansUltimate] = LmuNav;
        _navButtons[GameKind.IRacing] = IRacingNav;
        _navButtons[GameKind.AssettoCorsaEvo] = AceNav;
        _navButtons[GameKind.RaceRoom] = RaceRoomNav;
        _navButtons[GameKind.AssettoCorsaCompetizione] = AccNav;
        _navButtons[GameKind.Automobilista2] = Ams2Nav;

        _navStatuses[GameKind.LeMansUltimate] = LmuStatus;
        _navStatuses[GameKind.IRacing] = IRacingStatus;
        _navStatuses[GameKind.AssettoCorsaEvo] = AceStatus;
        _navStatuses[GameKind.RaceRoom] = RaceRoomStatus;
        _navStatuses[GameKind.AssettoCorsaCompetizione] = AccStatus;
        _navStatuses[GameKind.Automobilista2] = Ams2Status;

        _navDots[GameKind.LeMansUltimate] = LmuDot;
        _navDots[GameKind.IRacing] = IRacingDot;
        _navDots[GameKind.AssettoCorsaEvo] = AceDot;
        _navDots[GameKind.RaceRoom] = RaceRoomDot;
        _navDots[GameKind.AssettoCorsaCompetizione] = AccDot;
        _navDots[GameKind.Automobilista2] = Ams2Dot;

        foreach (var button in _navButtons.Values)
            button.Checked += SimulatorNav_Checked;

        HomeNav.Checked += HomeNav_Checked;
        HomeOsText.Text = WindowsVersionInfo.GetDisplayName();
        HomeArchitectureText.Text = $"{RuntimeInformation.OSArchitecture} · {(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")}";
        HomeProcessorText.Text = $"{Environment.ProcessorCount} logical processors";
        HomeRuntimeText.Text = $"PitMedic {AppInfo.Version} · .NET {Environment.Version.Major}";
        AboutVersionText.Text = $"PitMedic {AppInfo.Version}";
        HomeNav.IsChecked = true;
        RefreshHomePage();
    }

    private void HomeNav_Checked(object sender, RoutedEventArgs e)
    {
        if (HomePage is null || SimulatorPage is null || AboutPage is null) return;
        HomePage.Visibility = Visibility.Visible;
        SimulatorPage.Visibility = Visibility.Collapsed;
        AboutPage.Visibility = Visibility.Collapsed;
        RefreshHomePage();
    }

    private void SimulatorNav_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton button
            || button.Tag is not string tag
            || !Enum.TryParse(tag, out GameKind game))
            return;

        _selectedGame = game;
        HomePage.Visibility = Visibility.Collapsed;
        SimulatorPage.Visibility = Visibility.Visible;
        AboutPage.Visibility = Visibility.Collapsed;
        RefreshSelectedSimulatorPage();
    }

    private void UpdateGame(GameKind game, bool running)
    {
        _gameRunning[game] = running;
        if (running) _liveFaultGames.Remove(game);
        RefreshNavItem(game);
        if (game == _selectedGame) RefreshSelectedSimulatorPage();
        RefreshHomePage();
    }

    private void UpdateLiveFault(LiveFaultEvidence fault)
    {
        var game = fault.Game ?? GameKind.IRacing;
        _liveFaultGames.Add(game);
        RefreshNavItem(game);
        if (game == _selectedGame) RefreshSelectedSimulatorPage();
        RefreshHomePage();
        var display = UiGameName(game);
        RecorderStatus.Text = $"{display} · issue detected";
    }

    private void AddIncident(IncidentSummary incident)
    {
        _incidents.Insert(0, incident);
        while (_incidents.Count > 500) _incidents.RemoveAt(_incidents.Count - 1);
        if (TryGetGame(incident.Game, out var game))
            _liveFaultGames.Remove(game);
        RefreshSimulatorViews();
        RecorderStatus.Text = $"Captured · {incident.Timestamp:h:mm tt}";

        if (!incident.RepairAvailable) return;
        _ = Dispatcher.BeginInvoke(async () =>
        {
            await Task.Delay(450);
            var currentSettings = _monitoring.Settings.Current;
            if (incident.EstimatedMinutes <= 2
                && !incident.RequiresRepairApproval
                && currentSettings.AutoRunSafeRepairs
                && !currentSettings.AlwaysAskBeforeRepair)
            {
                _monitoring.BeginRepair(incident.Folder, automatic: true);
            }
            else
            {
                ShowRepairPrompt(incident);
            }
        });
    }

    private void UpdateRepair(RepairStatus status)
    {
        if (status.IsActive)
        {
            _repairBannerDismissed = false;
            RepairBanner.Visibility = _repairBannerDismissed ? Visibility.Collapsed : Visibility.Visible;
            if (!_repairWasActive)
            {
                // A repair should become the foreground PitMedic experience. Steam is intentionally
                // suppressed separately, so never hide/minimize PitMedic when validation starts.
                Show();
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                Activate();
            }
            EnsureRepairProgressWindow();
            _repairWasActive = true;
        }
        else
        {
            RepairBanner.Visibility = _repairBannerDismissed ? Visibility.Collapsed : Visibility.Visible;
            _repairWasActive = false;
        }

        RepairBannerTitle.Text = status.IsComplete
            ? (status.Success ? "Repair completed" : "Repair needs attention")
            : status.Title;
        RepairBannerMessage.Text = status.Message;
        RepairProgressBar.Value = status.Percent;
        RepairProgressText.Text = $"{status.Percent}%";

        if (status.IsComplete && !status.Success)
        {
            RepairBanner.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "WarnSoftBrush");
            RepairBanner.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "WarnBorderBrush");
            RepairBannerIconBorder.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "WarnSoftBrush");
            RepairBannerIcon.Text = "!";
            RepairBannerIcon.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "WarnTextBrush");
            RepairProgressBar.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "WarnBrush");
        }
        else
        {
            RepairBanner.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "GoodSoftBrush");
            RepairBanner.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "GoodBorderBrush");
            RepairBannerIconBorder.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "GoodSoftBrush");
            RepairBannerIcon.Text = status.IsComplete ? "✓" : "…";
            RepairBannerIcon.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "GoodTextBrush");
            RepairProgressBar.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "GoodBrush");
        }

        RecorderStatus.Text = status.IsComplete
            ? (status.Success ? "Repair complete" : "Repair needs attention")
            : $"Repair · {status.Percent}%";

        if (status.IsComplete)
            RefreshIncidents();
        else
            RefreshHomePage();
    }

    private void EnsureRepairProgressWindow()
    {
        if (_repairProgressWindow is { IsLoaded: true })
            return;

        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        _repairProgressWindow = new RepairProgressWindow(_monitoring) { Owner = this };
        _repairProgressWindow.Closed += (_, _) => _repairProgressWindow = null;
        _repairProgressWindow.Show();
        _repairProgressWindow.Activate();
    }

    private void RefreshIncidents()
    {
        _incidents.Clear();
        foreach (var item in _monitoring.IncidentHistory()) _incidents.Add(item);
        RefreshSimulatorViews();
    }

    private void RefreshSimulatorViews()
    {
        AllFindingsCountText.Text = _incidents.Count(i => !i.IsDismissed).ToString();
        foreach (var game in GameDefinition.Supported)
            RefreshNavItem(game.Kind);
        RefreshSelectedSimulatorPage();
        RefreshHomePage();
    }

    private void RefreshNavItem(GameKind game)
    {
        if (!_navStatuses.TryGetValue(game, out var status) || !_navDots.TryGetValue(game, out var dot)) return;

        var hasIssue = ActiveIncidentFor(game) is not null || _liveFaultGames.Contains(game);
        if (hasIssue)
        {
            status.Text = "Issue detected";
            dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "AccentBrush");
            return;
        }

        if (!IsMonitored(game))
        {
            status.Text = "Monitoring off";
            dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "SidebarMutedBrush");
            return;
        }

        var running = _gameRunning.TryGetValue(game, out var isRunning) && isRunning;
        status.Text = running ? "Running" : "Not running";
        dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, running ? "GoodBrush" : "SidebarMutedBrush");
    }

    private void RefreshHomePage()
    {
        if (HomeStatusText is null) return;

        var supported = GameDefinition.Supported.Select(x => x.Kind).ToArray();
        var monitored = supported.Count(IsMonitored);
        var running = supported.Count(game => _gameRunning.TryGetValue(game, out var value) && value);
        var companions = _monitoring.CompanionSoftwareStatuses().Where(x => x.IsDetected).ToArray();
        var companionRunning = companions.Count(x => x.IsRunning);
        var companionMonitoring = _settings.MonitorCompanionSoftware && companions.Length > 0;
        var activeFindings = _incidents.Count(i => !i.IsDismissed && !i.IsResolved && IsRecentFinding(i));

        HomeMonitoringSummary.Text = $"{monitored} monitored · {running} running";
        SetHomeReadiness(HomeLmuStatus, GameKind.LeMansUltimate);
        SetHomeReadiness(HomeIRacingStatus, GameKind.IRacing);
        SetHomeReadiness(HomeAceStatus, GameKind.AssettoCorsaEvo);
        SetHomeReadiness(HomeRaceRoomStatus, GameKind.RaceRoom);
        SetHomeReadiness(HomeAccStatus, GameKind.AssettoCorsaCompetizione);
        SetHomeReadiness(HomeAms2Status, GameKind.Automobilista2);
        RefreshHomeCompanionSoftware(companions);

        if (_monitoring.CurrentRepair?.IsActive == true)
        {
            SetHomeStatus("REPAIR RUNNING", "InfoSoftBrush", "InfoBorderBrush", "TelemetryBrush", "InfoTextBrush");
        }
        else if (activeFindings > 0)
        {
            SetHomeStatus(activeFindings == 1 ? "1 FINDING NEEDS REVIEW" : $"{activeFindings} FINDINGS NEED REVIEW",
                "WarnSoftBrush", "WarnBorderBrush", "AccentBrush", "WarnTextBrush");
        }
        else if (monitored == 0 && !companionMonitoring)
        {
            SetHomeStatus("MONITORING OFF", "Panel2Brush", "BorderBrush", "MutedBrush", "MutedBrush");
        }
        else if (running == 0 && companionRunning == 0)
        {
            SetHomeStatus("WAITING FOR SIMULATOR", "Panel2Brush", "BorderBrush", "MutedBrush", "MutedBrush");
        }
        else
        {
            SetHomeStatus("MONITORING ACTIVE", "GoodSoftBrush", "GoodBorderBrush", "GoodBrush", "GoodTextBrush");
        }

        _homeLatestIncident = _incidents.Where(x => !x.IsDismissed && IsRecentFinding(x)).OrderByDescending(x => x.Timestamp).FirstOrDefault();
        if (_homeLatestIncident is null)
        {
            HomeLatestFindingTime.Text = string.Empty;
            HomeLatestFindingTitle.Text = "No recent findings";
            HomeLatestFindingSummary.Text = "Nothing needing attention was captured in the past 48 hours.";
            HomeLatestFindingIcon.Text = "✓";
            HomeLatestFindingIconBorder.SetResourceReference(Border.BackgroundProperty, "GoodSoftBrush");
            HomeLatestFindingIcon.SetResourceReference(TextBlock.ForegroundProperty, "GoodTextBrush");
            HomeReviewFindingButton.Visibility = Visibility.Collapsed;
            return;
        }

        var latest = _homeLatestIncident;
        HomeLatestFindingTime.Text = latest.Timestamp.ToString("MMM d · h:mm tt");
        HomeLatestFindingTitle.Text = $"{latest.Game} · {latest.Category}";
        HomeLatestFindingSummary.Text = latest.IsResolved
            ? (string.IsNullOrWhiteSpace(latest.ResolutionText) ? "The repair record and evidence are available for review." : latest.ResolutionText)
            : string.IsNullOrWhiteSpace(latest.Summary) ? "PitMedic preserved this simulator or companion-software event for review." : latest.Summary;
        HomeLatestFindingIcon.Text = latest.IsResolved ? "✓" : "!";
        HomeLatestFindingIconBorder.SetResourceReference(Border.BackgroundProperty, latest.IsResolved ? "GoodSoftBrush" : "WarnSoftBrush");
        HomeLatestFindingIcon.SetResourceReference(TextBlock.ForegroundProperty, latest.IsResolved ? "GoodTextBrush" : "WarnTextBrush");
        HomeReviewFindingButton.Visibility = Visibility.Visible;
    }

    private void RefreshHomeCompanionSoftware(IReadOnlyList<CompanionSoftwareStatus> companions)
    {
        if (HomeCompanionSoftwareCard is null || HomeCompanionSoftwareList is null) return;

        HomeCompanionSoftwareCard.Visibility = companions.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (companions.Count == 0)
        {
            HomeCompanionSoftwareList.ItemsSource = null;
            return;
        }

        var items = companions.Select(companion =>
        {
            var hasIssue = _incidents.Any(incident =>
                !incident.IsDismissed
                && !incident.IsResolved
                && IsRecentFinding(incident)
                && incident.Game.Equals(companion.DisplayName, StringComparison.OrdinalIgnoreCase));

            if (hasIssue)
                return new CompanionSoftwareDisplayItem(
                    companion.DisplayName,
                    "PitMedic captured an application fault and preserved the evidence.",
                    "Issue detected",
                    ResourceBrush("WarnTextBrush"));
            if (!_settings.MonitorCompanionSoftware)
                return new CompanionSoftwareDisplayItem(
                    companion.DisplayName,
                    companion.IsRunning ? "Detected running on this PC." : "Detected on this PC.",
                    "Monitoring off",
                    ResourceBrush("MutedBrush"));
            if (companion.IsRunning)
                return new CompanionSoftwareDisplayItem(
                    companion.DisplayName,
                    "Crash monitoring is active.",
                    "Running",
                    ResourceBrush("GoodTextBrush"));
            return new CompanionSoftwareDisplayItem(
                companion.DisplayName,
                "Installed and ready for crash monitoring when launched.",
                "Ready",
                ResourceBrush("MutedBrush"));
        }).ToArray();

        HomeCompanionSoftwareList.ItemsSource = items;
        var running = companions.Count(x => x.IsRunning);
        HomeCompanionSoftwareSummary.Text = $"{companions.Count} detected · {running} running";
    }

    private Brush ResourceBrush(string key)
        => TryFindResource(key) as Brush ?? Brushes.Gray;

    private void SetHomeStatus(string text, string background, string border, string dot, string foreground)
    {
        HomeStatusText.Text = text;
        HomeStatusBorder.SetResourceReference(Border.BackgroundProperty, background);
        HomeStatusBorder.SetResourceReference(Border.BorderBrushProperty, border);
        HomeStatusDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, dot);
        HomeStatusText.SetResourceReference(TextBlock.ForegroundProperty, foreground);
    }

    private void SetHomeReadiness(TextBlock label, GameKind game)
    {
        if (ActiveIncidentFor(game) is not null || _liveFaultGames.Contains(game))
        {
            label.Text = "Issue detected";
            label.SetResourceReference(TextBlock.ForegroundProperty, "WarnTextBrush");
            return;
        }

        if (!IsMonitored(game))
        {
            label.Text = "Monitoring off";
            label.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            return;
        }

        var isRunning = _gameRunning.TryGetValue(game, out var running) && running;
        label.Text = isRunning ? "Running" : "Ready";
        label.SetResourceReference(TextBlock.ForegroundProperty, isRunning ? "GoodTextBrush" : "MutedBrush");
    }

    private void RefreshSelectedSimulatorPage()
    {
        if (SelectedSimulatorTitle is null) return;

        var name = UiGameName(_selectedGame);
        var active = ActiveIncidentFor(_selectedGame);
        var latest = IncidentsFor(_selectedGame).FirstOrDefault();
        _selectedPrimaryIncident = active ?? latest;

        SelectedSimulatorTitle.Text = name;
        SelectedFindingsHeading.Text = $"{FindingsPrefix(_selectedGame)} · LAST 48 HOURS";

        var hasIssue = active is not null || _liveFaultGames.Contains(_selectedGame);
        var running = _gameRunning.TryGetValue(_selectedGame, out var isRunning) && isRunning;
        var monitored = IsMonitored(_selectedGame);
        SelectedFindingEmptyDetail.Text = monitored
            ? $"No findings were captured for {name} in the past 48 hours."
            : $"Enable {name} in Settings to begin monitoring.";
        if (hasIssue)
            SetHeaderStatus("ISSUE DETECTED", "WarnSoftBrush", "WarnBorderBrush", "AccentBrush", "WarnTextBrush");
        else if (running)
            SetHeaderStatus("SESSION LIVE", "GoodSoftBrush", "GoodBorderBrush", "GoodBrush", "GoodTextBrush");
        else if (!monitored)
            SetHeaderStatus("MONITORING OFF", "Panel2Brush", "BorderBrush", "MutedBrush", "MutedBrush");
        else
            SetHeaderStatus("WAITING FOR SIMULATOR", "Panel2Brush", "BorderBrush", "MutedBrush", "MutedBrush");

        RefreshSessionStory(active ?? latest, running, monitored);
        RefreshSelectedActivity();
        RefreshSelectedFinding(active, latest);
        RefreshSelectedFooter(active ?? latest, running, monitored);
    }

    private void RefreshSelectedActivity()
    {
        if (ActivityTimeValue is null) return;
        var activity = _monitoring.SimulatorActivity(_selectedGame);
        var running = _gameRunning.TryGetValue(_selectedGame, out var isRunning) && isRunning;
        ActivityBestLapLabel.Text = running ? "SESSION BEST" : "LAST SESSION BEST";
        ActivityTimeValue.Text = FormatMonitoredTime(activity.TimeMonitored);

        var hasMileage = SimulatorDistanceTelemetryService.SupportsMileage(_selectedGame);
        if (hasMileage)
        {
            var miles = activity.MilesMonitored.GetValueOrDefault();
            ActivityMilesValue.Text = _settings.UseFahrenheit
                ? $"{miles:N1} mi"
                : $"{miles * 1.609344d:N1} km";
        }
        else
        {
            ActivityMilesValue.Text = "Not available";
        }

        _distanceTelemetryStatuses.TryGetValue(_selectedGame, out var distanceStatus);
        var showAms2Guidance = _selectedGame == GameKind.Automobilista2
            && running
            && distanceStatus is { IsAvailable: false };
        ActivityDistanceDetail.Visibility = showAms2Guidance ? Visibility.Visible : Visibility.Collapsed;
        ActivityDistanceDetail.Text = showAms2Guidance ? distanceStatus!.Message : string.Empty;

        RefreshBestLap(activity.BestLap, running);
    }

    private void UpdateDistanceTelemetryStatus(DistanceTelemetryStatus status)
    {
        _distanceTelemetryStatuses[status.Game] = status;
        if (_selectedGame == status.Game) RefreshSelectedActivity();
    }

    private void RefreshBestLap(BestLapRecord? lap, bool running)
    {
        if (ActivityBestLapValue is null) return;
        if (lap is null)
        {
            _displayedLapCombination = null;
            _benchmarkSourceUrl = null;
            _benchmarkLookupCancellation?.Cancel();
            ActivityBestLapValue.Text = "Waiting for a valid lap";
            ActivityBestLapDetail.Text = SimulatorLapTelemetryService.SupportsBestLap(_selectedGame)
                ? running
                    ? "Complete a valid lap; PitMedic will use the exact track, layout, and car."
                    : "No valid lap was recorded in the last session."
                : "Exact best-lap telemetry is not available for this simulator yet.";
            ActivityBenchmarkLabel.Text = "EXTERNAL REFERENCE LAP";
            ActivityBenchmarkValue.Text = "Waiting for best lap";
            ActivityBenchmarkDetail.Text = "No comparison is shown until the simulator confirms the combination.";
            ActivityBenchmarkSourceButton.Visibility = Visibility.Collapsed;
            return;
        }

        ActivityBestLapValue.Text = FormatLapTime(lap.LapSeconds);
        ActivityBestLapDetail.Text = string.IsNullOrWhiteSpace(lap.Layout)
            ? $"{lap.Track} · {lap.Car}"
            : $"{lap.Track} · {lap.Layout} · {lap.Car}";

        if (string.Equals(_displayedLapCombination, lap.CombinationKey, StringComparison.Ordinal)) return;
        _displayedLapCombination = lap.CombinationKey;
        _benchmarkSourceUrl = null;
        ActivityBenchmarkLabel.Text = "EXTERNAL REFERENCE LAP";
        ActivityBenchmarkValue.Text = "Checking…";
        ActivityBenchmarkDetail.Text = "Looking for the best exact-combination source.";
        ActivityBenchmarkSourceButton.Visibility = Visibility.Collapsed;

        _benchmarkLookupCancellation?.Cancel();
        _benchmarkLookupCancellation?.Dispose();
        _benchmarkLookupCancellation = new CancellationTokenSource();
        _ = RefreshBenchmarkAsync(lap, _benchmarkLookupCancellation.Token);
    }

    private async Task RefreshBenchmarkAsync(BestLapRecord lap, CancellationToken cancellationToken)
    {
        try
        {
            var benchmark = await _lapBenchmarks.FindAsync(lap, cancellationToken);
            if (cancellationToken.IsCancellationRequested
                || !string.Equals(_displayedLapCombination, lap.CombinationKey, StringComparison.Ordinal)) return;

            if (!benchmark.Available || benchmark.LapSeconds is not double benchmarkSeconds
                || benchmarkSeconds is < 20 or > 1_800)
            {
                ActivityBenchmarkValue.Text = "No reliable match";
                ActivityBenchmarkDetail.Text = "PitMedic did not find a trustworthy exact-combination comparison.";
                ActivityBenchmarkSourceButton.Visibility = Visibility.Collapsed;
                return;
            }

            var gap = lap.LapSeconds - benchmarkSeconds;
            var pace = lap.LapSeconds / benchmarkSeconds * 100d;
            ActivityBenchmarkValue.Text = FormatLapTime(benchmarkSeconds);
            ActivityBenchmarkLabel.Text = benchmark.SourceKind.Equals("official", StringComparison.OrdinalIgnoreCase)
                ? "OFFICIAL REFERENCE LAP"
                : "WEB REFERENCE LAP";
            ActivityBenchmarkSourceButton.Content = benchmark.SourceKind.Equals("official", StringComparison.OrdinalIgnoreCase)
                ? "VIEW OFFICIAL SOURCE ↗"
                : "WATCH SOURCE LAP ↗";
            ActivityBenchmarkDetail.Text = gap >= 0
                ? $"External source · Your best is +{gap:0.000}s · {pace:0.0}% pace · {benchmark.SourceName}"
                : $"External source · Your best is {-gap:0.000}s faster · {pace:0.0}% pace · {benchmark.SourceName}";
            _benchmarkSourceUrl = benchmark.SourceUrl;
            ActivityBenchmarkSourceButton.Visibility = Uri.TryCreate(_benchmarkSourceUrl, UriKind.Absolute, out _)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        catch (OperationCanceledException)
        {
            // A newer simulator/combination selection replaced this lookup.
        }
    }

    private void ActivityBenchmarkSource_Click(object sender, RoutedEventArgs e)
    {
        if (!Uri.TryCreate(_benchmarkSourceUrl, UriKind.Absolute, out var source)
            || (source.Scheme != Uri.UriSchemeHttps && source.Scheme != Uri.UriSchemeHttp)) return;
        Process.Start(new ProcessStartInfo(source.AbsoluteUri) { UseShellExecute = true });
    }

    private static string FormatLapTime(double seconds)
    {
        var duration = TimeSpan.FromSeconds(seconds);
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}.{duration.Milliseconds:000}"
            : $"{(int)duration.TotalMinutes}:{duration.Seconds:00}.{duration.Milliseconds:000}";
    }

    private static string FormatMonitoredTime(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours:N0}h {duration.Minutes:00}m";
        return $"{Math.Max(0, (int)duration.TotalMinutes):N0}m";
    }

    private void SetHeaderStatus(string text, string background, string border, string dot, string foreground)
    {
        SelectedHeaderStatusText.Text = text;
        SelectedHeaderStatusBorder.SetResourceReference(Border.BackgroundProperty, background);
        SelectedHeaderStatusBorder.SetResourceReference(Border.BorderBrushProperty, border);
        SelectedHeaderStatusDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, dot);
        SelectedHeaderStatusText.SetResourceReference(TextBlock.ForegroundProperty, foreground);
    }

    private void RefreshSessionStory(IncidentSummary? incident, bool running, bool monitored)
    {
        if (incident is null)
        {
            SessionStoryTime.Text = running ? "LIVE" : monitored ? "READY" : "OFF";
            SessionStoryEvent.Text = running ? "Monitoring active" : monitored ? "Waiting for simulator" : "Monitoring disabled";
            SessionStoryEvidence.Text = running
                ? "Telemetry and logs are being captured"
                : monitored ? "Waiting for the next session" : "Enable this simulator in Settings";
            SessionStorySeparatorTwo.Visibility = Visibility.Collapsed;
            SessionStoryRepair.Visibility = Visibility.Collapsed;
            SessionStoryAction.Visibility = Visibility.Collapsed;
            SessionStoryDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, running ? "GoodBrush" : monitored ? "TelemetryBrush" : "MutedBrush");
            return;
        }

        SessionStoryTime.Text = incident.Timestamp.ToString("MMM d, yyyy · h:mm tt");
        SessionStoryEvent.Text = incident.IsResolved ? "Last finding resolved" : "Session ended unexpectedly";
        SessionStoryEvidence.Text = "Evidence captured";
        SessionStorySeparatorTwo.Visibility = Visibility.Visible;
        SessionStoryRepair.Visibility = Visibility.Visible;
        SessionStoryRepair.Text = incident.IsResolved
            ? "Resolution saved"
            : incident.RepairAvailable ? "Safe repair found" : "Finding ready for review";
        SessionStoryAction.Content = incident.IsResolved ? "View details  →" : "Review finding  →";
        SessionStoryAction.Visibility = Visibility.Visible;
        SessionStoryDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, incident.IsResolved ? "GoodBrush" : "AccentBrush");
    }

    private void RefreshSelectedFinding(IncidentSummary? active, IncidentSummary? latest)
    {
        SelectedFindingCard.Visibility = active is null ? Visibility.Collapsed : Visibility.Visible;
        SelectedFindingEmpty.Visibility = active is null && latest is null
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (active is not null)
        {
            SelectedFindingTitle.Text = active.Category;
            SelectedFindingSummary.Text = string.IsNullOrWhiteSpace(active.Summary)
                ? "PitMedic captured evidence for this session."
                : active.Summary;
            SelectedFindingRepairMeta.Text = active.RepairAvailable
                ? $"SAFE REPAIR · ABOUT {Math.Max(1, active.EstimatedMinutes)} {(Math.Max(1, active.EstimatedMinutes) == 1 ? "MINUTE" : "MINUTES")}"
                : "FINDING CAPTURED · REVIEW AVAILABLE";
            SelectedFindingAction.Content = "REVIEW FINDING";
        }

        var evidence = active ?? latest;
        CapturedEvidencePanel.Visibility = evidence is null ? Visibility.Collapsed : Visibility.Visible;
        if (evidence is null) return;

        EvidenceTitleOne.Text = evidence.Category;
        EvidenceDetailOne.Text = string.IsNullOrWhiteSpace(evidence.Summary) ? "Evidence saved with the finding" : evidence.Summary;
        EvidenceTimeOne.Text = evidence.Timestamp.ToString("h:mm tt");
        EvidenceTitleTwo.Text = evidence.IsResolved ? "Finding resolved" : "Session ended unexpectedly";
        EvidenceDetailTwo.Text = evidence.IsResolved
            ? (string.IsNullOrWhiteSpace(evidence.ResolutionText) ? "Resolution retained in history" : evidence.ResolutionText)
            : "Process ended outside a normal exit";
        EvidenceTimeTwo.Text = evidence.Timestamp.ToString("h:mm tt");
    }

    private void RefreshSelectedFooter(IncidentSummary? incident, bool running, bool monitored)
    {
        if (incident is null)
        {
            SelectedFooterLabel.Text = running ? "SESSION ACTIVE" : monitored ? "READY" : "MONITORING OFF";
            SelectedFooterTitle.Text = running
                ? $"{UiGameName(_selectedGame)} is running"
                : monitored ? $"{UiGameName(_selectedGame)} monitoring is ready" : $"{UiGameName(_selectedGame)} monitoring is disabled";
            SelectedFooterDetail.Text = running
                ? "PitMedic is preserving live telemetry, logs, and Windows events."
                : monitored ? "PitMedic will preserve evidence when a finding is detected." : "Enable this simulator from Settings to resume monitoring.";
            SelectedFooterDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, running ? "GoodBrush" : monitored ? "TelemetryBrush" : "MutedBrush");
            return;
        }

        SelectedFooterLabel.Text = incident.IsResolved ? "FINDING RESOLVED" : "FINDING CAPTURED";
        SelectedFooterTitle.Text = incident.Category;
        SelectedFooterDetail.Text = incident.IsResolved
            ? "The resolution and captured evidence remain available in history."
            : incident.RepairAvailable ? "Evidence saved · safe repair ready for review" : "Evidence saved · review available";
        SelectedFooterDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, incident.IsResolved ? "GoodBrush" : "AccentBrush");
    }

    private IEnumerable<IncidentSummary> IncidentsFor(GameKind game)
        => _incidents.Where(i => !i.IsDismissed && IsRecentFinding(i) && IncidentMatches(i, game)).OrderByDescending(i => i.Timestamp);

    private IncidentSummary? ActiveIncidentFor(GameKind game)
        => IncidentsFor(game).FirstOrDefault(i => !i.IsResolved);

    private static bool IsRecentFinding(IncidentSummary incident)
        => incident.Timestamp >= DateTimeOffset.Now.Subtract(LatestFindingWindow);

    private static bool IncidentMatches(IncidentSummary incident, GameKind game)
    {
        var definition = GameDefinition.Supported.First(g => g.Kind == game);
        return string.Equals(incident.Game, definition.DisplayName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(incident.Game, UiGameName(game), StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetGame(string displayName, out GameKind game)
    {
        var match = GameDefinition.Supported.FirstOrDefault(definition =>
            string.Equals(displayName, definition.DisplayName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(displayName, UiGameName(definition.Kind), StringComparison.OrdinalIgnoreCase));
        game = match?.Kind ?? default;
        return match is not null;
    }

    private static string UiGameName(GameKind game)
        => game == GameKind.RaceRoom
            ? "RaceRoom"
            : GameDefinition.Supported.First(g => g.Kind == game).DisplayName;

    private bool IsMonitored(GameKind game) => game switch
    {
        GameKind.LeMansUltimate => _settings.MonitorLeMansUltimate,
        GameKind.IRacing => _settings.MonitorIRacing,
        GameKind.AssettoCorsaEvo => _settings.MonitorAssettoCorsaEvo,
        GameKind.RaceRoom => _settings.MonitorRaceRoom,
        GameKind.AssettoCorsaCompetizione => _settings.MonitorAssettoCorsaCompetizione,
        GameKind.Automobilista2 => _settings.MonitorAutomobilista2,
        _ => false
    };

    private static string FindingsPrefix(GameKind game) => game switch
    {
        GameKind.LeMansUltimate => "LMU",
        GameKind.IRacing => "IRACING",
        GameKind.AssettoCorsaEvo => "AC EVO",
        GameKind.RaceRoom => "RACEROOM",
        GameKind.AssettoCorsaCompetizione => "ACC",
        GameKind.Automobilista2 => "AMS2",
        _ => "SIMULATOR"
    };

    private void SessionStoryAction_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPrimaryIncident is not null) OpenIncident(_selectedPrimaryIncident);
    }

    private void SelectedFindingAction_Click(object sender, RoutedEventArgs e)
    {
        var active = ActiveIncidentFor(_selectedGame);
        if (active is not null) OpenIncident(active);
    }

    private void HomeReviewFinding_Click(object sender, RoutedEventArgs e)
    {
        if (_homeLatestIncident is not null) OpenIncident(_homeLatestIncident);
    }

    private void OpenIncident(IncidentSummary incident)
    {
        ShowIncidentDetails(incident);
    }

    private void ShowRepairPrompt(IncidentSummary incident)
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        var dialog = new RepairPromptWindow(_monitoring, incident) { Owner = this };
        dialog.ShowDialog();
    }

    private void IncidentAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not IncidentSummary incident) return;
        OpenIncident(incident);
    }

    private void ShowIncidentDetails(IncidentSummary incident)
    {
        var details = _monitoring.GetIncidentDetails(incident.Folder);
        if (details is null)
        {
            MessageBox.Show(this,
                "PitMedic retained the finding evidence, but a user-friendly explanation could not be reconstructed for this older record.",
                "Finding review unavailable",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }
        Action? runRepair = incident.RepairAvailable && !incident.IsResolved
            ? () => ShowRepairPrompt(incident)
            : null;
        Func<bool>? acknowledgeFinding = !incident.IsResolved && !incident.IsDismissed
            ? () => AcknowledgeIncident(incident)
            : null;
        var dialog = new IncidentDetailsWindow(details, runRepair, acknowledgeFinding) { Owner = this };
        dialog.ShowDialog();
    }

    private bool AcknowledgeIncident(IncidentSummary incident)
    {
        if (!_monitoring.AcknowledgeIncident(incident.Folder)) return false;
        _incidents.Remove(incident);
        if (TryGetGame(incident.Game, out var game)) _liveFaultGames.Remove(game);
        RefreshSimulatorViews();
        RecorderStatus.Text = "Finding acknowledged";
        return true;
    }

    private void RepairBannerDismiss_Click(object sender, RoutedEventArgs e)
    {
        _repairBannerDismissed = true;
        RepairBanner.Visibility = Visibility.Collapsed;
    }

    private void DrawChart()
    {
        var width = Math.Max(1, ThermalCanvas.ActualWidth);
        var height = Math.Max(1, ThermalCanvas.ActualHeight);
        var (windowStart, windowEnd, windowMinutes) = GetThermalWindow();
        var data = _chart.Where(s => s.Timestamp >= windowStart && s.Timestamp <= windowEnd).ToArray();
        data = Downsample(data, Math.Max(2, (int)Math.Ceiling(width)));

        ThermalWindowLabel.Text = $"{Math.Ceiling(windowMinutes):0} MIN";
        CpuLine.Points.Clear();
        GpuLine.Points.Clear();
        GpuPowerLine.Points.Clear();
        if (data.Length < 2) return;

        var powerScaleMax = GetPowerScaleMax(data);
        foreach (var sample in data)
        {
            var x = ToThermalX(sample.Timestamp, windowStart, windowEnd, width);
            if (sample.CpuTempC is float cpu) CpuLine.Points.Add(new System.Windows.Point(x, ToY(cpu, height)));
            if (sample.GpuTempC is float gpu) GpuLine.Points.Add(new System.Windows.Point(x, ToY(gpu, height)));
            if (sample.GpuPowerW is float power) GpuPowerLine.Points.Add(new System.Windows.Point(x, ToPowerY(power, height, powerScaleMax)));
        }
    }

    private static TelemetrySample[] Downsample(TelemetrySample[] data, int maximumPoints)
    {
        if (data.Length <= maximumPoints) return data;
        var result = new TelemetrySample[maximumPoints];
        var scale = (data.Length - 1d) / (maximumPoints - 1d);
        for (var i = 0; i < maximumPoints; i++)
            result[i] = data[(int)Math.Round(i * scale)];
        return result;
    }


    private (DateTimeOffset Start, DateTimeOffset End, double Minutes) GetThermalWindow()
    {
        var end = DateTimeOffset.Now;
        if (_chart.Count == 0)
            return (end.AddMinutes(-1), end, 1);

        var first = _chart.Peek().Timestamp;
        var elapsedMinutes = Math.Max(0, (end - first).TotalMinutes);
        var minutes = Math.Clamp(Math.Max(1, elapsedMinutes), 1, 60);
        var start = elapsedMinutes <= 1 ? end.AddMinutes(-1)
            : elapsedMinutes < 60 ? first
            : end.AddMinutes(-60);

        return (start, end, minutes);
    }

    private static double ToThermalX(DateTimeOffset timestamp, DateTimeOffset windowStart, DateTimeOffset windowEnd, double width)
    {
        var totalSeconds = Math.Max(1.0, (windowEnd - windowStart).TotalSeconds);
        var elapsedSeconds = (timestamp - windowStart).TotalSeconds;
        return Math.Clamp(elapsedSeconds / totalSeconds, 0.0, 1.0) * width;
    }

    private static double ToY(float tempC, double height)
    {
        var clamped = Math.Clamp(tempC, 20f, 110f);
        return height - ((clamped - 20f) / 90f * height);
    }


    private static double GetPowerScaleMax(IReadOnlyList<TelemetrySample> data)
    {
        var maxPower = data.Where(s => s.GpuPowerW.HasValue).Select(s => s.GpuPowerW!.Value).DefaultIfEmpty(0f).Max();
        if (maxPower <= 0) return 150;

        // Keep meaningful vertical headroom so the power trace does not ride along
        // the top edge of the chart during normal GPU load.
        var withHeadroom = Math.Max(150f, maxPower * 1.75f);
        return Math.Ceiling(withHeadroom / 25.0) * 25.0;
    }

    private static double ToPowerY(float watts, double height, double scaleMax)
    {
        var clamped = Math.Clamp(watts, 0f, (float)Math.Max(1.0, scaleMax));
        return height - (clamped / Math.Max(1.0, scaleMax) * height);
    }

    private string Temp(float? valueC)
    {
        if (!valueC.HasValue) return "--°";
        if (_settings.UseFahrenheit) return $"{valueC.Value * 9f / 5f + 32f:0}°F";
        return $"{valueC.Value:0}°C";
    }

    private static string Power(float? watts)
        => watts.HasValue ? $"{watts.Value:0} W" : "-- W";

    private static void SetBar(ProgressBar bar, TextBlock label, float? value)
    {
        bar.Value = Math.Clamp(value ?? 0, 0, 100);
        label.Text = value.HasValue ? $"{value:0}%" : "--%";
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            Maximize_Click(sender, e);
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed && WindowState != WindowState.Maximized) DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        UpdateMaximizeGlyph();
    }

    private void UpdateMaximizeGlyph()
    {
        if (MaximizeGlyph is null || MaximizeButton is null) return;
        var maximized = WindowState == WindowState.Maximized;
        MaximizeGlyph.Text = maximized ? "❐" : "□";
        MaximizeButton.ToolTip = maximized ? "Restore" : "Maximize";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    public void OpenIncidentHistory()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        var window = new IncidentHistoryWindow(_monitoring) { Owner = this };
        window.Show();
    }

    private void OpenIncidents_Click(object sender, RoutedEventArgs e) => OpenIncidentHistory();

    public void OpenSettingsDialog()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        var window = new SettingsWindow(_monitoring, _anonymousUsage, _updates) { Owner = this };
        window.ShowDialog();
    }

    private void ShowAvailableUpdate(AvailableUpdate update)
    {
        _availableUpdate = update;
        UpdateBannerTitle.Text = update.Title;
        UpdateBannerMessage.Text = $"PitMedic {update.Version} is ready. Download it when you are ready; PitMedic will never install an update without you.";
        UpdateBanner.Visibility = Visibility.Visible;
    }

    private void DownloadUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_availableUpdate is not null) OpenExternalUrl(_availableUpdate.DownloadUri.AbsoluteUri);
    }

    private void UpdateDetails_Click(object sender, RoutedEventArgs e)
    {
        if (_availableUpdate is not null) OpenExternalUrl(_availableUpdate.ReleaseUri.AbsoluteUri);
    }

    private void UpdateBannerDismiss_Click(object sender, RoutedEventArgs e)
    {
        if (_availableUpdate is not null) _updates.Dismiss(_availableUpdate.Version);
        UpdateBanner.Visibility = Visibility.Collapsed;
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettingsDialog();

    private void About_Click(object sender, RoutedEventArgs e)
    {
        HomeNav.IsChecked = false;
        foreach (var button in _navButtons.Values) button.IsChecked = false;
        HomePage.Visibility = Visibility.Collapsed;
        SimulatorPage.Visibility = Visibility.Collapsed;
        AboutPage.Visibility = Visibility.Visible;
    }

    private void ContactEmail_Click(object sender, RoutedEventArgs e) =>
        OpenExternalUrl($"mailto:{AppInfo.ContactEmail}");

    private void SupportPitMedic_Click(object sender, RoutedEventArgs e) =>
        OpenExternalUrl(AppInfo.SupportUrl);

    private void ProjectWebsite_Click(object sender, RoutedEventArgs e) =>
        OpenExternalUrl(AppInfo.ProjectUrl);

    private static void OpenExternalUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Write($"Could not open external link: {ex.GetType().Name}: {ex.Message}");
            MessageBox.Show("Windows could not open that link in your browser.", "PitMedic", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void ThermalCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (ThermalCanvas.ActualWidth <= 1 || ThermalCanvas.ActualHeight <= 1)
        {
            HideThermalHover();
            return;
        }

        var (windowStart, windowEnd, _) = GetThermalWindow();
        var data = _chart.Where(s => s.Timestamp >= windowStart && s.Timestamp <= windowEnd).ToArray();
        if (data.Length < 2)
        {
            HideThermalHover();
            return;
        }

        var point = e.GetPosition(ThermalCanvas);
        var ratio = Math.Clamp(point.X / ThermalCanvas.ActualWidth, 0.0, 1.0);
        var targetTime = windowStart.AddSeconds((windowEnd - windowStart).TotalSeconds * ratio);

        // There is intentionally no hover target in the unused part of a partially-filled
        // configured thermal window. This also ensures details only appear where a trace really exists.
        if (targetTime < data[0].Timestamp || targetTime > data[^1].Timestamp)
        {
            HideThermalHover();
            return;
        }

        var cpuAtCursor = InterpolateMetric(data, targetTime, s => s.CpuTempC);
        var gpuAtCursor = InterpolateMetric(data, targetTime, s => s.GpuTempC);
        var powerAtCursor = InterpolateMetric(data, targetTime, s => s.GpuPowerW);
        var powerScaleMax = GetPowerScaleMax(data);

        // Only reveal graph details when the pointer is actually touching a plotted line.
        const double hitTolerance = 7.0;
        var cpuY = cpuAtCursor.HasValue ? ToY(cpuAtCursor.Value, ThermalCanvas.ActualHeight) : double.NaN;
        var gpuY = gpuAtCursor.HasValue ? ToY(gpuAtCursor.Value, ThermalCanvas.ActualHeight) : double.NaN;
        var powerY = powerAtCursor.HasValue ? ToPowerY(powerAtCursor.Value, ThermalCanvas.ActualHeight, powerScaleMax) : double.NaN;
        var cpuDistance = double.IsNaN(cpuY) ? double.MaxValue : Math.Abs(point.Y - cpuY);
        var gpuDistance = double.IsNaN(gpuY) ? double.MaxValue : Math.Abs(point.Y - gpuY);
        var powerDistance = double.IsNaN(powerY) ? double.MaxValue : Math.Abs(point.Y - powerY);
        var nearestDistance = Math.Min(cpuDistance, Math.Min(gpuDistance, powerDistance));

        if (nearestDistance > hitTolerance)
        {
            HideThermalHover();
            return;
        }

        var selectedMetric = cpuDistance <= gpuDistance && cpuDistance <= powerDistance
            ? 0
            : gpuDistance <= powerDistance ? 1 : 2;
        var selectedY = selectedMetric == 0 ? cpuY : selectedMetric == 1 ? gpuY : powerY;
        var x = point.X;

        HoverLine.X1 = x;
        HoverLine.X2 = x;
        HoverLine.Y1 = Math.Max(0, selectedY - 18);
        HoverLine.Y2 = Math.Min(ThermalCanvas.ActualHeight, selectedY + 18);
        HoverLine.Visibility = Visibility.Visible;

        HoverCpuDot.Visibility = Visibility.Collapsed;
        HoverGpuDot.Visibility = Visibility.Collapsed;
        HoverPowerDot.Visibility = Visibility.Collapsed;
        if (selectedMetric == 0)
            PositionTempHoverDot(HoverCpuDot, x, cpuAtCursor, ThermalCanvas.ActualHeight);
        else if (selectedMetric == 1)
            PositionTempHoverDot(HoverGpuDot, x, gpuAtCursor, ThermalCanvas.ActualHeight);
        else
            PositionPowerHoverDot(HoverPowerDot, x, powerAtCursor, ThermalCanvas.ActualHeight, powerScaleMax);

        ThermalTooltipTime.Text = $"Time  {targetTime:h:mm:ss tt}";
        ThermalTooltipCpu.Text = $"CPU  {Temp(cpuAtCursor)}";
        ThermalTooltipGpu.Text = $"GPU  {Temp(gpuAtCursor)}";
        ThermalTooltipPower.Text = $"GPU Power  {Power(powerAtCursor)}";

        const double tooltipWidth = 165;
        var left = x + 12;
        if (left + tooltipWidth > ThermalCanvas.ActualWidth) left = Math.Max(6, x - tooltipWidth - 12);
        var top = Math.Clamp(selectedY - 48, 6, Math.Max(6, ThermalCanvas.ActualHeight - 105));
        Canvas.SetLeft(ThermalTooltip, left);
        Canvas.SetTop(ThermalTooltip, top);
        ThermalTooltip.Visibility = Visibility.Visible;
    }

    private static float? InterpolateMetric(
        IReadOnlyList<TelemetrySample> data,
        DateTimeOffset targetTime,
        Func<TelemetrySample, float?> selector)
    {
        if (data.Count == 0 || targetTime < data[0].Timestamp || targetTime > data[^1].Timestamp) return null;

        var rightIndex = 1;
        while (rightIndex < data.Count && data[rightIndex].Timestamp < targetTime) rightIndex++;
        if (rightIndex >= data.Count) return selector(data[^1]);

        var left = data[rightIndex - 1];
        var right = data[rightIndex];
        var leftValue = selector(left);
        var rightValue = selector(right);
        if (!leftValue.HasValue || !rightValue.HasValue) return null;

        var spanSeconds = (right.Timestamp - left.Timestamp).TotalSeconds;
        if (spanSeconds <= 0.001) return rightValue;

        var fraction = Math.Clamp((targetTime - left.Timestamp).TotalSeconds / spanSeconds, 0.0, 1.0);
        return (float)(leftValue.Value + ((rightValue.Value - leftValue.Value) * fraction));
    }

    private static void PositionTempHoverDot(System.Windows.Shapes.Ellipse dot, double x, float? tempC, double height)
    {
        if (!tempC.HasValue)
        {
            dot.Visibility = Visibility.Collapsed;
            return;
        }
        Canvas.SetLeft(dot, x - 4);
        Canvas.SetTop(dot, ToY(tempC.Value, height) - 4);
        dot.Visibility = Visibility.Visible;
    }

    private static void PositionPowerHoverDot(System.Windows.Shapes.Ellipse dot, double x, float? watts, double height, double scaleMax)
    {
        if (!watts.HasValue)
        {
            dot.Visibility = Visibility.Collapsed;
            return;
        }
        Canvas.SetLeft(dot, x - 4);
        Canvas.SetTop(dot, ToPowerY(watts.Value, height, scaleMax) - 4);
        dot.Visibility = Visibility.Visible;
    }

    private void HideThermalHover()
    {
        HoverLine.Visibility = Visibility.Collapsed;
        HoverCpuDot.Visibility = Visibility.Collapsed;
        HoverGpuDot.Visibility = Visibility.Collapsed;
        HoverPowerDot.Visibility = Visibility.Collapsed;
        ThermalTooltip.Visibility = Visibility.Collapsed;
    }

    private void ThermalCanvas_MouseLeave(object sender, MouseEventArgs e) => HideThermalHover();

    private void ThermalCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawChart();

    private sealed record CompanionSoftwareDisplayItem(
        string Name,
        string Detail,
        string Status,
        Brush StatusBrush);
}
