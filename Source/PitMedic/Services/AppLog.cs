namespace PitMedic.Services;

public static class AppLog
{
    private static readonly object Gate = new();
    private static string? _processLogPath;

    public static void SetProcessLogPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A log path is required.", nameof(path));
        lock (Gate) _processLogPath = Path.GetFullPath(path);
    }

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                var path = _processLogPath ?? AppPaths.AppLog;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
            }
        }
        catch { }
    }
}
