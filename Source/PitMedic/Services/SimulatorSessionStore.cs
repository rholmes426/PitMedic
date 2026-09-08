using System.Text.Json;
using PitMedic.Models;

namespace PitMedic.Services;

public sealed class SimulatorSessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _gate = new();
    private readonly string _path;
    private Dictionary<GameKind, SimulatorSessionSummary> _sessions;

    public SimulatorSessionStore(string? path = null)
    {
        _path = path ?? AppPaths.LastSessionsFile;
        _sessions = Load();
    }

    public SimulatorSessionSummary? Get(GameKind game)
    {
        lock (_gate) return _sessions.GetValueOrDefault(game);
    }

    public void Save(SimulatorSessionSummary session)
    {
        lock (_gate)
        {
            _sessions[session.Game] = session;
            try
            {
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                var temporaryPath = _path + ".tmp";
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(_sessions, JsonOptions));
                File.Move(temporaryPath, _path, true);
            }
            catch (Exception ex)
            {
                AppLog.Write($"Could not save simulator session summary: {ex.Message}");
            }
        }
    }

    private Dictionary<GameKind, SimulatorSessionSummary> Load()
    {
        try
        {
            if (!File.Exists(_path)) return new();
            return JsonSerializer.Deserialize<Dictionary<GameKind, SimulatorSessionSummary>>(File.ReadAllText(_path))
                ?? new();
        }
        catch (Exception ex)
        {
            AppLog.Write($"Could not load simulator session summaries: {ex.Message}");
            return new();
        }
    }
}
