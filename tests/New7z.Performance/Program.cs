using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using New7z.Core;

if(args.Length==2&&args[0]=="--extraction-fixtures")
{
    var fixtureRoot=Path.GetFullPath(args[1]);var fixtureSource=Path.Combine(fixtureRoot,"source");
    Directory.CreateDirectory(Path.Combine(fixtureSource,"empty"));
    var block=new byte[64*1024];new Random(7).NextBytes(block);
    for(var i=0;i<512;i++)
    {
        var directory=Path.Combine(fixtureSource,$"folder-{i/32:D2}");Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory,$"文件-{i:D4}.bin"),block);
    }
    var fixtureEngine=new ArchiveEngine();
    foreach(var format in new[]{"zip","7z","tar"})await fixtureEngine.CreateAsync(Path.Combine(fixtureRoot,"fixture."+format),[fixtureSource],format);
    Console.WriteLine("Created ZIP, solid 7z and TAR fixtures: 512 files, 32 MiB each.");return;
}

if(args.Length>=3&&args[0]=="--extract")
{
    var archive=Path.GetFullPath(args[1]);var destination=Path.GetFullPath(args[2]);
    Directory.CreateDirectory(destination);
    var extractor=new ArchiveEngine();var watch=Stopwatch.StartNew();
    var entries=await extractor.ListAsync(archive);var listSeconds=watch.Elapsed.TotalSeconds;
    Console.WriteLine(JsonSerializer.Serialize(new{Stage="listed",Entries=entries.Count,Files=entries.Count(e=>!e.IsDirectory),Bytes=entries.Where(e=>!e.IsDirectory).Sum(e=>(long)e.Size),Seconds=listSeconds}));
    long notifications=0;var progress=new CallbackProgress(_=>Interlocked.Increment(ref notifications));
    watch.Restart();var extraction=await extractor.ExtractAsync(archive,entries,destination,progress:progress);watch.Stop();
    var row=new{Archive=archive,Entries=entries.Count,extraction.Extracted,extraction.Skipped,ListSeconds=listSeconds,ExtractSeconds=watch.Elapsed.TotalSeconds,Notifications=notifications};
    Console.WriteLine(JsonSerializer.Serialize(row));
    // Hash outside the timed section. Manifests allow before/after comparisons without
    // repeatedly reopening solid archives to verify individual files.
    var manifest=new List<object>();
    foreach(var entry in entries.Where(e=>!e.IsDirectory).OrderBy(e=>e.Index))
    {
        var path=ArchiveEngine.ResolveOutput(destination,entry.Path);using var content=File.OpenRead(path);
        if((ulong)content.Length!=entry.Size)throw new IOException("Extracted length mismatch: "+entry.Path);
        manifest.Add(new{entry.Index,entry.Path,Length=content.Length,Sha256=Convert.ToHexString(SHA256.HashData(content))});
    }
    await File.WriteAllTextAsync(destination+".manifest.json",JsonSerializer.Serialize(manifest));
    await File.WriteAllTextAsync(destination+".result.json",JsonSerializer.Serialize(row));
    return;
}

if(args.Length<2)throw new ArgumentException("Usage: New7z.Performance <source-folder> <output-folder> [rounds] | --extract <archive> <destination> | --extraction-fixtures <output-folder>");
var source=Path.GetFullPath(args[0]);var output=Path.GetFullPath(args[1]);
if(!Directory.Exists(source))throw new DirectoryNotFoundException(source);
if(output.StartsWith(source.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Benchmark output must be outside the source.");
Directory.CreateDirectory(output);
var files=Directory.GetFiles(source,"*",SearchOption.AllDirectories);
var inputBytes=files.Sum(f=>new FileInfo(f).Length);
var engine=new ArchiveEngine();var rows=new List<object>();
var rounds=args.Length>2?int.Parse(args[2]):2;
var names=new[]{"new7z-zip-standard","dotnet-optimal","new7z-zip-fast","dotnet-fastest"};
// Warm JIT/engine startup with a generated fixture, not user content.
var warm=Path.Combine(output,"warm.txt");await File.WriteAllTextAsync(warm,"New7z performance warmup.");
await engine.CreateAsync(Path.Combine(output,"warm.zip"),[warm],"zip",1);
for(var round=1;round<=rounds;round++)
{
    foreach(var profile in round%2==1?names:names.Reverse())
    {
        var target=Path.Combine(output,$"{profile}-{round}.zip");
        long callbacks=0;var progress=new CallbackProgress(_=>Interlocked.Increment(ref callbacks));
        var process=Process.GetCurrentProcess();var cpu=process.TotalProcessorTime;var timer=Stopwatch.StartNew();
        if(profile.StartsWith("new7z"))await engine.CreateAsync(target,[source],"zip",profile.EndsWith("standard")?5:1,progress:progress);
        else ZipFile.CreateFromDirectory(source,target,profile=="dotnet-optimal"?CompressionLevel.Optimal:CompressionLevel.Fastest,true);
        timer.Stop();process.Refresh();
        var row=new {Profile=profile,Round=round,Seconds=timer.Elapsed.TotalSeconds,CpuSeconds=(process.TotalProcessorTime-cpu).TotalSeconds,OutputBytes=new FileInfo(target).Length,Callbacks=callbacks};
        rows.Add(row);Console.WriteLine(JsonSerializer.Serialize(row));
        // Decode names according to the current OEM page until UTF-8 ZIP output is enabled.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var stream=File.OpenRead(target);
        using var zip=new ZipArchive(stream,ZipArchiveMode.Read,false,Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage));
        foreach(var file in files)
        {
            var name=(Path.GetFileName(source)+"/"+Path.GetRelativePath(source,file)).Replace('\\','/');
            var entry=zip.GetEntry(name)??throw new IOException("Missing benchmark ZIP entry.");
            using var original=File.OpenRead(file);using var extracted=entry.Open();
            if(!SHA256.HashData(original).SequenceEqual(SHA256.HashData(extracted)))throw new IOException("Benchmark SHA-256 mismatch.");
        }
    }
}
var result=new {InputFolder=source,FileCount=files.Length,InputBytes=inputBytes,Comparison=".NET ZIP is a reference baseline, not a measured Explorer Compress to command.",Verified="All source files SHA-256",Rows=rows};
await File.WriteAllTextAsync(Path.Combine(output,"results.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));

sealed class CallbackProgress(Action<EngineProgress> callback):IProgress<EngineProgress>{public void Report(EngineProgress value)=>callback(value);}
