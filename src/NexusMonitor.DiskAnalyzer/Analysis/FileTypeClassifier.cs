namespace NexusMonitor.DiskAnalyzer.Analysis;

public enum FileCategory
{
    Video, Audio, Image, Document, Archive, Code, Executable, Data, System, Other
}

public static class FileTypeClassifier
{
    private static readonly Dictionary<string, FileCategory> _map = new(StringComparer.OrdinalIgnoreCase)
    {
        // Video
        [".mp4"] = FileCategory.Video, [".mkv"] = FileCategory.Video,
        [".avi"] = FileCategory.Video, [".mov"] = FileCategory.Video,
        [".wmv"] = FileCategory.Video, [".flv"] = FileCategory.Video,
        [".webm"] = FileCategory.Video, [".m4v"] = FileCategory.Video,
        [".ts"] = FileCategory.Video, [".mts"] = FileCategory.Video,
        // Audio
        [".mp3"] = FileCategory.Audio, [".flac"] = FileCategory.Audio,
        [".aac"] = FileCategory.Audio, [".ogg"] = FileCategory.Audio,
        [".wav"] = FileCategory.Audio, [".wma"] = FileCategory.Audio,
        [".m4a"] = FileCategory.Audio, [".opus"] = FileCategory.Audio,
        // Image
        [".jpg"] = FileCategory.Image, [".jpeg"] = FileCategory.Image,
        [".png"] = FileCategory.Image, [".gif"] = FileCategory.Image,
        [".bmp"] = FileCategory.Image, [".tiff"] = FileCategory.Image,
        [".webp"] = FileCategory.Image, [".heic"] = FileCategory.Image,
        [".raw"] = FileCategory.Image, [".cr2"] = FileCategory.Image,
        [".svg"] = FileCategory.Image, [".ico"] = FileCategory.Image,
        // Document
        [".pdf"] = FileCategory.Document, [".doc"] = FileCategory.Document,
        [".docx"] = FileCategory.Document, [".xls"] = FileCategory.Document,
        [".xlsx"] = FileCategory.Document, [".ppt"] = FileCategory.Document,
        [".pptx"] = FileCategory.Document, [".txt"] = FileCategory.Document,
        [".rtf"] = FileCategory.Document, [".odt"] = FileCategory.Document,
        [".md"] = FileCategory.Document, [".epub"] = FileCategory.Document,
        // Archive
        [".zip"] = FileCategory.Archive, [".rar"] = FileCategory.Archive,
        [".7z"] = FileCategory.Archive, [".tar"] = FileCategory.Archive,
        [".gz"] = FileCategory.Archive, [".bz2"] = FileCategory.Archive,
        [".xz"] = FileCategory.Archive, [".iso"] = FileCategory.Archive,
        [".dmg"] = FileCategory.Archive, [".cab"] = FileCategory.Archive,
        // Code
        [".cs"] = FileCategory.Code, [".py"] = FileCategory.Code,
        [".js"] = FileCategory.Code, [".tsx"] = FileCategory.Code,
        [".cpp"] = FileCategory.Code, [".c"] = FileCategory.Code,
        [".h"] = FileCategory.Code, [".java"] = FileCategory.Code,
        [".rs"] = FileCategory.Code, [".go"] = FileCategory.Code,
        [".html"] = FileCategory.Code, [".css"] = FileCategory.Code,
        [".json"] = FileCategory.Code, [".xml"] = FileCategory.Code,
        [".sql"] = FileCategory.Code, [".sh"] = FileCategory.Code,
        // Executable
        [".exe"] = FileCategory.Executable, [".dll"] = FileCategory.Executable,
        [".msi"] = FileCategory.Executable, [".app"] = FileCategory.Executable,
        [".deb"] = FileCategory.Executable, [".rpm"] = FileCategory.Executable,
        [".apk"] = FileCategory.Executable, [".so"] = FileCategory.Executable,
        [".dylib"] = FileCategory.Executable,
        // Data
        [".db"] = FileCategory.Data, [".sqlite"] = FileCategory.Data,
        [".bak"] = FileCategory.Data, [".log"] = FileCategory.Data,
        [".dat"] = FileCategory.Data, [".csv"] = FileCategory.Data,
        // System
        [".sys"] = FileCategory.System, [".bin"] = FileCategory.System,
        [".tmp"] = FileCategory.System, [".cache"] = FileCategory.System,
    };

    public static FileCategory Classify(string extension) =>
        _map.TryGetValue(extension, out var cat) ? cat : FileCategory.Other;

    // Consistent colors for each category — iOS-inspired palette
    public static uint GetCategoryColor(FileCategory cat) => cat switch
    {
        FileCategory.Video      => 0xFF5E5CE6, // purple
        FileCategory.Audio      => 0xFFFF9F0A, // orange
        FileCategory.Image      => 0xFF30D158, // green
        FileCategory.Document   => 0xFF0A84FF, // blue
        FileCategory.Archive    => 0xFFFF6B35, // orange-red
        FileCategory.Code       => 0xFF32D2FF, // cyan
        FileCategory.Executable => 0xFFFF453A, // red
        FileCategory.Data       => 0xFFFFD60A, // yellow
        FileCategory.System     => 0xFF636366, // gray
        _                       => 0xFF48484A, // dark gray
    };

    // Get a subdued version for folders
    public static uint GetFolderColor(int depth) => depth switch
    {
        0 => 0xFF1C1C1E,
        1 => 0xFF2C2C2E,
        2 => 0xFF3A3A3C,
        _ => 0xFF48484A,
    };
}
