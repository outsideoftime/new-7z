using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using New7z.Core;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using static New7z.Core.Loc;

namespace New7z.App;

public sealed partial class MainWindow
{
    private bool previewOpen;
    private bool previewDragging;
    private int previewDragCursorX,previewDragCursorY,previewDragWindowX,previewDragWindowY;
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint{public int X;public int Y;}
    [DllImport("user32.dll")]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")]
    private static extern IntPtr SetPreviewWindowOwner(IntPtr window,int index,IntPtr owner);

    private static bool InsideButton(object? source)
    {
        for(var current=source as DependencyObject;current!=null;current=VisualTreeHelper.GetParent(current))
            if(current is Button)return true;
        return false;
    }
    // The preview is its own window: dragging the header moves the window.
    private void PreviewWindowDragHeader(object sender,PointerRoutedEventArgs args)
    {
        if(previewWindowPointer==null)return;
        var point=args.GetCurrentPoint((UIElement)sender);
        if(!point.Properties.IsLeftButtonPressed)return;
        if(!GetCursorPos(out var cursor))return;
        previewDragging=true;previewDragCursorX=cursor.X;previewDragCursorY=cursor.Y;
        previewDragWindowX=previewWindowPointer.AppWindow.Position.X;previewDragWindowY=previewWindowPointer.AppWindow.Position.Y;
        ((UIElement)sender).CapturePointer(args.Pointer);args.Handled=true;
    }
    private void PreviewWindowDragMoved(object sender,PointerRoutedEventArgs args)
    {
        if(!previewDragging||previewWindowPointer==null)return;
        if(GetCursorPos(out var cursor))previewWindowPointer.AppWindow.Move(new PointInt32(previewDragWindowX+(cursor.X-previewDragCursorX),previewDragWindowY+(cursor.Y-previewDragCursorY)));
        args.Handled=true;
    }
    private void PreviewWindowDragReleased(object sender,PointerRoutedEventArgs args)
    {
        if(!previewDragging)return;
        previewDragging=false;((UIElement)sender).ReleasePointerCapture(args.Pointer);args.Handled=true;
    }

    private async Task ShowPreviewAsync(ArchiveEntry initial)
    {
        if(archive==null||previewOpen)return;
        previewOpen=true;
        var currentArchive=archive;var currentPassword=password;
        var candidates=CurrentRows().Where(e=>!e.IsDirectory&&(e.IsImage||e.IsText)).ToList();
        if(!candidates.Any(e=>e.Index==initial.Index))candidates=[initial];
        var current=candidates.FindIndex(e=>e.Index==initial.Index);var sequence=0;
        byte[]? data=null;string? decoded=null;CancellationTokenSource? readCancellation=null;

        var previewWindow=new Window{Title=initial.Name+" · New7z",SystemBackdrop=new MicaBackdrop()};
        previewWindowPointer=previewWindow;
        if(previewWindow.AppWindow.Presenter is OverlappedPresenter presenter){presenter.SetBorderAndTitleBar(true,false);presenter.IsMaximizable=true;}
        WindowAppearance.ApplyIcon(previewWindow);

        var root=new Grid{RequestedTheme=Root.RequestedTheme,Background=(Brush)Application.Current.Resources["ApplicationPageBackgroundThemeBrush"]};
        root.RowDefinitions.Add(new(){Height=new GridLength(44)});
        root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        var header=new Grid{Padding=new Thickness(20,0,12,0),ColumnSpacing=10,Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent)};
        header.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var headerTitle=Text(initial.Name,14,true);headerTitle.VerticalAlignment=VerticalAlignment.Center;headerTitle.TextTrimming=TextTrimming.CharacterEllipsis;
        header.Children.Add(headerTitle);
        var closeButton=new Button{Content=new FontIcon{Glyph="",FontSize=12},Width=36,Height=32,Padding=new Thickness(0),VerticalAlignment=VerticalAlignment.Center};
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(closeButton,T("关闭预览","Close preview"));ToolTipService.SetToolTip(closeButton,T("关闭预览","Close preview"));
        closeButton.Click+=(_,_)=>previewWindow.Close();Grid.SetColumn(closeButton,1);header.Children.Add(closeButton);
        header.PointerPressed+=PreviewWindowDragHeader;header.PointerMoved+=PreviewWindowDragMoved;
        header.PointerReleased+=PreviewWindowDragReleased;header.PointerCaptureLost+=PreviewWindowDragReleased;
        root.Children.Add(header);
        root.KeyDown+=(_,e)=>{if(e.Key==Windows.System.VirtualKey.Escape){e.Handled=true;previewWindow.Close();}};

