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
            var originalList=fileList;
            var originalFolder=folder;
            if(fileList!.Items.Count>0)fileList.SelectedItem=fileList.Items[0];
            var selectedKeys=fileList.SelectedItems.OfType<ArchiveRow>().Select(row=>row.FullPath).ToHashSet();
            sortColumn=New7z.Core.ArchiveSortColumn.Size;sortDescending=true;UpdateSortHeaders();RefreshRows();
            if(!ReferenceEquals(originalList,fileList)||!fileList.SelectedItems.OfType<ArchiveRow>().Select(row=>row.FullPath).ToHashSet().SetEquals(selectedKeys))throw new IOException("Sort recreated the list or lost selection.");
            string[] ArchiveProperties()=>[archiveFileCountText?.Text??"",archiveOriginalSizeText?.Text??"",archivePackedSizeText?.Text??""];
            var archiveProperties=ArchiveProperties();
            if(archiveProperties.Any(string.IsNullOrEmpty))throw new IOException("Archive properties did not render.");
            if(archiveFileCountText!.Text!=entries.Count(entry=>!entry.IsDirectory).ToString("N0")||archivePackedSizeText!.Text!=New7z.Core.ByteSize.Format((ulong)new FileInfo(input).Length))throw new IOException("Properties do not describe the whole archive.");
            fileList.SelectAll();UpdateSelectionDetails();
            if(!ArchiveProperties().SequenceEqual(archiveProperties))throw new IOException("Selection changed the whole-archive properties.");
            SetFolder("");SetFolder(originalFolder);
            if(!ReferenceEquals(originalList,fileList))throw new IOException("Folder navigation recreated the list.");
            query="no-such-name-"+Guid.NewGuid().ToString("N");RefreshRows();if(fileList.Items.Count!=0)throw new IOException("No-result search did not clear rows.");
            if(!ArchiveProperties().SequenceEqual(archiveProperties))throw new IOException("Folder navigation or search changed the whole-archive properties.");
            query="";RefreshRows();
            var images=new List<Image>();
            void ReadImages(DependencyObject node){if(node is Image view)images.Add(view);for(var i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)ReadImages(VisualTreeHelper.GetChild(node,i));}
            await Task.Delay(200);Root.UpdateLayout();ReadImages(fileList);
            if(fileList.Items.OfType<ArchiveRow>().Any(row=>!row.Entry.IsDirectory)&&!images.Any(view=>view.Source is BitmapImage bitmap&&bitmap.PixelWidth>0))throw new IOException("File type icons did not decode in the native list.");
            var archiveButtons=new List<string>();
            void ReadButtons(DependencyObject node){if(node is Button button&&button.Content is string label)archiveButtons.Add(label);for(var i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)ReadButtons(VisualTreeHelper.GetChild(node,i));}
            ReadButtons(ContentHost);
            if(archiveButtons.Contains(T("属性","Properties")))throw new IOException("The obsolete Properties button is still present.");
            if(archivePropertiesPanel?.ActualWidth!=240||archivePropertiesPanel.BorderThickness.Left!=1)throw new IOException("The archive sidebar or divider did not render.");
            var windowSize=AppWindow.Size;var scale=Root.XamlRoot.RasterizationScale;
            AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(900*scale),(int)(680*scale)));
            await Task.Delay(250);Root.UpdateLayout();
            if(archivePropertiesPanel.ActualWidth!=240||archivePropertiesPanel.Visibility!=Visibility.Visible||archiveTableScroll!.ViewportWidth<=0||archiveTableScroll.ScrollableWidth<=0)throw new IOException("Narrow layout hid the archive sidebar or lost horizontal table scrolling.");
            AppWindow.Resize(windowSize);
            report["ArchiveView"]="sort preserves selection; folder navigation preserves ListView; empty search; decoded file type icons";
            report["ArchiveProperties"]="whole-archive totals remain stable during selection, navigation and search; actual archive file size; no Properties button; 240 DIP sidebar with a vertical divider remains visible at 900 DIP, with horizontal table scrolling";
            var text=entries.FirstOrDefault(e=>!e.IsDirectory&&e.IsText);
            if(text!=null)
            {
                var bytes=await engine.ReadAsync(input,text);if(bytes.Length==0)throw new IOException("Text fixture empty.");report["Text"]="decoded "+New7z.Core.ArchiveEngine.DecodeText(bytes).Length+" characters";
                await VerifyPreviewAsync(text);
                report["TextPreview"]="one preview window; stable content after 1.4 seconds; find and wrap reuse the text control; close releases the preview session";
                var opening=ShowPreviewAsync(text);previewWindowPointer!.Close();await opening.WaitAsync(TimeSpan.FromSeconds(10));
                if(previewOpen||previewWindowPointer!=null)throw new IOException("Closing a loading preview did not release its session.");
                report["PreviewCancel"]="closed while reading; cancellation completed and released the preview session";
            }
            var image=entries.FirstOrDefault(e=>!e.IsDirectory&&e.IsImage);
            if(image!=null)
            {
                var bytes=await engine.ReadAsync(input,image);
                using var stream=new InMemoryRandomAccessStream();using(var writer=new DataWriter(stream)){writer.WriteBytes(bytes);await writer.StoreAsync();writer.DetachStream();}
                stream.Seek(0);var bitmap=new BitmapImage();await bitmap.SetSourceAsync(stream);
                if(bitmap.PixelWidth==0)throw new IOException("Image decode failed.");report["Image"]=$"decoded {bitmap.PixelWidth}x{bitmap.PixelHeight}";
                await VerifyPreviewAsync(image);
                report["ImagePreview"]="image loaded in a single owned preview window; close releases the preview session";
            }
            report["Result"]="PASS";
        }
        catch(Exception ex){report["Result"]="FAIL";report["Error"]=ex.ToString();}
        await File.WriteAllTextAsync(reportPath,JsonSerializer.Serialize(report,AppJsonContext.Default.DictionaryStringString));
        Close();
    }
    private async Task VerifyPreviewAsync(New7z.Core.ArchiveEntry entry)
    {
        var session=ShowPreviewAsync(entry);var window=previewWindowPointer??throw new IOException("Preview did not create a window.");
        try
        {
            if(GetVerificationWindowOwner(WinRT.Interop.WindowNative.GetWindowHandle(window),4)!=Hwnd)throw new IOException("The preview is not owned by the main window.");
            var root=(FrameworkElement)window.Content;
            T? Find<T>(DependencyObject node,string name) where T:FrameworkElement
            {
                if(node is T element&&element.Name==name)return element;
                for(var i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)if(Find<T>(VisualTreeHelper.GetChild(node,i),name) is {} child)return child;
                return null;
            }
            var timer=System.Diagnostics.Stopwatch.StartNew();Grid? host=null;
            while(timer.Elapsed<TimeSpan.FromSeconds(15))
            {
                await Task.Delay(50);root.UpdateLayout();
                if(session.IsCompleted){await session;throw new IOException("Preview closed before loading content.");}
                host=Find<Grid>(root,"PreviewContentHost");
                if(host?.Children.Any(child=>entry.IsText?child is TextBox:child is ScrollViewer)==true)break;
            }
            if(host?.Children.Count!=1||!(entry.IsText?host.Children[0] is TextBox:host.Children[0] is ScrollViewer))throw new IOException("Preview did not load its content.");
            var content=host.Children[0];
            await ShowPreviewAsync(entry);
            if(!ReferenceEquals(window,previewWindowPointer))throw new IOException("Repeated activation created a second preview window.");
            if(entry.IsText)
            {
                if(content is not TextBox text||text.Text.Length==0)throw new IOException("Text preview is empty.");
                var find=Find<TextBox>(root,"PreviewFind")??throw new IOException("Preview search field is missing.");
                var bodyHeight=host.ActualHeight;
                find.Text="no-such-preview-text-"+Guid.NewGuid().ToString("N");root.UpdateLayout();
                if(!ReferenceEquals(content,host.Children[0])||host.ActualHeight!=bodyHeight)throw new IOException("Finding text recreated or resized the preview content.");
                find.Text="";
                var wrap=Find<ToggleSwitch>(root,"PreviewWrap")??throw new IOException("Preview wrap control is missing.");
                wrap.IsOn=false;root.UpdateLayout();
                if(!ReferenceEquals(content,host.Children[0])||text.TextWrapping!=TextWrapping.NoWrap)throw new IOException("Changing wrap recreated the preview content.");
                wrap.IsOn=true;
            }
            await Task.Delay(1400);
            if(!ReferenceEquals(content,host.Children[0])||!ReferenceEquals(window,previewWindowPointer))throw new IOException("Preview content changed after its initial load.");
        }
        finally{window.Close();await session.WaitAsync(TimeSpan.FromSeconds(10));}
        if(previewOpen||previewWindowPointer!=null)throw new IOException("Closing the preview did not release its session.");
    }
    [System.Runtime.InteropServices.DllImport("user32.dll",EntryPoint="GetWindow")]
    private static extern nint GetVerificationWindowOwner(nint window,uint command);
}
