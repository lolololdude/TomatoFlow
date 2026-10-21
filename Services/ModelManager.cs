using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using TomatoFlow.Helpers;
using TomatoFlow.Models;

namespace TomatoFlow.Services;

public sealed record ModelDownloadProgress(long BytesReceived, long TotalBytes, double BytesPerSecond)
{
    public double Fraction => TotalBytes <= 0 ? 0 : Math.Clamp((double)BytesReceived / TotalBytes, 0, 1);
    public int Percent => (int)Math.Round(Fraction * 100);
    public string SpeedLabel => BytesPerSecond switch
    {
        >= 1_000_000 => $"{BytesPerSecond / 1_000_000d:0.0} MB/s",
        >= 1_000 => $"{BytesPerSecond / 1_000d:0} KB/s",
        _ => $"{BytesPerSecond:0} B/s"
    };
}

public sealed class ModelManager
{
    private readonly SettingsService _settings;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _downloadGate = new(1, 1);

    public ModelManager(SettingsService settings, HardwareInfo hardware)
    {
        _settings = settings;
        Hardware = hardware;
        _http = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("TomatoFlow/1.0");
    }

    public HardwareInfo Hardware { get; }

    public ModelDefinition Recommended => ModelCatalog.RecommendedFor(Hardware);

    public ModelDefinition SelectedModel
    {
        get
        {
            var selected = string.IsNullOrWhiteSpace(_settings.Settings.ModelId)
                ? Recommended
                : ModelCatalog.Get(_settings.Settings.ModelId);
            return selected;
        }
    }

    public string GetModelPath(ModelDefinition model)
    {
        return AppPaths.ModelPath(model.FileName);
    }

    public bool IsInstalled(ModelDefinition model)
    {
        try
        {
            var path = GetModelPath(model);
            return File.Exists(path) && new FileInfo(path).Length == model.SizeBytes;
        }
        catch
        {
            return false;
        }
    }

    public bool SelectedModelInstalled => IsInstalled(SelectedModel);

    public void SelectRecommendedIfUnset()
    {
        if (string.IsNullOrWhiteSpace(_settings.Settings.ModelId) || ModelCatalog.All.All(model => model.Id != _settings.Settings.ModelId))
        {
            _settings.Settings.ModelId = Recommended.Id;
            _settings.Save();
        }
    }

    public async Task<ModelDefinition> DownloadAsync(
        ModelDefinition model,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        await _downloadGate.WaitAsync(cancellationToken);
        try
        {
            AppPaths.EnsureCreated();
            var finalPath = GetModelPath(model);
            var partPath = finalPath + ".part";

            if (IsInstalled(model))
            {
                return model;
            }

            try
            {
                if (File.Exists(finalPath) && new FileInfo(finalPath).Length != model.SizeBytes)
                {
                    File.Delete(finalPath);
                }
            }
            catch
            {
            }

            var urls = new[]
            {
                model.DownloadUrl,
                $"https://hf-mirror.com/ggerganov/whisper.cpp/resolve/main/{model.FileName}"
            };

            Exception? lastError = null;
            foreach (var url in urls)
            {
                try
                {
                    await DownloadFromUrlAsync(url, partPath, model, progress, cancellationToken);
                    if (File.Exists(finalPath))
                    {
                        File.Delete(finalPath);
                    }

                    File.Move(partPath, finalPath);
                    Log.Info($"Installed model {model.Id} at {finalPath}");
                    return model;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    Log.Error($"Download failed for {model.Id} from {url}", ex);
                }
            }

            throw new InvalidOperationException(
                "The model download did not finish. Check your connection and try again.",
                lastError);
        }
        finally
        {
            _downloadGate.Release();
        }
    }

    public void Delete(ModelDefinition model)
    {
        try
        {
            var path = GetModelPath(model);
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            if (File.Exists(path + ".part"))
            {
                File.Delete(path + ".part");
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not delete model.", ex);
        }
    }

    private async Task DownloadFromUrlAsync(
        string url,
        string partPath,
        ModelDefinition model,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var existing = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;
        if (existing > model.SizeBytes)
        {
            File.Delete(partPath);
            existing = 0;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (existing > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existing, null);
        }

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && existing == model.SizeBytes)
        {
            return;
        }

        response.EnsureSuccessStatusCode();

        var append = existing > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (!append)
        {
            existing = 0;
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = new FileStream(
            partPath,
            append ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            128 * 1024,
            true);

        var buffer = new byte[128 * 1024];
        var total = model.SizeBytes;
        var received = existing;
        var stopwatch = Stopwatch.StartNew();
        var lastReport = TimeSpan.Zero;
        var lastBytes = received;

        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            received += read;

            var elapsed = stopwatch.Elapsed;
            if (elapsed - lastReport >= TimeSpan.FromMilliseconds(120))
            {
                var seconds = Math.Max(0.001, (elapsed - lastReport).TotalSeconds);
                var speed = (received - lastBytes) / seconds;
                progress?.Report(new ModelDownloadProgress(received, total, speed));
                lastReport = elapsed;
                lastBytes = received;
            }
        }

        await destination.FlushAsync(cancellationToken);
        progress?.Report(new ModelDownloadProgress(received, total, 0));

        if (received != model.SizeBytes)
        {
            throw new IOException($"Model file was incomplete ({received:N0} of {model.SizeBytes:N0} bytes).");
        }
    }
}
