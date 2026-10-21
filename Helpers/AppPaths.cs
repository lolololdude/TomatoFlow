namespace TomatoFlow.Helpers;

public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TomatoFlow");

    public static string Models { get; } = Path.Combine(Root, "models");
    public static string Temp { get; } = Path.Combine(Root, "temp");
    public static string Logs { get; } = Path.Combine(Root, "logs");
    public static string SettingsFile { get; } = Path.Combine(Root, "settings.json");
    public static string LogFile { get; } = Path.Combine(Logs, "tomatoflow.log");
    public static string BundledAssets { get; } = Path.Combine(AppContext.BaseDirectory, "Assets");
    public static string BundledAds { get; } = Path.Combine(BundledAssets, "Ads");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Models);
        Directory.CreateDirectory(Temp);
        Directory.CreateDirectory(Logs);
    }

    public static string ModelPath(string fileName)
    {
        return Path.Combine(Models, fileName);
    }
}
