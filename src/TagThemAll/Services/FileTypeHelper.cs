using System;
using System.Collections.Generic;
using TagThemAll.Models;

namespace TagThemAll.Services;

public static class FileTypeHelper
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff", ".tif", ".svg",
        ".ico", ".heic", ".heif", ".raw", ".cr2", ".nef", ".arw", ".dng", ".psd"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".mov", ".avi", ".wmv", ".flv", ".webm", ".m4v", ".mpeg", ".mpg", ".3gp", ".ts"
    };

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".flac", ".aac", ".ogg", ".wma", ".m4a", ".aiff", ".alac", ".opus"
    };

    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".rtf",
        ".odt", ".ods", ".odp", ".md", ".csv", ".json", ".xml", ".html"
    };

    public static FileFilterType GetFilterCategory(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) return FileFilterType.Other;
        if (!extension.StartsWith('.')) extension = "." + extension;

        if (ImageExtensions.Contains(extension)) return FileFilterType.Images;
        if (VideoExtensions.Contains(extension)) return FileFilterType.Videos;
        if (AudioExtensions.Contains(extension)) return FileFilterType.Audio;
        if (DocumentExtensions.Contains(extension)) return FileFilterType.Documents;

        return FileFilterType.Other;
    }

    public static string FormatFileSize(long bytes)
    {
        string[] sizes = { "o", "Ko", "Mo", "Go", "To" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}
