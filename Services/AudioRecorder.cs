using System.Diagnostics;
using TomatoFlow.Helpers;
using NAudio.Wave;

namespace TomatoFlow.Services;

public sealed record MicrophoneDevice(int Index, string Name)
{
    public override string ToString() => Name;
}

public sealed class AudioRecorder : IDisposable
{
    private readonly object _gate = new();
    private WaveInEvent? _waveIn;
    private WaveFileWriter? _writer;
    private string? _filePath;
    private Stopwatch? _stopwatch;
    private float _smoothedLevel;
    private bool _disposed;

    public event EventHandler<float>? LevelChanged;

    public bool IsRecording { get; private set; }
    public double MaxLevel { get; private set; }
    public TimeSpan Duration => _stopwatch?.Elapsed ?? TimeSpan.Zero;

    public static IReadOnlyList<MicrophoneDevice> GetDevices()
    {
        var devices = new List<MicrophoneDevice>();
        try
        {
            for (var i = 0; i < WaveInEvent.DeviceCount; i++)
            {
                var capabilities = WaveInEvent.GetCapabilities(i);
                devices.Add(new MicrophoneDevice(i, capabilities.ProductName));
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not enumerate microphones.", ex);
        }

        return devices;
    }

    public void Start(int deviceIndex)
    {
        lock (_gate)
        {
            if (IsRecording)
            {
                throw new InvalidOperationException("Already recording.");
            }

            AppPaths.EnsureCreated();
            var deviceCount = WaveInEvent.DeviceCount;
            if (deviceCount <= 0)
            {
                throw new InvalidOperationException("No microphone was found.");
            }

            if (deviceIndex < 0 || deviceIndex >= deviceCount)
            {
                deviceIndex = 0;
            }

            var format = new WaveFormat(16000, 16, 1);
            _filePath = Path.Combine(AppPaths.Temp, $"recording-{DateTime.Now:yyyyMMdd-HHmmss-fff}.wav");
            _writer = new WaveFileWriter(_filePath, format);
            _waveIn = new WaveInEvent
            {
                DeviceNumber = deviceIndex,
                WaveFormat = format,
                BufferMilliseconds = 100,
                NumberOfBuffers = 4
            };
            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.RecordingStopped += OnRecordingStopped;
            _smoothedLevel = 0;
            MaxLevel = 0;
            _stopwatch = Stopwatch.StartNew();
            IsRecording = true;
            _waveIn.StartRecording();
        }
    }

    public async Task<string?> StopAsync()
    {
        WaveInEvent? waveIn;
        TaskCompletionSource<bool>? stopped;
        lock (_gate)
        {
            if (!IsRecording || _waveIn is null)
            {
                return null;
            }

            IsRecording = false;
            waveIn = _waveIn;
            _stopwatch?.Stop();
            stopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            waveIn.RecordingStopped += (_, _) => stopped.TrySetResult(true);
        }

        try
        {
            waveIn.StopRecording();
        }
        catch (Exception ex)
        {
            Log.Error("Could not stop recording cleanly.", ex);
            stopped.TrySetResult(false);
        }

        await Task.WhenAny(stopped.Task, Task.Delay(3000));

        lock (_gate)
        {
            try
            {
                _writer?.Flush();
                _writer?.Dispose();
            }
            catch (Exception ex)
            {
                Log.Error("Could not finalize recording.", ex);
            }

            _writer = null;
            try
            {
                waveIn.Dispose();
            }
            catch
            {
            }

            _waveIn = null;
            var path = _filePath;
            _filePath = null;
            LevelChanged?.Invoke(this, 0);
            return path;
        }
    }

    public void Cancel()
    {
        string? path;
        lock (_gate)
        {
            path = _filePath;
        }

        _ = StopAsync().ContinueWith(_ =>
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                TryDelete(path);
            }
        }, TaskScheduler.Default);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            if (IsRecording)
            {
                _waveIn?.StopRecording();
            }

            _writer?.Dispose();
            _waveIn?.Dispose();
        }
        catch
        {
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs args)
    {
        WaveFileWriter? writer;
        lock (_gate)
        {
            writer = _writer;
        }

        try
        {
            writer?.Write(args.Buffer, 0, args.BytesRecorded);
        }
        catch (Exception ex)
        {
            Log.Error("Could not write audio buffer.", ex);
        }

        var peak = 0f;
        for (var i = 0; i + 1 < args.BytesRecorded; i += 2)
        {
            var sample = BitConverter.ToInt16(args.Buffer, i) / 32768f;
            var absolute = Math.Abs(sample);
            if (absolute > peak)
            {
                peak = absolute;
            }
        }

        MaxLevel = Math.Max(MaxLevel, peak);
        _smoothedLevel = (_smoothedLevel * 0.55f) + (peak * 0.45f);
        LevelChanged?.Invoke(this, _smoothedLevel);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs args)
    {
        if (args.Exception is not null)
        {
            Log.Error("Microphone recording stopped unexpectedly.", args.Exception);
        }
    }

    private static void TryDelete(string path)
    {
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
}
