using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;
using static New7z.Core.Loc;

namespace New7z.App;

public sealed partial class MainWindow
{
    private string? dragVerificationReport;
    private bool dragVerificationTextOnly;
    // Opt-in interactive diagnostic: exercise the real WinUI gesture + native
    // OLE loop without requiring an input tool to drag across window boundaries.
    private async Task PrepareDragVerificationAsync(string input,string reportPath,bool textOnly=false)
    {
        dragVerificationReport=reportPath;
        dragVerificationTextOnly=textOnly;
        await OpenArchiveAsync(input,false);
        var label=Text("Drag verification target",16,true);
        var target=new Border{Child=label,Width=280,Height=160,Padding=new Thickness(16),Margin=new Thickness(0,0,40,90),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,Background=(Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],BorderBrush=(Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],BorderThickness=new Thickness(2),AllowDrop=true};
        var drops=0;
        Root.AddHandler(UIElement.DragOverEvent,new DragEventHandler((_,e)=>{e.Handled=true;e.AcceptedOperation=Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;File.AppendAllText(reportPath+".trace","TargetOver"+Environment.NewLine);}),true);
        Root.AddHandler(UIElement.DropEvent,new DragEventHandler(async(_,e)=>{
            e.Handled=true;var deferral=e.GetDeferral();
            var report=new Dictionary<string,string>();
            try
            {
                var items=await e.DataView.GetStorageItemsAsync();
                if(items.Count==0)throw new IOException("The OLE drop contained no storage items.");
                var destination=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(reportPath))!,"drop-"+(++drops));
                Directory.CreateDirectory(destination);
                void Copy(string source,string output)
                {
                    if(File.Exists(source)){File.Copy(source,output);return;}
                    Directory.CreateDirectory(output);
                    foreach(var child in Directory.EnumerateFileSystemEntries(source))Copy(child,System.IO.Path.Combine(output,System.IO.Path.GetFileName(child)));
                }
                foreach(var item in items)Copy(item.Path,System.IO.Path.Combine(destination,item.Name));
                report["Result"]="PASS";report["Drops"]=drops.ToString();report["Items"]=string.Join(" | ",items.Select(item=>item.Name));report["Destination"]=destination;
                label.Text="PASS: "+drops+" drops\n"+report["Items"];
            }
            catch(Exception ex){report["Result"]="FAIL";report["Error"]=ex.ToString();label.Text="FAIL: "+ex.Message;}
            finally{deferral.Complete();}
            await File.WriteAllTextAsync(reportPath,JsonSerializer.Serialize(report,AppJsonContext.Default.DictionaryStringString));
        }),true);
        Grid.SetRowSpan(target,Root.RowDefinitions.Count);Root.Children.Add(target);
    }
    // Explicit local diagnostic entry point for validating the published/trimmed app.
    // It does not send desktop input, change preferences or invoke system registration.
    private async Task VerifyUiAsync(string input,string reportPath)
    {
        var report=new Dictionary<string,string>();
        try
        {
            SelectPage("settings");Root.UpdateLayout();
            var settingsLabels=new List<string>();
            void CollectSettingsLabels(DependencyObject node)
            {
                if(node is TextBlock label)settingsLabels.Add(label.Text);
                for(var i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)CollectSettingsLabels(VisualTreeHelper.GetChild(node,i));
            }
            CollectSettingsLabels(ContentHost);
            if(!settingsLabels.Contains(T("常规","General"))||!settingsLabels.Contains(T("压缩","Compression"))||!settingsLabels.Contains(T("右键菜单","Context menu")))throw new IOException("Settings categories did not render.");
            settingsCategory="compression";RenderPage();Root.UpdateLayout();
            settingsLabels.Clear();CollectSettingsLabels(ContentHost);
            if(!settingsLabels.Contains(T("默认压缩格式","Default archive format"))||!settingsLabels.Contains(T("默认压缩方法","Default compression method")))throw new IOException("Compression defaults did not render.");
            report["Settings"]="rendered settings hub and the compression defaults category";
            jobs.Add(new ArchiveJob("verification job","verification target"){Status="verification",Active=false});
            SelectPage("tasks");Root.UpdateLayout();
            report["Tasks"]="rendered a bound task card";
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
