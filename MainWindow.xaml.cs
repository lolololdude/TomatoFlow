using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using TomatoFlow.Helpers;
using TomatoFlow.Models;
using TomatoFlow.Services;
using Microsoft.Win32;
using Wf = System.Windows.Forms;
using IOPath = System.IO.Path;

namespace TomatoFlow;

public partial class MainWindow : Window
{
    private readonly SettingsService _settings;
    private readonly ModelManager _models;
    private readonly TranscriptionService _transcriber;
    private readonly DictationController _dictation;
    private readonly AdService _ads;
    private readonly TrayService _tray;
    private readonly DispatcherTimer _waveTimer;
    private readonly DispatcherTimer _toastTimer;
    private readonly List<Rectangle> _bars = new();
    private readonly double[] _barHeights = new double[26];
    private HotkeyService? _hotkey;
    private AdWindow? _adWindow;
    private CancellationTokenSource? _modelCts;
    private float _level;
    private double _barPhase;
    private bool _loading = true;
    private bool _capturingHotkey;
    private bool _downloading;
    private bool _forceClose;
    private bool _trayHintShown;

    public MainWindow(
        SettingsService settings,
        ModelManager models,
        TranscriptionService transcriber,
        DictationController dictation,
        AdService ads)
    {
        _settings = settings;
        _models = models;
        _transcriber = transcriber;
        _dictation = dictation;
        _ads = ads;

        InitializeComponent();

        _tray = new TrayService();
        _tray.OpenRequested += (_, _) => RestoreFromTray();
        _tray.ToggleDictationRequested += (_, _) => _ = _dictation.ToggleAsync();
        _tray.ExitRequested += (_, _) => ((App)Application.Current).RequestExit();
        _tray.FloatingOrbChanged += (_, value) => SetOrbVisible(value);
        _tray.AdsChanged += (_, value) => SetAdsEnabled(value);
        _tray.RemoveBundledAdsRequested += (_, _) => RemoveBundledAds_Click(this, new RoutedEventArgs());
        _tray.OutputModeChanged += (_, mode) => SetOutputMode(mode);

        _waveTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(50) };
        _waveTimer.Tick += OnWaveTick;
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _toastTimer.Tick += (_, _) => HideToast();

        InitializeWaveBars();

        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
        Closing += OnClosing;
        StateChanged += OnWindowStateChanged;

