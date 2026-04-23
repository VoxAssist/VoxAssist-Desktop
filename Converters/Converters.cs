using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace VoxAssist.Desktop.Converters;

public class BoolToConnectedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true ? "Connected" : "Disconnected";
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class BoolToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true ? Brushes.Green : Brushes.Red;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class BoolToFreezeTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true ? "Unfreeze" : "Freeze";
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class CompressionNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is VoxAssist.Desktop.Models.CompressionType type)
        {
            return type switch
            {
                VoxAssist.Desktop.Models.CompressionType.None => "None (PCM)",
                VoxAssist.Desktop.Models.CompressionType.Flac => "FLAC",
                VoxAssist.Desktop.Models.CompressionType.G711 => "G.711 (μ-law)",
                _ => type.ToString()
            };
        }
        return value?.ToString() ?? "";
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => throw new NotImplementedException();
}

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is false;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is false;
}

public class MicStatusColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value?.ToString() switch
        {
            "Listening..." => Brushes.Lime,
            "Processing..." => Brushes.Yellow,
            "Finalizing..." => Brushes.Orange,
            "Ready" => Brushes.White,
            _ => Brushes.Gray
        };
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class PrefixConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string? text = null;
        if (value is VoxAssist.Desktop.ViewModels.InteractionRecord record) text = record.DisplayMarkdown;
        else if (value is string s) text = s;

        if (text != null && text.Contains(": "))
        {
            return text.Substring(0, text.IndexOf(": ") + 1);
        }
        return "";
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class MessageConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string? text = null;
        if (value is VoxAssist.Desktop.ViewModels.InteractionRecord record) text = record.DisplayMarkdown;
        else if (value is string s) text = s;

        if (text != null)
        {
            if (text.Contains(": "))
            {
                return text.Substring(text.IndexOf(": ") + 1);
            }
            return text;
        }
        return "";
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class SaveButtonColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool canSave && canSave)
        {
            return Brush.Parse("#33AA33"); // Green
        }
        return Brush.Parse("#666666"); // Gray
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}
