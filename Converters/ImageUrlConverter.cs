using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Lumen.Converters;

public sealed class ImageUrlConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        var text = value as string;
        if (string.IsNullOrWhiteSpace(text))
            return null;

        return Uri.TryCreate(text, UriKind.Absolute, out var uri)
            ? new BitmapImage(uri)
            : null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
