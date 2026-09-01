using System.Diagnostics;
using Microsoft.Win32;
using PitMedic.Models;

namespace PitMedic.Services;

public sealed class CompanionSoftwareWatchService : IDisposable
{
    private static readonly TimeSpan DiscoveryInterval = TimeSpan.FromMinutes(1);
    private readonly object _gate = new();
    private readonly Dictionary<CompanionSoftwareKind, TrackedCompanion> _tracked = new();
    private readonly HashSet<CompanionSoftwareKind> _detected = new();
    private readonly HashSet<CompanionSoftwareKind> _installed = new();
    private readonly HashSet<CompanionSoftwareKind> _runningWithoutTracking = new();
    private readonly TelemetryBuffer _buffer;
    private readonly IncidentRecorder _recorder;
    private readonly SettingsService _settings;
    private DateTimeOffset _lastInstallDiscovery = DateTimeOffset.MinValue;

    public event Action<CompanionSoftwareStatus>? StatusChanged;

    public CompanionSoftwareWatchService(TelemetryBuffer buffer, IncidentRecorder recorder, SettingsService settings)
    {
        _buffer = buffer;
        _recorder = recorder;
        _settings = settings;
        RefreshInstallDiscovery(force: true);
    }

    public void Scan()
    {
        RefreshInstallDiscovery();

        if (!_settings.Current.MonitorCompanionSoftware)
        {
            StopTracking();
            DetectRunningWithoutTracking();
            return;
        }

        lock (_gate) _runningWithoutTracking.Clear();

        foreach (var software in CompanionSoftwareDefinition.Supported)
        {
            TrackedCompanion? current;
            lock (_gate)
                _tracked.TryGetValue(software.Kind, out current);

            if (current is not null && !SafeHasExited(current.Process))
                continue;

            if (current is not null)
                TriggerExit(current);

            var process = FindProcess(software);
            if (process is null) continue;

            try
            {
                process.EnableRaisingEvents = true;
                var tracked = new TrackedCompanion(software, process, SafeStart(process));
                tracked.ProcessPath = SafeProcessPath(process);
                process.Exited += (_, _) => TriggerExit(tracked);
                lock (_gate)
                {
                    _detected.Add(software.Kind);
                    _tracked[software.Kind] = tracked;
                }
                AppLog.Write($"Detected companion software {software.DisplayName}: PID {process.Id}, process={process.ProcessName}");
                RaiseStatus(software);
            }
            catch (Exception ex)
            {
                AppLog.Write($"Failed to track companion software {software.DisplayName}: {ex.Message}");
                process.Dispose();
            }
        }
    }

    public IReadOnlyList<CompanionSoftwareStatus> StatusSnapshot()
    {
        lock (_gate)
        {
            return CompanionSoftwareDefinition.Supported
                .Select(software => new CompanionSoftwareStatus(
                    software.Kind,
                    software.DisplayName,
                    _detected.Contains(software.Kind) || _installed.Contains(software.Kind),
                    (_tracked.TryGetValue(software.Kind, out var tracked) && !SafeHasExited(tracked.Process))
                    || _runningWithoutTracking.Contains(software.Kind)))
                .ToArray();
        }
    }

    private void DetectRunningWithoutTracking()
    {
        var runningNow = new HashSet<CompanionSoftwareKind>();
        foreach (var software in CompanionSoftwareDefinition.Supported)
        {
            var process = FindProcess(software);
            if (process is null) continue;
            process.Dispose();
            runningNow.Add(software.Kind);

            var changed = false;
            lock (_gate) changed = _detected.Add(software.Kind);
            if (changed) RaiseStatus(software);
        }

        CompanionSoftwareDefinition[] changedStatuses;
        lock (_gate)
        {
            changedStatuses = CompanionSoftwareDefinition.Supported
                .Where(software => _runningWithoutTracking.Contains(software.Kind) != runningNow.Contains(software.Kind))
                .ToArray();
            _runningWithoutTracking.Clear();
            _runningWithoutTracking.UnionWith(runningNow);
        }
        foreach (var software in changedStatuses) RaiseStatus(software);
    }

    private void StopTracking()
    {
        TrackedCompanion[] tracked;
        lock (_gate)
        {
            tracked = _tracked.Values.ToArray();
            _tracked.Clear();
        }

        foreach (var item in tracked)
        {
            Interlocked.Exchange(ref item.ExitHandled, 1);
            item.Process.Dispose();
            RaiseStatus(item.Software);
        }
    }

    private void TriggerExit(TrackedCompanion tracked)
    {
        if (Interlocked.Exchange(ref tracked.ExitHandled, 1) != 0) return;
        _ = OnExitedAsync(tracked);
    }

