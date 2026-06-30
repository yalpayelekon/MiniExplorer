using System.Globalization;
using System.Windows.Data;
using MiniExplorer.Services;

namespace MiniExplorer.Converters;

public sealed class LocConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = parameter as string;
        return string.IsNullOrEmpty(key) ? string.Empty : LocalizationService.Get(key);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
