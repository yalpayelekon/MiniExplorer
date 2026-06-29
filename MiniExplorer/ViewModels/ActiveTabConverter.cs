using System.Globalization;
using System.Windows.Data;
using MiniExplorer.ViewModels;

namespace MiniExplorer.ViewModels;

public sealed class ActiveTabConverter : IMultiValueConverter
{
    public static ActiveTabConverter Instance { get; } = new();

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not TabViewModel tab || values[1] is not TabViewModel active)
        {
            return false;
        }

        return ReferenceEquals(tab, active);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
