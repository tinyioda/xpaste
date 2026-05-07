using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace xpaste.Converters;

/// <summary>
/// Multi-type WPF value converter that maps truthy values to <see cref="Visibility.Visible"/>
/// and falsy values to <see cref="Visibility.Collapsed"/>.
/// <list type="bullet">
///   <item><description><c>bool</c> — <c>true</c> → Visible</description></item>
///   <item><description><c>string</c> — non-empty → Visible</description></item>
///   <item><description>anything else — non-null → Visible</description></item>
/// </list>
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    /// <inheritdoc/>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool visible = value switch {
            bool b => b,
            string s => !string.IsNullOrEmpty(s),
            _ => value != null
        };
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <inheritdoc/>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
