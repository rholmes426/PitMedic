using System.Runtime.InteropServices;

namespace PitMedic.Services;

/// <summary>Optional, read-only NVML clock-event queries from the installed NVIDIA driver.</summary>
internal sealed class NvidiaThermalMonitor : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Initialize();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Shutdown();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DeviceCount(out uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DeviceByIndex(uint index, out IntPtr device);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ClockReasons(IntPtr device, out ulong reasons);

    private IntPtr _library;
    private Shutdown? _shutdown;
    private DeviceCount? _count;
    private DeviceByIndex? _device;
    private ClockReasons? _supported;
    private ClockReasons? _current;
    private bool _attempted;
    private bool _initialized;

    public bool? Read()
    {
        if (!_attempted) Open();
        if (!_initialized || _count is null || _device is null || _supported is null || _current is null)
            return null;

        if (_count(out var count) != 0 || count == 0 || count > 64) return null;
        var complete = true;
        for (uint index = 0; index < count; index++)
        {
            if (_device(index, out var device) != 0
                || _supported(device, out var supported) != 0
                || _current(device, out var current) != 0)
            {
                complete = false;
                continue;
            }

            var limited = SessionThermalMonitor.NvidiaThermalLimit(supported, current);
            if (limited == true) return true;
            if (!limited.HasValue) complete = false;
        }
        return complete ? false : null;
    }

    private void Open()
    {
        _attempted = true;
        // Use only NVIDIA's documented driver installation locations. Avoid
        // DLL search through the simulator, working directory or PATH.
        var paths = new[]
        {
            Path.Combine(Environment.SystemDirectory, "nvml.dll"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvml.dll")
        };

        try
        {
            foreach (var path in paths)
                if (File.Exists(path) && NativeLibrary.TryLoad(path, out _library)) break;
            if (_library == IntPtr.Zero) return;

            var initialize = Export<Initialize>("nvmlInit_v2");
            _shutdown = Export<Shutdown>("nvmlShutdown");
            _count = Export<DeviceCount>("nvmlDeviceGetCount_v2");
            _device = Export<DeviceByIndex>("nvmlDeviceGetHandleByIndex_v2");
            _supported = Export<ClockReasons>("nvmlDeviceGetSupportedClocksEventReasons")
                ?? Export<ClockReasons>("nvmlDeviceGetSupportedClocksThrottleReasons");
            _current = Export<ClockReasons>("nvmlDeviceGetCurrentClocksEventReasons")
                ?? Export<ClockReasons>("nvmlDeviceGetCurrentClocksThrottleReasons");
            if (initialize is not null && _shutdown is not null && _count is not null && _device is not null
                && _supported is not null && _current is not null && initialize() == 0)
                _initialized = true;
        }
        catch (Exception ex)
        {
            AppLog.Write($"NVIDIA thermal telemetry unavailable: {ex.GetType().Name}");
        }
        if (!_initialized) Dispose();
    }

    private T? Export<T>(string name) where T : Delegate =>
        NativeLibrary.TryGetExport(_library, name, out var address)
            ? Marshal.GetDelegateForFunctionPointer<T>(address)
            : null;

    public void Dispose()
    {
        if (_initialized) _shutdown?.Invoke();
        _initialized = false;
        if (_library == IntPtr.Zero) return;
        NativeLibrary.Free(_library);
        _library = IntPtr.Zero;
    }
}
