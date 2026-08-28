using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using PitMedic.Models;

namespace PitMedic.Services;

public static class ElevatedRepairClient
{
    private const string RequestFileName = "request.json";
    private const string CancelFileName = "cancel.requested";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
    private static readonly JsonSerializerOptions PipeJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task<RepairStatus> RunAsync(
        IncidentRecord incident,
        RepairPlan plan,
        AppSettings settings,
        bool automatic,
        Guid repairStatusId,
        Action<RepairStatus> statusChanged,
        CancellationToken token)
    {
        if (!ElevatedRepairPolicy.RequiresElevation(plan.Id))
            throw new InvalidOperationException($"Repair '{plan.Id}' is not permitted to use the elevated helper.");

        var helperPath = Path.Combine(AppContext.BaseDirectory, "PitMedic.RepairHelper.exe");
        if (!File.Exists(helperPath))
            throw new FileNotFoundException("The PitMedic elevated repair helper is missing. Reinstall PitMedic and try again.", helperPath);

        Directory.CreateDirectory(AppPaths.RepairRequests);
        var requestId = Guid.NewGuid();
        var requestDirectory = Path.Combine(AppPaths.RepairRequests, requestId.ToString("N"));
        var pipeName = $"PitMedic.Repair.{requestId:N}";
        Directory.CreateDirectory(requestDirectory);

        var request = new ElevatedRepairRequest
        {
            RequestId = requestId,
            RepairStatusId = repairStatusId,
            ParentProcessId = Environment.ProcessId,
            StatusPipeName = pipeName,
            IncidentFolder = incident.IncidentFolder,
            RepairId = plan.Id,
            Automatic = automatic,
            KeepRepairBackups = settings.KeepRepairBackups
        };
        File.WriteAllText(
            Path.Combine(requestDirectory, RequestFileName),
            JsonSerializer.Serialize(request, JsonOptions));

        Process? helper = null;
        RepairStatus? lastStatus = null;
        var cancellationWritten = false;
        DateTimeOffset? processExitObserved = null;
        using var statusPipe = new NamedPipeServerStream(
            pipeName,
            PipeDirection.In,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        try
        {
            try
            {
                helper = Process.Start(new ProcessStartInfo
                {
                    FileName = helperPath,
                    Arguments = $"--request-dir {QuoteArgument(requestDirectory)}",
                    WorkingDirectory = AppContext.BaseDirectory,
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                }) ?? throw new InvalidOperationException("Windows did not start the elevated repair helper.");
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                throw new OperationCanceledException("The repair was cancelled at the Windows administrator prompt.", ex);
            }

            var cancelPath = Path.Combine(requestDirectory, CancelFileName);
            var connectionTask = statusPipe.WaitForConnectionAsync(CancellationToken.None);
            while (!connectionTask.IsCompleted)
            {
                WriteCancellationIfRequested(token, cancelPath, ref cancellationWritten);
                ThrowIfHelperExitedWithoutCompletion(helper, lastStatus, ref processExitObserved);
                await Task.WhenAny(connectionTask, Task.Delay(200, CancellationToken.None));
            }
            await connectionTask;

            using var reader = new StreamReader(statusPipe);
            Task<string?>? pendingRead = null;
            while (true)
            {
                WriteCancellationIfRequested(token, cancelPath, ref cancellationWritten);
                pendingRead ??= reader.ReadLineAsync(CancellationToken.None).AsTask();
                if (await Task.WhenAny(pendingRead, Task.Delay(200, CancellationToken.None)) == pendingRead)
                {
                    var json = await pendingRead;
                    pendingRead = null;
                    if (json is null)
                    {
                        ThrowIfHelperExitedWithoutCompletion(helper, lastStatus, ref processExitObserved, force: true);
                        throw new InvalidOperationException("The elevated helper closed its status channel before completing the repair.");
                    }

                    var status = JsonSerializer.Deserialize<RepairStatus>(json, PipeJsonOptions)
                        ?? throw new InvalidDataException("The elevated helper returned invalid repair status.");
                    lastStatus = status;
                    statusChanged(status);
                    if (status.IsComplete) return status;
                }

                ThrowIfHelperExitedWithoutCompletion(helper, lastStatus, ref processExitObserved);
            }
        }
        finally
        {
            helper?.Dispose();
            try { Directory.Delete(requestDirectory, true); } catch { }
        }
    }

    private static void WriteCancellationIfRequested(CancellationToken token, string cancelPath, ref bool written)
    {
        if (!token.IsCancellationRequested || written) return;
        File.WriteAllText(cancelPath, DateTimeOffset.UtcNow.ToString("O"));
        written = true;
    }

    private static void ThrowIfHelperExitedWithoutCompletion(
        Process helper,
        RepairStatus? lastStatus,
        ref DateTimeOffset? processExitObserved,
        bool force = false)
    {
        if (!helper.HasExited) return;
        processExitObserved ??= DateTimeOffset.UtcNow;
        if (!force && DateTimeOffset.UtcNow - processExitObserved <= TimeSpan.FromSeconds(3)) return;

        var detail = lastStatus is null
            ? $"The helper exited with code {helper.ExitCode} before returning repair status."
            : $"The helper exited with code {helper.ExitCode} before reporting completion.";
        throw new InvalidOperationException(detail);
    }

    private static string QuoteArgument(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
}
