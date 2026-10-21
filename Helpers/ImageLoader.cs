using System.Windows.Media.Imaging;

namespace TomatoFlow.Helpers;

public static class ImageLoader
{
    public static BitmapImage? Load(string? path, int decodePixelWidth = 0)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            if (decodePixelWidth > 0)
            {
                image.DecodePixelWidth = decodePixelWidth;
            }

            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }
}
