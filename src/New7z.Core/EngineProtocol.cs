using System.Text.Json;
using System.Text.Json.Serialization;

namespace New7z.Core;

internal sealed record EngineRequest
{
    public string Id {get;init;}=Guid.NewGuid().ToString("N");
    public string Action {get;init;}="";
    public string Language {get;init;}=Loc.Language;
    public string Path {get;init;}="";
    public string? Password {get;init;}
    public ArchiveEntry[] Entries {get;init;}=[];
    public string Destination {get;init;}="";
    public ConflictPolicy Policy {get;init;}
    public ExtractionLimits Limits {get;init;}=ExtractionLimits.Default;
    public ulong ReadLimit {get;init;}
    public string[] Sources {get;init;}=[];
    public SourceItem[] Additions {get;init;}=[];
    public uint[] Indices {get;init;}=[];
    public string Format {get;init;}="7z";
    public int Level {get;init;}=5;
    public bool EncryptNames {get;init;}
    public string? Method {get;init;}
}
internal sealed record EngineMessage
{
    public string Kind {get;init;}="result";
    public EngineProgress? Progress {get;init;}
    public ArchiveEntry[]? Entries {get;init;}
    public byte[]? Bytes {get;init;}
    public ExtractionResult? Extraction {get;init;}
    public string? ErrorType {get;init;}
    public string? Error {get;init;}
}
[JsonSerializable(typeof(EngineRequest))]
[JsonSerializable(typeof(EngineMessage))]
internal partial class EngineProtocolJson:JsonSerializerContext;

public static class EngineWorker
{
    public static async Task RunAsync()
    {
        Console.InputEncoding=new System.Text.UTF8Encoding(false);Console.OutputEncoding=new System.Text.UTF8Encoding(false);
        var writeLock=new object();
        void Send(EngineMessage message){lock(writeLock){Console.WriteLine(JsonSerializer.Serialize(message,EngineProtocolJson.Default.EngineMessage));Console.Out.Flush();}}
        try
        {
            var line=await Console.In.ReadLineAsync().ConfigureAwait(false)??throw new IOException("Missing request.");
            var request=JsonSerializer.Deserialize(line,EngineProtocolJson.Default.EngineRequest)??throw new IOException("Invalid request.");
            if(!Guid.TryParseExact(request.Id,"N",out _))throw new IOException("Invalid operation id.");
            Loc.Language=request.Language;
            using var cancellation=new CancellationTokenSource();
            using var acknowledgement=new SemaphoreSlim(0);
            _=Task.Run(async()=>{while(await Console.In.ReadLineAsync().ConfigureAwait(false) is { } command){if(command=="cancel")cancellation.Cancel();else if(command=="continue")acknowledgement.Release();}cancellation.Cancel();});
            var engine=new NativeArchiveEngine{OperationId=request.Id};
            var progress=new InlineEngineProgress(p=>{Send(new(){Kind="progress",Progress=p});acknowledgement.Wait(cancellation.Token);cancellation.Token.ThrowIfCancellationRequested();});
            var token=cancellation.Token;
            var result=new EngineMessage();
            if((request.Action=="create"&&request.Format is "zst" or "lz4")||(request.Action is "list" or "read" or "test" or "extract"&&StreamArchive.Matches(request.Path)))
            {
                result=await Task.Run(()=>StreamArchive.Execute(request,progress,token),token).ConfigureAwait(false);Send(result);return;
            }
            switch(request.Action)
            {
                case "list":result=result with{Entries=(await engine.ListAsync(request.Path,request.Password,token).ConfigureAwait(false)).ToArray()};break;
                case "read":result=result with{Bytes=await engine.ReadAsync(request.Path,request.Entries.Single(),request.Password,request.ReadLimit,token).ConfigureAwait(false)};break;
                case "test":await engine.TestAsync(request.Path,request.Password,progress,token).ConfigureAwait(false);break;
                case "create":await engine.CreateAsync(request.Path,request.Sources,request.Format,request.Level,request.Password,request.EncryptNames,request.Method,progress,token).ConfigureAwait(false);break;
                case "modify":await engine.ModifyAsync(request.Path,request.Indices,request.Additions,request.Password,progress,token).ConfigureAwait(false);break;
                case "extract":result=result with{Extraction=await engine.ExtractAsync(request.Path,request.Entries,request.Destination,request.Policy,request.Password,progress,token,request.Limits).ConfigureAwait(false)};break;
                default:throw new ArgumentException("Unknown worker action.");
            }
            Send(result);
        }
        catch(Exception ex){Send(new(){Kind="error",ErrorType=ex.GetType().Name,Error=ex.Message});}
    }
}