        _dictation.StateChanged += OnDictationStateChanged;
        _dictation.LevelChanged += (_, level) => _level = level;
        _dictation.StatusMessage += OnDictationStatus;
        _dictation.Error += OnDictationError;
        _dictation.TranscriptReady += OnTranscriptReady;
    }

    private void InitializeWaveBars()
    {
        var brush = TryFindResource("AccentBrush") as Brush ?? Brushes.Tomato;
        for (var i = 0; i < 26; i++)
        {
            var bar = new Rectangle
            {
                Width = 5,
                Height = 8,
                RadiusX = 2.5,
                RadiusY = 2.5,
                Fill = brush,
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Center,
                Opacity = 0.85
            };
            _bars.Add(bar);
            _barHeights[i] = 8;
            WaveGrid.Children.Add(bar);
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _hotkey = new HotkeyService(handle);
        _hotkey.Pressed += (_, _) => _ = _dictation.ToggleAsync();
        RegisterHotkey(ParseHotkey(_settings.Settings.Hotkey));
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        BuildComboBoxes();
        ApplyEdition();
        ApplySettingsToUi();
        ApplyBranding();
        UpdateStats();
        RebuildHistory();
        RebuildAds();
        _tray.Update(_settings.Settings, _dictation.State);
        _loading = false;

        var model = _models.SelectedModel;
        if (!_models.IsInstalled(model))
        {
            _ = StartModelInstallAsync(model, true);
        }
        else
        {
            UpdateModelUi();
            _ = WarmEngineAsync(model);
        }

        if (_settings.Settings.AdsEnabled && _ads.ShouldPlay())
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1800) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                ShowAd(false);
            };
            timer.Start();
        }
    }

    private void ApplyEdition()
    {
        NavAds.Visibility = BuildInfo.HasAdsTab ? Visibility.Visible : Visibility.Collapsed;
        NavAdvanced.Visibility = BuildInfo.HasAdvanced ? Visibility.Visible : Visibility.Collapsed;
        AdSettingsCard.Visibility = BuildInfo.HasAdsSettings ? Visibility.Visible : Visibility.Collapsed;
        EditionBadge.Visibility = BuildInfo.IsDeveloper ? Visibility.Visible : Visibility.Collapsed;
        EditionBadgeText.Text = $"{BuildInfo.EditionName.ToUpperInvariant()} BUILD";

        if (!BuildInfo.HasAdsTab)
        {
            NavAds.IsChecked = false;
        }

        if (!BuildInfo.HasAdvanced)
        {
            NavAdvanced.IsChecked = false;
        }
    }

    private void BuildComboBoxes()
    {
        _loading = true;

        ModelCombo.ItemsSource = ModelCatalog.All;

        var devices = AudioRecorder.GetDevices();
        if (devices.Count == 0)
        {
            devices = new[] { new MicrophoneDevice(0, "Default microphone") };
        }

        MicCombo.ItemsSource = devices;

        var languages = new (string Code, string Name)[]
        {
            ("auto", "Auto detect"),
            ("en", "English"),
            ("es", "Spanish"),
            ("fr", "French"),
            ("de", "German"),
            ("pt", "Portuguese"),
            ("it", "Italian"),
            ("nl", "Dutch"),
            ("pl", "Polish"),
            ("ru", "Russian"),
            ("uk", "Ukrainian"),
            ("tr", "Turkish"),
            ("ar", "Arabic"),
            ("hi", "Hindi"),
            ("ja", "Japanese"),
            ("ko", "Korean"),
            ("zh", "Chinese"),
            ("id", "Indonesian"),
            ("vi", "Vietnamese"),
            ("th", "Thai"),
            ("sv", "Swedish")
        };

        LanguageCombo.Items.Clear();
        foreach (var (code, name) in languages)
        {
            LanguageCombo.Items.Add(new ComboBoxItem { Content = name, Tag = code });
        }

        AdFrequencyCombo.Items.Clear();
        AdFrequencyCombo.Items.Add(new ComboBoxItem { Content = "Only at startup", Tag = AdFrequency.Startup });
        AdFrequencyCombo.Items.Add(new ComboBoxItem { Content = "After every dictation", Tag = AdFrequency.AfterTranscription });
        AdFrequencyCombo.Items.Add(new ComboBoxItem { Content = "Every 5 dictations", Tag = AdFrequency.EveryFiveTranscriptions });
        AdFrequencyCombo.Items.Add(new ComboBoxItem { Content = "Once an hour", Tag = AdFrequency.Hourly });

        AdSkipCombo.Items.Clear();
        for (var seconds = 3; seconds <= 10; seconds++)
        {
            AdSkipCombo.Items.Add(new ComboBoxItem { Content = $"{seconds} seconds", Tag = seconds });
        }

        _loading = false;
    }

    private void ApplySettingsToUi()
    {
        _loading = true;
        var settings = _settings.Settings;

        var gesture = ParseHotkey(settings.Hotkey) ?? HotkeyGesture.Default;
        HotkeyButton.Content = gesture.Display;
        SidebarHotkeyText.Text = gesture.Display;

        ModelCombo.SelectedItem = _models.SelectedModel;

        if (MicCombo.ItemsSource is IEnumerable<MicrophoneDevice> devices)
        {
            var selected = devices.FirstOrDefault(device => device.Index == settings.MicrophoneIndex) ?? devices.FirstOrDefault();
            MicCombo.SelectedItem = selected;
        }

        foreach (ComboBoxItem item in LanguageCombo.Items)
        {
            if ((string)item.Tag == settings.Language)
            {
                LanguageCombo.SelectedItem = item;
                break;
            }
        }

        VocabularyBox.Text = settings.CustomVocabulary;
        FillersToggle.IsChecked = settings.RemoveFillers;
        CapitalizeToggle.IsChecked = settings.AutoCapitalize;
        PasteRadio.IsChecked = settings.OutputMode == OutputMode.Paste;
        CopyRadio.IsChecked = settings.OutputMode == OutputMode.Clipboard;
        RestoreClipboardToggle.IsChecked = settings.RestoreClipboard;
        OrbToggle.IsChecked = settings.ShowFloatingOrb;
        MinimizeToggle.IsChecked = settings.MinimizeToTray;
        CloseTrayToggle.IsChecked = settings.CloseToTray;
        StartWithWindowsToggle.IsChecked = settings.StartWithWindows;
        LaunchMinimizedToggle.IsChecked = settings.LaunchMinimized;

        AdsToggle.IsChecked = settings.AdsEnabled;
        AdSoundToggle.IsChecked = settings.AdSound;
        foreach (ComboBoxItem item in AdFrequencyCombo.Items)
        {
            if ((AdFrequency)item.Tag == settings.AdFrequency)
            {
                AdFrequencyCombo.SelectedItem = item;
                break;
            }
        }

        foreach (ComboBoxItem item in AdSkipCombo.Items)
        {
            if ((int)item.Tag == settings.AdSkipSeconds)
            {
                AdSkipCombo.SelectedItem = item;
                break;
            }
        }

        HeroModeChip.Text = settings.OutputMode == OutputMode.Paste ? "PASTE MODE" : "CLIPBOARD MODE";
        _loading = false;
    }

    private void UpdateModelUi()
    {
        var settings = _settings.Settings;
        var model = _models.SelectedModel;
        var installed = _models.IsInstalled(model);

        HardwareText.Text = _models.Hardware.Summary;
        RecommendText.Text = _models.Hardware.RecommendationReason;
        StatModelText.Text = model.Name;
        ModelProgressBar.Value = installed ? 100 : 0;
        ModelStatusText.Text = installed
            ? $"Installed at {_models.GetModelPath(model)}"
            : $"{model.Name} is {model.SizeLabel}. Tomato Flow will download it from Hugging Face and keep it on this PC.";
        InstallModelButton.IsEnabled = !installed && !_downloading;
        RemoveModelButton.IsEnabled = installed && !_downloading;

        if (_transcriber.ActiveModel == model.Name && installed)
        {
            EngineRuntimeText.Text = $"Engine runtime: {_transcriber.ActiveRuntime}";
        }
        else
        {
            EngineRuntimeText.Text = installed ? "Engine is installed and will load on first use." : "Engine not installed yet.";
        }

        UpdateEnginePill(installed);
    }

    private void UpdateEnginePill(bool installed)
    {
        var accent = TryFindResource("AccentBrush") as Brush ?? Brushes.Tomato;
        var success = TryFindResource("SuccessBrush") as Brush ?? Brushes.Green;
        var danger = TryFindResource("DangerBrush") as Brush ?? Brushes.Red;

        if (_downloading)
        {
            EngineDot.Fill = accent;
            EnginePillText.Text = "ENGINE: INSTALLING";
            EnginePillText.Foreground = accent;
        }
        else if (!installed)
        {
            EngineDot.Fill = danger;
            EnginePillText.Text = "ENGINE: NEEDS MODEL";
            EnginePillText.Foreground = danger;
        }
        else if (_transcriber.ActiveRuntime is "Not loaded" or "Loading")
        {
            EngineDot.Fill = accent;
            EnginePillText.Text = "ENGINE: READY";
            EnginePillText.Foreground = accent;
        }
        else
        {
            EngineDot.Fill = success;
            EnginePillText.Text = $"ENGINE: {_transcriber.ActiveRuntime.ToUpperInvariant()}";
            EnginePillText.Foreground = success;
        }
    }

    private async Task WarmEngineAsync(ModelDefinition model)
    {
        UpdateEnginePill(true);
        var ok = await _transcriber.PreloadAsync(model);
        if (!IsLoaded)
        {
            return;
        }

        UpdateModelUi();
        if (ok)
        {
            Toast($"Engine loaded on {_transcriber.ActiveRuntime}.");
        }
        else
        {
            Toast("Engine could not load. Try a smaller model.");
        }
    }

    private async Task StartModelInstallAsync(ModelDefinition model, bool automatic)
    {
        if (_downloading)
        {
            return;
        }

        _downloading = true;
        _modelCts = new CancellationTokenSource();
        SetupOverlay.Visibility = Visibility.Visible;
        OverlayRetryButton.Visibility = Visibility.Collapsed;
        OverlayCancelButton.Visibility = Visibility.Visible;
        OverlayProgressBar.Value = 0;
        OverlayProgressText.Text = "";
        OverlayStatusText.Text = automatic
            ? $"Choosing the best fit for this PC: {model.Name} ({model.SizeLabel})."
            : $"Installing {model.Name} ({model.SizeLabel}).";
        UpdateModelUi();

        var progress = new Progress<ModelDownloadProgress>(report =>
        {
            OverlayProgressBar.Value = report.Fraction * 100;
            ModelProgressBar.Value = report.Fraction * 100;
            var received = report.BytesReceived / 1_000_000d;
            var total = report.TotalBytes / 1_000_000d;
            OverlayProgressText.Text = $"{report.Percent}% · {report.SpeedLabel} · {received:0} / {total:0} MB";
        });

        try
        {
            await _models.DownloadAsync(model, progress, _modelCts.Token);
            OverlayStatusText.Text = "Download complete. Loading the speech engine...";
            OverlayProgressBar.Value = 100;
            var loaded = await _transcriber.PreloadAsync(model, _modelCts.Token);
            SetupOverlay.Visibility = Visibility.Collapsed;
            UpdateModelUi();
            Toast(loaded ? $"Flow ready on {_transcriber.ActiveRuntime}." : "Model installed but the engine did not load.");
        }
        catch (OperationCanceledException)
        {
            SetupOverlay.Visibility = Visibility.Collapsed;
            Toast("Model install canceled.");
            UpdateModelUi();
        }
        catch (Exception ex)
        {
            Log.Error("Model install failed.", ex);
            OverlayStatusText.Text = "The download failed. Check your connection and try again.";
            OverlayProgressText.Text = ex.Message;
            OverlayRetryButton.Visibility = Visibility.Visible;
            OverlayCancelButton.Visibility = Visibility.Visible;
            UpdateModelUi();
        }
        finally
        {
            _downloading = false;
            _modelCts?.Dispose();
            _modelCts = null;
            UpdateModelUi();
        }
    }

    private void RegisterHotkey(HotkeyGesture? gesture)
    {
        if (_hotkey is null || gesture is null)
        {
            return;
        }

        if (_hotkey.Register(gesture, out var error))
        {
            HotkeyStatusText.Text = $"Registered globally · {gesture.Display}";
            SidebarHotkeyText.Text = gesture.Display;
        }
        else
        {
            HotkeyStatusText.Text = error;
        }
    }

    private static HotkeyGesture? ParseHotkey(string text)
    {
        return HotkeyGesture.TryParse(text, out var gesture) ? gesture : null;
    }

    private void OnDictationStateChanged(object? sender, DictationState state)
    {
        RunOnUi(() =>
        {
            switch (state)
            {
                case DictationState.Recording:
                    RecordLabel.Text = "TAP TO STOP";
                    HeaderRecordGlyph.Text = "\uE71A";
                    HeroStartButton.Content = "STOP DICTATION";
                    HeroStartButton.Background = TryFindResource("DangerBrush") as Brush ?? Brushes.IndianRed;
                    WaveStatusText.Text = "LISTENING";
                    SidebarStatusText.Text = "Recording";
                    _waveTimer.Start();
                    break;
                case DictationState.Transcribing:
                    RecordLabel.Text = "DECODING";
                    HeaderRecordGlyph.Text = "\uE895";
                    HeroStartButton.Content = "DECODING...";
                    HeroStartButton.Background = TryFindResource("PanelRaisedBrush") as Brush ?? Brushes.DimGray;
                    WaveStatusText.Text = "TOMATO IS TYPING";
                    SidebarStatusText.Text = "Transcribing";
                    _waveTimer.Stop();
                    break;
                default:
                    RecordLabel.Text = "TAP TO TALK";
                    HeaderRecordGlyph.Text = "\uE720";
                    HeroStartButton.Content = "START DICTATION";
                    HeroStartButton.Background = TryFindResource("TomatoGradient") as Brush ?? Brushes.Tomato;
                    WaveStatusText.Text = "READY FOR YOUR FLOW";
                    SidebarStatusText.Text = "Ready";
                    _waveTimer.Stop();
                    DecayBars();
                    break;
            }

            _tray.Update(_settings.Settings, state);
            _tray.UpdateIcon(state == DictationState.Recording);
            var app = (App)Application.Current;
            app.Orb?.SetState(state);
            app.Orb?.SetLevel(0);
        });
    }

    private void OnWaveTick(object? sender, EventArgs e)
    {
        var recording = _dictation.State == DictationState.Recording;
        _barPhase += 0.42;
        var energy = recording ? Math.Max(0.04, _level) : 0;

        for (var i = 0; i < _bars.Count; i++)
        {
            double target;
            if (recording)
            {
                var wave = (Math.Sin((_barPhase + i) * 0.55) + 1) / 2;
                target = 7 + (wave * energy * 120) + (Random.Shared.NextDouble() * 5);
            }
            else
            {
                target = 8;
            }

            target = Math.Clamp(target, 6, 48);
            _barHeights[i] += (target - _barHeights[i]) * 0.35;
            _bars[i].Height = _barHeights[i];
            _bars[i].Opacity = recording ? 0.95 : 0.4;
        }
    }

    private void DecayBars()
    {
        for (var i = 0; i < _bars.Count; i++)
        {
            _barHeights[i] = 8;
            _bars[i].Height = 8;
        }
    }

    private void OnDictationStatus(object? sender, string message)
    {
        RunOnUi(() =>
        {
            Toast(message);
            SidebarStatusText.Text = message.Length > 42 ? message[..42] : message;
        });
    }

    private void OnDictationError(object? sender, string message)
    {
        RunOnUi(() =>
        {
            Toast(message);
            _tray.Balloon("Tomato Flow", message, true);
        });
    }

    private void OnTranscriptReady(object? sender, TranscriptReadyEventArgs e)
    {
        RunOnUi(() =>
        {
            UpdateStats();
            RebuildHistory();
            if (_settings.Settings.AdsEnabled && _ads.ShouldPlay())
            {
                ShowAd(false);
            }
        });
    }

    private void UpdateStats()
    {
        var settings = _settings.Settings;
        StatWordsText.Text = settings.TotalWords.ToString("N0");
        StatSessionsText.Text = settings.TotalTranscriptions.ToString("N0");
    }

    private void RebuildHistory()
    {
        var items = _settings.Settings.History.Take(12).ToList();
        HistoryList.ItemsSource = items;
        HistoryEmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CopyHistory_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is TranscriptEntry entry)
        {
            try
            {
                Clipboard.SetText(entry.Text);
                Toast("Copied that flow again.");
            }
            catch (Exception ex)
            {
                Toast("Clipboard is busy: " + ex.Message);
            }
        }
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        _settings.ResetTranscriptHistory();
        UpdateStats();
        RebuildHistory();
        Toast("History cleared.");
    }

    private async void RecordButton_Click(object sender, RoutedEventArgs e)
    {
        await _dictation.ToggleAsync();
    }

    private void TestAd_Click(object sender, RoutedEventArgs e)
    {
        ShowAd(true);
    }

    private void ShowAd(bool manual)
    {
        if (!_settings.Settings.AdsEnabled && !manual)
        {
            return;
        }

        try
        {
            if (_adWindow is null)
            {
                _adWindow = new AdWindow(_ads, _settings);
                _adWindow.Closed += (_, _) => _adWindow = null;
            }

            _adWindow.PlayNext(manual);
            if (manual)
            {
                _adWindow.Activate();
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not show the ad window.", ex);
        }
    }

    private void SetAdsEnabled(bool enabled)
    {
        _settings.Settings.AdsEnabled = enabled;
        _settings.Save();
        _loading = true;
        AdsToggle.IsChecked = enabled;
        _loading = false;
        _tray.Update(_settings.Settings, _dictation.State);
    }

    private void SetOutputMode(OutputMode mode)
    {
        _settings.Settings.OutputMode = mode;
        _settings.Save();
        _loading = true;
        PasteRadio.IsChecked = mode == OutputMode.Paste;
        CopyRadio.IsChecked = mode == OutputMode.Clipboard;
        HeroModeChip.Text = mode == OutputMode.Paste ? "PASTE MODE" : "CLIPBOARD MODE";
        _loading = false;
        _tray.Update(_settings.Settings, _dictation.State);
    }

    private void SetOrbVisible(bool visible)
    {
        _settings.Settings.ShowFloatingOrb = visible;
        _settings.Save();
        _loading = true;
        OrbToggle.IsChecked = visible;
        _loading = false;
        ((App)Application.Current).SyncOrbVisibility();
        _tray.Update(_settings.Settings, _dictation.State);
    }

    private void SettingToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var settings = _settings.Settings;
        settings.RemoveFillers = FillersToggle.IsChecked == true;
        settings.AutoCapitalize = CapitalizeToggle.IsChecked == true;
        settings.RestoreClipboard = RestoreClipboardToggle.IsChecked == true;
        settings.ShowFloatingOrb = OrbToggle.IsChecked == true;
        settings.MinimizeToTray = MinimizeToggle.IsChecked == true;
        settings.CloseToTray = CloseTrayToggle.IsChecked == true;
        settings.StartWithWindows = StartWithWindowsToggle.IsChecked == true;
        settings.LaunchMinimized = LaunchMinimizedToggle.IsChecked == true;
        settings.AdsEnabled = AdsToggle.IsChecked == true;
        settings.AdSound = AdSoundToggle.IsChecked == true;
        _settings.Save();

        StartupService.Apply(settings.StartWithWindows, settings.LaunchMinimized);
        ((App)Application.Current).SyncOrbVisibility();
        _tray.Update(settings, _dictation.State);
    }

    private void OutputMode_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        SetOutputMode(PasteRadio.IsChecked == true ? OutputMode.Paste : OutputMode.Clipboard);
    }

    private void ModelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ModelCombo.SelectedItem is not ModelDefinition model)
        {
            return;
        }

        _settings.Settings.ModelId = model.Id;
        _settings.Save();
        UpdateModelUi();

        if (!_models.IsInstalled(model) && !_downloading)
        {
            _ = StartModelInstallAsync(model, false);
        }
        else
        {
            _ = WarmEngineAsync(model);
        }
    }

    private void InstallModel_Click(object sender, RoutedEventArgs e)
    {
        _ = StartModelInstallAsync(_models.SelectedModel, false);
    }

    private void RemoveModel_Click(object sender, RoutedEventArgs e)
    {
        var model = _models.SelectedModel;
        _models.Delete(model);
        UpdateModelUi();
        Toast($"{model.Name} removed from this PC.");
    }

    private void OverlayCancel_Click(object sender, RoutedEventArgs e)
    {
        _modelCts?.Cancel();
        SetupOverlay.Visibility = Visibility.Collapsed;
    }

    private void OverlayRetry_Click(object sender, RoutedEventArgs e)
    {
        _ = StartModelInstallAsync(_models.SelectedModel, false);
    }

    private void MicCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || MicCombo.SelectedItem is not MicrophoneDevice device)
        {
            return;
        }

        _settings.Settings.MicrophoneIndex = device.Index;
        _settings.Save();
    }

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LanguageCombo.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        _settings.Settings.Language = (string)item.Tag;
        _settings.Save();
    }

    private void VocabularyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        _settings.Settings.CustomVocabulary = VocabularyBox.Text;
        _settings.Save();
    }

    private void AdFrequencyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || AdFrequencyCombo.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        _settings.Settings.AdFrequency = (AdFrequency)item.Tag;
        _ads.ResetStartupFlag();
        _settings.Save();
    }

    private void AdSkipCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || AdSkipCombo.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        _settings.Settings.AdSkipSeconds = (int)item.Tag;
        _settings.Save();
    }

    private void HotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        _capturingHotkey = true;
        HotkeyButton.Content = "Press your shortcut...";
        HotkeyStatusText.Text = "Listening for keys. Include Ctrl, Alt, Shift, or Win. Escape cancels.";
        HotkeyButton.Focus();
        Keyboard.Focus(HotkeyButton);
    }

    private void HotkeyButton_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_capturingHotkey)
        {
            return;
        }

        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            EndHotkeyCapture(ParseHotkey(_settings.Settings.Hotkey) ?? HotkeyGesture.Default);
            HotkeyStatusText.Text = "Shortcut unchanged.";
            return;
        }

        if (HotkeyGesture.IsModifierKey(key))
        {
            return;
        }

        var gesture = HotkeyGesture.Create(Keyboard.Modifiers, key, false);
        if (gesture is null)
        {
            HotkeyStatusText.Text = "That needs at least one modifier (Ctrl, Alt, Shift, Win).";
            return;
        }

        if (_hotkey is null)
        {
            return;
        }

        if (_hotkey.Register(gesture, out var error))
        {
            _settings.Settings.Hotkey = gesture.Display;
            _settings.Save();
            EndHotkeyCapture(gesture);
            Toast($"Shortcut set to {gesture.Display}.");
        }
        else
        {
            HotkeyStatusText.Text = error;
            EndHotkeyCapture(_hotkey.Current ?? HotkeyGesture.Default);
        }
    }

    private void HotkeyButton_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_capturingHotkey)
        {
            EndHotkeyCapture(ParseHotkey(_settings.Settings.Hotkey) ?? HotkeyGesture.Default);
        }
    }

    private void EndHotkeyCapture(HotkeyGesture gesture)
    {
        _capturingHotkey = false;
        HotkeyButton.Content = gesture.Display;
        SidebarHotkeyText.Text = gesture.Display;
        if (_hotkey?.Current is not null)
        {
            HotkeyStatusText.Text = $"Registered globally · {gesture.Display}";
        }
    }

    private void ChooseLogo_Click(object sender, RoutedEventArgs e)
    {
        if (PickImage() is { } path)
        {
            _settings.Settings.LogoPath = CopyBranding(path, "logo");
            _settings.Save();
            ApplyBranding();
            ((App)Application.Current).ApplyBrandingToOrb();
            Toast("Logo updated.");
        }
    }

    private void ClearLogo_Click(object sender, RoutedEventArgs e)
    {
        _settings.Settings.LogoPath = "";
        _settings.Save();
        ApplyBranding();
        ((App)Application.Current).ApplyBrandingToOrb();
    }

    private void ChooseHero_Click(object sender, RoutedEventArgs e)
    {
        if (PickImage() is { } path)
        {
            _settings.Settings.HeroImagePath = CopyBranding(path, "hero");
            _settings.Save();
            ApplyBranding();
            Toast("Backdrop updated.");
        }
    }

    private void ClearHero_Click(object sender, RoutedEventArgs e)
    {
        _settings.Settings.HeroImagePath = "";
        _settings.Save();
        ApplyBranding();
    }

    private void AddGallery_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Add images to the Tomato wall",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        foreach (var file in dialog.FileNames)
        {
            _settings.Settings.GalleryImages.Add(CopyBranding(file, "wall"));
        }

        _settings.Save();
        RebuildGallery();
        Toast($"{dialog.FileNames.Length} image(s) added to the wall.");
    }

    private void ClearGallery_Click(object sender, RoutedEventArgs e)
    {
        _settings.Settings.GalleryImages.Clear();
        _settings.Save();
        RebuildGallery();
    }

    private void AddAd_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Add ad videos",
            Filter = "Video files|*.mp4;*.wmv;*.avi;*.mkv;*.mov;*.m4v;*.webm|All files|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var added = CopyIntoAdsFolder(dialog.FileNames);
        RebuildAds();
        Toast(added == 0
            ? "Those videos are already in the Tomato Ads folder."
            : $"{added} video(s) added to the Tomato Ads folder.");
    }

    private void AddAdFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Wf.FolderBrowserDialog
        {
            Description = "Pick a folder full of ad videos",
            UseDescriptionForTitle = true
        };
        if (dialog.ShowDialog() != Wf.DialogResult.OK)
        {
            return;
        }

        try
        {
            var files = Directory.EnumerateFiles(dialog.SelectedPath, "*", SearchOption.AllDirectories)
                .Where(AdFolders.IsVideo)
                .ToList();
            var added = CopyIntoAdsFolder(files);
            RebuildAds();
            Toast(added == 0
                ? "Those videos are already in the Tomato Ads folder."
                : $"{added} video(s) copied into the Tomato Ads folder.");
        }
        catch (Exception ex)
        {
            Toast("Could not read that folder: " + ex.Message);
        }
    }

    private int CopyIntoAdsFolder(IEnumerable<string> files)
    {
        AdFolders.EnsureCreated();
        var target = AdFolders.UserVideos;
        Directory.CreateDirectory(target);
        var added = 0;

        foreach (var file in files)
        {
            try
            {
                if (!File.Exists(file))
                {
                    continue;
                }

                var destination = IOPath.Combine(target, IOPath.GetFileName(file));
                if (destination.Equals(file, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!File.Exists(destination))
                {
                    File.Copy(file, destination);
                    added++;
                }
            }
            catch
            {
            }
        }

        return added;
    }

    private void RefreshAds_Click(object sender, RoutedEventArgs e)
    {
        RebuildAds();
        Toast("Playlist refreshed.");
    }

    private void OpenAdsFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            AdFolders.EnsureCreated();
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AdFolders.UserVideos}\"") { UseShellExecute = true });
            Toast("Drop any video in that folder and Tomato Vision will play it.");
        }
        catch (Exception ex)
        {
            Toast("Could not open the folder: " + ex.Message);
        }
    }

    private void RebuildAds()
    {
        AdListPanel.Children.Clear();
        var playlist = _ads.Playlist();
        foreach (var (video, source) in playlist)
        {
            var row = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(24, 12, 9)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(44, 21, 17)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 9, 8, 9),
                Margin = new Thickness(0, 0, 0, 7)
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new TextBlock
            {
                Text = IOPath.GetFileName(video),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = video + Environment.NewLine + source
            };
            var badge = new TextBlock
            {
                Text = source,
                FontSize = 10.5,
                Foreground = TryFindResource("MutedBrush") as Brush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 10, 0)
            };
            var remove = new Button
            {
                Content = "Remove",
                Style = TryFindResource("GhostButton") as Style,
                Tag = video,
                VerticalAlignment = VerticalAlignment.Center
            };
            remove.Click += (_, _) =>
            {
                _ads.Hide(video);
                RebuildAds();
                Toast("Removed from the playlist. The file stays on disk.");
            };
            Grid.SetColumn(badge, 1);
            Grid.SetColumn(remove, 2);
            grid.Children.Add(text);
            grid.Children.Add(badge);
            grid.Children.Add(remove);
            row.Child = grid;
            AdListPanel.Children.Add(row);
        }

        var total = playlist.Count;
        var hidden = _ads.HiddenCount;
        AdCountText.Text = total == 0
            ? hidden == 0 ? "No videos yet - the built-in ad card will play" : "No videos - hidden ads can be restored"
            : hidden == 0 ? $"{total} video(s) ready" : $"{total} video(s) ready · {hidden} hidden";
        RestoreHiddenButton.Visibility = hidden > 0 ? Visibility.Visible : Visibility.Collapsed;
        RestoreHiddenButton.Content = hidden > 0 ? $"RESTORE HIDDEN ({hidden})" : "RESTORE HIDDEN";
        RemoveBundledButton.Visibility = AdFolders.BundledCount > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RestoreHidden_Click(object sender, RoutedEventArgs e)
    {
        var hidden = _ads.HiddenCount;
        _ads.RestoreHidden();
        RebuildAds();
        RefreshAdvanced();
        Toast(hidden == 0 ? "There are no hidden ads." : $"{hidden} hidden ad(s) restored.");
    }

    private void RemoveBundledAds_Click(object sender, RoutedEventArgs e)
    {
        var count = AdFolders.BundledCount;
        if (count == 0)
        {
            Toast("There are no bundled ads to remove.");
            return;
        }

        var answer = MessageBox.Show(
            this,
            $"Delete {count} bundled ad file(s) from below the app?\n\n{AppPaths.BundledAds}\n\nThis cannot be undone.",
            "Remove bundled ads",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        var removed = _ads.RemoveBundled();
        RebuildAds();
        RefreshAdvanced();
        _tray.Update(_settings.Settings, _dictation.State);
        Toast(removed == 0 ? "Could not delete the bundled ads." : $"{removed} bundled ad(s) deleted.");
    }

    private void RefreshAdvanced()
    {
        if (!BuildInfo.HasAdvanced)
        {
            return;
        }

        var model = _models.SelectedModel;
        var playlist = _ads.Playlist();
        var version = typeof(MainWindow).Assembly.GetName().Version;
        AdvancedBuildText.Text =
            $"Edition {BuildInfo.EditionName} · version {version} · ads tab {OnOff(BuildInfo.HasAdsTab)} · " +
            $"ad settings {OnOff(BuildInfo.HasAdsSettings)} · advanced tools {OnOff(BuildInfo.HasAdvanced)} · " +
            $"all ad folders {OnOff(BuildInfo.HasAllAdFolders)}";

        AdvancedInfoText.Text = string.Join(Environment.NewLine, new[]
        {
            $"OS           {RuntimeInformation.OSDescription}",
            $"Runtime      {RuntimeInformation.FrameworkDescription}",
            $"Process      {(Environment.Is64BitProcess ? "x64" : "x86")}",
            $"CPU          {Environment.ProcessorCount} logical cores",
            $"Memory       {NativeMethods.GetTotalRamGb():0.0} GB",
            $"Device       {_models.Hardware.Summary}",
            $"Model        {model.Name} ({model.SizeLabel})",
            $"Engine       {_transcriber.ActiveRuntime}",
            $"Model path   {_models.GetModelPath(model)}",
            $"Data folder  {AppPaths.Root}",
            $"Videos       {playlist.Count} playable, {_ads.HiddenCount} hidden",
            $"Folders      {string.Join(", ", AdFolders.All.Select(folder => folder.Label))}"
        });

        AdvancedAdsText.Text = playlist.Count == 0
            ? "No playable videos. The built-in ad card is used instead."
            : $"Playlist: {string.Join(", ", playlist.Take(8).Select(item => IOPath.GetFileName(item.File)))}" +
              (playlist.Count > 8 ? $" and {playlist.Count - 8} more." : ".");

        AdvancedEngineText.Text = _transcriber.ActiveRuntime is "Not loaded" or "Loading"
            ? "The engine loads on your first dictation. Warm it up here to have it ready now."
            : $"Loaded on {_transcriber.ActiveRuntime} with {_transcriber.ActiveModel}.";
    }

    private static string OnOff(bool value)
    {
        return value ? "on" : "off";
    }

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Logs);
            if (File.Exists(AppPaths.LogFile))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{AppPaths.LogFile}\"") { UseShellExecute = true });
            }
            else
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.Logs}\"") { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            Toast("Could not open the log: " + ex.Message);
        }
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.Root}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Toast("Could not open the data folder: " + ex.Message);
        }
    }

    private void OpenModelFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Models);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.Models}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Toast("Could not open the model folder: " + ex.Message);
        }
    }

    private void CopyDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText($"{AdvancedBuildText.Text}{Environment.NewLine}{Environment.NewLine}{AdvancedInfoText.Text}");
            Toast("Diagnostics copied to the clipboard.");
        }
        catch (Exception ex)
        {
            Toast("Could not copy diagnostics: " + ex.Message);
        }
    }

    private void ReloadAds_Click(object sender, RoutedEventArgs e)
    {
        RebuildAds();
        RefreshAdvanced();
        Toast("Playlist reloaded.");
    }

    private void ResetOrb_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current is App app)
        {
            app.ResetOrbPosition();
            Toast("Tomato moved back to its corner.");
        }
    }

    private async void WarmEngine_Click(object sender, RoutedEventArgs e)
    {
        var model = _models.SelectedModel;
        if (!_models.IsInstalled(model))
        {
            Toast("Install the selected model first.");
            return;
        }

        Toast("Warming up the engine...");
        await _transcriber.PreloadAsync(model);
        RefreshAdvanced();
        Toast($"Engine ready on {_transcriber.ActiveRuntime}.");
    }

    public void SyncOrbSetting()
    {
        _loading = true;
        OrbToggle.IsChecked = _settings.Settings.ShowFloatingOrb;
        _loading = false;
        _tray.Update(_settings.Settings, _dictation.State);
    }

    private void RebuildGallery()
    {
        GalleryPanel.Children.Clear();
        var images = _settings.Settings.GalleryImages.ToList();
        GalleryCountText.Text = images.Count == 0 ? "Wall is empty" : $"{images.Count} image(s)";
        foreach (var path in images)
        {
            var card = new Border
            {
                Width = 148,
                Height = 112,
                CornerRadius = new CornerRadius(14),
                Margin = new Thickness(0, 0, 10, 10),
                Background = new SolidColorBrush(Color.FromRgb(18, 16, 11)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(43, 37, 24)),
                BorderThickness = new Thickness(1),
                ClipToBounds = true
            };
            var grid = new Grid();
            var image = new Image
            {
                Source = ImageLoader.Load(path, 300),
                Stretch = Stretch.UniformToFill
            };
            var remove = new Button
            {
                Content = "\uE711",
                Style = TryFindResource("IconButton") as Style,
                Width = 26,
                Height = 26,
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 6, 6, 0),
                ToolTip = "Remove",
                Tag = path
            };
            remove.Click += (_, _) =>
            {
                _settings.Settings.GalleryImages.Remove(path);
                _settings.Save();
                RebuildGallery();
            };
            grid.Children.Add(image);
            grid.Children.Add(remove);
            card.Child = grid;
            GalleryPanel.Children.Add(card);
        }
    }

    private void ApplyBranding()
    {
        var settings = _settings.Settings;
        var logo = ImageLoader.Load(settings.LogoPath, 320);
        if (logo is not null)
        {
            SidebarLogoImage.Source = logo;
            SidebarLogoImage.Visibility = Visibility.Visible;
            SidebarLogoDefault.Visibility = Visibility.Collapsed;
            LogoPreviewImage.Source = logo;
            LogoPreviewImage.Visibility = Visibility.Visible;
            LogoPreviewDefault.Visibility = Visibility.Collapsed;
        }
        else
        {
            SidebarLogoImage.Source = null;
            SidebarLogoImage.Visibility = Visibility.Collapsed;
            SidebarLogoDefault.Visibility = Visibility.Visible;
            LogoPreviewImage.Source = null;
            LogoPreviewImage.Visibility = Visibility.Collapsed;
            LogoPreviewDefault.Visibility = Visibility.Visible;
        }

        var hero = ImageLoader.Load(settings.HeroImagePath, 1400);
        HeroImage.Source = hero;
        HeroPreviewImage.Source = hero;
        HeroPreviewEmpty.Visibility = hero is null ? Visibility.Visible : Visibility.Collapsed;

        RebuildGallery();
    }

    private static string? PickImage()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose an image",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp"
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private static string CopyBranding(string source, string prefix)
    {
        var directory = IOPath.Combine(AppPaths.Root, "branding");
        Directory.CreateDirectory(directory);
        var target = IOPath.Combine(directory, $"{prefix}-{DateTime.Now:yyyyMMddHHmmssfff}{IOPath.GetExtension(source)}");
        File.Copy(source, target, true);
        return target;
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton button || button.Tag is not string tag)
        {
            return;
        }

        ShowPage(tag);
    }

    private void ShowPage(string tag)
    {
        if (tag == "Ads" && !BuildInfo.HasAdsTab)
        {
            tag = "Studio";
        }

        if (tag == "Advanced" && !BuildInfo.HasAdvanced)
        {
            tag = "Studio";
        }

        StudioPage.Visibility = tag == "Studio" ? Visibility.Visible : Visibility.Collapsed;
        SetupPage.Visibility = tag == "Setup" ? Visibility.Visible : Visibility.Collapsed;
        StylePage.Visibility = tag == "Style" ? Visibility.Visible : Visibility.Collapsed;
        AdsPage.Visibility = tag == "Ads" ? Visibility.Visible : Visibility.Collapsed;
        AdvancedPage.Visibility = tag == "Advanced" ? Visibility.Visible : Visibility.Collapsed;
        AboutPage.Visibility = tag == "About" ? Visibility.Visible : Visibility.Collapsed;

        (PageTitle.Text, PageSubtitle.Text) = tag switch
        {
            "Setup" => ("Setup", "Engine, mic, shortcut, output, and tray behavior."),
            "Style" => ("Style", "Custom logo, backdrop, and the Tomato wall."),
            "Ads" => ("Tomato Ads", "Your videos, played as skippable ads."),
            "Advanced" => ("Advanced", "Diagnostics, paths, and developer tools."),
            "About" => ("About", "The story, the credits, the disclaimers."),
            _ => ("Studio", "Your voice, your machine, your moment.")
        };

        if (tag == "Ads")
        {
            RebuildAds();
        }
        else if (tag == "Advanced")
        {
            RefreshAdvanced();
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        try
        {
            DragMove();
        }
        catch
        {
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.Settings.MinimizeToTray)
        {
            HideToTray("Tomato Flow is still in the tray.");
        }
        else
        {
            WindowState = WindowState.Minimized;
        }
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleMaximize();
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        var maximized = WindowState == WindowState.Maximized;
        WindowBorder.CornerRadius = maximized ? new CornerRadius(0) : new CornerRadius(18);
        MaximizeButton.Content = maximized ? "\uE923" : "\uE922";

        if (WindowState == WindowState.Minimized && _settings.Settings.MinimizeToTray)
        {
            HideToTray("Tomato Flow is still in the tray.");
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_forceClose || !_settings.Settings.CloseToTray)
        {
            return;
        }

        e.Cancel = true;
        HideToTray("Tomato Flow is still running in the tray.");
    }

    private void HideToTray(string message)
    {
        Hide();
        if (!_trayHintShown)
        {
            _trayHintShown = true;
            _tray.Balloon("Tomato Flow", message);
        }
    }

    public void RestoreFromTray()
    {
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    public void CloseForExit()
    {
        _forceClose = true;
        Close();
    }

    private void Toast(string message)
    {
        ToastText.Text = message;
        ToastBorder.Visibility = Visibility.Visible;
        ToastBorder.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void HideToast()
    {
        _toastTimer.Stop();
        var animation = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(240));
        animation.Completed += (_, _) => ToastBorder.Visibility = Visibility.Collapsed;
        ToastBorder.BeginAnimation(OpacityProperty, animation);
    }

    private static void RunOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.Invoke(action);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _waveTimer.Stop();
        _toastTimer.Stop();
        _modelCts?.Cancel();
        _hotkey?.Dispose();
        _tray.Dispose();
        _adWindow?.ForceClose();
        base.OnClosed(e);

        if (!_forceClose && !_settings.Settings.CloseToTray)
        {
            Dispatcher.BeginInvoke(new Action(() => ((App)Application.Current).RequestExit()));
        }
    }
}
