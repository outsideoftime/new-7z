using System.IO.Compression;
using New7z.Core;

internal static class SmartExtractionTests
{
    internal static async Task RunAsync(string parent,List<string> checks)
    {
        var root=Path.Combine(parent,"smart");Directory.CreateDirectory(root);var engine=new ArchiveEngine();
        void Assert(bool condition,string name){if(!condition)throw new Exception(name);checks.Add("smart extract: "+name);}
        async Task<(string Archive,string Target)> ExtractZip(string name,string[] paths,bool direct)
        {
            var folder=Path.Combine(root,name);Directory.CreateDirectory(folder);
            var archive=Path.Combine(folder,"archive.zip");
            using(var zip=ZipFile.Open(archive,ZipArchiveMode.Create))
                foreach(var path in paths)
                {
                    var entry=zip.CreateEntry(path);
                    if(!path.EndsWith('/')){using var writer=new StreamWriter(entry.Open());writer.Write(path);}
                }
            var entries=await engine.ListAsync(archive);
            var named=Path.Combine(folder,"archive");
            var target=SmartExtraction.GetDestination(folder,named,entries);
            Assert(target==(direct?folder:named),name+" destination");
            await engine.ExtractAsync(archive,entries,target);
            foreach(var path in paths)
                Assert(path.EndsWith('/')?Directory.Exists(Path.Combine(target,path)):await File.ReadAllTextAsync(Path.Combine(target,path))==path,name+" output "+path);
            if(direct)Assert(!Directory.Exists(named),name+" avoids extra wrapper");
            Assert(File.Exists(archive),name+" keeps source archive");
            return (archive,target);
        }
        await ExtractZip("single-folder",["项目/","项目/a.txt","项目/nested/b.txt"],true);
        await ExtractZip("implicit-folder",["项目/a.txt","项目/nested/b.txt"],true);
        await ExtractZip("empty-folder",["空目录/"],true);
        var single=await ExtractZip("single-file",["readme.txt"],true);
        await ExtractZip("multiple-files",["a.txt","b.txt"],false);
        await ExtractZip("multiple-folders",["one/a.txt","two/b.txt"],false);
        await ExtractZip("folder-and-file",["one/a.txt","readme.txt"],false);
        await ExtractZip("empty-archive",[],false);
        var singleEntries=await engine.ListAsync(single.Archive);
        await File.WriteAllTextAsync(Path.Combine(single.Target,"readme.txt"),"keep existing");
        await engine.ExtractAsync(single.Archive,singleEntries,single.Target);
        Assert(await File.ReadAllTextAsync(Path.Combine(single.Target,"readme.txt"))=="keep existing"&&
            await File.ReadAllTextAsync(Path.Combine(single.Target,"readme (2).txt"))=="readme.txt","single-file collision keeps both");
        Assert(SmartExtraction.GetDestination(root,Path.Combine(root,"named"),[
            new(){Path=@"Project\a.txt"},new(){Path="project/b.txt"}])==root,"Windows separators and root casing");
        var encryptedSource=Path.Combine(root,"input","加密目录");Directory.CreateDirectory(encryptedSource);
        await File.WriteAllTextAsync(Path.Combine(encryptedSource,"secret.txt"),"encrypted content");
        var encrypted=Path.Combine(root,"encrypted.7z");
        await engine.CreateAsync(encrypted,[encryptedSource],"7z",password:"smart-password",encryptNames:true);
        var encryptedEntries=await engine.ListAsync(encrypted,"smart-password");
        var encryptedTarget=SmartExtraction.GetDestination(root,Path.Combine(root,"encrypted"),encryptedEntries);
        await engine.ExtractAsync(encrypted,encryptedEntries,encryptedTarget,password:"smart-password");
        Assert(encryptedTarget==root&&await File.ReadAllTextAsync(Path.Combine(root,"加密目录","secret.txt"))=="encrypted content","encrypted 7z avoids extra wrapper");
        var unsafeArchive=Path.Combine(root,"unsafe.zip");
        using(var zip=ZipFile.Open(unsafeArchive,ZipArchiveMode.Create))
        {using var writer=new StreamWriter(zip.CreateEntry("../escaped.txt").Open());writer.Write("unsafe");}
        var unsafeEntries=await engine.ListAsync(unsafeArchive);
        var unsafeTarget=SmartExtraction.GetDestination(root,Path.Combine(root,"unsafe"),unsafeEntries);
        var rejected=false;
        try{await engine.ExtractAsync(unsafeArchive,unsafeEntries,unsafeTarget);}catch(IOException){rejected=true;}
        Assert(rejected&&!File.Exists(Path.Combine(parent,"escaped.txt")),"path traversal remains rejected");
    }
}
