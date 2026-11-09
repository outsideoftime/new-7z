using System.ComponentModel;
using System.Runtime.CompilerServices;
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
public sealed class ArchiveJob(string title,string destination):INotifyPropertyChanged
{
    public string Title {get;}=title;
    public string Destination {get;}=destination;
    public CancellationTokenSource Cancellation {get;}=new();
    private string status="";
    private double percent;
    private bool active=true;
    public string Status {get=>status;set{status=value;Changed();Changed(nameof(CanCancel));}}
    public double Percent {get=>percent;set{percent=value;Changed();}}
    public bool Active {get=>active;set{active=value;Changed();Changed(nameof(CanCancel));}}
    public bool CanCancel=>active;
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName]string? name=null)=>PropertyChanged?.Invoke(this,new(name));
}
public sealed class UserSettings
{
    public string Theme {get;set;}="system";
    public string Language {get;set;}="system";
    public string? IconPath {get;set;}
    public Dictionary<string,string> FormatIcons {get;set;}=[];
    public bool AutoCheckUpdates {get;set;}=true;
    public List<string> Recent {get;set;}=[];
    public static string Folder=>System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"New7z");
    public static UserSettings Load(){try{var loaded=System.Text.Json.JsonSerializer.Deserialize(File.ReadAllText(System.IO.Path.Combine(Folder,"settings.json")),AppJsonContext.Default.UserSettings)??new();loaded.FormatIcons??=[];return loaded;}catch{return new();}}
    public void Save(){Directory.CreateDirectory(Folder);var target=System.IO.Path.Combine(Folder,"settings.json");var temp=target+".tmp";File.WriteAllText(temp,System.Text.Json.JsonSerializer.Serialize(this,AppJsonContext.Default.UserSettings));File.Move(temp,target,true);}
}
