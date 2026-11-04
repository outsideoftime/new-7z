using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using New7z.Core;

if(args.Length<2)throw new ArgumentException("Usage: New7z.Performance <source-folder> <output-folder> [rounds]");
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
