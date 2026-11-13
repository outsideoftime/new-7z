using System.Diagnostics;
using K4os.Compression.LZ4.Streams;

namespace New7z.Core;

// Standard Zstandard/LZ4 frames contain one stream. Raw LZ4 blocks are rejected.
internal static class StreamArchive
{
    internal static bool Matches(string path)
    {
        if(!File.Exists(path))return false;
        using var stream=File.OpenRead(path);Span<byte> signature=stackalloc byte[4];
        return stream.Read(signature)==4&&(signature.SequenceEqual(new byte[]{4,34,77,24})||signature.SequenceEqual(new byte[]{40,181,47,253}));
    }
    private static bool IsZstd(string path){using var input=File.OpenRead(path);return input.ReadByte()==40;}
    private static ulong Copy(string path,Stream? destination,ulong limit,CancellationToken cancellation,IProgress<EngineProgress>? progress=null)
    {
        using var input=File.OpenRead(path);using Stream decoder=IsZstd(path)?new ZstdSharp.DecompressionStream(input):LZ4Stream.Decode(input);
        var buffer=new byte[64*1024];ulong count=0;var timer=Stopwatch.StartNew();
        int read;
        while((read=decoder.Read(buffer))!=0)
        {
            cancellation.ThrowIfCancellationRequested();
            if((ulong)read>limit-count)throw new IOException(Loc.T("内容超过解压大小限制。","Content exceeds the extraction size limit."));
            count+=(uint)read;destination?.Write(buffer,0,read);
            if(timer.ElapsedMilliseconds>=50){progress?.Report(new(0,count));timer.Restart();}
        }
        cancellation.ThrowIfCancellationRequested();return count;
    }
    internal static EngineMessage Execute(EngineRequest request,IProgress<EngineProgress> progress,CancellationToken cancellation)
    {
        if(request.Action=="create"){Create(request,progress,cancellation);return new();}
        var name=Path.GetFileNameWithoutExtension(request.Path);
        if(name.Length==0)name="content";
        if(request.Action=="list")
        {
            var size=Copy(request.Path,null,ExtractionLimits.Default.MaxFileBytes,cancellation,progress);
            return new(){Entries=[new(){Index=0,Path=name,Size=size,PackedSize=(ulong)new FileInfo(request.Path).Length}]};
        }
        if(request.Action=="test"){Copy(request.Path,null,ExtractionLimits.Default.MaxFileBytes,cancellation,progress);return new();}
        var entry=request.Entries.Single();
        if(entry.Index!=0||entry.IsDirectory||entry.IsLink)throw new IOException("Invalid LZ4 stream entry.");
        if(request.Action=="read")
        {
            using var memory=new MemoryStream();Copy(request.Path,memory,Math.Min(request.ReadLimit,int.MaxValue),cancellation);return new(){Bytes=memory.ToArray()};
        }
        if(request.Action!="extract")throw new IOException("LZ4 archive updates are not supported.");
        request.Limits.Validate(request.Entries);
        var root=Path.GetFullPath(request.Destination);var target=ArchiveEngine.ResolveOutput(root,name);
        if(string.Equals(Path.GetFullPath(request.Path),target,StringComparison.OrdinalIgnoreCase))throw new IOException("Cannot replace the source archive.");
        if(File.Exists(target)&&request.Policy==ConflictPolicy.Skip)return new(){Extraction=new(0,1)};
        if(request.Policy==ConflictPolicy.Rename)
        {
            var original=target;for(var suffix=2;File.Exists(target)||Directory.Exists(target);suffix++)target=Path.Combine(root,Path.GetFileNameWithoutExtension(original)+$" ({suffix})"+Path.GetExtension(original));
        }
        var zone=MarkOfTheWeb.Read(request.Path);Directory.CreateDirectory(root);NativeArchiveEngine.EnsureNoReparse(root);
        var temp=Path.Combine(root,".new7z-"+request.Id+"-0.partial");
        try
        {
            using(var output=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))Copy(request.Path,output,Math.Min(entry.Size,Math.Min(request.Limits.MaxFileBytes,request.Limits.MaxTotalBytes)),cancellation,progress);
            MarkOfTheWeb.Apply(temp,zone);NativeArchiveEngine.EnsureNoReparse(target);cancellation.ThrowIfCancellationRequested();File.Move(temp,target,request.Policy==ConflictPolicy.Replace);
            return new(){Extraction=new(1,0)};
        }
        finally{File.Delete(temp);}
    }
    private static void Create(EngineRequest request,IProgress<EngineProgress> progress,CancellationToken cancellation)
    {
        if(request.Sources.Length!=1||!File.Exists(request.Sources[0]))throw new ArgumentException(Loc.T("Zstandard 和 LZ4 每次压缩一个文件。多个文件或文件夹请先打包为 TAR。","Zstandard and LZ4 compress a single file. Pack folders or multiple files into TAR first."));
        if(!string.IsNullOrEmpty(request.Password)||request.EncryptNames)throw new ArgumentException(Loc.T("Zstandard 和 LZ4 不支持密码。","Zstandard and LZ4 do not support passwords."));
        if(request.Level is <0 or >9)throw new ArgumentOutOfRangeException(nameof(request.Level));
        var source=Path.GetFullPath(request.Sources[0]);NativeArchiveEngine.EnsureNoReparse(source);
        var output=Path.GetFullPath(request.Path);if(File.Exists(output)||Directory.Exists(output))throw new IOException("The output name already exists.");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);var temp=output+"."+request.Id+".partial";
        try
        {
            using(var input=File.OpenRead(source))
            using(var target=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {
                using Stream encoder=request.Format=="zst"?new ZstdSharp.CompressionStream(target,request.Level):LZ4Stream.Encode(target,new LZ4EncoderSettings{CompressionLevel=request.Level<=3?K4os.Compression.LZ4.LZ4Level.L00_FAST:request.Level>=8?K4os.Compression.LZ4.LZ4Level.L09_HC:K4os.Compression.LZ4.LZ4Level.L03_HC});
                encoder.Write(Array.Empty<byte>(),0,0); // Open an empty frame as well.
                var buffer=new byte[128*1024];ulong completed=0;var timer=Stopwatch.StartNew();
                progress.Report(new((ulong)input.Length,0));int count;
                while((count=input.Read(buffer))>0){cancellation.ThrowIfCancellationRequested();encoder.Write(buffer,0,count);completed+=(uint)count;if(timer.ElapsedMilliseconds>=50){progress.Report(new((ulong)input.Length,completed));timer.Restart();}}
                progress.Report(new((ulong)input.Length,completed));
            }
            cancellation.ThrowIfCancellationRequested();File.Move(temp,output,false);
        }
        finally{File.Delete(temp);}
    }
}

