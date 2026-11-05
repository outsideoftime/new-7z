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
            unchecked((int)0x80040202) => new IOException("文件超过预览或解压大小限制。请先选择合适的输出位置解压。"),
            unchecked((int)0x80040203) => new InvalidDataException("文件损坏、校验失败或不是受支持的压缩包。"),
            unchecked((int)0x80040204) => new IOException("这种压缩包格式不支持修改。仅支持 7z 和 ZIP。"),
            _ => new IOException($"文件操作失败（0x{code:X8}）：{Marshal.GetExceptionForHR(code)?.Message}")
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
        if (entry.IsDirectory || entry.IsLink) throw new IOException("不能预览文件夹或链接。");
        var callback=Callback(cancellation,null);
        Check(NativeMethods.NzExtract(path,password,entry.Index,null,limit,out var pointer,out var length,callback,IntPtr.Zero),cancellation);
        try { var bytes=new byte[checked((int)length)]; if(bytes.Length>0)Marshal.Copy(pointer,bytes,0,bytes.Length); return bytes; }
        finally { NativeMethods.NzFree(pointer); GC.KeepAlive(callback); }
    },cancellation);

    public Task TestAsync(string path,string? password=null,IProgress<EngineProgress>? progress=null,CancellationToken cancellation=default) => Task.Run(()=>
    {
        var callback=Callback(cancellation,progress);Check(NativeMethods.NzTest(path,password,callback,IntPtr.Zero),cancellation);GC.KeepAlive(callback);
    },cancellation);

    public Task CreateAsync(string output,IReadOnlyList<string> sources,string format="7z",int level=5,string? password=null,bool encryptNames=false,IProgress<EngineProgress>? progress=null,CancellationToken cancellation=default) => Task.Run(()=>
    {
        if(format is not ("7z" or "zip"))throw new ArgumentException("当前创建格式支持 7z 和 ZIP。");
        if(level is <0 or >9)throw new ArgumentOutOfRangeException(nameof(level));
        if(sources.Count==0)throw new ArgumentException("请选择要压缩的文件或文件夹。");
        output=System.IO.Path.GetFullPath(output);
        if(File.Exists(output)||Directory.Exists(output))throw new IOException("输出名称已存在，请更换名称，避免覆盖。");
        var paths=new List<string>();var names=new List<string>();
        foreach(var source in sources.Select(System.IO.Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellation.ThrowIfCancellationRequested();
            if(Directory.Exists(source)&&output.StartsWith(source.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))throw new IOException("压缩包不能保存到来源文件夹内部。");
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
            if(File.GetAttributes(full).HasFlag(FileAttributes.ReparsePoint))throw new IOException($"暂不打包符号链接或重解析点：{name}");
            if(!used.Add(name))throw new IOException($"来源包含重名项目：{name}。请分开打包或选择上级目录。");
            if(items.Count>=1000000)throw new IOException("文件数量超过首版处理上限。");
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
        if(!SupportsUpdate(archive))throw new IOException("当前只支持修改 7z 和 ZIP 压缩包。");
        var zip=System.IO.Path.GetExtension(archive).ToLowerInvariant()==".zip";
        if(removeIndices.Count==0&&additions.Count==0)return;
        if(!File.Exists(archive))throw new FileNotFoundException("压缩包不存在。",archive);
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
        foreach(var entry in entries){if(entry.IsLink)throw new IOException($"不解压链接条目：{entry.Path}");ResolveOutput(root,entry.Path);}
        var completed=0;var skipped=0;
        foreach(var entry in entries)
        {
            cancellation.ThrowIfCancellationRequested();
            var target=ResolveOutput(root,entry.Path);
            if(string.Equals(target,System.IO.Path.GetFullPath(archive),StringComparison.OrdinalIgnoreCase))throw new IOException("不能用解压内容替换源压缩包。");
            if(entry.IsDirectory){Directory.CreateDirectory(target);continue;}
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);EnsureNoReparse(System.IO.Path.GetDirectoryName(target)!);
            if(File.Exists(target))
            {
                if(policy==ConflictPolicy.Skip){skipped++;continue;}
                if(policy==ConflictPolicy.Rename)target=UniqueName(target);
            }
            var drive=new DriveInfo(System.IO.Path.GetPathRoot(root)!);
            if((ulong)drive.AvailableFreeSpace<entry.Size)throw new IOException("目标磁盘空间不足。");
            var temp=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(target)!,".new7z-"+Guid.NewGuid().ToString("N")+".partial");
            try
            {
                await Task.Run(()=>
                {
                    var cb=Callback(cancellation,null);Check(NativeMethods.NzExtract(archive,password,entry.Index,temp,Math.Max(entry.Size,1),out var unused,out _,cb,IntPtr.Zero),cancellation);GC.KeepAlive(cb);
                },cancellation);
                cancellation.ThrowIfCancellationRequested();EnsureNoReparse(System.IO.Path.GetDirectoryName(target)!);
                if(File.Exists(target)&&File.GetAttributes(target).HasFlag(FileAttributes.ReparsePoint))throw new IOException("目标文件是重解析点，已停止写入。");
                File.Move(temp,target,policy==ConflictPolicy.Replace);completed++;
                if(entry.FileTime>0)File.SetLastWriteTimeUtc(target,DateTime.FromFileTimeUtc(entry.FileTime));
                progress?.Report(new((ulong)entries.Count,(ulong)(completed+skipped)));
            }
            finally { if(File.Exists(temp))File.Delete(temp); }
        }
        return new(completed,skipped);
    }
    public static string ResolveOutput(string root,string relative)
    {
        if(string.IsNullOrWhiteSpace(relative)||relative.Contains(':')||relative.StartsWith('/')||relative.StartsWith('\\'))throw new IOException("归档包含不安全的绝对路径。");
        var parts=relative.Replace('\\','/').Split('/');
        foreach(var part in parts)
            if(part is "." or ".."||part.TrimEnd(' ','.')!=part||part.IndexOfAny(System.IO.Path.GetInvalidFileNameChars())>=0||System.Text.RegularExpressions.Regex.IsMatch(part,@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)",System.Text.RegularExpressions.RegexOptions.IgnoreCase))throw new IOException("归档包含不安全的目标文件名。");
        var result=System.IO.Path.GetFullPath(System.IO.Path.Combine(root,relative.Replace('/',System.IO.Path.DirectorySeparatorChar)));
        if(!result.StartsWith(System.IO.Path.GetFullPath(root).TrimEnd('\\','/')+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("归档路径超出目标目录。");
        EnsureNoReparse(result);return result;
    }
    private static void EnsureNoReparse(string path)
    {
        for(var current=path;!string.IsNullOrEmpty(current);current=System.IO.Path.GetDirectoryName(current))
            if((File.Exists(current)||Directory.Exists(current))&&File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))throw new IOException("目标路径包含重解析点，已停止操作。");
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
        catch(DecoderFallbackException) when(encoding=="自动"){return Encoding.GetEncoding(54936).GetString(bytes);}
    }
}
