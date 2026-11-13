using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace New7z.Core;

public enum ConflictPolicy { Rename, Skip, Replace }
public sealed record ExtractionResult(int Extracted, int Skipped);
public sealed record SourceItem(string Path, string Name);

// Only the worker initializes 7z.dll. A failed decoder never runs in the UI process.
public sealed class ArchiveEngine
{
    public async Task<IReadOnlyList<ArchiveEntry>> ListAsync(string path,string? password=null,CancellationToken cancellation=default)
        => (await RunAsync(new(){Action="list",Path=path,Password=password},null,cancellation).ConfigureAwait(false)).Entries??[];
    public async Task<byte[]> ReadAsync(string path,ArchiveEntry entry,string? password=null,ulong limit=32UL*1024*1024,CancellationToken cancellation=default)
        => (await RunAsync(new(){Action="read",Path=path,Entries=[entry],Password=password,ReadLimit=limit},null,cancellation).ConfigureAwait(false)).Bytes??[];
    public async Task TestAsync(string path,string? password=null,IProgress<EngineProgress>? progress=null,CancellationToken cancellation=default)
        => await RunAsync(new(){Action="test",Path=path,Password=password},progress,cancellation).ConfigureAwait(false);
    public async Task CreateAsync(string output,IReadOnlyList<string> sources,string format="7z",int level=5,string? password=null,bool encryptNames=false,string? method=null,IProgress<EngineProgress>? progress=null,CancellationToken cancellation=default)
        => await RunAsync(new(){Action="create",Path=output,Sources=sources.ToArray(),Format=format,Level=level,Password=password,EncryptNames=encryptNames,Method=method},progress,cancellation).ConfigureAwait(false);
    public async Task ModifyAsync(string archive,IReadOnlyList<uint> removeIndices,IReadOnlyList<SourceItem> additions,string? password=null,IProgress<EngineProgress>? progress=null,CancellationToken cancellation=default)
        => await RunAsync(new(){Action="modify",Path=archive,Indices=removeIndices.ToArray(),Additions=additions.ToArray(),Password=password},progress,cancellation).ConfigureAwait(false);
    public async Task<ExtractionResult> ExtractAsync(string archive,IReadOnlyList<ArchiveEntry> entries,string destination,ConflictPolicy policy=ConflictPolicy.Rename,string? password=null,IProgress<EngineProgress>? progress=null,CancellationToken cancellation=default,ExtractionLimits? limits=null)
        => (await RunAsync(new(){Action="extract",Path=archive,Entries=entries.ToArray(),Destination=destination,Policy=policy,Password=password,Limits=limits??ExtractionLimits.Default},progress,cancellation).ConfigureAwait(false)).Extraction!;
    public static bool SupportsUpdate(string path)=>NativeArchiveEngine.SupportsUpdate(path);
    public static IReadOnlyList<SourceItem> ExpandSources(IReadOnlyList<string> sources,string? folder=null,CancellationToken cancellation=default)=>NativeArchiveEngine.ExpandSources(sources,folder,cancellation);
    public static string ResolveOutput(string root,string path)=>NativeArchiveEngine.ResolveOutput(root,path);
    public static string DecodeText(byte[] bytes,string encoding="自动")=>NativeArchiveEngine.DecodeText(bytes,encoding);

