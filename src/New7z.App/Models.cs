using New7z.Core;

namespace New7z.App;
public sealed record ArchiveRow(ArchiveEntry Entry)
{
    public string Name=>Entry.Name;
    public string FullPath=>Entry.NormalizedPath;
    public string SizeText=>Entry.SizeText;
    public string PackedSizeText=>Entry.PackedSizeText;
    public string ModifiedText=>Entry.ModifiedText;
    public string Glyph=>Entry.IsDirectory?"\uE8B7":Entry.IsImage?"\uEB9F":Entry.IsText?"\uE8A5":"\uE7C3";
}
public sealed class ArchiveJob(string title,string destination)
{
    public string Title {get;}=title;
    public string Destination {get;}=destination;
    public CancellationTokenSource Cancellation {get;}=new();
    private string status="";
    private double percent;
    private bool active=true;
    private string currentItem="";
    public string Status {get=>status;set{status=value;Changed();}}
    public double Percent {get=>percent;set{percent=value;Changed();}}
    public string PercentText=>$"{percent:0}%";
    public string CurrentItem {get=>currentItem;set{currentItem=value;Changed();}}
    public bool Active {get=>active;set{active=value;Changed();}}
    public bool CanCancel=>active;
    public event Action<ArchiveJob>? Updated;
    private void Changed()=>Updated?.Invoke(this);
}
public sealed class UserSettings
{
    public string Theme {get;set;}="system";
    public string Language {get;set;}="system";
    public string? IconPath {get;set;}
    public string DefaultFormat {get;set;}="7z";
    public string Default7zMethod {get;set;}="LZMA2";
    public string DefaultZipMethod {get;set;}="Deflate";
    public Dictionary<string,string> FormatIcons {get;set;}=[];
    public bool AutoCheckUpdates {get;set;}=true;
    public List<string> Recent {get;set;}=[];
    public static string Folder=>System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"New7z");
    public static string NormalizeFormat(string? value)=>value?.ToLowerInvariant() switch{"zip"=>"zip","tar"=>"tar",_=>"7z"};
    public static UserSettings Load(){try{var loaded=System.Text.Json.JsonSerializer.Deserialize(File.ReadAllText(System.IO.Path.Combine(Folder,"settings.json")),AppJsonContext.Default.UserSettings)??new();loaded.FormatIcons??=[];loaded.DefaultFormat=NormalizeFormat(loaded.DefaultFormat);loaded.Default7zMethod=CompressionMethods.Normalize("7z",loaded.Default7zMethod)??CompressionMethods.Default("7z");loaded.DefaultZipMethod=CompressionMethods.Normalize("zip",loaded.DefaultZipMethod)??CompressionMethods.Default("zip");return loaded;}catch{return new();}}
    public void Save(){Directory.CreateDirectory(Folder);var target=System.IO.Path.Combine(Folder,"settings.json");var temp=target+".tmp";File.WriteAllText(temp,System.Text.Json.JsonSerializer.Serialize(this,AppJsonContext.Default.UserSettings));File.Move(temp,target,true);}
}
public sealed record CompressionChoice(string Value,string Display){public override string ToString()=>Display;}
public static class CompressionOptions
{
    public static IReadOnlyList<CompressionChoice> For(string format)=>CompressionMethods.For(format).Select(value=>new CompressionChoice(value,Label(value))).ToArray();
    public static string Label(string value)=>value switch
    {
        "LZMA2"=>Loc.T("LZMA2（推荐）","LZMA2 (recommended)"),
        "Deflate"=>Loc.T("Deflate（推荐）","Deflate (recommended)"),
        "Copy"=>Loc.T("存储（不压缩）","Store (no compression)"),
        _=>value
    };
    public static string Default(UserSettings settings,string format)=>format switch
    {
        "zip"=>CompressionMethods.Normalize("zip",settings.DefaultZipMethod)??CompressionMethods.Default("zip"),
        "7z"=>CompressionMethods.Normalize("7z",settings.Default7zMethod)??CompressionMethods.Default("7z"),
        _=>""
    };
}
