using System.Buffers;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace New7z.Core;

// Fast, standard UTF-8 ZIP for unencrypted speed/balanced profiles. 7-Zip remains
// the reader and the writer for encrypted ZIP, size-priority ZIP and all 7z files.
internal static class FastZipWriter
{
    public static void Create(string output,IReadOnlyList<string> paths,IReadOnlyList<string> names,int level,IProgress<EngineProgress>? progress,CancellationToken cancellation)
    {
        ulong total=0,completed=0;
        foreach(var path in paths)
        {
            cancellation.ThrowIfCancellationRequested();
            if(!Directory.Exists(path))total+=checked((ulong)new FileInfo(path).Length);
        }
        var compression=level==0?CompressionLevel.NoCompression:level<=3?CompressionLevel.Fastest:CompressionLevel.Optimal;
        var buffer=ArrayPool<byte>.Shared.Rent(128*1024);var timer=Stopwatch.StartNew();
        void Report(bool force=false)
        {
            if(progress==null||!force&&timer.ElapsedMilliseconds<50)return;
            timer.Restart();progress.Report(new(total,completed));
        }
        try
        {
            using(var stream=new FileStream(output,FileMode.CreateNew,FileAccess.Write,FileShare.None,128*1024,FileOptions.SequentialScan))
            using(var zip=new ZipArchive(stream,ZipArchiveMode.Create,false,Encoding.UTF8))
            {
                Report(true);
                for(var i=0;i<paths.Count;i++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var attributes=File.GetAttributes(paths[i]);
                    if(attributes.HasFlag(FileAttributes.ReparsePoint))throw new IOException("暂不打包符号链接或重解析点。");
                    var directory=attributes.HasFlag(FileAttributes.Directory);
                    var entry=zip.CreateEntry(names[i].TrimEnd('/')+(directory?"/":""),compression);
                    var time=File.GetLastWriteTime(paths[i]);
                    if(time.Year is >=1980 and <=2107)entry.LastWriteTime=new DateTimeOffset(time);
                    entry.ExternalAttributes=(int)attributes;
                    if(directory)continue;
                    using var input=new FileStream(paths[i],FileMode.Open,FileAccess.Read,FileShare.Read,128*1024,FileOptions.SequentialScan);
                    using var target=entry.Open();
                    int count;
                    while((count=input.Read(buffer,0,buffer.Length))>0)
                    {
                        cancellation.ThrowIfCancellationRequested();target.Write(buffer,0,count);completed+=(uint)count;Report();
                    }
                }
            }
            cancellation.ThrowIfCancellationRequested();Report(true);
        }
        finally{ArrayPool<byte>.Shared.Return(buffer);}
    }
}
