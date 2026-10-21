using System.Diagnostics;
using System.Text.RegularExpressions;
using TomatoFlow.Helpers;
using TomatoFlow.Models;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace TomatoFlow.Services;

public sealed record TranscriptionResult(string Text, string Language, TimeSpan Duration);

public sealed class TranscriptionService : IDisposable
{
    private static readonly Regex MultiWhitespace = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex SoundMarker = new(@"\[[^\]]*\]|\([^\)]*(?:music|applause|laughter|silence)[^\)]*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex FillerWords = new(@"\b(?:um+|uh+|erm+|hmm+)\b[,]?\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ModelManager _models;
    private WhisperFactory? _factory;
    private string? _loadedPath;
    private bool _disposed;

    public TranscriptionService(ModelManager models)
    {
        _models = models;
    }

    public string ActiveRuntime { get; private set; } = "Not loaded";
    public string ActiveModel { get; private set; } = "";

    public async Task<bool> PreloadAsync(ModelDefinition model, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureFactory(model);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Could not preload the speech engine.", ex);
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<TranscriptionResult> TranscribeAsync(
        string wavPath,
        ModelDefinition model,
        string language,
        string customVocabulary,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureFactory(model);
            var factory = _factory ?? throw new InvalidOperationException("Speech engine is not ready.");
            var builder = factory.CreateBuilder()
                .WithThreads(Math.Max(2, Math.Min(8, Environment.ProcessorCount - 1)))
                .WithTemperature(0f)
                .WithNoContext();

            if (string.Equals(language, "auto", StringComparison.OrdinalIgnoreCase))
            {
                builder.WithLanguageDetection();
            }
            else
            {
                builder.WithLanguage(string.IsNullOrWhiteSpace(language) ? "en" : language);
            }

            var prompt = BuildPrompt(customVocabulary);
            if (!string.IsNullOrWhiteSpace(prompt))
            {
                builder.WithPrompt(prompt);
            }

            var stopwatch = Stopwatch.StartNew();
            var segments = new List<string>();
            var detectedLanguage = string.IsNullOrWhiteSpace(language) ? "auto" : language;

            using var processor = builder.Build();
            await using var file = File.OpenRead(wavPath);
            await foreach (var segment in processor.ProcessAsync(file, cancellationToken))
            {
                if (!string.IsNullOrWhiteSpace(segment.Language))
                {
                    detectedLanguage = segment.Language;
                }

                var segmentText = segment.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(segmentText))
                {
                    segments.Add(segmentText);
                }
            }

            stopwatch.Stop();
            var text = CleanText(string.Join(" ", segments));
            return new TranscriptionResult(text, detectedLanguage, stopwatch.Elapsed);
        }
        finally
        {
            _gate.Release();
        }
    }

    public static string CleanText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var cleaned = SoundMarker.Replace(text, " ");
        cleaned = MultiWhitespace.Replace(cleaned, " ").Trim();
        return cleaned;
    }

    public static string PostProcess(string text, AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var result = text.Trim();
        if (settings.RemoveFillers)
        {
            result = FillerWords.Replace(result, "");
            result = MultiWhitespace.Replace(result, " ").Trim();
        }

        if (settings.AutoCapitalize && result.Length > 0)
        {
            result = char.ToUpperInvariant(result[0]) + result[1..];
        }

        return result;
    }

    private void EnsureFactory(ModelDefinition model)
    {
        var path = _models.GetModelPath(model);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The selected model is not installed.", path);
        }

        if (_factory is not null && string.Equals(_loadedPath, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _factory?.Dispose();
        _factory = null;
        _loadedPath = null;
        ActiveRuntime = "Loading";
        ActiveModel = model.Name;

        var stopwatch = Stopwatch.StartNew();
        _factory = WhisperFactory.FromPath(path);
        _loadedPath = path;
        stopwatch.Stop();
        ActiveRuntime = DetectRuntime();
        Log.Info($"Loaded {model.Id} on {ActiveRuntime} in {stopwatch.ElapsedMilliseconds} ms.");
    }

    private static string DetectRuntime()
    {
        try
        {
            return RuntimeOptions.LoadedLibrary switch
            {
                RuntimeLibrary.Cuda or RuntimeLibrary.Cuda12 => "CUDA",
                RuntimeLibrary.Vulkan => "Vulkan",
                RuntimeLibrary.CpuNoAvx => "CPU (no AVX)",
                _ => "CPU"
            };
        }
        catch
        {
            return "CPU";
        }
    }

    private static string BuildPrompt(string customVocabulary)
    {
        if (string.IsNullOrWhiteSpace(customVocabulary))
        {
            return "";
        }

        var words = customVocabulary
            .Replace("\r", " ")
            .Split(new[] { '\n', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(80)
            .ToArray();
        return words.Length == 0 ? "" : $"Vocabulary: {string.Join(", ", words)}.";
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _factory?.Dispose();
        _gate.Dispose();
    }
}
