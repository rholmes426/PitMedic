using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using PitMedic.Models;

namespace PitMedic.Services;

/// <summary>Reads only visible native loading messages owned by iRacing.</summary>
internal static class IRacingLoadingDialog
{
    private const uint ReadWindowText = 0x000D;
    private const uint AbortIfHung = 0x0002;
    private static readonly string[] ProcessNames = GameDefinition.Supported.Single(game => game.Kind == GameKind.IRacing)
        .ProcessNames.Concat(new[] { "iRacingUI", "iRacingUI64" }).ToArray();

    public static bool IsError63Visible()
    {
        var owners = new HashSet<uint>();
        foreach (var name in ProcessNames)
        {
            Process[] processes;
            try { processes = Process.GetProcessesByName(name); }
            catch (InvalidOperationException) { continue; }
            catch (System.ComponentModel.Win32Exception) { continue; }

            foreach (var process in processes)
            {
                using (process) owners.Add((uint)process.Id);
            }
        }
        if (owners.Count == 0) return false;

        var found = false;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var owner);
            if (!owners.Contains(owner) || !IsWindowVisible(window)) return true;

            var title = new StringBuilder(256);
            GetWindowText(window, title, title.Capacity);
            var caption = title.ToString();
            found = IRacingLiveLogMonitor.IsLoadingError63(caption) || HasMatchingDialogText(window, owner, caption);
            return !found;
        }, 0);
        return found;
    }

    private static bool HasMatchingDialogText(nint window, uint owner, string title)
    {
        var windowClass = new StringBuilder(32);
        GetClassName(window, windowClass, windowClass.Capacity);
        if (windowClass.ToString() != "#32770") return false;

        var labels = new StringBuilder(title);
        var current = new StringBuilder(256);
        var reads = 0;
        var found = false;
        EnumChildWindows(window, (child, parameter) =>
        {
            GetWindowThreadProcessId(child, out var childOwner);
            if (childOwner != owner || !IsWindowVisible(child)) return true;
            windowClass.Clear();
            GetClassName(child, windowClass, windowClass.Capacity);
            if (windowClass.ToString() != "Static") return true;

            current.Clear();
            // WM_GETTEXT on static labels only. A hung dialog cannot stall the monitor.
            if (SendMessageTimeout(child, ReadWindowText, (nuint)current.Capacity, current, AbortIfHung, 35, out _) != 0)
            {
                labels.Append(' ').Append(current);
                found = IRacingLiveLogMonitor.IsLoadingError63(labels.ToString());
            }
            return !found && ++reads < 8;
        }, 0);
        return found;
    }

    private delegate bool WindowCallback(nint window, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(WindowCallback callback, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(nint parent, WindowCallback callback, nint parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint window, StringBuilder text, int capacity);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, StringBuilder text, int capacity);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint SendMessageTimeout(nint window, uint message, nuint wParam, StringBuilder text,
        uint flags, uint timeoutMs, out nuint result);
}
