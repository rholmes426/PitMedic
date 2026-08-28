using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using PitMedic.Models;
using PitMedic.Services;

namespace PitMedic.RepairHelper;

internal static class Program
{
    private const string RequestFileName = "request.json";
    private const string CancelFileName = "cancel.requested";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
    private static readonly JsonSerializerOptions PipeJsonOptions = new();

    public static async Task<int> Main(string[] args)
    {
        string? requestDirectory = null;
        NamedPipeClientStream? statusPipe = null;
        StreamWriter? statusWriter = null;
        var statusGate = new object();
        var secureLogReady = false;
        var repairStatusId = Guid.Empty;
        var incidentFolder = string.Empty;

        try
        {
            requestDirectory = ParseAndValidateRequestDirectory(args);
            var request = LoadAndValidateRequest(requestDirectory);
            repairStatusId = request.RepairStatusId;
            incidentFolder = request.IncidentFolder;
            ValidateParentProcess(request.ParentProcessId);

            statusPipe = new NamedPipeClientStream(
                ".",
                request.StatusPipeName,
                PipeDirection.Out,
                PipeOptions.Asynchronous);
            await statusPipe.ConnectAsync(15_000);
            statusWriter = new StreamWriter(statusPipe, new UTF8Encoding(false)) { AutoFlush = true };

            EnsureElevatedStorage();
            AppLog.SetProcessLogPath(AppPaths.ElevatedAppLog);
            secureLogReady = true;

            var incident = LoadAndValidateIncident(request.IncidentFolder);

            // The request and incident live in the current user's profile. Treat both as
            // untrusted input: reconstruct the plan from captured diagnostic evidence rather
            // than accepting a serialized repair plan supplied by the unelevated process.
            var plan = RepairPlanner.TryCreateFromIncident(incident with { RecommendedRepair = null })
                ?? throw new InvalidOperationException("The incident no longer has a repair plan.");

            if (!plan.Id.Equals(request.RepairId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The requested repair does not match the incident's current repair plan.");
            if (!ElevatedRepairPolicy.RequiresElevation(plan.Id))
                throw new InvalidOperationException($"Repair '{plan.Id}' is not in the elevated helper allowlist.");

            var settings = new AppSettings { KeepRepairBackups = request.KeepRepairBackups };
            using var service = new RepairService(
                usage: null,
                executeElevatedRepairsLocally: true,
                persistStatus: false,
                repairStatusId: request.RepairStatusId);
            service.StatusChanged += status => WriteStatus(statusWriter!, statusGate, status);

            if (!service.Begin(incident, plan, settings, request.Automatic))
                throw new InvalidOperationException("The elevated repair could not be started.");

            var cancelPath = Path.Combine(requestDirectory, CancelFileName);
            while (service.Current?.IsComplete != true)
            {
                if (File.Exists(cancelPath)) service.Cancel();
                await Task.Delay(200);
            }

            var final = service.Current!;
            return final.Success ? 0 : 1;
        }
        catch (Exception ex)
        {
            if (secureLogReady) AppLog.Write($"Elevated repair helper rejected or failed a request: {ex}");
            if (statusWriter is not null)
            {
                WriteStatus(statusWriter, statusGate, new RepairStatus
                {
                    RepairId = repairStatusId == Guid.Empty ? Guid.NewGuid() : repairStatusId,
                    IncidentFolder = incidentFolder,
                    Title = "Elevated repair",
                    Stage = "Repair needs attention",
                    Message = ex.Message,
                    Detail = "The helper stopped without running any operation outside its allowlist.",
                    Percent = 100,
                    StartedAt = DateTimeOffset.Now,
                    IsActive = false,
                    IsComplete = true,
                    Success = false
                });
            }
            return 2;
        }
        finally
        {
            statusWriter?.Dispose();
            statusPipe?.Dispose();
        }
    }

    private static string ParseAndValidateRequestDirectory(string[] args)
    {
        if (args.Length != 2 || !args[0].Equals("--request-dir", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The helper accepts only --request-dir followed by a PitMedic request directory.");

        var root = Path.GetFullPath(AppPaths.RepairRequests).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(args[1]).TrimEnd(Path.DirectorySeparatorChar);
        if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("The request directory is outside PitMedic's repair-request root.");
        if (!Directory.Exists(candidate))
            throw new DirectoryNotFoundException("The repair-request directory does not exist.");

        return candidate;
    }

    private static ElevatedRepairRequest LoadAndValidateRequest(string requestDirectory)
    {
        var requestPath = Path.Combine(requestDirectory, RequestFileName);
        var request = JsonSerializer.Deserialize<ElevatedRepairRequest>(File.ReadAllText(requestPath), JsonOptions)
            ?? throw new InvalidDataException("The repair request is not valid JSON.");

        if (request.ProtocolVersion != ElevatedRepairRequest.CurrentProtocolVersion)
            throw new InvalidDataException("The repair request uses an unsupported protocol version.");
        if (request.RequestId == Guid.Empty
            || !Path.GetFileName(requestDirectory).Equals(request.RequestId.ToString("N"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The repair request identifier does not match its request directory.");
        if (request.RepairStatusId == Guid.Empty)
            throw new InvalidDataException("The repair request has no valid status identifier.");
        if (request.ParentProcessId <= 0)
            throw new InvalidDataException("The repair request has no valid parent process.");
        if (string.IsNullOrWhiteSpace(request.RepairId))
            throw new InvalidDataException("The repair request has no repair identifier.");
        if (!request.StatusPipeName.Equals($"PitMedic.Repair.{request.RequestId:N}", StringComparison.Ordinal))
            throw new InvalidDataException("The repair request has an invalid status channel.");

        return request;
    }

    private static void ValidateParentProcess(int processId)
    {
        using var parent = Process.GetProcessById(processId);
        var actualPath = parent.MainModule?.FileName;
        var expectedPath = Path.Combine(AppContext.BaseDirectory, "PitMedic.exe");
        if (string.IsNullOrWhiteSpace(actualPath)
            || !Path.GetFullPath(actualPath).Equals(Path.GetFullPath(expectedPath), StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("The repair request did not originate from the installed PitMedic application.");
    }

    private static IncidentRecord LoadAndValidateIncident(string incidentFolder)
    {
        var incidentRoot = Path.GetFullPath(AppPaths.Incidents).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(incidentFolder).TrimEnd(Path.DirectorySeparatorChar);
        if (!candidate.StartsWith(incidentRoot, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("The requested incident is outside PitMedic's incident storage.");

        var incidentPath = Path.Combine(candidate, "incident.json");
        var incident = JsonSerializer.Deserialize<IncidentRecord>(File.ReadAllText(incidentPath), JsonOptions)
            ?? throw new InvalidDataException("The incident record is not valid JSON.");
        if (!Path.GetFullPath(incident.IncidentFolder).TrimEnd(Path.DirectorySeparatorChar)
                .Equals(candidate, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The incident record does not match the requested incident folder.");
        return incident;
    }

    private static void EnsureElevatedStorage()
    {
        RejectReparsePointIfPresent(AppPaths.ElevatedRoot);
        RejectReparsePointIfPresent(AppPaths.ElevatedRepairs);
        Directory.CreateDirectory(AppPaths.ElevatedRepairs);
        RejectReparsePointIfPresent(AppPaths.ElevatedRoot);
        RejectReparsePointIfPresent(AppPaths.ElevatedRepairs);
    }

    private static void RejectReparsePointIfPresent(string path)
    {
        if (!Directory.Exists(path)) return;
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException($"Elevated repair storage cannot use a link or junction: {path}");
    }

    private static void WriteStatus(StreamWriter writer, object gate, RepairStatus status)
    {
        try
        {
            lock (gate) writer.WriteLine(JsonSerializer.Serialize(status, PipeJsonOptions));
        }
        catch { }
    }
}
