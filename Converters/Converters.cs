using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using AutoClickerPro.Models;

namespace AutoClickerPro.Converters;

/// <summary>Maps a MacroState to a status-dot brush color (green/amber/gray).</summary>
public sealed class StateToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Running = new(Color.FromRgb(0x4C, 0xD9, 0x64));
    private static readonly SolidColorBrush Paused = new(Color.FromRgb(0xF5, 0xA6, 0x23));
    private static readonly SolidColorBrush Stopped = new(Color.FromRgb(0x6B, 0x70, 0x7A));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        MacroState.Running => Running,
        MacroState.Paused => Paused,
        _ => Stopped
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Generic enum-to-bool converter for binding radio buttons directly to an enum value.</summary>
public sealed class EnumToBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || parameter == null) return false;
        return value.ToString() == parameter.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b && b && parameter != null)
            return Enum.Parse(targetType, parameter.ToString()!);
        return Binding.DoNothing;
    }
}

/// <summary>Formats a raw CPS double as "12.3 CPS" for live display.</summary>
public sealed class CpsDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double cps = value is double d ? d : 0;
        return $"{cps:0.0} CPS";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
