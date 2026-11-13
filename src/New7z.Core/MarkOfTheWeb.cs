namespace New7z.Core;

internal static class MarkOfTheWeb
{
    internal static byte[]? Read(string archive)
    {
        try
        {
            using var stream=new FileStream(archive+":Zone.Identifier",FileMode.Open,FileAccess.Read,FileShare.Read);
            if(stream.Length>64*1024)throw new IOException("Zone.Identifier is too large.");
            var bytes=new byte[checked((int)stream.Length)];stream.ReadExactly(bytes);
            return bytes;
        }
        catch(FileNotFoundException){return null;}
        catch(DirectoryNotFoundException){return null;}
    }
    internal static void Apply(string file,byte[]? zone)
    {
        if(zone==null)return;
        // Fail before commit if the destination cannot preserve the security zone.
        using var stream=new FileStream(file+":Zone.Identifier",FileMode.CreateNew,FileAccess.Write,FileShare.None);
        stream.Write(zone);
    }
}
