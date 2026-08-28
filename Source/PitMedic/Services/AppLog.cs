namespace PitMedic.Services;

public static class AppLog
{
    private static readonly object Gate = new();

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.Root);
                File.AppendAllText(AppPaths.AppLog, $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
            }
        }
        catch { }
    }
}
