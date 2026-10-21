namespace TomatoFlow.Models;

public sealed record ModelDefinition(
    string Id,
    string Name,
    string FileName,
    long SizeBytes,
    string Description,
    string Speed,
    string Accuracy,
    bool EnglishOnly,
    bool Recommended)
{
    public string SizeLabel => SizeBytes switch
    {
        >= 1_000_000_000 => $"{SizeBytes / 1_000_000_000d:0.0} GB",
        _ => $"{SizeBytes / 1_000_000d:0} MB"
    };

    public string DownloadUrl => $"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/{FileName}";
}

public static class ModelCatalog
{
    public static IReadOnlyList<ModelDefinition> All { get; } = new List<ModelDefinition>
    {
        new(
            "turbo",
            "Large v3 Turbo  -  Q5",
            "ggml-large-v3-turbo-q5_0.bin",
            574_041_195,
            "The flagship. Huge accuracy, multilingual, and fast enough to feel instant on a good GPU.",
            "Fast",
            "Best",
            false,
            true),
        new(
            "medium.en",
            "Medium English  -  Q5",
            "ggml-medium.en-q5_0.bin",
            539_225_533,
            "Excellent English accuracy for machines without a strong GPU.",
            "Balanced",
            "High",
            true,
            false),
        new(
            "small.en",
            "Small English  -  Q5",
            "ggml-small.en-q5_1.bin",
            190_098_681,
            "A great lightweight daily driver. Snappy on almost anything.",
            "Very fast",
            "Good",
            true,
            false),
        new(
            "base.en",
            "Base English  -  Q5",
            "ggml-base.en-q5_1.bin",
            59_721_011,
            "Tiny and quick. Perfect for older laptops or a quick first run.",
            "Instant",
            "Decent",
            true,
            false),
        new(
            "tiny.en",
            "Tiny English  -  Q5",
            "ggml-tiny.en-q5_1.bin",
            32_166_155,
            "The emergency backup. Smallest download, lowest accuracy.",
            "Instant",
            "Light",
            true,
            false)
    };

    public static ModelDefinition Get(string id)
    {
        return All.FirstOrDefault(model => model.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
               ?? All[0];
    }

    public static ModelDefinition RecommendedFor(HardwareInfo hardware)
    {
        return hardware.BestModelId == "turbo" ? All[0] : hardware.BestModelId == "small.en" ? All[2] : All[3];
    }
}
