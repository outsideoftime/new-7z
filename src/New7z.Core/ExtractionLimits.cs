namespace New7z.Core;

public sealed record ExtractionLimits(int MaxEntries=1_000_000,ulong MaxFileBytes=64UL*1024*1024*1024,ulong MaxTotalBytes=100UL*1024*1024*1024)
{
    public static ExtractionLimits Default {get;}=new();
    internal void Validate(IReadOnlyList<ArchiveEntry> entries)
    {
        if(MaxEntries<=0||MaxFileBytes==0||MaxTotalBytes==0)throw new ArgumentException("Extraction limits must be positive.");
        if(entries.Count>MaxEntries)throw Limit();
        ulong total=0;
        foreach(var entry in entries.Where(e=>!e.IsDirectory))
        {
            if(entry.Size>MaxFileBytes||entry.Size>MaxTotalBytes||total>MaxTotalBytes-entry.Size)throw Limit();
            total+=entry.Size;
        }
    }
    private static IOException Limit()=>new(Loc.T("压缩包超过解压安全上限（文件数量、单文件或总大小）。请缩小所选范围。","The archive exceeds the extraction safety limit (entry count, file size or total size). Select fewer items."));
}
