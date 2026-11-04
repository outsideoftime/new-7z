namespace New7z.Core;

public sealed record ArchiveEntry
{
    public uint Index { get; init; }
    public string Path { get; init; } = "";
    public ulong Size { get; init; }
    public ulong? PackedSize { get; init; }
    public bool IsDirectory { get; init; }
    public bool Encrypted { get; init; }
    public bool IsLink { get; init; }
    public long FileTime { get; init; }
    public string NormalizedPath => Path.Replace('\\', '/').TrimEnd('/');
    public string Name => NormalizedPath.Split('/').Last();
    public string Directory => NormalizedPath.Contains('/') ? NormalizedPath[..NormalizedPath.LastIndexOf('/')] : "";
    public string Extension => System.IO.Path.GetExtension(Name).ToLowerInvariant();
    public string SizeText => IsDirectory ? "文件夹" : ByteSize.Format(Size);
    public string PackedSizeText => PackedSize is { } n ? ByteSize.Format(n) : "—";
    public string ModifiedText => FileTime > 0 ? DateTime.FromFileTimeUtc(FileTime).ToLocalTime().ToString("yyyy/MM/dd HH:mm") : "—";
    public bool IsImage => new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif" }.Contains(Extension);
    public bool IsText => new[] { ".txt", ".md", ".log", ".csv", ".json", ".xml", ".yaml", ".yml", ".ini", ".cfg", ".js", ".ts", ".css", ".cs", ".cpp", ".h", ".html", ".htm", ".sql", ".sh", ".ps1" }.Contains(Extension);
}
public static class ByteSize
{
    public static string Format(ulong value) => value switch
    {
        >= 1UL << 30 => $"{value / (double)(1UL << 30):0.##} GiB",
        >= 1UL << 20 => $"{value / (double)(1UL << 20):0.##} MiB",
        >= 1UL << 10 => $"{value / 1024.0:0.##} KiB",
        _ => $"{value} B"
    };
}
public sealed record EngineProgress(ulong Total, ulong Completed);
public sealed class ArchivePasswordException() : Exception("压缩包需要密码，或密码不正确。请重新输入。");
