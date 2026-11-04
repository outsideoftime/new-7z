using System.Text.Json;
namespace New7z.App;

internal sealed record ShellRequest(string Action,string[] Paths)
{
    public static async Task<ShellRequest> ReadAsync(string path)
    {
        var root=System.IO.Path.GetFullPath(System.IO.Path.Combine(UserSettings.Folder,"Requests"));
        var full=System.IO.Path.GetFullPath(path);
        if(!string.Equals(System.IO.Path.GetDirectoryName(full),root,StringComparison.OrdinalIgnoreCase)||
           !Guid.TryParse(System.IO.Path.GetFileNameWithoutExtension(full),out _)||
           !string.Equals(System.IO.Path.GetExtension(full),".json",StringComparison.OrdinalIgnoreCase))
            throw new IOException("无效的右键菜单请求。");
        var file=new FileInfo(full);
        if(!file.Exists||file.Length>1024*1024||file.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("右键菜单请求不存在或超过大小限制。");
        ShellRequest? request;
        try {request=JsonSerializer.Deserialize(await File.ReadAllTextAsync(full),AppJsonContext.Default.ShellRequest);}
        finally {File.Delete(full);}
        if(request?.Paths is not {Length: >0 and <=10000}||request.Paths.Any(p=>string.IsNullOrWhiteSpace(p)||!System.IO.Path.IsPathFullyQualified(p)||!File.Exists(p)&&!Directory.Exists(p)))
            throw new IOException("选择的文件已移动或不存在。");
        if(request.Action is not ("open" or "create" or "create-7z" or "create-zip" or "extract-to" or "extract-named" or "extract-here" or "test"))
            throw new IOException("不支持的右键菜单命令。");
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
    public static string CreateTarget(ShellRequest request,string format)
    {
        var first=request.Paths[0];var parent=System.IO.Path.GetDirectoryName(first)!;
        var name=request.Paths.Length>1?"归档":Directory.Exists(first)?System.IO.Path.GetFileName(first.TrimEnd('\\','/')):System.IO.Path.GetFileNameWithoutExtension(first);
        return UniqueOutput(System.IO.Path.Combine(parent,name+"."+format));
    }
}
