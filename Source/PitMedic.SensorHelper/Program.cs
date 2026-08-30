using System.Runtime.InteropServices;
using System.Text.Json;
using LibreHardwareMonitor.Hardware;

namespace PitMedic.SensorHelper;

internal static class Program
{
    private const string ServiceName = "PitMedicSensor";
    private const uint ServiceWin32OwnProcess = 0x00000010;
    private const uint ServiceStopped = 0x00000001;
    private const uint ServiceStartPending = 0x00000002;
    private const uint ServiceStopPending = 0x00000003;
    private const uint ServiceRunning = 0x00000004;
    private const uint ServiceAcceptStop = 0x00000001;
    private const uint ServiceAcceptShutdown = 0x00000004;
    private const uint ServiceControlStop = 0x00000001;
    private const uint ServiceControlInterrogate = 0x00000004;
    private const uint ServiceControlShutdown = 0x00000005;

    private static readonly ServiceMainDelegate ServiceMainCallback = ServiceMain;
    private static readonly ServiceControlHandlerExDelegate ControlHandlerCallback = ServiceControlHandler;
    private static CancellationTokenSource? _stopSource;
    private static IntPtr _statusHandle;
    private static ServiceStatus _status;

    private static int Main(string[] args)
    {
        if (args.Length != 0)
            return 2;

        var table = new[]
        {
            new ServiceTableEntry { ServiceName = ServiceName, ServiceMain = ServiceMainCallback },
            new ServiceTableEntry()
        };

        return StartServiceCtrlDispatcher(table) ? 0 : Marshal.GetLastWin32Error();
    }

    private static void ServiceMain(uint argumentCount, IntPtr arguments)
    {
        _statusHandle = RegisterServiceCtrlHandlerEx(ServiceName, ControlHandlerCallback, IntPtr.Zero);
        if (_statusHandle == IntPtr.Zero) return;

        _stopSource = new CancellationTokenSource();
        ReportStatus(ServiceStartPending, waitHint: 5_000);
        ReportStatus(ServiceRunning);

        try
        {
            RunSensorLoop(_stopSource.Token);
            ReportStatus(ServiceStopped);
        }
        catch
        {
            ReportStatus(ServiceStopped, win32ExitCode: 1);
        }
        finally
        {
            _stopSource.Dispose();
            _stopSource = null;
        }
    }

    private static uint ServiceControlHandler(uint control, uint eventType, IntPtr eventData, IntPtr context)
    {
        if (control is ServiceControlStop or ServiceControlShutdown)
        {
            ReportStatus(ServiceStopPending, waitHint: 5_000);
            _stopSource?.Cancel();
        }
        else if (control == ServiceControlInterrogate)
        {
            SetServiceStatus(_statusHandle, ref _status);
        }

        return 0;
    }

    private static void RunSensorLoop(CancellationToken token)
    {
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "PitMedic");
        var sensorPath = Path.Combine(dataDirectory, "sensor.json");
        var temporaryPath = Path.Combine(dataDirectory, $"sensor-{Environment.ProcessId}.tmp");
        Directory.CreateDirectory(dataDirectory);

        Computer? computer = null;
        string? startupError = null;
        var nextOpenAttempt = DateTimeOffset.MinValue;

