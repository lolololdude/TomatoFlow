using TomatoFlow.Helpers;
using TomatoFlow.Models;

namespace TomatoFlow.Services;

public sealed class AdService
{
    private readonly SettingsService _settings;
    private readonly Random _random = new();
    private bool _startupPlayed;

    public AdService(SettingsService settings)
    {
        _settings = settings;
    }

    public static IReadOnlyList<string> Slogans { get; } = new[]
    {
        "THE FLOW IS RIPE.",
        "TAKE THAT, CLOUD.",
        "YOUR WORDS. YOUR MACHINE. YOUR MOMENT.",
        "NOT A SUBSCRIPTION. NOT EVEN CLOSE.",
        "DICTATION WITH TOMATO ENERGY.",
        "LOCAL. FRESH. JUICY.",
        "EVERY WORD A STATEMENT.",
        "THE ONLY ADS YOU'LL ACTUALLY ENJOY."
    };

    public bool ShouldPlay()
    {
        var settings = _settings.Settings;
        if (!settings.AdsEnabled)
        {
            return false;
        }

        return settings.AdFrequency switch
        {
            AdFrequency.Startup => !_startupPlayed,
            AdFrequency.AfterTranscription => true,
            AdFrequency.EveryFiveTranscriptions => settings.TranscriptionsSinceAd >= 5,
            AdFrequency.Hourly => settings.LastAdAt is null
                                  || DateTimeOffset.Now - settings.LastAdAt.Value >= TimeSpan.FromHours(1),
            _ => false
        };
    }

    public void MarkPlayed()
    {
        var settings = _settings.Settings;
        settings.TranscriptionsSinceAd = 0;
        settings.LastAdAt = DateTimeOffset.Now;
        _startupPlayed = true;
        _settings.Save();
    }

    public string? NextVideo()
    {
        var videos = EffectiveVideos();
        if (videos.Count == 0)
        {
            return null;
        }

        var index = _random.Next(videos.Count);
        if (videos.Count > 1 && index == _settings.Settings.LastAdIndex)
        {
            index = (index + 1) % videos.Count;
        }

        _settings.Settings.LastAdIndex = index;
        return videos[index];
    }

    public int HiddenCount => _settings.Settings.HiddenAdVideos.Count;

    public bool IsHidden(string path)
    {
        return _settings.Settings.HiddenAdVideos.Any(hidden => hidden.Equals(path, StringComparison.OrdinalIgnoreCase));
    }

    public void Hide(string path)
    {
        if (!IsHidden(path))
        {
            _settings.Settings.HiddenAdVideos.Add(path);
            _settings.Save();
        }
    }

    public void RestoreHidden()
    {
        if (_settings.Settings.HiddenAdVideos.Count == 0)
        {
            return;
        }

        _settings.Settings.HiddenAdVideos.Clear();
        _settings.Save();
    }

    public int RemoveBundled()
    {
        var files = AdFolders.Scan()
            .Where(entry => !entry.Folder.IsUserFolder)
            .Select(entry => entry.File)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var removed = 0;
        foreach (var file in files)
        {
            try
            {
                File.Delete(file);
                _settings.Settings.HiddenAdVideos.RemoveAll(hidden => hidden.Equals(file, StringComparison.OrdinalIgnoreCase));
                removed++;
            }
            catch
            {
            }
        }

        if (removed > 0)
        {
            _settings.Save();
        }

        return removed;
    }

    public IReadOnlyList<string> EffectiveVideos()
    {
        var result = new List<string>();
        foreach (var video in _settings.Settings.AdVideos)
        {
            if (File.Exists(video) && !IsHidden(video) && !result.Contains(video, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(video);
            }
        }

        foreach (var (file, _) in AdFolders.Scan())
        {
            if (!IsHidden(file) && !result.Contains(file, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(file);
            }
        }

        return result;
    }

    public IReadOnlyList<(string File, string Source)> Playlist()
    {
        var result = new List<(string, string)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var video in _settings.Settings.AdVideos)
        {
            if (File.Exists(video) && !IsHidden(video) && seen.Add(video))
            {
                result.Add((video, "Picked by you"));
            }
        }

        foreach (var (file, folder) in AdFolders.Scan())
        {
            if (!IsHidden(file) && seen.Add(file))
            {
                result.Add((file, folder.Label));
            }
        }

        return result;
    }

    public string? RandomSlogan()
    {
        return Slogans[_random.Next(Slogans.Count)];
    }

    public void ResetStartupFlag()
    {
        _startupPlayed = false;
    }
}
