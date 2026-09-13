using PitMedic.Services;

internal static class SteamValidationWindowTests
{
    private static readonly SteamWindow Library = new(new IntPtr(1), 100);
    private static readonly SteamWindow Validation = new(new IntPtr(2), 101);

    internal static void Run()
    {
        InitialWindowsAreHiddenAndRestored();
        ReopenedWindowEndsAllHiding();
        UserInputPreservesNewWindows();
        StartupEndsWithoutUserInput();
        CompletedStartupCannotHideAgain();
        CancellationLeavesNoWindowWorker();
        LaterUserChoicesArePreserved();
        SessionsDoNotShareWindowState();
        Console.WriteLine("Steam window control regression tests passed (8 scenarios).");
    }

    private static void InitialWindowsAreHiddenAndRestored()
    {
        var windows = new FakeWindows(Library);
        var session = new SteamValidationWindowSession(windows, () => TimeSpan.Zero);
        session.HideStartupWindows(default);
        Check(!windows.Visible.Contains(Library), "Steam must be hidden initially.");
        windows.Visible.Add(Validation);
        session.HideStartupWindows(default);
        Check(!windows.Visible.Contains(Validation), "A delayed validation window must be hidden once during startup.");
        session.CompleteStartup();
        session.Dispose();
        session.Dispose();
        Check(windows.Visible.SetEquals(new[] { Library }), "Only originally visible windows should be restored.");
        Check(windows.RestoreCalls == 1, "Cleanup must be idempotent.");
    }

    private static void ReopenedWindowEndsAllHiding()
    {
        var windows = new FakeWindows(Library);
        using var session = new SteamValidationWindowSession(windows, () => TimeSpan.Zero);
        session.HideStartupWindows(default);
        // Simulate opening Steam without a reported input event, with a new window
        // enumerated first. No window from this point onward should be hidden.
        windows.Visible.Add(Validation);
        windows.Visible.Add(Library);
        Check(!session.HideStartupWindows(default), "Reopening Steam must end startup hiding immediately.");
        Check(windows.Visible.SetEquals(new[] { Library, Validation }), "Both reopened and new windows must stay visible.");
        windows.Visible.Remove(Library);
        Check(!session.HideStartupWindows(default), "Hiding must not resume after the reopened window closes.");
        Check(windows.HideCalls == 1, "A reopened Steam window must never be hidden twice.");
    }

    private static void UserInputPreservesNewWindows()
    {
        foreach (uint? newInput in new uint?[] { 101, 99, 0, null })
        {
            var windows = new FakeWindows(Library);
            var session = new SteamValidationWindowSession(windows, () => TimeSpan.Zero);
            session.HideStartupWindows(default);
            windows.Input = newInput;
            windows.Visible.Add(Validation);
            Check(!session.HideStartupWindows(default), "Any changed or unavailable input timestamp must give the user control.");
            Check(windows.Visible.Contains(Validation), "A new Steam window opened by the user must stay visible.");
            windows.Input = 100;
            Check(!session.HideStartupWindows(default), "Once given to the user, window control must not be reclaimed.");
            session.Dispose();
            Check(windows.RestoreCalls == 0, "Cleanup must not override the user's window choices.");
        }

        var unavailable = new FakeWindows(Library) { Input = null };
        using var unknownInput = new SteamValidationWindowSession(unavailable, () => TimeSpan.Zero);
        Check(!unknownInput.HideStartupWindows(default) && unavailable.HideCalls == 0,
            "If input tracking is unavailable initially, Steam must remain accessible.");

        var moving = new FakeWindows(Library);
        using var duringCapture = new SteamValidationWindowSession(moving, () => TimeSpan.Zero);
        moving.OnCapture = () => moving.Input++;
        Check(!duringCapture.HideStartupWindows(default) && moving.HideCalls == 0,
            "Input arriving during enumeration must be checked before hiding a window.");
    }

    private static void StartupEndsWithoutUserInput()
    {
        var elapsed = TimeSpan.Zero;
        var windows = new FakeWindows(Library);
        using var session = new SteamValidationWindowSession(windows, () => elapsed);
        session.HideStartupWindows(default);
        elapsed = SteamValidationWindowSession.StartupDuration;
        windows.Visible.Add(Validation);
        Check(!session.HideStartupWindows(default), "Startup hiding must expire even when the user provides no input.");
        elapsed = TimeSpan.FromMinutes(35);
        windows.Visible.Add(Library);
        Check(!session.HideStartupWindows(default), "A long-running repair must never resume hiding Steam.");
        Check(windows.HideCalls == 1 && windows.Visible.Count == 2, "Steam must remain usable throughout a long repair.");
    }

    private static void CompletedStartupCannotHideAgain()
    {
        var windows = new FakeWindows(Library);
        var session = new SteamValidationWindowSession(windows, () => TimeSpan.Zero);
        session.HideStartupWindows(default);
        session.CompleteStartup();
        windows.Visible.Add(Validation);
        Check(!session.HideStartupWindows(default), "Completing launch must permanently disable hiding.");
        session.Dispose();
        Check(!session.HideStartupWindows(default) && windows.HideCalls == 1, "A disposed session must not alter visibility.");
    }

    private static void CancellationLeavesNoWindowWorker()
    {
        var windows = new FakeWindows(Library);
        var session = new SteamValidationWindowSession(windows, () => TimeSpan.Zero);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var canceled = false;
        try { session.HideStartupWindows(cts.Token); }
        catch (OperationCanceledException) { canceled = true; }
        Check(canceled && windows.HideCalls == 0, "Cancellation before launch must not hide Steam.");
        session.HideStartupWindows(default);
        session.Dispose(); // The same cleanup used after launch failures or cancellation.
        Check(windows.Visible.Contains(Library), "Launch failure or cancellation must restore an untouched original window.");
        windows.Visible.Add(Validation);
        Check(!session.HideStartupWindows(default), "Cancellation cleanup must leave no active hiding session.");
    }

    private static void LaterUserChoicesArePreserved()
    {
        var windows = new FakeWindows(Library);
        var session = new SteamValidationWindowSession(windows, () => TimeSpan.Zero);
        session.HideStartupWindows(default);
        session.CompleteStartup();
        // The user opens and then closes Steam later in the repair.
        windows.Input++;
        session.Dispose();
        Check(windows.RestoreCalls == 0, "Repair completion must not reopen Steam after the user has chosen to close it.");
    }

    private static void SessionsDoNotShareWindowState()
    {
        var windows = new FakeWindows(Library);
        using (var first = new SteamValidationWindowSession(windows, () => TimeSpan.Zero))
            first.HideStartupWindows(default);
        using var second = new SteamValidationWindowSession(windows, () => TimeSpan.Zero);
        Check(second.HideStartupWindows(default) && windows.HideCalls == 2,
            "A separate repair must still perform its own initial hide.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class FakeWindows(params SteamWindow[] visible) : ISteamWindowAccess
    {
        internal uint? Input { get; set; } = 100;
        internal HashSet<SteamWindow> Visible { get; } = new(visible);
        internal int HideCalls { get; private set; }
        internal int RestoreCalls { get; private set; }
        internal Action? OnCapture { get; set; }
        public uint? GetLastInputTime() => Input;
        public IReadOnlyList<SteamWindow> CaptureVisibleWindows()
        {
            OnCapture?.Invoke();
            return Visible.ToArray();
        }
        public void Hide(SteamWindow window) { HideCalls++; Visible.Remove(window); }
        public void Restore(SteamWindow window) { RestoreCalls++; Visible.Add(window); }
    }
}
