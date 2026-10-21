using System.Text.Json;
using System.Text.Json.Serialization;
using TomatoFlow.Helpers;
using TomatoFlow.Models;

namespace TomatoFlow.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly object _gate = new();

    public SettingsService()
    {
        AppPaths.EnsureCreated();
        Settings = Load();
    }

    public AppSettings Settings { get; private set; }

    public event EventHandler? Changed;

    public void Save()
    {
        try
        {
            lock (_gate)
            {
                var tempFile = AppPaths.SettingsFile + ".tmp";
                var json = JsonSerializer.Serialize(Settings, JsonOptions);
                File.WriteAllText(tempFile, json);
                File.Move(tempFile, AppPaths.SettingsFile, true);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not save settings.", ex);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Update(Action<AppSettings> action, bool save = true)
    {
        action(Settings);
        if (save)
        {
            Save();
        }
    }

    public void ResetTranscriptHistory()
    {
        Settings.History.Clear();
        Settings.TotalWords = 0;
        Settings.TotalTranscriptions = 0;
        Settings.TotalRecordingSeconds = 0;
        Save();
    }

    private static AppSettings Load()
    {
        try
        {
            if (!File.Exists(AppPaths.SettingsFile))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(AppPaths.SettingsFile);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            settings.History ??= new List<TranscriptEntry>();
            settings.GalleryImages ??= new List<string>();
            settings.AdVideos ??= new List<string>();
            settings.HiddenAdVideos ??= new List<string>();
            if (settings.AdSkipSeconds < 1)
            {
                settings.AdSkipSeconds = 5;
            }

            return settings;
        }
        catch (Exception ex)
        {
            Log.Error("Could not load settings. Starting fresh.", ex);
            try
            {
                if (File.Exists(AppPaths.SettingsFile))
                {
                    File.Copy(AppPaths.SettingsFile, $"{AppPaths.SettingsFile}.broken-{DateTime.Now:yyyyMMdd-HHmmss}", true);
                }
            }
            catch
            {
            }

            return new AppSettings();
        }
    }
}
