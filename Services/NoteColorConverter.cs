using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace StickyNotes.Services;

public sealed class NoteColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var color = value?.ToString()?.ToLowerInvariant();

        return color switch
        {
            "mint"   => new SolidColorBrush(Color.FromRgb(174, 229, 209)),
            "blue"   => new SolidColorBrush(Color.FromRgb(183, 220, 244)),
            "purple" => new SolidColorBrush(Color.FromRgb(214, 193, 246)),
            "pink"   => new SolidColorBrush(Color.FromRgb(248, 194, 207)),
            "peach"  => new SolidColorBrush(Color.FromRgb(255, 208, 166)),
            "green"  => new SolidColorBrush(Color.FromRgb(205, 232, 169)),
            "gray"   => new SolidColorBrush(Color.FromRgb(216, 221, 228)),
            _        => new SolidColorBrush(Color.FromRgb(255, 221, 105)),
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

