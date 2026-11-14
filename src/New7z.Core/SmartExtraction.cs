namespace New7z.Core;

public static class SmartExtraction
{
    // Keep entry paths intact. A shared top-level directory may be implicit in the archive.
    public static string GetDestination(string parentFolder,string namedFolder,IReadOnlyList<ArchiveEntry> entries)
    {
        string? root=null;
        foreach(var entry in entries)
        {
            var path=entry.NormalizedPath;
            var separator=path.IndexOf('/');
            var name=separator<0?path:path[..separator];
            if(name.Length==0)return namedFolder;
            if(root==null)root=name;
            else if(!string.Equals(root,name,StringComparison.OrdinalIgnoreCase))return namedFolder;
        }
        return root==null?namedFolder:parentFolder;
    }
}
