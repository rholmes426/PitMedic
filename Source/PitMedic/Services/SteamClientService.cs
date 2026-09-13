using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PitMedic.Services;

public static class SteamClientService
{
    private const int SwHide = 0;
    private const int SwShowNoActivate = 8;

    public static async Task<IDisposable> StartValidationAsync(string appId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var windowSession = new SteamValidationWindowSession(new NativeSteamWindowAccess());
        try
        {
            windowSession.HideStartupWindows(token);
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
                using var process = Process.Start(info);
                if (process is null) throw new InvalidOperationException("Steam could not be started for validation.");
            }
            else
            {
                using var process = Process.Start(new ProcessStartInfo($"steam://validate/{appId}")
                {
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
                if (process is null) throw new InvalidOperationException("Steam could not be opened to start validation.");
            }

            // Steam can create its Chromium UI shortly after receiving the URI.
            // Only handle the launch interval, never the rest of the repair.
            try
            {
                while (windowSession.HideStartupWindows(token))
                    await Task.Delay(100, token);
            }
            finally { windowSession.CompleteStartup(); }
            await Task.Delay(900, token);
            return windowSession;
        }
        catch
        {
            windowSession.Dispose();
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

    private sealed class NativeSteamWindowAccess : ISteamWindowAccess
    {
        public uint? GetLastInputTime()
        {
            var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
            return GetLastInputInfo(ref info) ? info.Time : null;
        }

        public IReadOnlyList<SteamWindow> CaptureVisibleWindows()
        {
            var windows = new List<SteamWindow>();
            var pids = GetSteamUiProcessIds();
            EnumWindows((hWnd, _) =>
            {
                if (!IsWindowVisible(hWnd)) return true;
                GetWindowThreadProcessId(hWnd, out var pid);
                if (pids.Contains((int)pid)) windows.Add(new SteamWindow(hWnd, pid));
                return true;
            }, IntPtr.Zero);
            return windows;
        }

        public void Hide(SteamWindow window)
        {
            if (StillBelongsToProcess(window) && IsWindowVisible(window.Handle))
                ShowWindow(window.Handle, SwHide);
        }

        public void Restore(SteamWindow window)
        {
            // Do not activate Steam or act on a recycled handle from another process.
            if (StillBelongsToProcess(window) && !IsWindowVisible(window.Handle)
                && GetSteamUiProcessIds().Contains((int)window.ProcessId))
                ShowWindow(window.Handle, SwShowNoActivate);
        }

        private static bool StillBelongsToProcess(SteamWindow window)
        {
            if (!IsWindow(window.Handle)) return false;
            GetWindowThreadProcessId(window.Handle, out var pid);
            return pid == window.ProcessId;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

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
