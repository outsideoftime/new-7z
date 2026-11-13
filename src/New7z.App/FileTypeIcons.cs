using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using New7z.Core;

namespace New7z.App;

internal static class FileTypeIcons
{
    // ImageSource instances are shared across recycled rows; no disk probe per row.
    private static readonly Dictionary<string,ImageSource> Cache=new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string,string> Extensions=new(StringComparer.OrdinalIgnoreCase);
    static FileTypeIcons()
    {
        void Map(string icon,string extensions){foreach(var ext in extensions.Split(' ',StringSplitOptions.RemoveEmptyEntries))Extensions["."+ext]=icon;}
        Map("csharp","cs csx csproj sln");Map("cpp","cpp cc cxx hpp hxx");Map("c","c h");Map("python","py pyw pyi");
        Map("typescript","ts mts cts");Map("js","js mjs cjs");Map("react","jsx tsx");Map("java","java jar");Map("kotlin","kt kts");Map("go","go mod");Map("rust","rs");Map("swift","swift");Map("ruby","rb gem");Map("php","php");
        Map("powershell","ps1 psm1 psd1");Map("shell","sh bash zsh bat cmd");Map("html","html htm xhtml");Map("css","css");Map("sass","scss sass");Map("vue","vue");
        Map("json","json jsonc json5");Map("yaml","yaml yml");Map("xml","xml xaml axaml resx props targets");Map("markdown","md markdown");Map("config","ini cfg conf config toml properties env");Map("log","log");Map("text","txt text rst csv tsv");
        Map("image","png jpg jpeg gif bmp webp ico tif tiff svg avif");Map("audio","mp3 wav flac aac ogg m4a opus wma");Map("video","mp4 mkv avi mov webm wmv m4v");
        Map("pdf","pdf");Map("word","doc docx odt rtf");Map("excel","xls xlsx ods");Map("powerpoint","ppt pptx odp");
        Map("archive","7z zip rar tar gz bz2 xz iso zst zstd tzst lz4 cab wim tgz");Map("binary","exe dll sys bin dat pdb");Map("font","ttf otf woff woff2");Map("sql","sql db sqlite sqlite3");Map("unity","unity prefab asset mat meta");Map("blender","blend fbx obj gltf glb");
    }
    internal static string Key(string name)
    {
        if(name.Equals("Dockerfile",StringComparison.OrdinalIgnoreCase))return "docker";
        if(name.StartsWith(".git",StringComparison.OrdinalIgnoreCase))return "git";
        return Extensions.GetValueOrDefault(Path.GetExtension(name),"text");
    }
    internal static ImageSource? For(ArchiveEntry entry)
    {
        if(entry.IsDirectory)return null;
        var key=Key(entry.Name);
        if(!Cache.TryGetValue(key,out var image))
        {
            image=new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory,"Assets","FileTypes",key+".png"))){DecodePixelWidth=48};Cache[key]=image;
        }
        return image;
    }
}
