using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PitMedic.Services;

public static class SteamClientService
{
    private const int SwHide = 0;
    private const int SwShow = 5;

    public static async Task<IDisposable> StartValidationAsync(string appId, CancellationToken token)
    {
        // Begin suppressing Steam's top-level windows BEFORE invoking the validation URI. Modern
        // Steam renders much of its UI in steamwebhelper.exe, so watching only steam.exe is not enough.
        var suppression = new SteamUiSuppressionSession(token);
        try
        {
            var steamExe = FindSteamExe();
            if (!string.IsNullOrWhiteSpace(steamExe) && File.Exists(steamExe))
            {
                var info = new ProcessStartInfo
                {
                    FileName = steamExe,
                    Arguments = $"-silent \"steam://validate/{appId}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = Path.GetDirectoryName(steamExe) ?? string.Empty
                };
                var process = Process.Start(info);
                if (process is null) throw new InvalidOperationException("Steam could not be started for validation.");
            }
            else
            {
                var process = Process.Start(new ProcessStartInfo($"steam://validate/{appId}")
                {
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
                if (process is null) throw new InvalidOperationException("Steam could not be opened to start validation.");
            }

            await suppression.PulseAsync(token);
            await Task.Delay(900, token);
            return suppression;
        }
        catch
        {
            suppression.Dispose();
            throw;
        }
    }

    private static string? FindSteamExe()
    {
        foreach (var process in Process.GetProcessesByName("steam"))
        {
            try
            {
                var path = process.MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) return path;
            }
            catch { }
            finally { process.Dispose(); }
        }

        var x86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var x64 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        foreach (var candidate in new[] { Path.Combine(x86, "Steam", "steam.exe"), Path.Combine(x64, "Steam", "steam.exe") })
            if (File.Exists(candidate)) return candidate;
        return null;
    }

    private sealed class SteamUiSuppressionSession : IDisposable
    {
        private readonly CancellationTokenSource _cts;
        private readonly Task _worker;
        private readonly HashSet<IntPtr> _originallyVisible;
        private int _disposed;

        public SteamUiSuppressionSession(CancellationToken repairToken)
        {
            _originallyVisible = CaptureVisibleSteamWindows();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(repairToken);
            HideAllSteamWindows();
            _worker = Task.Run(() => SuppressLoopAsync(_cts.Token), CancellationToken.None);
        }

        public async Task PulseAsync(CancellationToken token)
        {
            // Steam can create its Chromium window a moment after receiving the URI. Pulse for the
            // first few seconds so the repair UI remains the user's visible foreground experience.
            for (var i = 0; i < 20; i++)
            {
                token.ThrowIfCancellationRequested();
                HideAllSteamWindows();
                await Task.Delay(100, token);
            }
        }

        private static async Task SuppressLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                HideAllSteamWindows();
                try { await Task.Delay(250, token); }
                catch (OperationCanceledException) { break; }
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try { _cts.Cancel(); } catch { }
            try { _worker.Wait(TimeSpan.FromSeconds(1)); } catch { }

            // If the user had Steam visible before PitMedic began the repair, restore only those
            // original windows. Newly created validation windows stay hidden.
            foreach (var handle in _originallyVisible)
            {
                try { if (IsWindow(handle)) ShowWindow(handle, SwShow); } catch { }
            }
            _cts.Dispose();
        }
    }

    private static HashSet<IntPtr> CaptureVisibleSteamWindows()
    {
        var handles = new HashSet<IntPtr>();
        var pids = GetSteamUiProcessIds();
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true;
            GetWindowThreadProcessId(hWnd, out var pid);
            if (pids.Contains((int)pid)) handles.Add(hWnd);
            return true;
        }, IntPtr.Zero);
        return handles;
    }

    private static void HideAllSteamWindows()
    {
        var pids = GetSteamUiProcessIds();
        if (pids.Count == 0) return;
        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out var pid);
            if (pids.Contains((int)pid) && IsWindowVisible(hWnd))
                ShowWindow(hWnd, SwHide);
            return true;
        }, IntPtr.Zero);
    }

    private static HashSet<int> GetSteamUiProcessIds()
    {
        var ids = new HashSet<int>();
        foreach (var name in new[] { "steam", "steamwebhelper" })
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                try { ids.Add(process.Id); }
                catch { }
                finally { process.Dispose(); }
            }
        }
        return ids;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);
}
