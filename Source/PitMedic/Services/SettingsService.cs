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
        settings.SamplingSeconds = settings.SamplingSeconds is 1 or 2 or 5 ? settings.SamplingSeconds : 2;
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
            // PitMedic runs unelevated, so the standard per-user Run key is sufficient and
            // avoids spawning Task Scheduler processes during every application start.
            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            {
                key?.DeleteValue("SimWatch", false);
                if (!enabled)
                {
                    key?.DeleteValue("PitMedic", false);
                    return;
                }

                var exe = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(exe))
                    key?.SetValue("PitMedic", $"\"{exe}\" --startup", RegistryValueKind.String);
            }
        }
        catch (Exception ex)
        {
            AppLog.Write($"Startup task update warning: {ex.Message}");
        }
    }

}
