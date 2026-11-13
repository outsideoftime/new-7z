using System.Text.Json.Serialization;
namespace New7z.Core;

public sealed record ArchiveEntry
{
    private static readonly HashSet<string> ImageExtensions=new(StringComparer.OrdinalIgnoreCase){ ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".ico", ".tif", ".tiff" };
    private static readonly HashSet<string> TextExtensions=new(StringComparer.OrdinalIgnoreCase){
        // Documents and notes
        ".txt", ".text", ".md", ".markdown", ".rst", ".adoc", ".asciidoc", ".log", ".csv", ".tsv", ".tex", ".bib", ".org", ".nfo",
        // Data and configuration
        ".json", ".jsonc", ".json5", ".xml", ".xsl", ".xslt", ".xsd", ".yaml", ".yml", ".toml", ".ini", ".cfg", ".conf", ".config",
        ".properties", ".env", ".plist", ".reg", ".inf", ".manifest", ".resx", ".rss", ".atom", ".svg", ".sln", ".csproj", ".vbproj",
        ".fsproj", ".props", ".targets", ".nuspec", ".iss", ".rc", ".def", ".cmake", ".gradle", ".mk", ".mak", ".gitignore",
        ".gitattributes", ".editorconfig", ".npmrc", ".yarnrc", ".prettierrc", ".eslintrc", ".babelrc", ".dockerignore",
        // Programming languages and scripts
        ".c", ".cc", ".cxx", ".cpp", ".c++", ".h", ".hh", ".hpp", ".hxx", ".inl", ".m", ".mm", ".cs", ".fs", ".fsx", ".vb",
        ".java", ".kt", ".kts", ".scala", ".sc", ".groovy", ".go", ".rs", ".swift", ".dart", ".py", ".pyw", ".pyi", ".rb",
        ".php", ".pl", ".pm", ".lua", ".r", ".jl", ".nim", ".zig", ".v", ".sv", ".vhd", ".vhdl", ".tcl", ".awk", ".sed", ".asm",
        ".s", ".inc", ".d", ".pas", ".pp", ".f", ".f90", ".f95", ".for", ".lisp", ".scm", ".rkt", ".ml", ".mli", ".hs", ".erl",
        ".ex", ".exs", ".clj", ".cljs", ".elm", ".sol", ".proto", ".thrift", ".graphql", ".gql", ".tf", ".hcl", ".nix", ".bat",
        ".cmd", ".ps1", ".psm1", ".psd1", ".vbs", ".vba", ".ahk", ".au3", ".ino", ".sh", ".bash", ".zsh", ".fish", ".js", ".mjs",
        ".cjs", ".jsx", ".ts", ".tsx", ".mts", ".cts", ".css", ".scss", ".sass", ".less", ".styl", ".html", ".htm", ".xhtml",
        ".vue", ".svelte", ".astro", ".razor", ".cshtml", ".xaml", ".axaml", ".sql", ".spl", ".diff", ".patch"
    };
    private static readonly HashSet<string> TextFileNames=new(StringComparer.OrdinalIgnoreCase){
        "makefile", "gnumakefile", "cmakelists.txt", "dockerfile", "containerfile", "readme", "license", "licence", "copying", "notice",
        "changelog", "changes", "authors", "contributors", "vagrantfile", "jenkinsfile", "procfile", "gemfile", "rakefile", "brewfile",
        "podfile", "fastfile"
    };
    public uint Index { get; init; }
    public string Path { get; init; } = "";
    public ulong Size { get; init; }
    public ulong? PackedSize { get; init; }
    public bool IsDirectory { get; init; }
    public bool Encrypted { get; init; }
    public bool IsLink { get; init; }
    public long FileTime { get; init; }
    [JsonIgnore] public string NormalizedPath => Path.Replace('\\', '/').TrimEnd('/');
    [JsonIgnore] public string Name => NormalizedPath.Split('/').Last();
    [JsonIgnore] public string Directory => NormalizedPath.Contains('/') ? NormalizedPath[..NormalizedPath.LastIndexOf('/')] : "";
    [JsonIgnore] public string Extension => System.IO.Path.GetExtension(Name).ToLowerInvariant();
    [JsonIgnore] public string SizeText => IsDirectory ? Loc.T("文件夹", "Folder") : ByteSize.Format(Size);
    [JsonIgnore] public string PackedSizeText => PackedSize is { } n ? ByteSize.Format(n) : "—";
    [JsonIgnore] public string ModifiedText => FileTime is > 0 and <= 2650467743999999999 ? DateTime.FromFileTimeUtc(FileTime).ToLocalTime().ToString("yyyy/MM/dd HH:mm") : "—";
    [JsonIgnore] public bool IsImage => ImageExtensions.Contains(Extension);
    [JsonIgnore] public bool IsText => TextExtensions.Contains(Extension) || TextFileNames.Contains(Name);
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
public sealed record EngineProgress(ulong Total, ulong Completed, string? CurrentItem=null);
public sealed class ArchivePasswordException() : Exception(Loc.T("压缩包需要密码，或密码不正确。请重新输入。", "The archive requires a password, or the password is incorrect. Please enter it again."));
