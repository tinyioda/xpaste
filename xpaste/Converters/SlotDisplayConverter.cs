using System.Globalization;
using System.Windows.Data;

namespace xpaste.Converters;

/// <summary>
/// Converts an integer slot number (1–10) to its human-readable hotkey label
/// (e.g. <c>5</c> → <c>"Ctrl+Shift+5"</c>, <c>10</c> → <c>"Ctrl+Shift+0"</c>).
/// Returns <c>"Unassigned"</c> for slot 0 or any out-of-range value.
/// </summary>
public class SlotDisplayConverter : IValueConverter
{
    /// <inheritdoc/>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int slot && slot >= 1 && slot <= 10)
        {
            return $"Ctrl+Shift+{(slot == 10 ? "0" : slot.ToString())}";
        }

        return "Unassigned";
    }

    /// <inheritdoc/>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
