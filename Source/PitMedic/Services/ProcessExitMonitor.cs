using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PitMedic.Services;

// Process.HasExited can cache an incorrect exit indication after an inaccessible handle.
// Keep our own SYNCHRONIZE handle and require a signaled process object before reading
// its exit code. Neither STILL_ACTIVE nor a failed query is proof of termination.
public sealed class ProcessExitMonitor : IDisposable
{
    private readonly object _gate = new();
    private readonly int _pid;
    private readonly DateTime? _started;
    private readonly SafeWaitHandle? _handle;

    public ProcessExitMonitor(Process process)
    {
        _pid = process.Id;
        try { _started = process.StartTime; } catch { }
        if (OperatingSystem.IsWindows())
            _handle = OpenProcess(0x00100000 | 0x00001000, false, _pid);
    }

    public bool TryGetExitCode(out int? exitCode)
    {
        lock (_gate) return ReadExitCode(out exitCode);
    }

    private bool ReadExitCode(out int? exitCode)
    {
        exitCode = null;
        if (_handle is { IsInvalid: false, IsClosed: false })
        {
            var wait = WaitForSingleObject(_handle, 0);
            if (wait != 0) return false; // WAIT_TIMEOUT or WAIT_FAILED: keep tracking.
            if (GetExitCodeProcess(_handle, out var code)) exitCode = unchecked((int)code);
            return true; // A real process is allowed to terminate with code 259.
        }

        // Access denied is unknown, not exited. A fresh process object avoids cached
        // HasExited state. PID disappearance or reuse confirms the old session ended.
        try
        {
            using var current = Process.GetProcessById(_pid);
            if (_started.HasValue && current.StartTime != _started.Value) return true;
            if (!OperatingSystem.IsWindows() && current.HasExited)
            {
                exitCode = current.ExitCode;
                return true;
            }
            return false;
        }
        catch (ArgumentException) { return true; } // PID no longer exists.
        catch { return false; }
    }

    public void Dispose() { lock (_gate) _handle?.Dispose(); }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeWaitHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(SafeWaitHandle handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(SafeWaitHandle handle, out uint exitCode);
}
