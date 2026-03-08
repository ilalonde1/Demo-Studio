using System.Globalization;
using System.Windows.Data;

namespace DemoStudio.Desktop.App;

public sealed class DurationToWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double seconds || double.IsNaN(seconds) || seconds < 0)
        {
            return 120d;
        }

        // Scale duration into a readable timeline segment width.
        var width = 120d + (seconds * 8d);
        if (width < 120d)
        {
            return 120d;
        }

        if (width > 420d)
        {
            return 420d;
        }

        return width;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
