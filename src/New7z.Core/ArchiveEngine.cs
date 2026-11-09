using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace New7z.Core;

public enum ConflictPolicy { Rename, Skip, Replace }
public sealed record ExtractionResult(int Extracted, int Skipped);
public sealed record SourceItem(string Path, string Name);

public sealed class ArchiveEngine
{
    public ArchiveEngine() => Check(NativeMethods.NzInitialize(System.IO.Path.Combine(AppContext.BaseDirectory, "7z.dll")), CancellationToken.None);
    private static NativeMethods.ProgressCallback Callback(CancellationToken cancellation, IProgress<EngineProgress>? progress)
    {
        long last=0;
        return (total,done,_)=>
        {
            // Check cancellation on every native callback; throttle only UI notifications.
            if(cancellation.IsCancellationRequested)return 1;
            if(progress==null)return 0;
            var now=Stopwatch.GetTimestamp();var previous=Interlocked.Read(ref last);
            if(previous!=0&&now-previous<Stopwatch.Frequency/20&&!(total>0&&done>=total))return 0;
            if(Interlocked.CompareExchange(ref last,now,previous)!=previous)return 0;
            try{progress.Report(new(total,done));}catch{return 1;}
            return 0;
        };
    }
    private static void Check(int code, CancellationToken cancellation)
    {
        if (code >= 0) return;
        if (cancellation.IsCancellationRequested || code == unchecked((int)0x80004004)) throw new OperationCanceledException(cancellation);
        throw code switch
        {
            unchecked((int)0x80040201) => new ArchivePasswordException(),
            unchecked((int)0x80040202) => new IOException(Loc.T("文件超过预览或解压大小限制。请先选择合适的输出位置解压。", "The file exceeds the preview or extraction size limit. Choose an output location and extract it first.")),
            unchecked((int)0x80040203) => new InvalidDataException(Loc.T("文件损坏、校验失败或不是受支持的压缩包。", "The file is damaged, failed verification, or is not a supported archive.")),
            unchecked((int)0x80040204) => new IOException(Loc.T("这种压缩包格式不支持修改。仅支持 7z 和 ZIP。", "This archive format cannot be modified. Only 7z and ZIP are supported.")),
            _ => new IOException(Loc.T($"文件操作失败（0x{code:X8}）：{Marshal.GetExceptionForHR(code)?.Message}", $"File operation failed (0x{code:X8}): {Marshal.GetExceptionForHR(code)?.Message}"))
        };
    }
    public Task<IReadOnlyList<ArchiveEntry>> ListAsync(string path, string? password = null, CancellationToken cancellation = default) => Task.Run<IReadOnlyList<ArchiveEntry>>(() =>
    {
        cancellation.ThrowIfCancellationRequested();
        var callback = Callback(cancellation, null);
        Check(NativeMethods.NzList(System.IO.Path.GetFullPath(path), password, out var json, callback, IntPtr.Zero), cancellation);
        try { return JsonSerializer.Deserialize(Marshal.PtrToStringUni(json)!,ArchiveJsonContext.Default.ListArchiveEntry) ?? []; }
        finally { NativeMethods.NzFree(json); GC.KeepAlive(callback); }
    }, cancellation);

    public Task<byte[]> ReadAsync(string path, ArchiveEntry entry, string? password = null, ulong limit = 32UL * 1024 * 1024, CancellationToken cancellation = default) => Task.Run(() =>
    {
        cancellation.ThrowIfCancellationRequested();
        if (entry.IsDirectory || entry.IsLink) throw new IOException(Loc.T("不能预览文件夹或链接。", "Folders and links cannot be previewed."));
        var callback=Callback(cancellation,null);
        Check(NativeMethods.NzExtract(path,password,entry.Index,null,limit,out var pointer,out var length,callback,IntPtr.Zero),cancellation);
        try { var bytes=new byte[checked((int)length)]; if(bytes.Length>0)Marshal.Copy(pointer,bytes,0,bytes.Length); return bytes; }
        finally { NativeMethods.NzFree(pointer); GC.KeepAlive(callback); }
    },cancellation);

    public Task TestAsync(string path,string? password=null,IProgress<EngineProgress>? progress=null,CancellationToken cancellation=default) => Task.Run(()=>
    {
        var callback=Callback(cancellation,progress);Check(NativeMethods.NzTest(path,password,callback,IntPtr.Zero),cancellation);GC.KeepAlive(callback);
    },cancellation);

