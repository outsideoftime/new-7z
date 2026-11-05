using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using New7z.Core;

[assembly:System.Runtime.Versioning.SupportedOSPlatform("windows")]

var root=Path.Combine(Path.GetTempPath(),"New7z-tests-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var checks=new List<string>();
void Assert(bool condition,string name){if(!condition)throw new Exception(name);checks.Add(name);}
try
{
    var preferencesKey=@"Software\New7z.Tests\"+Guid.NewGuid().ToString("N");
    try
    {
        var store=new ExplorerPreferencesStore(Microsoft.Win32.Registry.CurrentUser,preferencesKey);
        Assert(store.Read()==new ExplorerPreferences(),"settings default: menu enabled, all commands, manual close");
        var selected=new ExplorerPreferences(false,ExplorerCommands.ExtractNamed|ExplorerCommands.CreateZip,true);
        store.Save(selected);Assert(store.Read()==selected,"Explorer settings persist across store instances");
        var reopened=new ExplorerPreferencesStore(Microsoft.Win32.Registry.CurrentUser,preferencesKey);
        Assert(reopened.Read()==selected,"Explorer settings reopen");
        reopened.Save(new ExplorerPreferences());Assert(store.Read()==new ExplorerPreferences(),"Explorer settings restore defaults");
    }
    finally{Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(preferencesKey,false);}
    foreach(var (path,isText) in new[]{("src/app.cs",true),("script.py",true),("main.rs",true),("config.toml",true),("index.tsx",true),("Makefile",true),("README",true),("LICENSE",true),("photo.png",false),("setup.exe",false)})
        Assert(new ArchiveEntry{Path=path}.IsText==isText,"text preview classification: "+path);
    Assert(new ArchiveEntry{Path="icon.ico"}.IsImage&&new ArchiveEntry{Path="scan.tiff"}.IsImage,"image preview classification: ico/tiff");
    var compressPreferencesKey=@"Software\New7z.Tests\"+Guid.NewGuid().ToString("N");
    var compressBlockedKey=compressPreferencesKey+".Blocked";
    try
    {
        var compress=new WindowsCompressMenuSettingsStore(Microsoft.Win32.Registry.CurrentUser,compressPreferencesKey,compressBlockedKey);const string backupName="WindowsCompressMenuBackup";
        bool Hidden()=>new ExplorerPreferencesStore(Microsoft.Win32.Registry.CurrentUser,compressPreferencesKey).Read().HideWindowsCompressMenu;
        object? BlockedValue(){using var key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(compressBlockedKey);return key?.GetValue(WindowsCompressMenuSettingsStore.CompressToClsid,null,Microsoft.Win32.RegistryValueOptions.DoNotExpandEnvironmentNames);}
        Assert(!Hidden(),"Windows compress menu defaults to visible");
        Assert(compress.SetHidden(true),"Windows compress menu hide writes a block entry");
        Assert(Hidden()&&BlockedValue() is string marker&&marker.StartsWith("New7z:",StringComparison.Ordinal),"Windows compress menu hide persists preference and owner marker");
        Assert(compress.SetHidden(false),"Windows compress menu unhide succeeds");
        Assert(!Hidden()&&BlockedValue() is null,"Windows compress menu unhide removes its own block entry");
        using(var blocked=Microsoft.Win32.Registry.CurrentUser.CreateSubKey(compressBlockedKey,true))blocked.SetValue(WindowsCompressMenuSettingsStore.CompressToClsid,"other-tool",Microsoft.Win32.RegistryValueKind.String);
        Assert(compress.SetHidden(true)&&compress.SetHidden(false),"Windows compress menu hide and unhide over an existing entry");
        Assert((string?)BlockedValue()=="other-tool","Windows compress menu restores the original block value");
        Assert(compress.SetHidden(true),"Windows compress menu hide before external change");
        using(var changed=Microsoft.Win32.Registry.CurrentUser.CreateSubKey(compressBlockedKey,true))changed.SetValue(WindowsCompressMenuSettingsStore.CompressToClsid,"changed-elsewhere",Microsoft.Win32.RegistryValueKind.String);
        Assert(!compress.SetHidden(false),"Windows compress menu reports an external change");
        Assert((string?)BlockedValue()=="changed-elsewhere"&&!Hidden(),"Windows compress menu keeps the external block value");
        Assert(compress.SetHidden(true),"Windows compress menu hide before backup loss");
        using(var settings=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(compressPreferencesKey,true))settings!.DeleteSubKeyTree(backupName,false);
        Assert(compress.SetHidden(false),"Windows compress menu unhide without backup");
        Assert(!Hidden()&&BlockedValue() is null,"Windows compress menu removes its own marker when the backup is lost");
        using(var foreign=Microsoft.Win32.Registry.CurrentUser.CreateSubKey(compressBlockedKey,true))foreign.SetValue(WindowsCompressMenuSettingsStore.CompressToClsid,"foreign",Microsoft.Win32.RegistryValueKind.String);
        new ExplorerPreferencesStore(Microsoft.Win32.Registry.CurrentUser,compressPreferencesKey).Save(new ExplorerPreferences(HideWindowsCompressMenu:true));
        var unhideRefused=false;try{compress.SetHidden(false);}catch(IOException){unhideRefused=true;}
        Assert(unhideRefused&&(string?)BlockedValue()=="foreign","Windows compress menu never clears a foreign entry without a backup");
        var hideRefused=false;try{compress.SetHidden(true);}catch(IOException){hideRefused=true;}
        Assert(hideRefused&&(string?)BlockedValue()=="foreign","Windows compress menu never rebaselines a missing backup as the original value");
    }
    finally
    {
        Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(compressPreferencesKey,false);
        Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(compressBlockedKey,false);
    }
    var engine=new ArchiveEngine();
    var source=Path.Combine(root,"中文 source");Directory.CreateDirectory(Path.Combine(source,"empty"));
    var text=Path.Combine(source,"说明.txt");await File.WriteAllTextAsync(text,"你好，New7z！\n<hello>read only</hello>",new UTF8Encoding(false));
    var binary=Path.Combine(source,"data.bin");var original=RandomNumberGenerator.GetBytes(128000);await File.WriteAllBytesAsync(binary,original);
    // A valid PNG preview fixture, independent of any system 7-Zip installation.
    await File.WriteAllBytesAsync(Path.Combine(source,"image.png"),Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l9sAAAAASUVORK5CYII="));
    var brand=Path.Combine(Environment.CurrentDirectory,"图标","logo.png");if(File.Exists(brand))File.Copy(brand,Path.Combine(source,"image.png"),true);
    foreach(var format in new[]{"7z","zip"})
    {
        var archive=Path.Combine(root,"真实压缩."+format);
        await engine.CreateAsync(archive,[source],format);
        var entries=await engine.ListAsync(archive);
        Assert(entries.Any(e=>e.Name=="说明.txt"),format+" list Unicode names");
        Assert(entries.Any(e=>e.Name=="empty"&&e.IsDirectory),format+" empty directory");
        var entry=entries.Single(e=>e.Name=="说明.txt");var bytes=await engine.ReadAsync(archive,entry);
        Assert(ArchiveEngine.DecodeText(bytes).Contains("你好"),format+" read text");
        var image=await engine.ReadAsync(archive,entries.Single(e=>e.Name=="image.png"));Assert(image[0]==137,format+" read PNG");
        var folder=Path.Combine(root,"extract-"+format);var result=await engine.ExtractAsync(archive,entries,folder);
        Assert(result.Extracted==3,format+" extract count");
        Assert((await File.ReadAllBytesAsync(Path.Combine(folder,Path.GetFileName(source),"data.bin"))).SequenceEqual(original),format+" binary roundtrip");
        var renamed=await engine.ExtractAsync(archive,[entry],folder);Assert(File.Exists(Path.Combine(folder,Path.GetFileName(source),"说明 (2).txt")),format+" safe rename conflict");
        await engine.TestAsync(archive);checks.Add(format+" integrity test");
        if(format=="zip")
        {
            using var interoperable=ZipFile.OpenRead(archive);
            Assert(interoperable.Entries.Any(e=>e.FullName.EndsWith("说明.txt",StringComparison.Ordinal)),"balanced ZIP UTF-8 names without legacy encoding override");
        }
    }
    foreach(var compressionLevel in new[]{1,9})
    {
        var profileZip=Path.Combine(root,$"profile-{compressionLevel}.zip");await engine.CreateAsync(profileZip,[source],"zip",compressionLevel);
        await engine.TestAsync(profileZip);Assert((await engine.ListAsync(profileZip)).Any(e=>e.Name=="说明.txt"),$"ZIP level {compressionLevel} roundtrip");
    }
    var cancelOutput=Path.Combine(root,"cancelled.zip");
    using(var writingCancellation=new CancellationTokenSource())
    {
        var progress=new InlineProgress(p=>{if(p.Completed>0)writingCancellation.Cancel();});
        try{await engine.CreateAsync(cancelOutput,[source],"zip",progress:progress,cancellation:writingCancellation.Token);throw new Exception("Cancelled ZIP was committed.");}
        catch(OperationCanceledException){Assert(!File.Exists(cancelOutput)&&!Directory.EnumerateFiles(root,"cancelled.zip.*.partial").Any(),"ZIP cancellation cleans temporary output before commit");}
    }
    var encrypted=Path.Combine(root,"加密.7z");await engine.CreateAsync(encrypted,[source],password:"password-测试",encryptNames:true);
    try{await engine.ListAsync(encrypted);throw new Exception("Missing password was accepted");}catch(ArchivePasswordException){checks.Add("encrypted header password required");}
    var encryptedItems=await engine.ListAsync(encrypted,"password-测试");Assert(encryptedItems.Count>0,"encrypted header open");
    try{await engine.ReadAsync(encrypted,encryptedItems.First(e=>!e.IsDirectory),"wrong");throw new Exception("Wrong password accepted");}catch(ArchivePasswordException){checks.Add("wrong password rejected");}
    var encryptedZip=Path.Combine(root,"加密.zip");await engine.CreateAsync(encryptedZip,[text],"zip",password:"secret");var zipItems=await engine.ListAsync(encryptedZip);Assert((await engine.ReadAsync(encryptedZip,zipItems.Single(),"secret")).Length>0,"ZIP AES read");
    foreach(var format in new[]{"7z","zip"})
    {
        var modifiable=Path.Combine(root,"修改."+format);
        await engine.CreateAsync(modifiable,[source],format);
        var before=await engine.ListAsync(modifiable);
        var added=Path.Combine(root,"新增说明.txt");await File.WriteAllTextAsync(added,"新增内容",new UTF8Encoding(false));
        var addedFolder=Path.Combine(root,"新增目录");Directory.CreateDirectory(Path.Combine(addedFolder,"子目录"));await File.WriteAllTextAsync(Path.Combine(addedFolder,"子目录","内.txt"),"嵌套");
        var additions=ArchiveEngine.ExpandSources([added,addedFolder]);
        await engine.ModifyAsync(modifiable,[],additions);
        var afterAdd=await engine.ListAsync(modifiable);
        Assert(afterAdd.Count==before.Count+additions.Count,format+" update adds items");
        Assert(afterAdd.Any(e=>e.NormalizedPath=="新增说明.txt"),format+" update adds file");
        Assert(afterAdd.Any(e=>e.NormalizedPath=="新增目录/子目录/内.txt"),format+" update adds nested folder");
        Assert(ArchiveEngine.DecodeText(await engine.ReadAsync(modifiable,afterAdd.Single(e=>e.Name=="新增说明.txt")))== "新增内容",format+" update reads added file");
        Assert((await engine.ReadAsync(modifiable,afterAdd.Single(e=>e.Name=="data.bin"))).SequenceEqual(original),format+" update keeps existing data");
        await File.WriteAllTextAsync(added,"替换内容");
        var replaceTarget=afterAdd.Single(e=>e.Name=="新增说明.txt");
        await engine.ModifyAsync(modifiable,[replaceTarget.Index],ArchiveEngine.ExpandSources([added]));
        var replaced=await engine.ListAsync(modifiable);
        Assert(replaced.Count==afterAdd.Count,format+" replace keeps item count");
        Assert(ArchiveEngine.DecodeText(await engine.ReadAsync(modifiable,replaced.Single(e=>e.Name=="新增说明.txt")))== "替换内容",format+" replace writes new data");
        var nestedFile=Path.Combine(root,"嵌套新增.txt");await File.WriteAllTextAsync(nestedFile,"嵌套新增");
        await engine.ModifyAsync(modifiable,[],ArchiveEngine.ExpandSources([nestedFile],"新增目录/子目录"));
        Assert((await engine.ListAsync(modifiable)).Any(e=>e.NormalizedPath=="新增目录/子目录/嵌套新增.txt"),format+" update adds into an archive folder");
        var noteIndex=replaced.Single(e=>e.Name=="说明.txt").Index;
        var folderIndex=replaced.Single(e=>e.NormalizedPath=="新增目录/子目录"&&e.IsDirectory).Index;
        await engine.ModifyAsync(modifiable,[noteIndex,folderIndex],[]);
        var afterDelete=await engine.ListAsync(modifiable);
        Assert(!afterDelete.Any(e=>e.NormalizedPath=="说明.txt"),format+" delete removes file");
        Assert(!afterDelete.Any(e=>e.NormalizedPath=="新增目录/子目录"&&e.IsDirectory),format+" delete removes folder entry");
        await engine.TestAsync(modifiable);checks.Add(format+" integrity after update");
        var remaining=await engine.ListAsync(modifiable);
        await engine.ModifyAsync(modifiable,remaining.Select(e=>e.Index).ToList(),[]);
        Assert((await engine.ListAsync(modifiable)).Count==0,format+" delete all leaves an empty archive");
    }
    var secured=Path.Combine(root,"修改加密.7z");await engine.CreateAsync(secured,[source],password:"修改密码",encryptNames:true);
    var securedEntries=await engine.ListAsync(secured,"修改密码");
    var securedExtra=Path.Combine(root,"加密新增.txt");await File.WriteAllTextAsync(securedExtra,"加密新增");
    await engine.ModifyAsync(secured,[securedEntries.Single(e=>e.Name=="说明.txt").Index],ArchiveEngine.ExpandSources([securedExtra]),"修改密码");
    try{await engine.ListAsync(secured);throw new Exception("Updated 7z header was not encrypted");}catch(ArchivePasswordException){checks.Add("7z update keeps encrypted header");}
    var securedAfter=await engine.ListAsync(secured,"修改密码");
    Assert(!securedAfter.Any(e=>e.Name=="说明.txt")&&securedAfter.Any(e=>e.Name=="加密新增.txt"),"encrypted 7z update changes entries");
    Assert((await engine.ReadAsync(secured,securedAfter.Single(e=>e.Name=="data.bin"),"修改密码")).SequenceEqual(original),"encrypted 7z update keeps data");
    var securedZip=Path.Combine(root,"修改加密.zip");await engine.CreateAsync(securedZip,[text],"zip",password:"zip-pass");
    await engine.ModifyAsync(securedZip,[],[new SourceItem(securedExtra,"加密新增.txt")],"zip-pass");
    var securedZipEntries=await engine.ListAsync(securedZip);
    Assert(securedZipEntries.Any(e=>e.Name=="加密新增.txt"),"encrypted ZIP update adds entry");
    Assert((await engine.ReadAsync(securedZip,securedZipEntries.Single(e=>e.Name=="加密新增.txt"),"zip-pass")).Length>0,"encrypted ZIP update reads added entry");
    Assert(ZipUsesAes(await File.ReadAllBytesAsync(securedZip)),"encrypted ZIP update keeps AES encryption");
    var unsupportedFile=Path.Combine(root,"note.txt");await File.WriteAllTextAsync(unsupportedFile,"x");
    var updateRefused=false;try{await engine.ModifyAsync(unsupportedFile,[],ArchiveEngine.ExpandSources([text]));}catch(IOException){updateRefused=true;}
    Assert(updateRefused,"update refuses non-archive paths");
    Assert(GitHubUpdateChecker.TryParseTag("v0.1.16",out var releaseVersion)&&releaseVersion==new Version(0,1,16),"update release tag parses");
    Assert(!GitHubUpdateChecker.TryParseTag("nightly",out _),"update release tag rejects non-version tags");
    foreach(var unsafePath in new[]{"../escape.txt","/absolute.txt","C:\\escape.txt","file.txt:ads","CON.txt","trailing. "})
    {try{ArchiveEngine.ResolveOutput(root,unsafePath);throw new Exception("Unsafe path accepted: "+unsafePath);}catch(IOException){checks.Add("reject "+unsafePath);}}
    var malicious=Path.Combine(root,"malicious.zip");using(var zip=ZipFile.Open(malicious,ZipArchiveMode.Create)){using var writer=new StreamWriter(zip.CreateEntry("../escape.txt").Open());writer.Write("unsafe");}
    try{await engine.ExtractAsync(malicious,await engine.ListAsync(malicious),Path.Combine(root,"malicious-out"));throw new Exception("Traversal extracted");}catch(IOException){checks.Add("archive traversal blocked");}
    var limitArchive=Path.Combine(root,"真实压缩.zip");var large=(await engine.ListAsync(limitArchive)).Single(e=>e.Name=="data.bin");
    try{await engine.ReadAsync(limitArchive,large,limit:100);throw new Exception("Limit bypass");}catch(IOException){checks.Add("preview byte cap");}
    using var cts=new CancellationTokenSource();cts.Cancel();try{await engine.ListAsync(limitArchive,cancellation:cts.Token);throw new Exception("Cancellation ignored");}catch(OperationCanceledException){checks.Add("cancel before start");}
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{passed=checks.Count,checks},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
    if(args.Contains("--keep")){Console.WriteLine("Fixtures: "+root);root="";}
}
finally {if(root.Length>0)Directory.Delete(root,true);}

static bool ZipUsesAes(byte[] data)
{
    for(var i=0;i+46<data.Length;i++)
    {
        if(data[i]!=0x50||data[i+1]!=0x4b||data[i+2]!=1||data[i+3]!=2)continue;
        int nameLength=BitConverter.ToUInt16(data,i+28),extraLength=BitConverter.ToUInt16(data,i+30);
        var extra=i+46+nameLength;var end=Math.Min(data.Length,extra+extraLength);
        for(var p=extra;p+4<=end;)
        {
            var id=BitConverter.ToUInt16(data,p);var size=BitConverter.ToUInt16(data,p+2);
            if(id==0x9901)return true;
            p+=4+size;
        }
    }
    return false;
}

sealed class InlineProgress(Action<EngineProgress> callback):IProgress<EngineProgress>{public void Report(EngineProgress value)=>callback(value);}

