using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Runtime.ExceptionServices;

namespace New7z.Core;

internal sealed class NativeArchiveEngine
{
    internal string OperationId { get; init; } = Guid.NewGuid().ToString("N");
    public NativeArchiveEngine() => Check(NativeMethods.NzInitialize(System.IO.Path.Combine(AppContext.BaseDirectory, "7z.dll")), CancellationToken.None);
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

    public static string GetDragDiagnostics()
    {
        NativeMethods.NzGetDragDiagnostics(out var pointer);
        try{return Marshal.PtrToStringUni(pointer)??"";}finally{NativeMethods.NzFree(pointer);}
    }
    private static readonly NativeMethods.DragPrepareThunk PrepareDrag=(state,progress,context)=>
    {
        try{return ((NativeMethods.DragPrepareCallback)GCHandle.FromIntPtr(state).Target!)(progress,context);}catch{return unchecked((int)0x80004005);}
    };
    private static readonly NativeMethods.DragReleaseCallback ReleaseDrag=state=>{try{GCHandle.FromIntPtr(state).Free();}catch{}};

    public static ArchiveDragData CreateDragData(IntPtr package,string archive,IReadOnlyList<ArchiveEntry> selected,IReadOnlyList<ArchiveEntry> roots,string tempPath,IntPtr owner,string? password=null,NativeMethods.DragPrepareCallback? prepare=null)
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
        ArgumentNullException.ThrowIfNull(prepare);
        var state=GCHandle.Alloc(prepare);IntPtr session;
        try
        {
            Check(NativeMethods.NzCreateArchiveDragDataIsolated(package,owner,System.IO.Path.GetFullPath(archive),password,selected.Select(e=>e.Index).ToArray(),selected.Select(e=>outputByIndex[e.Index]).ToArray(),(uint)selected.Count,rootPaths,(uint)rootPaths.Length,root,Loc.T("正在解压拖动内容","Extracting dragged items"),PrepareDrag,GCHandle.ToIntPtr(state),ReleaseDrag,out session),CancellationToken.None);
        }
        catch{state.Free();throw;}
        return new ArchiveDragData(session,prepare);
    }

    public sealed class ArchiveDragData:IDisposable
    {
        private IntPtr session;
        private readonly NativeMethods.DragPrepareCallback? prepare;
        internal ArchiveDragData(IntPtr value,NativeMethods.DragPrepareCallback? prepare){session=value;this.prepare=prepare;}
        public void ThrowIfFailed()=>Check(NativeMethods.NzArchiveDragResult(session),CancellationToken.None);
        public bool WasTransferred=>NativeMethods.NzArchiveDragWasTransferred(session)!=0;
        public void Dispose(){var value=Interlocked.Exchange(ref session,IntPtr.Zero);if(value!=IntPtr.Zero)NativeMethods.NzReleaseArchiveDragData(value);GC.KeepAlive(prepare);GC.SuppressFinalize(this);}
        ~ArchiveDragData()=>Dispose();
    }

    public Task CreateAsync(string output,IReadOnlyList<string> sources,string format="7z",int level=5,string? password=null,bool encryptNames=false,string? method=null,IProgress<EngineProgress>? progress=null,CancellationToken cancellation=default) => Task.Run(()=>
    {
        if(format is not ("7z" or "zip" or "tar"))throw new ArgumentException(Loc.T("当前创建格式支持 7z、ZIP 和 TAR。", "Only 7z, ZIP and TAR can be created."));
        if(format=="tar"&&!string.IsNullOrEmpty(password))throw new ArgumentException(Loc.T("TAR 不支持密码，请改用 7z 或 ZIP。", "TAR does not support passwords; use 7z or ZIP instead."));
        if(format=="tar")method=null;
        else if(method!=null)
        {
            var normalized=CompressionMethods.Normalize(format,method);
            if(normalized==null)throw new ArgumentException(Loc.T($"不支持的压缩方法：{method}", $"Unsupported compression method: {method}"));
            method=normalized;
        }
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
        var temp=output+"."+OperationId+".partial";
        try
        {
            if(format=="zip"&&string.IsNullOrEmpty(password)&&level<=6&&(method==null||method=="Deflate"))
                FastZipWriter.Create(temp,paths,names,level,progress,cancellation);
            else
            {
                // The ZIP handler selects Store from level 0; its "m=Copy" path is not accepted.
                var nativeLevel=format=="zip"&&method=="Copy"?0:level;
                var nativeMethod=format=="zip"&&method=="Copy"?null:method;
                var callback=Callback(cancellation,progress);Check(NativeMethods.NzCreate(temp,format=="zip"?1:format=="tar"?2:0,nativeLevel,nativeMethod,password,encryptNames?1:0,paths.ToArray(),names.ToArray(),(uint)paths.Count,callback,IntPtr.Zero),cancellation);GC.KeepAlive(callback);
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
        var zone=MarkOfTheWeb.Read(archive);
        var temp=archive+"."+OperationId+".partial";
        try
        {
            var callback=Callback(cancellation,progress);
            Check(NativeMethods.NzUpdate(archive,temp,password,zip?1:0,removeIndices.ToArray(),(uint)removeIndices.Count,
                additions.Select(a=>a.Path).ToArray(),additions.Select(a=>a.Name).ToArray(),(uint)additions.Count,callback,IntPtr.Zero),cancellation);
            GC.KeepAlive(callback);
            cancellation.ThrowIfCancellationRequested();
            MarkOfTheWeb.Apply(temp,zone);
            File.Move(temp,archive,true);
        }
        finally{if(File.Exists(temp))File.Delete(temp);}
    },cancellation);

    public Task<ExtractionResult> ExtractAsync(string archive,IReadOnlyList<ArchiveEntry> entries,string destination,ConflictPolicy policy=ConflictPolicy.Rename,string? password=null,IProgress<EngineProgress>? progress=null,CancellationToken cancellation=default,ExtractionLimits? limits=null) => Task.Run(()=>
    {
        cancellation.ThrowIfCancellationRequested();
        archive=System.IO.Path.GetFullPath(archive);
        (limits??ExtractionLimits.Default).Validate(entries);
        var zone=MarkOfTheWeb.Read(archive);
        var root=System.IO.Path.GetFullPath(destination);Directory.CreateDirectory(root);EnsureNoReparse(root);
        // Preflight every path before creating any output, including entries hidden by directory structure.
        var plans=new List<ExtractionItem>(entries.Count);
        foreach(var entry in entries)
        {
            cancellation.ThrowIfCancellationRequested();
            if(entry.IsLink)throw new IOException(Loc.T($"不解压链接条目：{entry.Path}", $"Link entries are not extracted: {entry.Path}"));
            var target=ResolveOutput(root,entry.Path);
            if(string.Equals(target,archive,StringComparison.OrdinalIgnoreCase))throw new IOException(Loc.T("不能用解压内容替换源压缩包。", "Extracted content cannot replace the source archive."));
            plans.Add(new(entry,target,entry.IsDirectory?"":System.IO.Path.Combine(System.IO.Path.GetDirectoryName(target)!,".new7z-"+OperationId+"-"+entry.Index+".partial")));
        }
        foreach(var item in plans.Where(p=>p.Entry.IsDirectory)){cancellation.ThrowIfCancellationRequested();EnsureNoReparse(item.Target);Directory.CreateDirectory(item.Target);}
        var files=plans.Where(p=>!p.Entry.IsDirectory).ToArray();
        var byIndex=files.ToDictionary(p=>p.Entry.Index);
        var completed=0;var skipped=0;var totalFiles=(ulong)files.Length;
        var drive=new DriveInfo(System.IO.Path.GetPathRoot(root)!);var availableSpace=(ulong)drive.AvailableFreeSpace;
        string? activeTemp=null,currentItem=null;string currentTarget="";
        ExceptionDispatchInfo? callbackError=null;long lastReport=0;
        void ReportProgress(bool force=false)
        {
            if(progress==null)return;
            var now=Stopwatch.GetTimestamp();
            if(!force&&lastReport!=0&&now-lastReport<Stopwatch.Frequency/20)return;
            lastReport=now;progress.Report(new(totalFiles,(ulong)(completed+skipped),currentItem));
        }
        NativeMethods.ExtractItemCallback itemCallback=(index,stage,_)=>
        {
            try
            {
                cancellation.ThrowIfCancellationRequested();var item=byIndex[index];var entry=item.Entry;
                if(stage==0)
                {
                    currentItem=entry.Path;currentTarget=item.Target;EnsureNoReparse(currentTarget);ReportProgress();
                    var parent=System.IO.Path.GetDirectoryName(currentTarget)!;Directory.CreateDirectory(parent);EnsureNoReparse(parent);
                    if(File.Exists(currentTarget))
                    {
                        if(policy==ConflictPolicy.Skip){skipped++;ReportProgress();return 1;}
                        if(policy==ConflictPolicy.Rename)currentTarget=UniqueName(currentTarget);
                    }
                    // Keep a conservative budget and refresh only when necessary, rather
                    // than querying the volume for each of thousands of small files.
                    if(availableSpace<entry.Size)availableSpace=(ulong)drive.AvailableFreeSpace;
                    if(availableSpace<entry.Size)throw new IOException(Loc.T("目标磁盘空间不足。", "The destination disk is out of space."));
                    activeTemp=item.Temp;
                }
                else
                {
                    EnsureNoReparse(currentTarget);
                    MarkOfTheWeb.Apply(item.Temp,zone);
                    File.Move(item.Temp,currentTarget,policy==ConflictPolicy.Replace);activeTemp=null;completed++;
                    availableSpace-=entry.Size;
                    ReportProgress();
                }
                return 0;
            }
            catch(Exception ex){callbackError=ExceptionDispatchInfo.Capture(ex);return unchecked((int)0x80004004);}
        };
        NativeMethods.ProgressCallback callback=(_,_,_)=>
        {
            if(cancellation.IsCancellationRequested)return 1;
            try{ReportProgress();return 0;}catch(Exception ex){callbackError=ExceptionDispatchInfo.Capture(ex);return 1;}
        };
        try
        {
            var code=NativeMethods.NzExtractBatch(archive,password,files.Select(p=>p.Entry.Index).ToArray(),files.Select(p=>p.Temp).ToArray(),files.Select(p=>Math.Max(p.Entry.Size,1)).ToArray(),(uint)files.Length,itemCallback,callback,IntPtr.Zero);
            callbackError?.Throw();Check(code,cancellation);cancellation.ThrowIfCancellationRequested();
            ReportProgress(true);return new ExtractionResult(completed,skipped);
        }
        finally{GC.KeepAlive(itemCallback);GC.KeepAlive(callback);if(activeTemp!=null&&File.Exists(activeTemp))File.Delete(activeTemp);}
    },cancellation);
    private sealed record ExtractionItem(ArchiveEntry Entry,string Target,string Temp);
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
    internal static void EnsureNoReparse(string path)
    {
        for(var current=path;!string.IsNullOrEmpty(current);current=System.IO.Path.GetDirectoryName(current))
        {
            // One metadata query per component. File.Exists + Directory.Exists +
            // GetAttributes used to repeat the same filesystem lookup up to three times.
            var attributes=NativeMethods.GetFileAttributes(current);
            if(attributes==uint.MaxValue)
            {
                var error=Marshal.GetLastWin32Error();
                if(error is 2 or 3)continue; // Missing file or parent: checked again before writing.
                throw new IOException(new System.ComponentModel.Win32Exception(error).Message,unchecked((int)(0x80070000u|(uint)error)));
            }
            if((attributes&(uint)FileAttributes.ReparsePoint)!=0)throw new IOException(Loc.T("目标路径包含重解析点，已停止操作。", "The target path contains a reparse point; the operation stopped."));
        }
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