    internal static async Task<EngineMessage> RunAsync(EngineRequest request,IProgress<EngineProgress>? progress,CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var host=Path.Combine(AppContext.BaseDirectory,"New7z.EngineHost.exe");
        if(!File.Exists(host))throw new FileNotFoundException(Loc.T("解压引擎进程缺失，请修复安装。","The archive worker is missing. Repair the installation."),host);
        using var process=new Process{StartInfo=new(host){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardInputEncoding=new UTF8Encoding(false),StandardOutputEncoding=new UTF8Encoding(false)}};
        process.Start();
        using var job=new EngineProcessJob(process);
        var errors=process.StandardError.ReadToEndAsync();
        ExceptionDispatchInfo? callbackError=null;
        using var killTimer=new Timer(_=>{try{if(!process.HasExited)process.Kill(true);}catch{}},null,Timeout.Infinite,Timeout.Infinite);
        var inputLock=new object();var cancelSent=0;
        void CancelWorker()
        {
            if(Interlocked.Exchange(ref cancelSent,1)!=0)return;
            try{lock(inputLock){process.StandardInput.WriteLine("cancel");process.StandardInput.Flush();}}catch{}
            killTimer.Change(5000,Timeout.Infinite);
        }
        try
        {
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request,EngineProtocolJson.Default.EngineRequest)).ConfigureAwait(false);
            await process.StandardInput.FlushAsync().ConfigureAwait(false);
            using var registration=cancellation.Register(CancelWorker);
            EngineMessage? result=null;
            while(await process.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                var message=JsonSerializer.Deserialize(line,EngineProtocolJson.Default.EngineMessage)??throw new IOException("Invalid worker response.");
                if(message.Kind=="progress")
                {
                    try{if(callbackError==null)progress?.Report(message.Progress!);}
                    catch(Exception ex){callbackError=ExceptionDispatchInfo.Capture(ex);CancelWorker();}
                    if(callbackError==null&&!cancellation.IsCancellationRequested)
                        lock(inputLock){process.StandardInput.WriteLine("continue");process.StandardInput.Flush();}
                }
                else result=message;
            }
            await process.WaitForExitAsync().ConfigureAwait(false);
            await errors.ConfigureAwait(false);
            callbackError?.Throw();cancellation.ThrowIfCancellationRequested();
            if(process.ExitCode!=0||result==null)throw new IOException(Loc.T($"解压引擎进程异常退出（0x{process.ExitCode:X8}），应用可继续使用。",$"The archive worker exited unexpectedly (0x{process.ExitCode:X8}). The app can still be used."));
            if(result.Kind=="error")throw result.ErrorType switch
            {
                nameof(ArchivePasswordException)=>new ArchivePasswordException(),
                nameof(OperationCanceledException)=>new OperationCanceledException(cancellation),
                nameof(ArgumentException)=>new ArgumentException(result.Error),
                nameof(ArgumentOutOfRangeException)=>new ArgumentOutOfRangeException(null,result.Error),
                nameof(InvalidDataException)=>new InvalidDataException(result.Error),
                _=>new IOException(result.Error)
            };
            return result;
        }
        finally
        {
            if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync().ConfigureAwait(false);}
            Cleanup(request);
        }
    }
    private static void Cleanup(EngineRequest request)
    {
        var paths=new List<string>();
        if(request.Action is "create" or "modify")paths.Add(Path.GetFullPath(request.Path)+"."+request.Id+".partial");
        if(request.Action=="extract")foreach(var entry in request.Entries.Where(e=>!e.IsDirectory))
        {
            try{var target=ResolveOutput(Path.GetFullPath(request.Destination),entry.Path);paths.Add(Path.Combine(Path.GetDirectoryName(target)!,".new7z-"+request.Id+"-"+entry.Index+".partial"));}catch(IOException){}
        }
        foreach(var path in paths){try{NativeArchiveEngine.EnsureNoReparse(path);File.Delete(path);}catch{}}
    }
    public string GetDragDiagnostics()=>NativeArchiveEngine.GetDragDiagnostics();
    public ArchiveDragData CreateDragData(IntPtr package,string archive,IReadOnlyList<ArchiveEntry> selected,IReadOnlyList<ArchiveEntry> roots,string tempPath,IntPtr owner,string? password=null)
    {
        NativeMethods.DragPrepareCallback prepare=(callback,context)=>
        {
            try
            {
                using var cancellation=new CancellationTokenSource();
                var report=Marshal.GetDelegateForFunctionPointer<NativeMethods.ProgressCallback>(callback);
                using var pending=new System.Collections.Concurrent.BlockingCollection<(EngineProgress Progress,ManualResetEventSlim Acknowledged)>();
                var progress=new InlineEngineProgress(p=>{using var acknowledged=new ManualResetEventSlim();pending.Add((p,acknowledged));acknowledged.Wait();});
                var extraction=ExtractAsync(archive,selected,tempPath,password:password,progress:progress,cancellation:cancellation.Token);
                // Keep the shell's COM progress dialog on the apartment that created it.
                while(!extraction.IsCompleted)
                    if(pending.TryTake(out var item,50)){try{if(report(item.Progress.Total,item.Progress.Completed,context)!=0)cancellation.Cancel();}finally{item.Acknowledged.Set();}}
                extraction.GetAwaiter().GetResult();return 0;
            }
            catch(OperationCanceledException){return unchecked((int)0x80004004);}
            catch(ArchivePasswordException){return unchecked((int)0x80040201);}
            catch{return unchecked((int)0x80004005);}
        };
        return new(NativeArchiveEngine.CreateDragData(package,archive,selected,roots,tempPath,owner,password,prepare));
    }
    public sealed class ArchiveDragData:IDisposable
    {
        private readonly NativeArchiveEngine.ArchiveDragData data;
        internal ArchiveDragData(NativeArchiveEngine.ArchiveDragData data)=>this.data=data;
        public void ThrowIfFailed()=>data.ThrowIfFailed();
        public bool WasTransferred=>data.WasTransferred;
        public void Dispose()=>data.Dispose();
    }
}
internal sealed class InlineEngineProgress(Action<EngineProgress> action):IProgress<EngineProgress>{public void Report(EngineProgress value)=>action(value);}
