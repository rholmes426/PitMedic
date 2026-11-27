using System.Diagnostics.Eventing.Reader;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace PitMedic.Services;

[SupportedOSPlatform("windows")]
internal static class WindowsHealthReader
{
    public static IReadOnlyList<WindowsHealthEvent>? ReadEvents()
    {
        var events = new List<WindowsHealthEvent>();
        try
        {
            foreach (var filter in WindowsHealthPolicy.Queries)
            {
                var query = new EventLogQuery("System", PathType.LogName, filter) { ReverseDirection = true };
                using var reader = new EventLogReader(query);
                // Bounded per issue so noisy DNS events do not conceal storage or hardware evidence.
                for (var i = 0; i < 32; i++)
                {
                    using var record = reader.ReadEvent();
                    if (record is null) break;
                    if (record.ProviderName is string provider && record.TimeCreated is DateTime time)
                        events.Add(new(provider, record.Id, record.Level, new DateTimeOffset(time)));
                }
            }
            return events;
        }
        catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException
            or System.Security.SecurityException or TimeoutException)
        {
            return null;
        }
    }

    public static bool? ReadAutomaticAudioServiceStopped()
    {
        try
        {
            using var manager = OpenSCManager(null, null, 0x0001); // SC_MANAGER_CONNECT
            if (manager.IsInvalid) return null;
            var unavailable = false;
            foreach (var name in new[] { "Audiosrv", "AudioEndpointBuilder" })
            {
                using var config = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{name}");
                if (config?.GetValue("Start") is not int start) { unavailable = true; continue; }
                if (start != 2) continue; // SERVICE_AUTO_START, not disabled or manual.
                using var service = OpenService(manager, name, 0x0004); // SERVICE_QUERY_STATUS only
                if (service.IsInvalid || !QueryServiceStatus(service, out var state)) { unavailable = true; continue; }
                if (state.CurrentState == 1) return true; // SERVICE_STOPPED, not a pending transition.
            }
            return unavailable ? null : false;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException
            or IOException)
        {
            return null;
        }
    }

    [DllImport("advapi32.dll", EntryPoint = "OpenSCManagerW", CharSet = CharSet.Unicode)]
    private static extern ServiceHandle OpenSCManager(string? machine, string? database, uint access);

    [DllImport("advapi32.dll", EntryPoint = "OpenServiceW", CharSet = CharSet.Unicode)]
    private static extern ServiceHandle OpenService(ServiceHandle manager, string name, uint access);

    [DllImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatus(ServiceHandle service, out ServiceStatus status);

    [DllImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr handle);

    private sealed class ServiceHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public ServiceHandle() : base(ownsHandle: true) { }
        protected override bool ReleaseHandle() => CloseServiceHandle(handle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public uint ServiceType, CurrentState, ControlsAccepted, Win32ExitCode, ServiceSpecificExitCode, CheckPoint, WaitHint;
    }
}
