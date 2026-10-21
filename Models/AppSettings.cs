namespace TomatoFlow.Models;

public enum OutputMode
{
    Paste,
    Clipboard
}

public enum AdFrequency
{
    Startup,
    AfterTranscription,
    EveryFiveTranscriptions,
    Hourly
}

public sealed class AppSettings
{
    public string Hotkey { get; set; } = "Ctrl+Alt+D";
    public OutputMode OutputMode { get; set; } = OutputMode.Paste;
    public bool RestoreClipboard { get; set; }
    public bool RemoveFillers { get; set; } = true;
    public bool AutoCapitalize { get; set; } = true;

    public string ModelId { get; set; } = "";
    public string Language { get; set; } = "en";
    public string CustomVocabulary { get; set; } = "";
    public int MicrophoneIndex { get; set; } = -1;

    public bool ShowFloatingOrb { get; set; } = true;
    public double OrbLeft { get; set; } = -99999;
    public double OrbTop { get; set; } = -99999;

    public bool MinimizeToTray { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool LaunchMinimized { get; set; }

    public string LogoPath { get; set; } = "";
    public string HeroImagePath { get; set; } = "";
    public List<string> GalleryImages { get; set; } = new();

    public bool AdsEnabled { get; set; } = true;
    public AdFrequency AdFrequency { get; set; } = AdFrequency.EveryFiveTranscriptions;
    public int AdSkipSeconds { get; set; } = 5;
    public bool AdSound { get; set; } = true;
    public List<string> AdVideos { get; set; } = new();
    public List<string> HiddenAdVideos { get; set; } = new();
    public int LastAdIndex { get; set; } = -1;

    public int TotalWords { get; set; }
    public int TotalTranscriptions { get; set; }
    public double TotalRecordingSeconds { get; set; }
    public int TranscriptionsSinceAd { get; set; }
    public DateTimeOffset? LastAdAt { get; set; }

    public List<TranscriptEntry> History { get; set; } = new();
}

public sealed class TranscriptEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Text { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public double DurationSeconds { get; set; }
    public string ModelId { get; set; } = "";
    public bool Pasted { get; set; }
    public int WordCount { get; set; }
}