    public bool DragArchive(string archive,IReadOnlyList<ArchiveEntry> selected,IReadOnlyList<ArchiveEntry> roots,string tempPath,IntPtr owner,string? password=null,Action<EngineProgress>? progress=null)
    {
        if(selected.Count==0||roots.Count==0)throw new ArgumentException(Loc.T("没有可以拖动的压缩包内容。", "There is nothing to drag from this archive."));
        var root=System.IO.Path.GetFullPath(tempPath);Directory.CreateDirectory(root);EnsureNoReparse(root);
        var used=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var outputByIndex=new Dictionary<uint,string>();
        foreach(var entry in selected)
        {
            if(entry.IsLink)throw new IOException(Loc.T($"不拖动链接条目：{entry.Path}", $"Link entries cannot be dragged: {entry.Path}"));
            var output=ResolveOutput(root,entry.NormalizedPath);
            if(!used.Add(output))
            {
                var parent=System.IO.Path.GetDirectoryName(output)!;var name=System.IO.Path.GetFileName(output);
                var extension=entry.IsDirectory?"":System.IO.Path.GetExtension(name);var stem=entry.IsDirectory||extension.Length==0?name:name[..^extension.Length];
                for(var suffix=2;;suffix++)
                {
                    var candidate=System.IO.Path.Combine(parent,$"{stem} ({suffix}){extension}");
                    if(used.Add(candidate)){output=candidate;break;}
                }
            }
            outputByIndex.Add(entry.Index,output);
        }
        var rootPaths=roots.Select(entry=>outputByIndex.TryGetValue(entry.Index,out var output)?output:ResolveOutput(root,entry.NormalizedPath)).ToArray();
        var callback=new NativeMethods.ProgressCallback((total,done,_)=>
        {
            try{progress?.Invoke(new(total,done));return 0;}catch{return 1;}
        });
        var result=NativeMethods.NzDragArchive(owner,System.IO.Path.GetFullPath(archive),password,selected.Select(e=>e.Index).ToArray(),selected.Select(e=>outputByIndex[e.Index]).ToArray(),(uint)selected.Count,rootPaths,(uint)rootPaths.Length,root,callback,IntPtr.Zero);
        GC.KeepAlive(callback);Check(result,CancellationToken.None);return result==0;
    }

    public Task CreateAsync(string output,IReadOnlyList<string> sources,string format="7z",int level=5,string? password=null,bool encryptNames=false,IProgress<EngineProgress>? progress=null,CancellationToken cancellation=default) => Task.Run(()=>
    {
        if(format is not ("7z" or "zip"))throw new ArgumentException(Loc.T("当前创建格式支持 7z 和 ZIP。", "Only 7z and ZIP can be created."));
        if(level is <0 or >9)throw new ArgumentOutOfRangeException(nameof(level));
        if(sources.Count==0)throw new ArgumentException(Loc.T("请选择要压缩的文件或文件夹。", "Select the files or folders to compress."));
        output=System.IO.Path.GetFullPath(output);
        if(File.Exists(output)||Directory.Exists(output))throw new IOException(Loc.T("输出名称已存在，请更换名称，避免覆盖。", "The output name already exists. Choose another name to avoid overwriting."));
        var paths=new List<string>();var names=new List<string>();
        foreach(var source in sources.Select(System.IO.Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellation.ThrowIfCancellationRequested();
            if(Directory.Exists(source)&&output.StartsWith(source.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))throw new IOException(Loc.T("压缩包不能保存到来源文件夹内部。", "The archive cannot be saved inside a source folder."));
        }
        foreach(var item in ExpandSources(sources,null,cancellation)){paths.Add(item.Path);names.Add(item.Name);}
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(output)!);
        var temp=output+"."+Guid.NewGuid().ToString("N")+".partial";
        try
        {
            if(format=="zip"&&string.IsNullOrEmpty(password)&&level<=6)
                FastZipWriter.Create(temp,paths,names,level,progress,cancellation);
            else
            {
                var callback=Callback(cancellation,progress);Check(NativeMethods.NzCreate(temp,format=="zip"?1:0,level,password,encryptNames?1:0,paths.ToArray(),names.ToArray(),(uint)paths.Count,callback,IntPtr.Zero),cancellation);GC.KeepAlive(callback);
            }
            cancellation.ThrowIfCancellationRequested();File.Move(temp,output,false);
        }
        finally { if(File.Exists(temp))File.Delete(temp); }
    },cancellation);

    public static bool SupportsUpdate(string path)=>System.IO.Path.GetExtension(path).ToLowerInvariant() is ".7z" or ".zip";

