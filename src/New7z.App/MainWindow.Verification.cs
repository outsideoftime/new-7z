using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace New7z.App;

public sealed partial class MainWindow
{
    // Explicit local diagnostic entry point for validating the published/trimmed app.
    // It does not send desktop input, change preferences or invoke system registration.
    private async Task VerifyUiAsync(string input,string reportPath)
    {
        var report=new Dictionary<string,string>();
        try
        {
            SelectPage("settings");Root.UpdateLayout();
            report["Settings"]="rendered native menu, association and popup controls";
            await OpenArchiveAsync(input,false);
            if(archive==null||entries.Count==0)throw new IOException("Archive UI did not load.");
            var firstDirectory=entries.FirstOrDefault(e=>e.IsDirectory);
            if(firstDirectory!=null){folder=firstDirectory.NormalizedPath;ShowArchive();}
            await Task.Delay(200);Root.UpdateLayout();
            var visibleNames=entries.Where(e=>!e.IsDirectory&&e.Directory==folder).Select(e=>e.Name).ToArray();
            var rendered=new List<string>();
            void ReadText(DependencyObject node)
            {
                if(node is TextBlock label)rendered.Add(label.Text);
                for(var i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)ReadText(VisualTreeHelper.GetChild(node,i));
            }
            ReadText(ContentHost);
            if(visibleNames.Length>0&&!visibleNames.Any(name=>rendered.Contains(name)))throw new IOException("Published XAML file-name binding did not render.");
            report["ArchiveBinding"]="file names rendered via native XAML bindings";
            var text=entries.FirstOrDefault(e=>!e.IsDirectory&&e.IsText);
            if(text!=null){var bytes=await engine.ReadAsync(input,text);if(bytes.Length==0)throw new IOException("Text fixture empty.");report["Text"]="decoded "+New7z.Core.ArchiveEngine.DecodeText(bytes).Length+" characters";}
            var image=entries.FirstOrDefault(e=>!e.IsDirectory&&e.IsImage);
            if(image!=null)
            {
                var bytes=await engine.ReadAsync(input,image);
                using var stream=new InMemoryRandomAccessStream();using(var writer=new DataWriter(stream)){writer.WriteBytes(bytes);await writer.StoreAsync();writer.DetachStream();}
                stream.Seek(0);var bitmap=new BitmapImage();await bitmap.SetSourceAsync(stream);
                if(bitmap.PixelWidth==0)throw new IOException("Image decode failed.");report["Image"]=$"decoded {bitmap.PixelWidth}x{bitmap.PixelHeight}";
            }
            report["Result"]="PASS";
        }
        catch(Exception ex){report["Result"]="FAIL";report["Error"]=ex.ToString();}
        await File.WriteAllTextAsync(reportPath,JsonSerializer.Serialize(report,AppJsonContext.Default.DictionaryStringString));
        Close();
    }
}
