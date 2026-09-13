using System.Diagnostics;

namespace PitMedic.Services;

internal readonly record struct SteamWindow(IntPtr Handle, uint ProcessId);

internal interface ISteamWindowAccess
{
    uint? GetLastInputTime();
    IReadOnlyList<SteamWindow> CaptureVisibleWindows();
    void Hide(SteamWindow window);
    void Restore(SteamWindow window);
}

// Window management is limited to launch. The repair can keep this scope alive
// to restore previously visible windows, but it never starts a background worker.
internal sealed class SteamValidationWindowSession : IDisposable
{
    internal static readonly TimeSpan StartupDuration = TimeSpan.FromSeconds(2);
    private readonly ISteamWindowAccess _windows;
    private readonly Func<TimeSpan> _elapsed;
    private readonly uint? _inputAtStart;
    private readonly IReadOnlyList<SteamWindow> _originallyVisible;
    private readonly HashSet<SteamWindow> _hidden = new();
    private bool _startupComplete;
    private bool _userHasControl;
    private bool _disposed;

    internal SteamValidationWindowSession(ISteamWindowAccess windows, Func<TimeSpan>? elapsed = null)
    {
        _windows = windows;
        var started = Stopwatch.GetTimestamp();
        _elapsed = elapsed ?? (() => Stopwatch.GetElapsedTime(started));
        _inputAtStart = windows.GetLastInputTime();
        _originallyVisible = windows.CaptureVisibleWindows();
    }

    internal bool HideStartupWindows(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_disposed || _startupComplete) return false;
        if (_elapsed() >= StartupDuration)
        {
            CompleteStartup();
            return false;
        }
        if (UserHasControl()) return false;

        var visible = _windows.CaptureVisibleWindows();
        // A window shown again after our initial hide belongs to the user now,
        // even if no input event was reported (for example, an accessibility tool).
        if (visible.Any(_hidden.Contains))
        {
            _userHasControl = true;
            return false;
        }

        foreach (var window in visible)
        {
            token.ThrowIfCancellationRequested();
            if (UserHasControl()) return false;
            if (_hidden.Add(window)) _windows.Hide(window);
        }
        return true;
    }

    internal void CompleteStartup() => _startupComplete = true;

    private bool UserHasControl()
    {
        // Any new input ends hiding, including input outside Steam. Input timestamps
        // may wrap or move backwards; compare identity rather than ordering. If
        // Windows cannot report input, leave window visibility under user control.
        if (!_inputAtStart.HasValue || _windows.GetLastInputTime() != _inputAtStart)
            _userHasControl = true;
        return _userHasControl;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CompleteStartup();
        if (UserHasControl()) return;

        foreach (var window in _originallyVisible)
        {
            if (!_hidden.Contains(window)) continue;
            try { _windows.Restore(window); } catch { }
        }
    }
}
