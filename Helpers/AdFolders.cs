using System.IO;
using TomatoFlow.Models;

namespace TomatoFlow.Helpers;

public sealed record AdFolder(string Path, string Label, bool IsUserFolder);

public static class AdFolders
{
    private static readonly string[] VideoExtensions =
    {
        ".mp4", ".m4v", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".mpg", ".mpeg"
    };

    public static string UserVideos
    {
        get
        {
            var videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            if (string.IsNullOrWhiteSpace(videos))
            {
                videos = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            }

            return Path.Combine(videos, "Tomato Ads");
        }
    }

    public static string AppDataAds => Path.Combine(AppPaths.Root, "ads");

    public static string PortableAds => Path.Combine(AppContext.BaseDirectory, "Ads");

    public static int BundledCount
    {
        get
        {
            try
            {
                if (!Directory.Exists(AppPaths.BundledAds))
                {
                    return 0;
                }

                return Directory.EnumerateFiles(AppPaths.BundledAds, "*", SearchOption.AllDirectories).Count(IsVideo);
            }
            catch
            {
                return 0;
            }
        }
    }

    public static IReadOnlyList<AdFolder> All
    {
        get
        {
            var folders = new List<AdFolder>();
            if (BuildInfo.HasAllAdFolders)
            {
                folders.Add(new AdFolder(UserVideos, "My Videos / Tomato Ads", true));
                folders.Add(new AdFolder(AppDataAds, "Tomato Flow data / ads", true));
            }

            folders.Add(new AdFolder(PortableAds, "App folder / Ads", true));
            folders.Add(new AdFolder(AppPaths.BundledAds, "Bundled ads", false));
            return folders;
        }
    }

    public static void EnsureCreated()
    {
        foreach (var folder in All)
        {
            if (!folder.IsUserFolder)
            {
                continue;
            }

            try
            {
                Directory.CreateDirectory(folder.Path);
            }
            catch
            {
            }
        }
    }

    public static bool IsVideo(string path)
    {
        var extension = Path.GetExtension(path);
        return VideoExtensions.Any(ext => ext.Equals(extension, StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<(string File, AdFolder Folder)> Scan()
    {
        var found = new List<(string, AdFolder)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var folder in All)
        {
            try
            {
                if (!Directory.Exists(folder.Path))
                {
                    continue;
                }

                foreach (var file in Directory.EnumerateFiles(folder.Path, "*", SearchOption.AllDirectories))
                {
                    if (!IsVideo(file))
                    {
                        continue;
                    }

                    if (seen.Add(file))
                    {
                        found.Add((file, folder));
                    }
                }
            }
            catch
            {
            }
        }

        return found;
    }
}
