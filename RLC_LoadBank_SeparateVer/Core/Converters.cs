using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using RLC_LoadBank_SeparateVer.Models;

namespace RLC_LoadBank_SeparateVer.Core
{
    /// <summary>Shared palette (kept in code so converters and styles agree).</summary>
    public static class Palette
    {
        public static readonly Color Ink = Color.FromRgb(0x2D, 0x33, 0x3F);
        public static readonly Color Green = Color.FromRgb(0x27, 0xA8, 0x6A);
        public static readonly Color Orange = Color.FromRgb(0xE8, 0x94, 0x3A);
        public static readonly Color Red = Color.FromRgb(0xE0, 0x4F, 0x4F);
        public static readonly Color Grey = Color.FromRgb(0xAD, 0xB4, 0xBF);
        public static readonly Color Blue = Color.FromRgb(0x2F, 0x6F, 0xE0);
    }

    /// <summary>McState -> fill brush for the diagram circles / legend.</summary>
    public class McStateToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            var color = value is McState s
                ? s switch
                {
                    McState.On => Palette.Green,
                    McState.CommWait => Palette.Orange,
                    McState.Trip => Palette.Red,
                    McState.Alarm => Palette.Red,
                    _ => Palette.Grey,
                }
                : Palette.Grey;
            return new SolidColorBrush(color);
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }

    /// <summary>ConnState -> status brush (green connected / grey / red).</summary>
    public class ConnStateToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            var color = value is ConnState s
                ? s switch
                {
                    ConnState.Connected => Palette.Green,
                    ConnState.Connecting => Palette.Orange,
                    ConnState.Error => Palette.Red,
                    _ => Palette.Grey,
                }
                : Palette.Grey;
            return new SolidColorBrush(color);
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }

    /// <summary>AlarmLevel -> badge brush (INFO blue / ALARM orange / TRIP red).</summary>
    public class AlarmLevelToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            var color = value is AlarmLevel l
                ? l switch
                {
                    AlarmLevel.Trip => Palette.Red,
                    AlarmLevel.Alarm => Palette.Orange,
                    _ => Palette.Blue,
                }
                : Palette.Blue;
            return new SolidColorBrush(color);
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }

    /// <summary>bool -> Visibility (true = Visible). Param "invert" flips it.</summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            bool b = value is bool v && v;
            if (p is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase)) b = !b;
            return b ? Visibility.Visible : Visibility.Collapsed;
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }

    /// <summary>Returns true when the bound enum equals the ConverterParameter.</summary>
    public class EnumEqualsConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c) =>
            value != null && p != null && value.ToString() == p.ToString();
        public object ConvertBack(object v, Type t, object p, CultureInfo c) =>
            (v is bool b && b && p != null) ? Enum.Parse(t, p.ToString()) : Binding.DoNothing;
    }

    /// <summary>
    /// bool → SolidColorBrush.
    /// ConverterParameter = "trueHex|falseHex"  e.g. "#27A86A|#E04F4F"
    /// </summary>
    public class BoolToBgBrushConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            bool on  = value is bool b && b;
            string param = p as string ?? "#27A86A|#ADB4BF";
            var parts = param.Split('|');
            string hex = on ? parts[0] : (parts.Length > 1 ? parts[1] : parts[0]);
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                return new SolidColorBrush(color);
            }
            catch { return new SolidColorBrush(Colors.Transparent); }
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }

    /// <summary>
    /// bool → string label.
    /// ConverterParameter = "trueText|falseText"  e.g. "정상|동작"
    /// </summary>
    public class BoolToTextConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            bool on    = value is bool b && b;
            string param = p as string ?? "True|False";
            var parts  = param.Split('|');
            return on ? parts[0] : (parts.Length > 1 ? parts[1] : param);
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }
}