    public static IReadOnlyList<SourceItem> ExpandSources(IReadOnlyList<string> sources,string? folder=null,CancellationToken cancellation=default)
    {
        var items=new List<SourceItem>();var used=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var prefix=string.IsNullOrEmpty(folder)?"":folder.Replace('\\','/').Trim('/')+"/";
        void Add(string full,string name)
        {
            cancellation.ThrowIfCancellationRequested();
            if(File.GetAttributes(full).HasFlag(FileAttributes.ReparsePoint))throw new IOException(Loc.T($"暂不打包符号链接或重解析点：{name}", $"Symbolic links and reparse points are not packed yet: {name}"));
            if(!used.Add(name))throw new IOException(Loc.T($"来源包含重名项目：{name}。请分开打包或选择上级目录。", $"Sources contain duplicate items: {name}. Pack them separately or choose the parent folder."));
            if(items.Count>=1000000)throw new IOException(Loc.T("文件数量超过首版处理上限。", "The number of files exceeds the supported limit."));
            items.Add(new(full,name.Replace('\\','/')));
            if(Directory.Exists(full))foreach(var item in Directory.EnumerateFileSystemEntries(full))Add(item,System.IO.Path.Combine(name,System.IO.Path.GetFileName(item)));
        }
        foreach(var source in sources.Select(System.IO.Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
            Add(source,prefix+System.IO.Path.GetFileName(source.TrimEnd('\\','/')));
        return items;
    }

    // Rewrites the archive to a temporary file, dropping the given entry indices and appending
    // the sources, then swaps the result in only after the engine completed successfully.
    public Task ModifyAsync(string archive,IReadOnlyList<uint> removeIndices,IReadOnlyList<SourceItem> additions,string? password=null,IProgress<EngineProgress>? progress=null,CancellationToken cancellation=default) => Task.Run(()=>
    {
        archive=System.IO.Path.GetFullPath(archive);
        if(!SupportsUpdate(archive))throw new IOException(Loc.T("当前只支持修改 7z 和 ZIP 压缩包。", "Only 7z and ZIP archives can be modified."));
        var zip=System.IO.Path.GetExtension(archive).ToLowerInvariant()==".zip";
        if(removeIndices.Count==0&&additions.Count==0)return;
        if(!File.Exists(archive))throw new FileNotFoundException(Loc.T("压缩包不存在。", "The archive does not exist."),archive);
        var temp=archive+"."+Guid.NewGuid().ToString("N")+".partial";
        try
        {
            var callback=Callback(cancellation,progress);
            Check(NativeMethods.NzUpdate(archive,temp,password,zip?1:0,removeIndices.ToArray(),(uint)removeIndices.Count,
                additions.Select(a=>a.Path).ToArray(),additions.Select(a=>a.Name).ToArray(),(uint)additions.Count,callback,IntPtr.Zero),cancellation);
            GC.KeepAlive(callback);
            cancellation.ThrowIfCancellationRequested();
            File.Move(temp,archive,true);
        }
        finally{if(File.Exists(temp))File.Delete(temp);}
    },cancellation);

    public async Task<ExtractionResult> ExtractAsync(string archive,IReadOnlyList<ArchiveEntry> entries,string destination,ConflictPolicy policy=ConflictPolicy.Rename,string? password=null,IProgress<EngineProgress>? progress=null,CancellationToken cancellation=default)
    {
        var root=System.IO.Path.GetFullPath(destination);Directory.CreateDirectory(root);EnsureNoReparse(root);
        // Preflight every path before creating any output, including entries hidden by directory structure.
        foreach(var entry in entries){if(entry.IsLink)throw new IOException(Loc.T($"不解压链接条目：{entry.Path}", $"Link entries are not extracted: {entry.Path}"));ResolveOutput(root,entry.Path);}
        var completed=0;var skipped=0;var totalFiles=(ulong)entries.Count(e=>!e.IsDirectory);
        foreach(var entry in entries)
        {
            cancellation.ThrowIfCancellationRequested();
            var target=ResolveOutput(root,entry.Path);
            if(string.Equals(target,System.IO.Path.GetFullPath(archive),StringComparison.OrdinalIgnoreCase))throw new IOException(Loc.T("不能用解压内容替换源压缩包。", "Extracted content cannot replace the source archive."));
            if(entry.IsDirectory){Directory.CreateDirectory(target);continue;}
            progress?.Report(new(totalFiles,(ulong)(completed+skipped),entry.Path));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);EnsureNoReparse(System.IO.Path.GetDirectoryName(target)!);
            if(File.Exists(target))
            {
                if(policy==ConflictPolicy.Skip){skipped++;progress?.Report(new(totalFiles,(ulong)(completed+skipped),entry.Path));continue;}
                if(policy==ConflictPolicy.Rename)target=UniqueName(target);
            }
            var drive=new DriveInfo(System.IO.Path.GetPathRoot(root)!);
            if((ulong)drive.AvailableFreeSpace<entry.Size)throw new IOException(Loc.T("目标磁盘空间不足。", "The destination disk is out of space."));
            var temp=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(target)!,".new7z-"+Guid.NewGuid().ToString("N")+".partial");
            try
            {
                await Task.Run(()=>
                {
                    var cb=Callback(cancellation,null);Check(NativeMethods.NzExtract(archive,password,entry.Index,temp,Math.Max(entry.Size,1),out var unused,out _,cb,IntPtr.Zero),cancellation);GC.KeepAlive(cb);
                },cancellation);
                cancellation.ThrowIfCancellationRequested();EnsureNoReparse(System.IO.Path.GetDirectoryName(target)!);
                if(File.Exists(target)&&File.GetAttributes(target).HasFlag(FileAttributes.ReparsePoint))throw new IOException(Loc.T("目标文件是重解析点，已停止写入。", "The target file is a reparse point; writing stopped."));
                File.Move(temp,target,policy==ConflictPolicy.Replace);completed++;
                if(entry.FileTime>0)File.SetLastWriteTimeUtc(target,DateTime.FromFileTimeUtc(entry.FileTime));
                progress?.Report(new(totalFiles,(ulong)(completed+skipped),entry.Path));
            }
            finally { if(File.Exists(temp))File.Delete(temp); }
        }
        return new(completed,skipped);
    }
    public static string ResolveOutput(string root,string relative)
    {
        if(string.IsNullOrWhiteSpace(relative)||relative.Contains(':')||relative.StartsWith('/')||relative.StartsWith('\\'))throw new IOException(Loc.T("归档包含不安全的绝对路径。", "The archive contains an unsafe absolute path."));
        var parts=relative.Replace('\\','/').Split('/');
        foreach(var part in parts)
            if(part is "." or ".."||part.TrimEnd(' ','.')!=part||part.IndexOfAny(System.IO.Path.GetInvalidFileNameChars())>=0||System.Text.RegularExpressions.Regex.IsMatch(part,@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)",System.Text.RegularExpressions.RegexOptions.IgnoreCase))throw new IOException(Loc.T("归档包含不安全的目标文件名。", "The archive contains an unsafe target file name."));
        var result=System.IO.Path.GetFullPath(System.IO.Path.Combine(root,relative.Replace('/',System.IO.Path.DirectorySeparatorChar)));
        if(!result.StartsWith(System.IO.Path.GetFullPath(root).TrimEnd('\\','/')+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException(Loc.T("归档路径超出目标目录。", "The archive path escapes the destination folder."));
        EnsureNoReparse(result);return result;
    }
    private static void EnsureNoReparse(string path)
    {
        for(var current=path;!string.IsNullOrEmpty(current);current=System.IO.Path.GetDirectoryName(current))
            if((File.Exists(current)||Directory.Exists(current))&&File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))throw new IOException(Loc.T("目标路径包含重解析点，已停止操作。", "The target path contains a reparse point; the operation stopped."));
    }
    private static string UniqueName(string path)
    {
        var parent=System.IO.Path.GetDirectoryName(path)!;var name=System.IO.Path.GetFileNameWithoutExtension(path);var ext=System.IO.Path.GetExtension(path);var result=path;
        for(var i=2;File.Exists(result)||Directory.Exists(result);i++)result=System.IO.Path.Combine(parent,$"{name} ({i}){ext}");
        return result;
    }
    public static string DecodeText(byte[] bytes,string encoding="自动")
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        if(encoding=="GB18030")return Encoding.GetEncoding(54936).GetString(bytes);
        if(encoding=="UTF-16 LE")return Encoding.Unicode.GetString(bytes).TrimStart('\uFEFF');
        if(bytes.AsSpan().StartsWith(new byte[]{0xff,0xfe}))return Encoding.Unicode.GetString(bytes).TrimStart('\uFEFF');
        if(bytes.AsSpan().StartsWith(new byte[]{0xfe,0xff}))return Encoding.BigEndianUnicode.GetString(bytes).TrimStart('\uFEFF');
        try { return new UTF8Encoding(false,true).GetString(bytes).TrimStart('\uFEFF'); }
        catch(DecoderFallbackException) when(encoding is "自动" or "Auto"){return Encoding.GetEncoding(54936).GetString(bytes);}
    }
}
