namespace New7z.Core;

// Methods exposed by the bundled 7-Zip encoders. Names are the values passed to
// the archive handler; "Copy" stores data without compression.
public static class CompressionMethods
{
    public static readonly IReadOnlyList<string> SevenZip=["LZMA2","LZMA","BZip2","PPMd","Deflate","Copy"];
    public static readonly IReadOnlyList<string> Zip=["Deflate","Deflate64","BZip2","LZMA","PPMd","Copy"];
    public static IReadOnlyList<string> For(string format)=>format switch{"7z"=>SevenZip,"zip"=>Zip,_=>[]};
    public static string Default(string format)=>format=="zip"?"Deflate":"LZMA2";
    public static string? Normalize(string format,string? method)=>method is null?null:For(format).FirstOrDefault(value=>string.Equals(value,method,StringComparison.OrdinalIgnoreCase));
}
