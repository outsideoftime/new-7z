namespace New7z.Core;

public enum ArchiveSortColumn { Name, Size, PackedSize, Modified }
public static class ArchiveSorting
{
    public static IReadOnlyList<ArchiveEntry> Sort(IEnumerable<ArchiveEntry> entries,ArchiveSortColumn column=ArchiveSortColumn.Name,bool descending=false)
    {
        var rows=entries.OrderByDescending(e=>e.IsDirectory);
        // Unknown metadata remains after known values in either direction.
        if(column==ArchiveSortColumn.PackedSize)rows=rows.ThenBy(e=>e.PackedSize==null);
        if(column==ArchiveSortColumn.Modified)rows=rows.ThenBy(e=>e.FileTime<=0);
        IOrderedEnumerable<ArchiveEntry> ordered=column switch
        {
            ArchiveSortColumn.Size=>descending?rows.ThenByDescending(e=>e.Size):rows.ThenBy(e=>e.Size),
            ArchiveSortColumn.PackedSize=>descending?rows.ThenByDescending(e=>e.PackedSize):rows.ThenBy(e=>e.PackedSize),
            ArchiveSortColumn.Modified=>descending?rows.ThenByDescending(e=>e.FileTime):rows.ThenBy(e=>e.FileTime),
            _=>descending?rows.ThenByDescending(e=>e.Name,StringComparer.CurrentCultureIgnoreCase):rows.ThenBy(e=>e.Name,StringComparer.CurrentCultureIgnoreCase)
        };
        return ordered.ThenBy(e=>e.Name,StringComparer.CurrentCultureIgnoreCase).ThenBy(e=>e.NormalizedPath,StringComparer.Ordinal).ThenBy(e=>e.Index).ToArray();
    }
}