        try
        {
            var visitor = new UpdateVisitor();
            while (!token.IsCancellationRequested)
            {
                var now = DateTimeOffset.UtcNow;
                if (computer is null && now >= nextOpenAttempt)
                {
                    computer = TryOpenComputer(out startupError);
                    nextOpenAttempt = now.AddSeconds(30);
                }

                SensorMessage message;
                try
                {
                    if (computer is null)
                    {
                        message = new SensorMessage
                        {
                            Timestamp = DateTimeOffset.UtcNow,
                            Error = startupError ?? "Hardware sensors are unavailable"
                        };
                    }
                    else
                    {
                        computer.Accept(visitor);
                        var all = Flatten(computer.Hardware).ToList();
                        var cpu = all.Where(h => h.HardwareType == HardwareType.Cpu).ToList();
                        message = new SensorMessage
                        {
                            Timestamp = DateTimeOffset.UtcNow,
                            CpuTempC = BestCpuTemp(cpu, all),
                            CpuLoadPct = Named(cpu, SensorType.Load, "CPU Total") ?? Max(cpu, SensorType.Load),
                            CpuClockMhz = Max(cpu, SensorType.Clock),
                            CpuPowerW = Named(cpu, SensorType.Power, "CPU Package") ?? Max(cpu, SensorType.Power)
                        };
                    }
                }
                catch (Exception ex)
                {
                    message = new SensorMessage
                    {
                        Timestamp = DateTimeOffset.UtcNow,
                        Error = $"{ex.GetType().Name}: {ex.Message}"
                    };
                }

                TryPublishSensorMessage(temporaryPath, sensorPath, message);
                token.WaitHandle.WaitOne(TimeSpan.FromSeconds(2));
            }
        }
        finally
        {
            TryCloseComputer(computer);
            TryDelete(temporaryPath);
            TryDelete(sensorPath);
        }
    }

    private static Computer? TryOpenComputer(out string? error)
    {
        var computer = new Computer
        {
            IsCpuEnabled = true,
            IsMotherboardEnabled = true
        };

        try
        {
            computer.Open();
            error = null;
            return computer;
        }
        catch (Exception ex)
        {
            TryCloseComputer(computer);
            error = $"{ex.GetType().Name}: {ex.Message}";
            return null;
        }
    }

    private static void TryPublishSensorMessage(string temporaryPath, string sensorPath, SensorMessage message)
    {
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(message));
            File.Move(temporaryPath, sensorPath, overwrite: true);
        }
        catch (IOException)
        {
            TryDelete(temporaryPath);
        }
        catch (UnauthorizedAccessException)
        {
            TryDelete(temporaryPath);
        }
    }

    private static void TryCloseComputer(Computer? computer)
    {
        try { computer?.Close(); }
        catch { }
    }

    private static void ReportStatus(uint currentState, uint waitHint = 0, uint win32ExitCode = 0)
    {
        if (_statusHandle == IntPtr.Zero) return;
        _status = new ServiceStatus
        {
            ServiceType = ServiceWin32OwnProcess,
            CurrentState = currentState,
            ControlsAccepted = currentState == ServiceRunning ? ServiceAcceptStop | ServiceAcceptShutdown : 0,
            Win32ExitCode = win32ExitCode,
            WaitHint = waitHint
        };
        SetServiceStatus(_statusHandle, ref _status);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }

    private static IEnumerable<IHardware> Flatten(IEnumerable<IHardware> hardware)
    {
        foreach (var item in hardware)
        {
            yield return item;
            foreach (var sub in Flatten(item.SubHardware)) yield return sub;
        }
    }

    private static IEnumerable<ISensor> Sensors(IEnumerable<IHardware> hardware, SensorType type) =>
        hardware.SelectMany(h => h.Sensors).Where(s => s.SensorType == type && s.Value.HasValue);

    private static float? Named(IEnumerable<IHardware> hardware, SensorType type, string name) =>
        Sensors(hardware, type).FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;

    private static float? Max(IEnumerable<IHardware> hardware, SensorType type)
    {
        var values = Sensors(hardware, type).Select(s => s.Value!.Value).ToArray();
        return values.Length == 0 ? null : values.Max();
    }

    private static float? BestCpuTemp(IEnumerable<IHardware> cpu, IEnumerable<IHardware> all)
    {
        var cpuTemps = Sensors(cpu, SensorType.Temperature).ToList();
        var preferredNames = new[] { "CPU Package", "Package", "Tctl/Tdie", "Tctl", "Tdie", "Core Max", "Core Average" };
        foreach (var preferred in preferredNames)
        {
            var sensor = cpuTemps.FirstOrDefault(s => s.Name.Equals(preferred, StringComparison.OrdinalIgnoreCase))
                      ?? cpuTemps.FirstOrDefault(s => s.Name.Contains(preferred, StringComparison.OrdinalIgnoreCase));
            if (sensor?.Value is float value) return value;
        }
        if (cpuTemps.Count > 0) return cpuTemps.Max(s => s.Value!.Value);

        return Sensors(all, SensorType.Temperature)
            .Where(s => s.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase)
                     || s.Name.Contains("Tctl", StringComparison.OrdinalIgnoreCase)
                     || s.Name.Contains("Tdie", StringComparison.OrdinalIgnoreCase)
                     || s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => ScoreCpuTemperatureName(s.Name))
            .FirstOrDefault()?.Value;
    }

    private static int ScoreCpuTemperatureName(string name)
    {
        if (name.Contains("CPU Package", StringComparison.OrdinalIgnoreCase)) return 100;
        if (name.Contains("Tctl", StringComparison.OrdinalIgnoreCase) || name.Contains("Tdie", StringComparison.OrdinalIgnoreCase)) return 90;
        if (name.Contains("CPU", StringComparison.OrdinalIgnoreCase)) return 80;
        if (name.Contains("Package", StringComparison.OrdinalIgnoreCase)) return 60;
        return 0;
    }

    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);
        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (var subHardware in hardware.SubHardware) subHardware.Accept(this);
        }
        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }
    }

    private sealed record SensorMessage
    {
        public DateTimeOffset Timestamp { get; init; }
        public float? CpuTempC { get; init; }
        public float? CpuLoadPct { get; init; }
        public float? CpuClockMhz { get; init; }
        public float? CpuPowerW { get; init; }
        public string? Error { get; init; }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ServiceTableEntry
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string? ServiceName;
        public ServiceMainDelegate? ServiceMain;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
    }

    private delegate void ServiceMainDelegate(uint argumentCount, IntPtr arguments);
    private delegate uint ServiceControlHandlerExDelegate(uint control, uint eventType, IntPtr eventData, IntPtr context);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StartServiceCtrlDispatcher([In] ServiceTableEntry[] serviceTable);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr RegisterServiceCtrlHandlerEx(
        string serviceName,
        ServiceControlHandlerExDelegate handler,
        IntPtr context);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetServiceStatus(IntPtr serviceStatusHandle, ref ServiceStatus serviceStatus);
}
