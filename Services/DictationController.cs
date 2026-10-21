using System.Text.RegularExpressions;
using System.Windows;
using TomatoFlow.Helpers;
using TomatoFlow.Models;

namespace TomatoFlow.Services;

public enum DictationState
{
    Idle,
    Recording,
    Transcribing
}

public sealed record TranscriptReadyEventArgs(
    string Text,
    bool Pasted,
    bool Copied,
    double DurationSeconds,
    TimeSpan TranscriptionTime,
    TranscriptEntry Entry);

public sealed class DictationController : IDisposable
{
    private static readonly Regex WordSplit = new(@"\S+", RegexOptions.Compiled);
    private readonly SettingsService _settings;
    private readonly AudioRecorder _recorder;
    private readonly TranscriptionService _transcriber;
    private readonly ModelManager _models;
    private readonly TextOutputService _output;
    private readonly SemaphoreSlim _transition = new(1, 1);
    private IntPtr _targetWindow;
    private DateTimeOffset _startedAt;

    public DictationController(
        SettingsService settings,
        AudioRecorder recorder,
        TranscriptionService transcriber,
        ModelManager models,
        TextOutputService output)
    {
        _settings = settings;
        _recorder = recorder;
        _transcriber = transcriber;
        _models = models;
        _output = output;
        _recorder.LevelChanged += (_, level) => LevelChanged?.Invoke(this, level);
    }

    public DictationState State { get; private set; } = DictationState.Idle;
    public double LastLevel { get; private set; }
    public TimeSpan LastRecordingDuration { get; private set; }
    public TimeSpan LastTranscriptionDuration { get; private set; }

    public event EventHandler<DictationState>? StateChanged;
    public event EventHandler<float>? LevelChanged;
    public event EventHandler<TranscriptReadyEventArgs>? TranscriptReady;
    public event EventHandler<string>? StatusMessage;
    public event EventHandler<string>? Error;

    public async Task ToggleAsync()
    {
        if (State == DictationState.Recording)
        {
            await StopAsync();
            return;
        }

        if (State == DictationState.Idle)
        {
            await StartAsync();
        }
    }

    public async Task StartAsync()
    {
        await _transition.WaitAsync();
        try
        {
            if (State != DictationState.Idle)
            {
                return;
            }

            var model = _models.SelectedModel;
            if (!_models.IsInstalled(model))
            {
                Error?.Invoke(this, "The model is still installing. Give the flow a moment.");
                return;
            }

            try
            {
                _targetWindow = NativeMethods.GetForegroundWindow();
                _recorder.Start(_settings.Settings.MicrophoneIndex);
                _startedAt = DateTimeOffset.Now;
                SetState(DictationState.Recording);
                StatusMessage?.Invoke(this, "Listening. Press the shortcut again when you are done.");
            }
            catch (Exception ex)
            {
                Log.Error("Could not start recording.", ex);
                Error?.Invoke(this, ex.Message);
                SetState(DictationState.Idle);
            }
        }
        finally
        {
            _transition.Release();
        }
    }

    public async Task StopAsync()
    {
        await _transition.WaitAsync();
        try
        {
            if (State != DictationState.Recording)
            {
                return;
            }

            SetState(DictationState.Transcribing);
            StatusMessage?.Invoke(this, "Tomato Flow is decoding...");
            var duration = DateTimeOffset.Now - _startedAt;
            LastRecordingDuration = duration;
            var wavPath = await _recorder.StopAsync();
            var maxLevel = _recorder.MaxLevel;

            try
            {
                if (string.IsNullOrWhiteSpace(wavPath) || !File.Exists(wavPath))
                {
                    Error?.Invoke(this, "The microphone did not produce a recording.");
                    return;
                }

                if (duration.TotalSeconds < 0.28 || maxLevel < 0.006)
                {
                    StatusMessage?.Invoke(this, "No speech detected. Try again, a little closer to the mic.");
                    return;
                }

                var settings = _settings.Settings;
                var model = _models.SelectedModel;
                var result = await _transcriber.TranscribeAsync(
                    wavPath,
                    model,
                    settings.Language,
                    settings.CustomVocabulary);
                LastTranscriptionDuration = result.Duration;
                var text = TranscriptionService.PostProcess(result.Text, settings);

                if (string.IsNullOrWhiteSpace(text))
                {
                    StatusMessage?.Invoke(this, "No words this time. The mic heard vibes only.");
                    return;
                }

                var output = await _output.DeliverAsync(
                    text,
                    settings.OutputMode,
                    _targetWindow,
                    settings.RestoreClipboard);

                if (!output.Copied)
                {
                    Error?.Invoke(this, "Could not write to the clipboard. Another app may be holding it.");
                    return;
                }

                var words = WordSplit.Matches(text).Count;
                var entry = new TranscriptEntry
                {
                    Text = text,
                    CreatedAt = DateTimeOffset.Now,
                    DurationSeconds = duration.TotalSeconds,
                    ModelId = model.Id,
                    Pasted = output.Pasted,
                    WordCount = words
                };

                settings.History.Insert(0, entry);
                if (settings.History.Count > 80)
                {
                    settings.History.RemoveRange(80, settings.History.Count - 80);
                }

                settings.TotalWords += words;
                settings.TotalTranscriptions++;
                settings.TotalRecordingSeconds += duration.TotalSeconds;
                settings.TranscriptionsSinceAd++;
                _settings.Save();

                var status = output.Pasted
                    ? "Pasted into your app."
                    : settings.OutputMode == OutputMode.Clipboard
                        ? "Copied to clipboard."
                        : "Copied to clipboard. The target app could not be reached.";
                StatusMessage?.Invoke(this, status);
                TranscriptReady?.Invoke(
                    this,
                    new TranscriptReadyEventArgs(
                        text,
                        output.Pasted,
                        output.Copied,
                        duration.TotalSeconds,
                        result.Duration,
                        entry));
            }
            catch (Exception ex)
            {
                Log.Error("Transcription failed.", ex);
                Error?.Invoke(this, "The flow hit a snag: " + ex.Message);
            }
            finally
            {
                TryDelete(wavPath);
                SetState(DictationState.Idle);
            }
        }
        finally
        {
            _transition.Release();
        }
    }

    private void SetState(DictationState state)
    {
        State = state;
        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => StateChanged?.Invoke(this, state));
        }
        else
        {
            StateChanged?.Invoke(this, state);
        }
    }

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        _recorder.Dispose();
        _transcriber.Dispose();
        _transition.Dispose();
    }
}
