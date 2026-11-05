using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using New7z.Core;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace New7z.App;

public sealed partial class MainWindow
{
    private bool previewOpen;
    private async Task ShowPreviewAsync(ArchiveEntry initial)
    {
        if(archive==null||previewOpen)return;
        previewOpen=true;
        var currentArchive=archive;var currentPassword=password;
        var candidates=CurrentRows().Where(e=>!e.IsDirectory&&(e.IsImage||e.IsText)).ToList();
        if(!candidates.Any(e=>e.Index==initial.Index))candidates=[initial];
        var current=candidates.FindIndex(e=>e.Index==initial.Index);var sequence=0;
        byte[]? data=null;string? decoded=null;CancellationTokenSource? readCancellation=null;
        var panel=Stack(12);panel.MinWidth=Math.Clamp(AppWindow.ClientSize.Width/Root.XamlRoot.RasterizationScale-160,480,900);
        panel.Children.Add(Text(System.IO.Path.GetFileName(currentArchive)+" / "+initial.NormalizedPath,12));
        var controls=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};
        var previous=new Button{Content="← 上一个"};
        var next=new Button{Content="下一个 →"};
        var position=Text("");position.VerticalAlignment=VerticalAlignment.Center;
        controls.Children.Add(previous);controls.Children.Add(position);controls.Children.Add(next);
        var zoom=new ComboBox{ItemsSource=new[]{"适应窗口","25%","50%","100%","200%"},SelectedIndex=0,MinWidth=120};Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(zoom,"图片缩放");controls.Children.Add(zoom);
        var encoding=new ComboBox{ItemsSource=new[]{"自动","UTF-8","GB18030","UTF-16 LE"},SelectedIndex=0,MinWidth=120};Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(encoding,"文本编码");controls.Children.Add(encoding);
        var wrapLabel=Text("自动换行",12);wrapLabel.VerticalAlignment=VerticalAlignment.Center;controls.Children.Add(wrapLabel);
        var wrap=new ToggleSwitch{IsOn=true,VerticalAlignment=VerticalAlignment.Center,OnContent="开",OffContent="关"};controls.Children.Add(wrap);panel.Children.Add(controls);
        var find=new TextBox{PlaceholderText="查找文本内容",Header="内容查找"};var matches=Text("",12);panel.Children.Add(find);panel.Children.Add(matches);
        var host=new Grid{Height=Math.Clamp(AppWindow.ClientSize.Height/Root.XamlRoot.RasterizationScale-310,180,430),Background=(Brush)Application.Current.Resources["LayerFillColorDefaultBrush"],CornerRadius=new CornerRadius(6)};panel.Children.Add(host);
        var metadata=Text("",12);panel.Children.Add(metadata);
        var preview=new ContentDialog{Title=initial.Name,Content=panel,CloseButtonText="关闭预览",XamlRoot=Root.XamlRoot,RequestedTheme=Root.RequestedTheme};preview.Resources["ContentDialogMaxWidth"]=1100d;
        var previewTask=preview.ShowAsync();
        Image? image=null;BitmapImage? bitmap=null;
        void RenderText()
        {
            if(data==null)return;
            try
            {
                decoded=ArchiveEngine.DecodeText(data,encoding.SelectedItem?.ToString()??"自动");
                if(decoded.Contains('\0'))throw new InvalidDataException("内容可能为二进制文件，请更换编码或先解压查看。");
                var lines=decoded.Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n').Split('\n');
                var numbered=string.Join(Environment.NewLine,lines.Take(100000).Select((line,i)=>$"{i+1,5}  {line}"));
                var box=new TextBox{Text=numbered,Height=host.Height,VerticalAlignment=VerticalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Top,IsReadOnly=true,AcceptsReturn=true,TextWrapping=wrap.IsOn?TextWrapping.Wrap:TextWrapping.NoWrap,FontFamily=new FontFamily("Consolas"),FontSize=13,BorderThickness=new Thickness(0),Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent)};
                ScrollViewer.SetHorizontalScrollBarVisibility(box,wrap.IsOn?ScrollBarVisibility.Disabled:ScrollBarVisibility.Auto);ScrollViewer.SetVerticalScrollBarVisibility(box,ScrollBarVisibility.Auto);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(box,"只读文本内容，带行号");host.Children.Clear();host.Children.Add(box);
                var q=find.Text;
                if(q.Length>0){var offset=numbered.IndexOf(q,StringComparison.CurrentCultureIgnoreCase);var count=0;for(var start=0;(start=numbered.IndexOf(q,start,StringComparison.CurrentCultureIgnoreCase))>=0;start+=q.Length)count++;matches.Text=count>0?$"找到 {count} 处匹配，已选择第一处":"没有找到匹配内容";if(offset>=0)box.Select(offset,q.Length);}else matches.Text="";
                metadata.Text=$"{ByteSize.Format((ulong)data.Length)} · {encoding.SelectedItem} · 只读文本";
            }
            catch(Exception ex){host.Children.Clear();host.Children.Add(Text(ex.Message));}
        }
        void RenderZoom()
        {
            if(image==null||bitmap==null)return;
            if(zoom.SelectedIndex==0){image.Width=double.NaN;image.Height=double.NaN;image.MaxWidth=980;image.MaxHeight=420;image.Stretch=Stretch.Uniform;}
            else{var ratio=zoom.SelectedIndex switch{1=>.25,2=>.5,3=>1,_=>2};image.MaxWidth=double.PositiveInfinity;image.MaxHeight=double.PositiveInfinity;image.Width=bitmap.PixelWidth*ratio;image.Height=bitmap.PixelHeight*ratio;image.Stretch=Stretch.Fill;}
        }
        previous.Click+=async(_,_)=>{if(current>0){current--;await LoadAsync();}};next.Click+=async(_,_)=>{if(current<candidates.Count-1){current++;await LoadAsync();}};
        zoom.SelectionChanged+=(_,_)=>RenderZoom();encoding.SelectionChanged+=(_,_)=>{if(candidates[current].IsText)RenderText();};wrap.Toggled+=(_,_)=>{if(candidates[current].IsText)RenderText();};find.TextChanged+=(_,_)=>{if(candidates[current].IsText)RenderText();};
        async Task LoadAsync()
        {
            readCancellation?.Cancel();var tokenSource=new CancellationTokenSource();readCancellation=tokenSource;var token=tokenSource.Token;var version=++sequence;
            var entry=candidates[current];data=null;decoded=null;image=null;bitmap=null;
            preview.Title=entry.Name;previous.IsEnabled=current>0;next.IsEnabled=current<candidates.Count-1;position.Text=$"{current+1} / {candidates.Count}";
            zoom.Visibility=entry.IsImage?Visibility.Visible:Visibility.Collapsed;encoding.Visibility=wrap.Visibility=find.Visibility=matches.Visibility=entry.IsText?Visibility.Visible:Visibility.Collapsed;find.Text="";
            host.Children.Clear();host.Children.Add(new ProgressRing{IsActive=true,Width=36,Height=36});metadata.Text="正在读取所选条目…";
            try
            {
                if(!entry.IsImage&&!entry.IsText)throw new NotSupportedException("暂不支持这种文件的内置预览。请先解压，再使用对应应用打开。");
                while(true)
                {
                    try{data=await engine.ReadAsync(currentArchive,entry,currentPassword,entry.IsText?2UL*1024*1024:32UL*1024*1024,token);break;}
                    catch(ArchivePasswordException){throw new ArchivePasswordException();}
                }
                if(version!=sequence||token.IsCancellationRequested)return;
                if(entry.IsText)RenderText();
                else
                {
                    using var stream=new InMemoryRandomAccessStream();using(var writer=new DataWriter(stream.GetOutputStreamAt(0))){writer.WriteBytes(data);await writer.StoreAsync();}stream.Seek(0);
                    var decoder=await BitmapDecoder.CreateAsync(stream);
                    if((ulong)decoder.PixelWidth*decoder.PixelHeight>40_000_000)throw new IOException("图片超过 4000 万像素的快速预览限制，请先解压查看。");
                    stream.Seek(0);var loaded=new BitmapImage();await loaded.SetSourceAsync(stream);if(version!=sequence||token.IsCancellationRequested)return;
                    bitmap=loaded;image=new Image{Source=bitmap};zoom.SelectedIndex=0;RenderZoom();var scroll=new ScrollViewer{HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=image};host.Children.Clear();host.Children.Add(scroll);metadata.Text=$"{decoder.PixelWidth} × {decoder.PixelHeight} · {ByteSize.Format((ulong)data.Length)} · 只读图片";
                }
            }
            catch(OperationCanceledException){}
            catch(Exception ex)
            {
                if(version!=sequence)return;host.Children.Clear();var error=Stack(12);error.Padding=new Thickness(24);error.Children.Add(Text("无法预览这个文件",18,true));error.Children.Add(Text(ex.Message));
                if(ex is ArchivePasswordException)
                {
                    var secret=new PasswordBox{Header="输入密码后重试",PasswordRevealMode=PasswordRevealMode.Peek};error.Children.Add(secret);error.Children.Add(Button("使用此密码重试",async(_,_)=>{currentPassword=secret.Password;password=currentPassword;await LoadAsync();}));
                }
                else error.Children.Add(Button("重新读取",async(_,_)=>await LoadAsync()));
                host.Children.Add(error);metadata.Text="读取未完成 · 源文件未修改";
            }
            finally{tokenSource.Dispose();if(ReferenceEquals(readCancellation,tokenSource))readCancellation=null;}
        }
        try{await LoadAsync();await previewTask;}finally{++sequence;readCancellation?.Cancel();previewOpen=false;data=null;decoded=null;}
    }
}