    private async Task OnExitedAsync(TrackedCompanion tracked)
    {
        var ended = DateTimeOffset.Now;
        int? exitCode = null;
        try { exitCode = tracked.Process.ExitCode; } catch { }
        var pid = tracked.ProcessId;

        lock (_gate)
        {
            if (_tracked.TryGetValue(tracked.Software.Kind, out var current) && current.ProcessId == pid)
                _tracked.Remove(tracked.Software.Kind);
            _detected.Add(tracked.Software.Kind);
        }
        RaiseStatus(tracked.Software);

        AppLog.Write($"{tracked.Software.DisplayName} exited: PID {pid}, exitCode={(exitCode.HasValue ? $"0x{unchecked((uint)exitCode.Value):X8}" : "unknown")}");
        try
        {
            if (_settings.Current.MonitorCompanionSoftware)
            {
                var settings = _settings.Current;
                await _recorder.RecordCompanionAsync(
                    tracked.Software,
                    pid,
                    tracked.Started,
                    ended,
                    exitCode,
                    tracked.ProcessPath,
                    _buffer.Snapshot(settings.BufferMinutes));
            }
        }
        catch (Exception ex)
        {
            AppLog.Write($"Companion software issue recording failed for {tracked.Software.DisplayName}: {ex}");
        }
        finally
        {
            tracked.Process.Dispose();
        }
    }

    private void RefreshInstallDiscovery(bool force = false)
    {
        var now = DateTimeOffset.UtcNow;
        if (!force && now - _lastInstallDiscovery < DiscoveryInterval) return;
        _lastInstallDiscovery = now;

        foreach (var software in CompanionSoftwareDefinition.Supported)
        {
            if (!IsInstalled(software)) continue;
            var changed = false;
            lock (_gate)
            {
                changed = _installed.Add(software.Kind);
                _detected.Add(software.Kind);
            }
            if (changed) RaiseStatus(software);
        }
    }

    private static bool IsInstalled(CompanionSoftwareDefinition software)
    {
        if (software.DefaultExecutablePaths.Any(File.Exists)) return true;

        foreach (var (hive, view) in new[]
        {
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32),
            (RegistryHive.CurrentUser, RegistryView.Default)
        })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null) continue;

                foreach (var subKeyName in uninstall.GetSubKeyNames())
                {
                    using var entry = uninstall.OpenSubKey(subKeyName);
                    var displayName = entry?.GetValue("DisplayName")?.ToString();
                    if (software.InstallDisplayNames.Any(name =>
                        displayName?.Contains(name, StringComparison.OrdinalIgnoreCase) == true))
                        return true;
                }
            }
            catch
            {
                // Installation discovery is best effort; process monitoring still detects custom installs.
            }
        }

        return false;
    }

    private static Process? FindProcess(CompanionSoftwareDefinition software)
    {
        var candidates = new List<Process>();
        foreach (var alias in software.ProcessNames)
        {
            try { candidates.AddRange(Process.GetProcessesByName(alias)); }
            catch { }
        }
        if (candidates.Count == 0) return null;
        var selected = candidates.OrderByDescending(SafeStart).First();
        foreach (var process in candidates.Where(process => process.Id != selected.Id)) process.Dispose();
        return selected;
    }

    private void RaiseStatus(CompanionSoftwareDefinition software)
    {
        CompanionSoftwareStatus status;
        lock (_gate)
        {
            status = new CompanionSoftwareStatus(
                software.Kind,
                software.DisplayName,
                _detected.Contains(software.Kind) || _installed.Contains(software.Kind),
                (_tracked.TryGetValue(software.Kind, out var tracked) && !SafeHasExited(tracked.Process))
                || _runningWithoutTracking.Contains(software.Kind));
        }
        StatusChanged?.Invoke(status);
    }

    private static DateTimeOffset SafeStart(Process process)
    {
        try { return process.StartTime; } catch { return DateTimeOffset.Now; }
    }

    private static bool SafeHasExited(Process process)
    {
        try { return process.HasExited; } catch { return true; }
    }

    private static string SafeProcessPath(Process process)
    {
        try { return process.MainModule?.FileName ?? string.Empty; }
        catch { return string.Empty; }
    }

    public void Dispose() => StopTracking();

    private sealed class TrackedCompanion
    {
        public CompanionSoftwareDefinition Software { get; }
        public Process Process { get; }
        public int ProcessId { get; }
        public DateTimeOffset Started { get; }
        public string ProcessPath { get; set; } = string.Empty;
        public int ExitHandled;

        public TrackedCompanion(CompanionSoftwareDefinition software, Process process, DateTimeOffset started)
        {
            Software = software;
            Process = process;
            ProcessId = process.Id;
            Started = started;
        }
    }
}
