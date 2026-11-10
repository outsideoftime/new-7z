using System.Text.Json;
using System.Text.Json.Serialization;
using static New7z.Core.Loc;
namespace New7z.App;

internal sealed record ShellRequest(string Action,string[] Paths)
{
    // In-process only: extracting selected entries from an archive already open in the app.
    public string[]? Entries {get;init;}
    public string? Target {get;init;}
    [JsonIgnore] public string? Password {get;init;}
    public static async Task<ShellRequest> ReadAsync(string path)
    {
        var root=System.IO.Path.GetFullPath(System.IO.Path.Combine(UserSettings.Folder,"Requests"));
        var full=System.IO.Path.GetFullPath(path);
        if(!string.Equals(System.IO.Path.GetDirectoryName(full),root,StringComparison.OrdinalIgnoreCase)||
           !Guid.TryParse(System.IO.Path.GetFileNameWithoutExtension(full),out _)||
           !string.Equals(System.IO.Path.GetExtension(full),".json",StringComparison.OrdinalIgnoreCase))
            throw new IOException(T("无效的右键菜单请求。","Invalid shell menu request."));
        var file=new FileInfo(full);
        if(!file.Exists||file.Length>1024*1024||file.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new IOException(T("右键菜单请求不存在或超过大小限制。","The shell menu request is missing or exceeds the size limit."));
        ShellRequest? request;
        try {request=JsonSerializer.Deserialize(await File.ReadAllTextAsync(full),AppJsonContext.Default.ShellRequest);}
        finally {File.Delete(full);}
        if(request?.Paths is not {Length: >0 and <=10000}||request.Paths.Any(p=>string.IsNullOrWhiteSpace(p)||!System.IO.Path.IsPathFullyQualified(p)||!File.Exists(p)&&!Directory.Exists(p)))
            throw new IOException(T("选择的文件已移动或不存在。","A selected file has moved or no longer exists."));
        if(request.Action is not ("open" or "create" or "create-7z" or "create-zip" or "create-tar" or "extract-to" or "extract-named" or "extract-here" or "test"))
            throw new IOException(T("不支持的右键菜单命令。","Unsupported shell menu command."));
        return request;
    }
}

internal static class ShellPaths
{
    public static string ArchiveStem(string path)
    {
        var name=System.IO.Path.GetFileName(path);
        foreach(var ext in new[]{".tar.gz",".tar.xz",".tar.bz2"})
            if(name.EndsWith(ext,StringComparison.OrdinalIgnoreCase))return name[..^ext.Length];
        return System.IO.Path.GetFileNameWithoutExtension(name);
    }
    public static string UniqueOutput(string path,HashSet<string>? reserved=null)
    {
        var result=path;
        for(var i=2;File.Exists(result)||Directory.Exists(result)||reserved?.Contains(result)==true;i++)
            result=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!,System.IO.Path.GetFileNameWithoutExtension(path)+$" ({i})"+System.IO.Path.GetExtension(path));
        reserved?.Add(result);return result;
    }
    public static string SuggestedName(IReadOnlyList<string> paths)
    {
        if(paths.Count==0)return T("归档","Archive");
        if(paths.Count>1)return CommonParentName(paths)??T("归档","Archive");
        var first=paths[0];
        var name=Directory.Exists(first)?System.IO.Path.GetFileName(first.TrimEnd('\\','/')):System.IO.Path.GetFileNameWithoutExtension(first);
        return string.IsNullOrWhiteSpace(name)?T("归档","Archive"):name;
    }
    public static string SuggestedFolder(IReadOnlyList<string> paths)=>paths.Count==0?Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments):ParentOf(paths[0]);
    public static string CreateTarget(ShellRequest request,string format)=>UniqueOutput(System.IO.Path.Combine(SuggestedFolder(request.Paths),SuggestedName(request.Paths)+"."+format));
    private static string ParentOf(string path)
    {
        var full=System.IO.Path.GetFullPath(path);
        var trimmed=full.TrimEnd(System.IO.Path.DirectorySeparatorChar,System.IO.Path.AltDirectorySeparatorChar);
        return System.IO.Path.GetDirectoryName(trimmed) is {Length:>0} parent?parent:full;
    }
    private static string? CommonParentName(IReadOnlyList<string> paths)
    {
        var common=ParentOf(paths[0]);
        foreach(var path in paths.Skip(1))
        {
            var candidate=ParentOf(path);
            while(common.Length>0&&!IsSameOrParent(common,candidate))common=System.IO.Path.GetDirectoryName(common)??"";
            if(common.Length==0)return null;
        }
        var name=System.IO.Path.GetFileName(common.TrimEnd(System.IO.Path.DirectorySeparatorChar,System.IO.Path.AltDirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(name)?null:name;
    }
    private static bool IsSameOrParent(string parent,string child)
    {
        if(string.Equals(parent,child,StringComparison.OrdinalIgnoreCase))return true;
        var prefix=parent.EndsWith(System.IO.Path.DirectorySeparatorChar)?parent:parent+System.IO.Path.DirectorySeparatorChar;
        return child.StartsWith(prefix,StringComparison.OrdinalIgnoreCase);
    }
}
