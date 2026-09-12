using Microsoft.UI.Xaml.Media.Imaging;

namespace Lumen.Services;

public static class ArtworkPreloader
{
    private static readonly List<BitmapImage> Warm = [];
    private const int MaxWarmImages = 48;

    public static void Preload(IEnumerable<string?> urls)
    {
        foreach (var url in urls.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) continue;
            try
            {
                Warm.Add(new BitmapImage { UriSource = uri });
                while (Warm.Count > MaxWarmImages)
                    Warm.RemoveAt(0);
            }
            catch { }
        }
    }
}
