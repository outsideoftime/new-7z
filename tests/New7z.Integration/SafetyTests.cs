using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using New7z.Core;

internal static class SafetyTests
{
    internal static async Task RunAsync(string parent,List<string> checks)
    {
        var root=Path.Combine(parent,"safety");Directory.CreateDirectory(root);var engine=new ArchiveEngine();
        void Assert(bool condition,string name){if(!condition)throw new Exception(name);checks.Add(name);}
        async Task Reject(Func<Task> action,string name){try{await action();}catch(IOException){checks.Add(name);return;}throw new Exception(name+": was accepted");}
        var content=Path.Combine(root,"payload.txt");await File.WriteAllTextAsync(content,"New7z safety fixture.");
        foreach(var type in new[]{TarEntryType.SymbolicLink,TarEntryType.HardLink})
        {
            var archive=Path.Combine(root,type+".tar");
            using(var stream=File.Create(archive))using(var tar=new TarWriter(stream))
            {
                tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile,"safe.txt"){DataStream=new MemoryStream("safe"u8.ToArray())});
                tar.WriteEntry(new PaxTarEntry(type,"link.txt"){LinkName="../outside.txt"});
            }
            var entries=await engine.ListAsync(archive);Assert(entries.Single(e=>e.Name=="link.txt").IsLink,"detect TAR "+type);
            var output=Path.Combine(root,"reject-"+type);await Reject(()=>engine.ExtractAsync(archive,entries,output),"reject TAR "+type);
            Assert(!File.Exists(Path.Combine(output,"safe.txt")),"link preflight prevents earlier output "+type);
            await Reject(()=>engine.ReadAsync(archive,entries.Single(e=>e.IsLink)),"reject link preview "+type);
        }
        var zipPath=Path.Combine(root,"fixture.zip");await engine.CreateAsync(zipPath,[content],"zip");var zipEntries=await engine.ListAsync(zipPath);
        var linkZip=Path.Combine(root,"unix-link.zip");
        using(var zip=ZipFile.Open(linkZip,ZipArchiveMode.Create))
        {
            var entry=zip.CreateEntry("unix-link.txt");entry.ExternalAttributes=unchecked((int)0xA1FF0000);using var writer=new StreamWriter(entry.Open());writer.Write("../outside.txt");
        }
        var linkEntries=await engine.ListAsync(linkZip);Assert(linkEntries.Single().IsLink,"detect ZIP POSIX symlink mode");
        await Reject(()=>engine.ExtractAsync(linkZip,linkEntries,Path.Combine(root,"zip-link-out")),"reject ZIP symlink");
        await Reject(()=>engine.ReadAsync(linkZip,linkEntries.Single() with{IsLink=false}),"native preview rechecks link metadata");
        var outside=Path.Combine(root,"outside");Directory.CreateDirectory(outside);var target=Path.Combine(root,"junction");
        var start=new ProcessStartInfo("cmd.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in new[]{"/c","mklink","/J",target,outside})start.ArgumentList.Add(arg);
        using(var mklink=Process.Start(start)!){await mklink.WaitForExitAsync();if(mklink.ExitCode!=0)throw new IOException("Cannot create junction fixture: "+await mklink.StandardError.ReadToEndAsync());}
        try
        {
            await Reject(()=>engine.ExtractAsync(zipPath,zipEntries,target),"reject destination junction");
            Assert(!File.Exists(Path.Combine(outside,"payload.txt")),"junction does not write outside destination");
            await Reject(()=>engine.CreateAsync(Path.Combine(root,"junction-input.zip"),[target],"zip"),"reject source junction");
        }
        finally{Directory.Delete(target);}
        var bomb=Path.Combine(root,"bomb.zip");
        using(var zip=ZipFile.Open(bomb,ZipArchiveMode.Create))
            for(var i=0;i<3;i++){using var output=zip.CreateEntry($"zeros-{i}.bin",CompressionLevel.SmallestSize).Open();output.Write(new byte[1024*1024]);}
        var bombEntries=await engine.ListAsync(bomb);var bombOut=Path.Combine(root,"bomb-out");
        await Reject(()=>engine.ExtractAsync(bomb,bombEntries,bombOut,limits:new(MaxTotalBytes:2*1024*1024)),"reject aggregate extraction bomb before output");
        await Reject(()=>engine.ExtractAsync(bomb,bombEntries,bombOut,limits:new(MaxFileBytes:1024)),"reject oversized extraction entry");
        await Reject(()=>engine.ExtractAsync(bomb,bombEntries,bombOut,limits:new(MaxEntries:2)),"reject extraction entry count");
        await Reject(()=>engine.ExtractAsync(bomb,[bombEntries[0] with{Size=ulong.MaxValue}],bombOut),"reject ulong size overflow");
        Assert(!Directory.Exists(bombOut)||!Directory.EnumerateFileSystemEntries(bombOut).Any(),"bomb rejection creates no output");
        var zone="[ZoneTransfer]\r\nZoneId=3\r\nReferrerUrl=https://example.invalid/page\r\nHostUrl=https://example.invalid/archive.zip\r\n";
        foreach(var format in new[]{"zip","7z","tar","zst","lz4"})
        {
            var archive=Path.Combine(root,"marked."+format);await engine.CreateAsync(archive,[content],format);
            await File.WriteAllTextAsync(archive+":Zone.Identifier",zone,new UTF8Encoding(false));
            var list=await engine.ListAsync(archive);var output=Path.Combine(root,"motw-"+format);
            await engine.ExtractAsync(archive,list,output);var extracted=Path.Combine(output,list.Single().Path);
            Assert(await File.ReadAllTextAsync(extracted+":Zone.Identifier")==zone,"MotW exact propagation "+format);
            Assert(await File.ReadAllTextAsync(extracted)==await File.ReadAllTextAsync(content),"stream roundtrip "+format);
            await engine.TestAsync(archive);checks.Add("stream integrity "+format);
            var preview=await engine.ReadAsync(archive,list.Single());var original=await File.ReadAllBytesAsync(content);
            Assert(preview.SequenceEqual(original),"stream preview "+format);
            await File.WriteAllTextAsync(extracted+":Zone.Identifier","existing zone");
            await engine.ExtractAsync(archive,list,output,ConflictPolicy.Skip);
            Assert(await File.ReadAllTextAsync(extracted+":Zone.Identifier")=="existing zone","MotW skip preserves destination "+format);
            await engine.ExtractAsync(archive,list,output,ConflictPolicy.Replace);
            Assert(await File.ReadAllTextAsync(extracted+":Zone.Identifier")==zone,"MotW replace applies source zone "+format);
            await engine.ExtractAsync(archive,list,output,ConflictPolicy.Rename);
            var renamed=Path.Combine(output,Path.GetFileNameWithoutExtension(extracted)+" (2)"+Path.GetExtension(extracted));
            Assert(await File.ReadAllTextAsync(renamed+":Zone.Identifier")==zone,"MotW rename propagation "+format);
            if(format is "zst" or "lz4")
            {
                var bad=Path.Combine(root,"truncated."+format);var bytes=await File.ReadAllBytesAsync(archive);await File.WriteAllBytesAsync(bad,bytes[..^3]);
                await Reject(()=>engine.TestAsync(bad),"reject truncated frame "+format);
                await Reject(()=>engine.ReadAsync(archive,list.Single(),limit:2),"stream preview output cap "+format);
                await Reject(()=>engine.ExtractAsync(archive,[list.Single() with{Size=1}],Path.Combine(root,"stream-limit-"+format)),"stream extraction output cap "+format);
            }
        }
        foreach(var format in new[]{"zst","lz4"})
        {
            var rejected=Path.Combine(root,"multi."+format);var refused=false;
            try{await engine.CreateAsync(rejected,[content,zipPath],format);}catch(ArgumentException){refused=true;}
            Assert(refused&&!File.Exists(rejected),"stream format rejects multiple sources "+format);
            refused=false;try{await engine.CreateAsync(rejected,[content],format,password:"secret");}catch(ArgumentException){refused=true;}
            Assert(refused&&!File.Exists(rejected),"stream format rejects unsupported encryption "+format);
        }
        var nested=Path.Combine(root,"nested.zip");await engine.CreateAsync(nested,[zipPath],"zip");await File.WriteAllTextAsync(nested+":Zone.Identifier",zone);
        var nestedOut=Path.Combine(root,"nested-out");await engine.ExtractAsync(nested,await engine.ListAsync(nested),nestedOut);
        var inner=Path.Combine(nestedOut,"fixture.zip");var innerOut=Path.Combine(root,"inner-out");await engine.ExtractAsync(inner,await engine.ListAsync(inner),innerOut);
        Assert(await File.ReadAllTextAsync(Path.Combine(innerOut,"payload.txt:Zone.Identifier"))==zone,"MotW nested archive propagation");
        var plainOut=Path.Combine(root,"plain-out");await engine.ExtractAsync(zipPath,zipEntries,plainOut);
        Assert(!File.Exists(Path.Combine(plainOut,"payload.txt:Zone.Identifier")),"local archive does not invent MotW");
        await File.WriteAllTextAsync(zipPath+":Zone.Identifier",zone);
        await engine.ModifyAsync(zipPath,[],[new SourceItem(content,"added.txt")]);
        Assert(await File.ReadAllTextAsync(zipPath+":Zone.Identifier")==zone,"archive modification preserves MotW");
        var killed=false;var crashOutput=Path.Combine(root,"crashed.7z");
        await Reject(()=>engine.CreateAsync(crashOutput,[content],progress:new InlineProgress(_=>
        {
            if(killed)return;
            var host=Process.GetProcessesByName("New7z.EngineHost").First(p=>string.Equals(p.MainModule?.FileName,Path.Combine(AppContext.BaseDirectory,"New7z.EngineHost.exe"),StringComparison.OrdinalIgnoreCase));
            using(host){host.Kill();killed=true;}
        })),"abrupt worker termination contained");
        Assert(killed&&!File.Exists(crashOutput)&&!Directory.EnumerateFiles(root,"crashed.7z.*.partial").Any(),"worker crash cleans owned temporary output");
        Assert((await engine.ListAsync(zipPath)).Count==zipEntries.Count+1,"engine recovers with a fresh worker after crash");
        Assert(!Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Any(m=>m.ModuleName.Equals("7z.dll",StringComparison.OrdinalIgnoreCase)),"UI caller never loads 7z.dll");
        var rows=new ArchiveEntry[]{new(){Index=0,Path="b",Size=2,PackedSize=1,FileTime=3},new(){Index=1,Path="a",Size=10,PackedSize=null},new(){Index=2,Path="dir",IsDirectory=true}};
        Assert(ArchiveSorting.Sort(rows,ArchiveSortColumn.Size).Select(e=>e.Index).SequenceEqual(new uint[]{2,0,1}),"numeric size sorting keeps directories first");
        Assert(ArchiveSorting.Sort(rows,ArchiveSortColumn.PackedSize,true).Select(e=>e.Index).SequenceEqual(new uint[]{2,0,1}),"descending packed sorting keeps unknown values last");
        Assert(ArchiveSorting.Sort(rows,ArchiveSortColumn.Name,true).Select(e=>e.Index).SequenceEqual(new uint[]{2,0,1}),"descending name sorting keeps directories first");
    }
}
