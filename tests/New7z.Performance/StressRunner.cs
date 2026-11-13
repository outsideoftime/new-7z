using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using New7z.Core;

internal static class StressRunner
{
    internal static async Task RunAsync(string output,int largeMiB,int fileCount)
    {
        if(largeMiB<1||fileCount is <1 or >1_000_000)throw new ArgumentOutOfRangeException();
        output=Path.GetFullPath(output);Directory.CreateDirectory(output);
        var root=Path.Combine(output,"run-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var disk=new DriveInfo(Path.GetPathRoot(root)!);var needed=(long)largeMiB*1024*1024*3+2L*1024*1024*1024;
        if(disk.AvailableFreeSpace<needed)throw new IOException("Stress test needs at least "+needed+" free bytes.");
        var engine=new ArchiveEngine();var results=new List<object>();long peakWorker=0;var started=DateTimeOffset.UtcNow;
        using var sample=new Timer(_=>
        {
            foreach(var process in Process.GetProcessesByName("New7z.EngineHost"))using(process){try{var bytes=process.WorkingSet64;InterlockedExtensions.Max(ref peakWorker,bytes);}catch{}}
        },null,0,100);
        try
        {
            var large=Path.Combine(root,"large.bin");var block=new byte[1024*1024];new Random(51).NextBytes(block);
            using(var stream=new FileStream(large,FileMode.CreateNew,FileAccess.Write,FileShare.None,1024*1024))
                for(var i=0;i<largeMiB;i++)await stream.WriteAsync(block);
            var expectedLength=(long)largeMiB*1024*1024;
            byte[] expectedHash;using(var input=File.OpenRead(large))expectedHash=await SHA256.HashDataAsync(input);
            var watch=Stopwatch.StartNew();var zip=Path.Combine(root,"large.zip");await engine.CreateAsync(zip,[large],"zip",0);
            var createSeconds=watch.Elapsed.TotalSeconds;var entries=await engine.ListAsync(zip);
            if(entries.Single().Size!=(ulong)expectedLength)throw new IOException("Large entry size was truncated.");
            using(var reference=ZipFile.OpenRead(zip))if(reference.Entries.Single().Length!=expectedLength)throw new IOException("Independent ZIP64 metadata verification failed.");
            watch.Restart();var extracted=Path.Combine(root,"large-out");await engine.ExtractAsync(zip,entries,extracted);
            var extractSeconds=watch.Elapsed.TotalSeconds;
            using(var stream=File.OpenRead(Path.Combine(extracted,"large.bin"))){var hash=await SHA256.HashDataAsync(stream);if(!expectedHash.SequenceEqual(hash))throw new IOException("Large file SHA-256 mismatch.");}
            results.Add(new{Case="large-zip",Bytes=expectedLength,Zip64=expectedLength>uint.MaxValue,CreateSeconds=createSeconds,ExtractSeconds=extractSeconds,SHA256=Convert.ToHexString(expectedHash)});
            File.Delete(large);File.Delete(zip);Directory.Delete(extracted,true);
            var many=Path.Combine(root,"many");Directory.CreateDirectory(many);
            var payload="New7z many-file pressure fixture."u8.ToArray();
            for(var i=0;i<fileCount;i++){var directory=Path.Combine(many,$"folder-{i/1000:D4}");Directory.CreateDirectory(directory);await File.WriteAllBytesAsync(Path.Combine(directory,$"文件-{i:D7}.txt"),payload);}
            foreach(var format in new[]{"zip","7z"})
            {
                var archive=Path.Combine(root,"many."+format);watch.Restart();await engine.CreateAsync(archive,[many],format,1);var packedSeconds=watch.Elapsed.TotalSeconds;
                watch.Restart();var items=await engine.ListAsync(archive);var listSeconds=watch.Elapsed.TotalSeconds;
                if(items.Count(e=>!e.IsDirectory)!=fileCount)throw new IOException("Many-file entry count mismatch.");
                var destination=Path.Combine(root,"many-out-"+format);watch.Restart();var result=await engine.ExtractAsync(archive,items,destination);var unpackSeconds=watch.Elapsed.TotalSeconds;
                if(result.Extracted!=fileCount)throw new IOException("Many-file extraction count mismatch.");
                foreach(var item in items.Where(e=>!e.IsDirectory))if(!(await File.ReadAllBytesAsync(ArchiveEngine.ResolveOutput(destination,item.Path))).SequenceEqual(payload))throw new IOException("Many-file content mismatch: "+item.Path);
                watch.Restart();var sorted=ArchiveSorting.Sort(items,ArchiveSortColumn.Size,true);var sortSeconds=watch.Elapsed.TotalSeconds;
                results.Add(new{Case="many-"+format,Files=fileCount,Entries=items.Count,CreateSeconds=packedSeconds,ListSeconds=listSeconds,ExtractSeconds=unpackSeconds,SortSeconds=sortSeconds,VerifiedFiles=fileCount});
                Directory.Delete(destination,true);File.Delete(archive);
            }
            var report=new{Status="PASS",StartedUtc=started,FinishedUtc=DateTimeOffset.UtcNow,LargeMiB=largeMiB,FileCount=fileCount,PeakWorkerWorkingSetBytes=peakWorker,ParentPeakWorkingSetBytes=Process.GetCurrentProcess().PeakWorkingSet64,Results=results};
            var json=JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true});await File.WriteAllTextAsync(Path.Combine(output,"stress-result.json"),json);Console.WriteLine(json);
        }
        finally{Directory.Delete(root,true);}
    }
    private static class InterlockedExtensions
    {
        internal static void Max(ref long target,long value){long old;do{old=Interlocked.Read(ref target);if(old>=value)return;}while(Interlocked.CompareExchange(ref target,value,old)!=old);}
    }
}
