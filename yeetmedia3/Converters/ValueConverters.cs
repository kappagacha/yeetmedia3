using System.Globalization;

namespace Yeetmedia3.Converters;

public class InverseBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
            return !boolValue;
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
            return !boolValue;
        return false;
    }
}

public class StringToBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return !string.IsNullOrWhiteSpace(value as string);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class FileSizeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null) return "—";

        if (long.TryParse(value.ToString(), out long bytes))
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
        return "—";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class MimeTypeToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string mimeType = value as string ?? "";

        if (mimeType.Contains("folder"))
            return "📁";
        else if (mimeType.Contains("image"))
            return "🖼️";
        else if (mimeType.Contains("video"))
            return "🎬";
        else if (mimeType.Contains("audio"))
            return "🎵";
        else if (mimeType.Contains("pdf"))
            return "📄";
        else if (mimeType.Contains("spreadsheet") || mimeType.Contains("excel"))
            return "📊";
        else if (mimeType.Contains("presentation") || mimeType.Contains("powerpoint"))
            return "📽️";
        else if (mimeType.Contains("document") || mimeType.Contains("word") || mimeType.Contains("text"))
            return "📝";
        else if (mimeType.Contains("zip") || mimeType.Contains("compressed"))
            return "🗜️";
        else
            return "📎";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}