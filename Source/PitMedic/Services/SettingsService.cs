using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32;
using PitMedic.Models;

namespace PitMedic.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _gate = new();
    private AppSettings _current;

    public event Action<AppSettings>? SettingsChanged;

    public SettingsService()
    {
        _current = Load();
    }

    public AppSettings Current
    {
        get
        {
            lock (_gate) return Clone(_current);
        }
    }

    public void Save(AppSettings settings)
    {
        Normalize(settings);
        bool startupChanged;
        lock (_gate)
        {
            startupChanged = _current.StartWithWindows != settings.StartWithWindows;
            _current = Clone(settings);
        }
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(settings, JsonOptions));
        if (startupChanged) ApplyStartup(settings.StartWithWindows);
        SettingsChanged?.Invoke(Clone(settings));
    }

    public void RefreshStartupRegistration() => ApplyStartup(Current.StartWithWindows);

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var value = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile), JsonOptions);
                if (value is not null)
                {
                    Normalize(value);
                    return value;
                }
            }
        }
        catch { }
        return new AppSettings();
    }

    private static void Normalize(AppSettings settings)
    {
        settings.SamplingSeconds = settings.SamplingSeconds is 1 or 2 or 5 ? settings.SamplingSeconds : 1;
        settings.BufferMinutes = settings.BufferMinutes is 5 or 10 or 15 or 30 ? settings.BufferMinutes : 10;
        settings.ThermalTraceMinutes = settings.ThermalTraceMinutes is 5 or 10 or 15 or 20 or 30 or 45 or 60 ? settings.ThermalTraceMinutes : 10;
    }

    private static AppSettings Clone(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
    }

    private static void ApplyStartup(bool enabled)
    {
        try
        {
            // Elevated applications use a per-user scheduled task at highest privilege.
            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            {
                key?.DeleteValue("SimWatch", false);
                key?.DeleteValue("PitMedic", false);
            }

            // Retire the legacy startup task created by builds before the PitMedic rename.
            RunTaskScheduler("/Delete", "/TN", "SimWatch", "/F");

            if (!enabled)
            {
                RunTaskScheduler("/Delete", "/TN", "PitMedic", "/F");
                return;
            }

            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe)) return;
            var user = $"{Environment.UserDomainName}\\{Environment.UserName}";
            RunTaskScheduler("/Create", "/TN", "PitMedic", "/TR", $"\"{exe}\" --startup", "/SC", "ONLOGON",
                "/RL", "HIGHEST", "/RU", user, "/IT", "/F");
        }
        catch (Exception ex)
        {
            AppLog.Write($"Startup task update warning: {ex.Message}");
        }
    }

    private static void RunTaskScheduler(params string[] args)
    {
        var psi = new ProcessStartInfo("schtasks.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi);
        if (process is null) return;
        process.WaitForExit(10000);
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        if (process.ExitCode != 0 && !args.Contains("/Delete", StringComparer.OrdinalIgnoreCase))
            AppLog.Write($"schtasks returned {process.ExitCode}: {output} {error}");
    }
}