        var body=new Grid{Padding=new Thickness(20,6,20,16),RowSpacing=10};
        body.RowDefinitions.Add(new(){Height=GridLength.Auto});
        body.RowDefinitions.Add(new(){Height=GridLength.Auto});
        body.RowDefinitions.Add(new(){Height=GridLength.Auto});
        body.RowDefinitions.Add(new(){Height=GridLength.Auto});
        body.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        body.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var pathText=Text(System.IO.Path.GetFileName(currentArchive)+" / "+initial.NormalizedPath,12);body.Children.Add(pathText);

        var controls=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8,VerticalAlignment=VerticalAlignment.Center};
        Button ToolButton(string label,int minWidth=0){var b=new Button{Content=label,Height=32,VerticalAlignment=VerticalAlignment.Center};if(minWidth>0)b.MinWidth=minWidth;return b;}
        var previous=ToolButton(T("← 上一个","← Previous"),88);
        var next=ToolButton(T("下一个 →","Next →"),88);
        var position=Text("");position.VerticalAlignment=VerticalAlignment.Center;
        controls.Children.Add(previous);controls.Children.Add(position);controls.Children.Add(next);
        var zoom=new ComboBox{ItemsSource=new[]{T("适应窗口","Fit"),"25%","50%","100%","200%"},SelectedIndex=0,MinWidth=110,Height=32,VerticalAlignment=VerticalAlignment.Center};
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(zoom,T("图片缩放","Image zoom"));controls.Children.Add(zoom);
        var encoding=new ComboBox{ItemsSource=new[]{T("自动","Auto"),"UTF-8","GB18030","UTF-16 LE"},SelectedIndex=0,MinWidth=110,Height=32,VerticalAlignment=VerticalAlignment.Center};
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(encoding,T("文本编码","Text encoding"));controls.Children.Add(encoding);
        var wrapLabel=Text(T("自动换行","Wrap"),12);wrapLabel.VerticalAlignment=VerticalAlignment.Center;controls.Children.Add(wrapLabel);
        var wrap=new ToggleSwitch{Name="PreviewWrap",IsOn=true,VerticalAlignment=VerticalAlignment.Center,OnContent=T("开","On"),OffContent=T("关","Off"),MinHeight=32};controls.Children.Add(wrap);
        Grid.SetRow(controls,1);body.Children.Add(controls);

        var find=new TextBox{Name="PreviewFind",PlaceholderText=T("查找文本内容","Find in text"),Header=T("内容查找","Find content")};Grid.SetRow(find,2);body.Children.Add(find);
        var matches=Text("",12);matches.MinHeight=18;Grid.SetRow(matches,3);body.Children.Add(matches);
        var host=new Grid{Name="PreviewContentHost",Background=(Brush)Application.Current.Resources["LayerFillColorDefaultBrush"],CornerRadius=new CornerRadius(6),VerticalAlignment=VerticalAlignment.Stretch};Grid.SetRow(host,4);body.Children.Add(host);
        var metadata=Text("",12);metadata.Name="PreviewMetadata";metadata.MinHeight=18;Grid.SetRow(metadata,5);body.Children.Add(metadata);
        Grid.SetRow(body,1);root.Children.Add(body);
        previewWindow.Content=root;

        var scale=Root.XamlRoot.RasterizationScale;
        // Center on the display that holds the main window; a fresh window otherwise opens on the primary display.
        var area=DisplayArea.GetFromWindowId(AppWindow.Id,DisplayAreaFallback.Primary).WorkArea;
        var width=Math.Min((int)(1100*scale),area.Width-60);var height=Math.Min((int)(800*scale),area.Height-60);
        previewWindow.AppWindow.MoveAndResize(new RectInt32(area.X+(area.Width-width)/2,area.Y+(area.Height-height)/2,width,height));
        var closed=new TaskCompletionSource();
        previewWindow.Closed+=(_,_)=>{++sequence;readCancellation?.Cancel();previewDragging=false;closed.TrySetResult();};
        var previewHwnd=WinRT.Interop.WindowNative.GetWindowHandle(previewWindow);
        // An owned window stays above its parent without repeatedly stealing focus.
        SetPreviewWindowOwner(previewHwnd,-8,Hwnd); // GWLP_HWNDPARENT

        Image? image=null;BitmapImage? bitmap=null;
        var textContent=new TextBox{Name="PreviewTextContent",VerticalAlignment=VerticalAlignment.Stretch,HorizontalAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Top,IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,FontFamily=new FontFamily("Consolas"),FontSize=13,BorderThickness=new Thickness(0),Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent)};
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(textContent,T("只读文本内容，带行号","Read-only text with line numbers"));
        ScrollViewer.SetVerticalScrollBarVisibility(textContent,ScrollBarVisibility.Auto);
        void UpdateTextMatches()
        {
            if(data==null||!candidates[current].IsText)return;
            var numbered=textContent.Text;var q=find.Text;
            if(q.Length==0){matches.Text="";textContent.Select(textContent.SelectionStart,0);return;}
            var offset=numbered.IndexOf(q,StringComparison.CurrentCultureIgnoreCase);var count=0;
            for(var start=0;(start=numbered.IndexOf(q,start,StringComparison.CurrentCultureIgnoreCase))>=0;start+=q.Length)count++;
            matches.Text=count>0?T($"找到 {count} 处匹配，已选择第一处",$"Found {count} matches; selected the first"):T("没有找到匹配内容","No matches found");
            if(offset>=0)textContent.Select(offset,q.Length);else textContent.Select(textContent.SelectionStart,0);
        }
        void UpdateTextWrapping()
        {
            textContent.TextWrapping=wrap.IsOn?TextWrapping.Wrap:TextWrapping.NoWrap;
            ScrollViewer.SetHorizontalScrollBarVisibility(textContent,wrap.IsOn?ScrollBarVisibility.Disabled:ScrollBarVisibility.Auto);
        }
        void RenderText()
        {
            if(data==null)return;
            try
            {
                decoded=ArchiveEngine.DecodeText(data,encoding.SelectedItem?.ToString()??T("自动","Auto"));
                if(decoded.Contains('\0'))throw new InvalidDataException(T("内容可能为二进制文件，请更换编码或先解压查看。","The content looks binary. Try another encoding or extract the file first."));
                var lines=decoded.Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n').Split('\n');
                var numbered=string.Join(Environment.NewLine,lines.Take(100000).Select((line,i)=>$"{i+1,5}  {line}"));
                // Set the text after AcceptsReturn/TextWrapping so WinUI keeps every line.
                UpdateTextWrapping();textContent.Text=numbered;
                if(!host.Children.Contains(textContent)){host.Children.Clear();host.Children.Add(textContent);}
                UpdateTextMatches();
                metadata.Text=$"{ByteSize.Format((ulong)data.Length)} · {encoding.SelectedItem} · {T("只读文本","read-only text")}";
            }
            catch(Exception ex){host.Children.Clear();host.Children.Add(Text(ex.Message));}
        }
        void RenderZoom()
        {
            if(image==null||bitmap==null)return;
            if(zoom.SelectedIndex==0){image.Width=double.NaN;image.Height=double.NaN;image.MaxWidth=980;image.MaxHeight=host.ActualHeight>60?host.ActualHeight:420;image.Stretch=Stretch.Uniform;}
            else{var ratio=zoom.SelectedIndex switch{1=>.25,2=>.5,3=>1,_=>2};image.MaxWidth=double.PositiveInfinity;image.MaxHeight=double.PositiveInfinity;image.Width=bitmap.PixelWidth*ratio;image.Height=bitmap.PixelHeight*ratio;image.Stretch=Stretch.Fill;}
        }
        previous.Click+=async(_,_)=>{if(current>0){current--;await LoadAsync();}};next.Click+=async(_,_)=>{if(current<candidates.Count-1){current++;await LoadAsync();}};
        zoom.SelectionChanged+=(_,_)=>RenderZoom();encoding.SelectionChanged+=(_,_)=>{if(candidates[current].IsText)RenderText();};wrap.Toggled+=(_,_)=>UpdateTextWrapping();
        var findComposing=false;find.TextCompositionStarted+=(_,_)=>findComposing=true;find.TextCompositionEnded+=(_,_)=>{findComposing=false;UpdateTextMatches();};find.TextChanged+=(_,_)=>{if(!findComposing)UpdateTextMatches();};
        async Task LoadAsync()
        {
            if(closed.Task.IsCompleted)return;
            readCancellation?.Cancel();var tokenSource=new CancellationTokenSource();readCancellation=tokenSource;var token=tokenSource.Token;var version=++sequence;
            var entry=candidates[current];data=null;decoded=null;image=null;bitmap=null;
            headerTitle.Text=entry.Name;previewWindow.Title=entry.Name+" · New7z";previous.IsEnabled=current>0;next.IsEnabled=current<candidates.Count-1;position.Text=$"{current+1} / {candidates.Count}";
            zoom.Visibility=entry.IsImage?Visibility.Visible:Visibility.Collapsed;encoding.Visibility=wrap.Visibility=wrapLabel.Visibility=find.Visibility=matches.Visibility=entry.IsText?Visibility.Visible:Visibility.Collapsed;find.Text="";matches.Text="";
            host.Children.Clear();host.Children.Add(new ProgressRing{IsActive=true,Width=36,Height=36});metadata.Text=T("正在读取所选条目…","Loading the selected entry…");
            try
            {
                if(!entry.IsImage&&!entry.IsText)throw new NotSupportedException(T("暂不支持这种文件的内置预览。请先解压，再使用对应应用打开。","This file type cannot be previewed. Extract it and open it in its app."));
                var loadedData=await engine.ReadAsync(currentArchive,entry,currentPassword,entry.IsText?2UL*1024*1024:32UL*1024*1024,token);
                if(version!=sequence||token.IsCancellationRequested)return;
                data=loadedData;
                if(entry.IsText)RenderText();
                else
                {
                    using var stream=new InMemoryRandomAccessStream();using(var writer=new DataWriter(stream.GetOutputStreamAt(0))){writer.WriteBytes(data);await writer.StoreAsync();}stream.Seek(0);
                    var decoder=await BitmapDecoder.CreateAsync(stream);
                    if((ulong)decoder.PixelWidth*decoder.PixelHeight>40_000_000)throw new IOException(T("图片超过 4000 万像素的快速预览限制，请先解压查看。","The image exceeds the 40-megapixel preview limit. Extract it to view."));
                    stream.Seek(0);var loaded=new BitmapImage();await loaded.SetSourceAsync(stream);if(version!=sequence||token.IsCancellationRequested)return;
                    bitmap=loaded;image=new Image{Source=bitmap};zoom.SelectedIndex=0;RenderZoom();var scroll=new ScrollViewer{HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=image};host.Children.Clear();host.Children.Add(scroll);metadata.Text=$"{decoder.PixelWidth} × {decoder.PixelHeight} · {ByteSize.Format((ulong)data.Length)} · {T("只读图片","read-only image")}";
                }
            }
            catch(OperationCanceledException){}
            catch(Exception ex)
            {
                if(version!=sequence||token.IsCancellationRequested)return;host.Children.Clear();var error=Stack(12);error.Padding=new Thickness(24);error.Children.Add(Text(T("无法预览这个文件","Cannot preview this file"),18,true));error.Children.Add(Text(ex.Message));
                if(ex is ArchivePasswordException)
                {
                    var secret=new PasswordBox{Header=T("输入密码后重试","Enter the password to retry"),PasswordRevealMode=PasswordRevealMode.Peek};error.Children.Add(secret);error.Children.Add(Button(T("使用此密码重试","Retry with this password"),async(_,_)=>{currentPassword=secret.Password;password=currentPassword;await LoadAsync();}));
                }
                else error.Children.Add(Button(T("重新读取","Read again"),async(_,_)=>await LoadAsync()));
                host.Children.Add(error);metadata.Text=T("读取未完成 · 源文件未修改","Not loaded · the archive is unchanged");
            }
            finally{tokenSource.Dispose();if(ReferenceEquals(readCancellation,tokenSource))readCancellation=null;}
        }
        try
        {
            // Configure the first entry before showing the window, so controls do
            // not appear briefly and then collapse on the first visible frame.
            var initialLoad=LoadAsync();previewWindow.Activate();
            await initialLoad;await closed.Task;
        }
        finally{++sequence;readCancellation?.Cancel();previewOpen=false;previewWindowPointer=null;data=null;decoded=null;}
    }

    private Window? previewWindowPointer;
}
