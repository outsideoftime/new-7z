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
    private string status="等待中";
    private double percent;
    public string Status {get=>status;set{status=value;Changed();Changed(nameof(CanCancel));}}
    public double Percent {get=>percent;set{percent=value;Changed();}}
    public bool CanCancel=>Status is "等待中" or "正在处理";
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName]string? name=null)=>PropertyChanged?.Invoke(this,new(name));
}
public sealed class UserSettings
{
    public string Theme {get;set;}="系统";
    public string? IconPath {get;set;}
    public bool AutoCheckUpdates {get;set;}=true;
    public List<string> Recent {get;set;}=[];
    public static string Folder=>System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"New7z");
    public static UserSettings Load(){try{return System.Text.Json.JsonSerializer.Deserialize(File.ReadAllText(System.IO.Path.Combine(Folder,"settings.json")),AppJsonContext.Default.UserSettings)??new();}catch{return new();}}
    public void Save(){Directory.CreateDirectory(Folder);var target=System.IO.Path.Combine(Folder,"settings.json");var temp=target+".tmp";File.WriteAllText(temp,System.Text.Json.JsonSerializer.Serialize(this,AppJsonContext.Default.UserSettings));File.Move(temp,target,true);}
}
