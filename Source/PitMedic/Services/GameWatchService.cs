using System.Diagnostics;
using System.Runtime.InteropServices;
using PitMedic.Models;

namespace PitMedic.Services;

public sealed class GameWatchService : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<GameKind, TrackedGame> _tracked = new();
    private readonly TelemetryBuffer _buffer;
    private readonly IncidentRecorder _recorder;
    private readonly SettingsService _settings;
    private readonly IRacingLiveLogMonitor _iracingGlobalMonitor = new();
    private int _globalFaultRecording;
    private DateTimeOffset _lastHelperServiceFault;

    public event Action<GameKind, bool>? GameStatusChanged;
    public event Action<SimulatorSessionSummary>? SessionCompleted;
    public event Action<LiveFaultEvidence>? LiveFaultDetected;

    public GameWatchService(TelemetryBuffer buffer, IncidentRecorder recorder, SettingsService settings)
    {
        _buffer = buffer;
        _recorder = recorder;
        _settings = settings;
        _iracingGlobalMonitor.StartSession(DateTimeOffset.Now);
    }

    public void Scan()
    {
        foreach (var game in GameDefinition.Supported)
        {
            if (!ShouldMonitor(game.Kind)) continue;

            TrackedGame? current = null;
            lock (_gate)
            {
                if (_tracked.TryGetValue(game.Kind, out current) && !current.Lifetime.TryGetExitCode(out _))
                {
                    ObserveMainWindow(current);
                    PollLiveFaults(current);
                    continue;
                }
            }

            if (current is not null && current.Lifetime.TryGetExitCode(out _))
                TriggerExit(current);

            var process = FindProcess(game);
            if (process is null) continue;

            try
            {
                var tracked = new TrackedGame(game, process, SafeStart(process));
                tracked.LiveLogMonitor = SimulatorLiveLogMonitor.Create(game.Kind);
                tracked.LiveLogMonitor?.StartSession(tracked.Started);
                ObserveMainWindow(tracked);
                lock (_gate) _tracked[game.Kind] = tracked;
                AppLog.Write($"Detected {game.DisplayName}: PID {process.Id}, process={process.ProcessName}");
                GameStatusChanged?.Invoke(game.Kind, true);
            }
            catch (Exception ex)
            {
                AppLog.Write($"Failed to track {game.DisplayName}: {ex.Message}");
                process.Dispose();
            }
        }

        // iRacing can report a launch/service/UI failure before iRacingSim64DX11.exe ever starts.
        // Keep a lightweight log watcher active even when the simulator process is absent so those
        // errors become repairable incidents instead of disappearing when the user closes the UI.
        if (_settings.Current.MonitorIRacing && !IsRunning(GameKind.IRacing))
            PollGlobalIRacingFaults();
    }

    public bool IsRunning(GameKind kind)
    {
        lock (_gate)
            return _tracked.TryGetValue(kind, out var item) && !item.Lifetime.TryGetExitCode(out _);
    }

    internal int? ClosedGameProcessId(GameKind kind)
    {
        lock (_gate)
        {
            return _tracked.TryGetValue(kind, out var tracked)
                && HasClosedMainWindow(tracked) ? tracked.Process.Id : null;
        }
    }

    internal bool TryStopClosedGame(GameKind kind, Process process, CancellationToken token)
    {
        lock (_gate)
        {
            if (!_tracked.TryGetValue(kind, out var tracked)
                || tracked.Process.Id != process.Id) return false;

            try
            {
                // Keep the process object open while checking identity and terminating it.
                if (process.SafeHandle.IsInvalid
                    || tracked.Started != new DateTimeOffset(process.StartTime)
                    || !HasClosedMainWindow(tracked)) return false;

                token.ThrowIfCancellationRequested();
                if (kind == GameKind.IRacing) IRacingUpdateGuard.EnsureIdle();
                process.Kill();
                tracked.StoppedForRepair = true;
                return true;
            }
            catch (InvalidOperationException) when (tracked.Lifetime.TryGetExitCode(out _))
            {
                return false; // Normal shutdown won the race.
            }
            catch (System.ComponentModel.Win32Exception) when (tracked.Lifetime.TryGetExitCode(out _))
            {
                return false;
            }
        }
    }

    private static void ObserveMainWindow(TrackedGame tracked)
    {
        try
        {
            tracked.Process.Refresh();
            var window = tracked.Process.MainWindowHandle;
            if (window != 0) tracked.LastMainWindow = window;
        }
        catch (InvalidOperationException) { } // Exited during the sample.
        catch (System.ComponentModel.Win32Exception) { } // Window state is unavailable.
    }

    private static bool HasClosedMainWindow(TrackedGame tracked)
    {
        if (tracked.LastMainWindow == 0 || tracked.Lifetime.TryGetExitCode(out _)) return false;
        try
        {
            tracked.Process.Refresh();
            // A hidden or minimized window still exists. Require destruction of the
            // previously observed window as well as absence of a replacement.
            return tracked.Process.MainWindowHandle == 0 && !IsWindow(tracked.LastMainWindow);
        }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    private bool ShouldMonitor(GameKind kind)
    {
        var settings = _settings.Current;
        return kind switch
        {
            GameKind.LeMansUltimate => settings.MonitorLeMansUltimate,
            GameKind.IRacing => settings.MonitorIRacing,
            GameKind.AssettoCorsaEvo => settings.MonitorAssettoCorsaEvo,
            GameKind.RaceRoom => settings.MonitorRaceRoom,
            GameKind.AssettoCorsaCompetizione => settings.MonitorAssettoCorsaCompetizione,
            GameKind.Automobilista2 => settings.MonitorAutomobilista2,
            _ => false
        };
    }

    private static Process? FindProcess(GameDefinition game)
    {
        var candidates = new List<Process>();
        foreach (var alias in game.ProcessNames)
        {
            try { candidates.AddRange(Process.GetProcessesByName(alias)); }
            catch { }
        }
        if (candidates.Count == 0) return null;
        var selected = candidates.OrderByDescending(SafeStart).First();
        foreach (var p in candidates.Where(p => p.Id != selected.Id)) p.Dispose();
        return selected;
    }

    private void PollLiveFaults(TrackedGame tracked)
    {
        if (tracked.LiveLogMonitor is null) return;
        foreach (var fault in tracked.LiveLogMonitor.Poll())
        {
            lock (tracked.FaultGate) tracked.LiveFaults.Add(fault);
            AppLog.Write($"Live {tracked.Game.DisplayName} fault detected: {fault.Category} | {fault.Message}");
            LiveFaultDetected?.Invoke(fault);
        }
    }

    private void PollGlobalIRacingFaults()
    {
        // The "System Not in Service" condition can prevent the simulator process from ever
        // starting, and some UI builds do not reliably write the dialog text to a log. Detect
        // the condition directly whenever the iRacing UI is open but the helper service is absent.
        var now = DateTimeOffset.Now;
        if (!IRacingUpdateGuard.IsBusy()
            && IsAnyProcessRunning("iRacingUI", "iRacingUI64")
            && !IsAnyProcessRunning("iRacingService64", "iRacingService")
            && now - _lastHelperServiceFault > TimeSpan.FromSeconds(75))
        {
            _lastHelperServiceFault = now;
            var serviceFault = new LiveFaultEvidence(now, "helper-service", "iRacing Helper Service failure",
                "iRacing UI is running but the iRacing Helper Service is not running.", "process-state", GameKind.IRacing);
            AppLog.Write($"Live iRacing service-state fault detected: {serviceFault.Message}");
            LiveFaultDetected?.Invoke(serviceFault);
            _ = RecordGlobalIRacingFaultAsync(serviceFault);
        }

        foreach (var fault in _iracingGlobalMonitor.Poll())
        {
            AppLog.Write($"Live iRacing UI/service fault detected before simulator start: {fault.Category} | {fault.Message}");
            LiveFaultDetected?.Invoke(fault);
            _ = RecordGlobalIRacingFaultAsync(fault);
        }
    }

    private static bool IsAnyProcessRunning(params string[] names)
    {
        foreach (var name in names)
        {
            Process[] processes;
            try { processes = Process.GetProcessesByName(name); } catch { continue; }
            try { if (processes.Length > 0) return true; }
            finally { foreach (var process in processes) process.Dispose(); }
        }
        return false;
    }

    private async Task RecordGlobalIRacingFaultAsync(LiveFaultEvidence fault)
    {
        // Serialize global UI/service incidents so a burst of related log lines cannot create
        // several folders at once. The live monitor itself also applies a per-signature cooldown.
        if (Interlocked.Exchange(ref _globalFaultRecording, 1) != 0) return;
        try
        {
            var game = GameDefinition.Supported.First(g => g.Kind == GameKind.IRacing);
            var settings = _settings.Current;
            await _recorder.RecordLiveFaultAsync(game, fault, _buffer.Snapshot(settings.BufferMinutes));
        }
        catch (Exception ex)
        {
            AppLog.Write($"Could not record live iRacing UI/service issue: {ex}");
        }
        finally
        {
            Interlocked.Exchange(ref _globalFaultRecording, 0);
        }
    }

    private void TriggerExit(TrackedGame tracked)
    {
        // Scan is the sole producer; status failures retain this tracked session.
        if (!tracked.Lifetime.TryGetExitCode(out var exitCode)) return;
        if (Interlocked.Exchange(ref tracked.ExitHandled, 1) != 0) return;
        _ = OnExitedAsync(tracked, exitCode);
    }

    private async Task OnExitedAsync(TrackedGame tracked, int? exitCode)
    {
        var ended = DateTimeOffset.Now;
        var pid = tracked.Process.Id;

        lock (_gate)
        {
            if (_tracked.TryGetValue(tracked.Game.Kind, out var current) && ReferenceEquals(current, tracked))
                _tracked.Remove(tracked.Game.Kind);
        }
        GameStatusChanged?.Invoke(tracked.Game.Kind, false);
        if (tracked.Game.Kind == GameKind.IRacing)
            _iracingGlobalMonitor.StartSession(ended);
        AppLog.Write($"{tracked.Game.DisplayName} exited: PID {pid}, exitCode={(exitCode.HasValue ? $"0x{unchecked((uint)exitCode.Value):X8}" : "unknown")}");

        var outcome = SimulatorSessionOutcome.ResultUnavailable;
        try
        {
            if (tracked.StoppedForRepair) return; // The repair owns this exit, so do not create a second incident.
            PollLiveFaults(tracked);
            var settings = _settings.Current;
            LiveFaultEvidence[] liveFaults;
            lock (tracked.FaultGate) liveFaults = tracked.LiveFaults.ToArray();
            var incident = await _recorder.RecordAsync(tracked.Game, pid, tracked.Started, ended, exitCode,
                _buffer.Snapshot(settings.BufferMinutes), settings.CaptureEveryGameExit, liveFaults);
            outcome = SimulatorSessionPolicy.OutcomeFor(incident);
        }
        catch (Exception ex)
        {
            AppLog.Write($"Issue recording failed for {tracked.Game.DisplayName}: {ex}");
        }
        finally
        {
            SessionCompleted?.Invoke(new SimulatorSessionSummary
            {
                Game = tracked.Game.Kind,
                Started = tracked.Started,
                Ended = ended,
                Outcome = outcome
            });
            tracked.Lifetime.Dispose();
            tracked.Process.Dispose();
        }
    }

    private static DateTimeOffset SafeStart(Process process)
    {
        try { return process.StartTime; } catch { return DateTimeOffset.Now; }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var item in _tracked.Values)
            {
                Interlocked.Exchange(ref item.ExitHandled, 1);
                item.Lifetime.Dispose();
                item.Process.Dispose();
            }
            _tracked.Clear();
        }
    }

    private sealed class TrackedGame
    {
        public GameDefinition Game { get; }
        public Process Process { get; }
        public ProcessExitMonitor Lifetime { get; }
        public DateTimeOffset Started { get; }
        public ILiveLogMonitor? LiveLogMonitor { get; set; }
        public object FaultGate { get; } = new();
        public List<LiveFaultEvidence> LiveFaults { get; } = new();
        public int ExitHandled;
        public nint LastMainWindow;
        public bool StoppedForRepair;

        public TrackedGame(GameDefinition game, Process process, DateTimeOffset started)
        {
            Game = game;
            Process = process;
            Lifetime = new ProcessExitMonitor(process);
            Started = started;
        }
    }
}
