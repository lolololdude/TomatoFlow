using System.Threading;
using System.Windows;
using System.Windows.Threading;
using TomatoFlow.Helpers;
using TomatoFlow.Services;

namespace TomatoFlow;

public partial class App : Application
{
    private Mutex? _mutex;
    private MainWindow? _main;
    private bool _exiting;

    public static App Host => (App)Current;

    public SettingsService SettingsService { get; private set; } = null!;
    public ModelManager Models { get; private set; } = null!;
    public TranscriptionService Transcriber { get; private set; } = null!;
    public AudioRecorder Recorder { get; private set; } = null!;
    public DictationController Dictation { get; private set; } = null!;
    public AdService Ads { get; private set; } = null!;
    public FloatingOrbWindow? Orb { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, "TomatoFlow.SingleInstance", out var created);
        if (!created)
        {
            MessageBox.Show("Tomato Flow is already running. Look for the tomato in your tray.", "Tomato Flow",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        AppPaths.EnsureCreated();
        AdFolders.EnsureCreated();
        Log.Info("Tomato Flow starting.");

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        SettingsService = new SettingsService();
        var hardware = HardwareDetector.Detect();
        Models = new ModelManager(SettingsService, hardware);
        Models.SelectRecommendedIfUnset();
        Transcriber = new TranscriptionService(Models);
        Recorder = new AudioRecorder();
        Ads = new AdService(SettingsService);
        Dictation = new DictationController(SettingsService, Recorder, Transcriber, Models, new TextOutputService());
        AdWindow.Prewarm();

        if (SettingsService.Settings.ShowFloatingOrb)
        {
            CreateOrb();
        }

        _main = new MainWindow(SettingsService, Models, Transcriber, Dictation, Ads);

        var startMinimized = e.Args.Contains("--minimized") || SettingsService.Settings.LaunchMinimized;
        _main.Show();
        if (startMinimized)
        {
            _main.Hide();
        }
    }

    private void CreateOrb()
    {
        if (Orb is not null)
        {
            return;
        }

        Orb = new FloatingOrbWindow(SettingsService);
        Orb.ToggleRequested += async (_, _) => await Dictation.ToggleAsync();
        Orb.OpenRequested += (_, _) => _main?.RestoreFromTray();
        Orb.HideRequested += (_, _) => HideOrb();
        Orb.ApplyBranding(SettingsService.Settings);
        Orb.Show();
    }

    public void SyncOrbVisibility()
    {
        if (SettingsService.Settings.ShowFloatingOrb)
        {
            CreateOrb();
            Orb?.Show();
        }
        else
        {
            Orb?.Hide();
        }
    }

    public void ApplyBrandingToOrb()
    {
        Orb?.ApplyBranding(SettingsService.Settings);
    }

    public void HideOrb()
    {
        SettingsService.Settings.ShowFloatingOrb = false;
        SettingsService.Save();
        SyncOrbVisibility();
        _main?.SyncOrbSetting();
    }

    public void ResetOrbPosition()
    {
        if (Orb is null)
        {
            CreateOrb();
        }

        Orb?.ResetPosition();
    }

    public void RequestExit()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        Log.Info("Tomato Flow exiting.");
        try
        {
            Orb?.Close();
            _main?.CloseForExit();
            Dictation.Dispose();
            AdWindow.Shutdown();
        }
        catch (Exception ex)
        {
            Log.Error("Error while shutting down.", ex);
        }

        Shutdown();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled dispatcher exception.", e.Exception);
        MessageBox.Show(
            "Tomato Flow hit an unexpected snag. It has been written to the log.\n\n" + e.Exception.Message,
            "Tomato Flow",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _mutex?.ReleaseMutex();
        }
        catch
        {
        }

        _mutex?.Dispose();
        base.OnExit(e);
    }
}
