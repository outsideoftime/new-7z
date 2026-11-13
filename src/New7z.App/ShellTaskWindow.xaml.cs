using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using New7z.Core;
using Windows.Storage.Pickers;
using WinRT.Interop;
using static New7z.Core.Loc;

namespace New7z.App;

public sealed partial class ShellTaskWindow:Window
{
    private readonly ShellRequest? request;
    private readonly string? startupError;
    private readonly ArchiveEngine engine=new();
    private CancellationTokenSource cancellation=new();
    private bool loaded,running,closingDialog,closeWhenDone,windowClosed;
    private int batchIndex;
    private readonly string taskId=Guid.NewGuid().ToString("N");
    private string? destination;
    private string[] targets=[];
    private TextBox? output;
    private ComboBox? format,method,level,conflict;
    private PasswordBox? secret;
    private CheckBox? encryptNames;
    private ContentDialog? activeDialog;

    internal ShellTaskWindow(ShellRequest? shellRequest,string? error=null)
    {
        InitializeComponent();request=shellRequest;startupError=error;
        Heading.Text=T("准备任务","Preparing Task");StatusText.Text=T("正在准备…","Preparing…");
        AutomationProperties.SetName(Progress,T("任务进度","Task progress"));
        RetryButton.Content=T("重试","Retry");FolderButton.Content=T("打开文件夹","Open Folder");
        StartButton.Content=T("开始","Start");CancelButton.Content=T("取消任务","Cancel Task");
        Title="New7z · "+T("任务","Task");SystemBackdrop=new MicaBackdrop();
        if(AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(true,false);
            presenter.IsMaximizable=false;
        }
        WindowAppearance.ApplyIcon(this);WindowAppearance.ApplyTheme(Root);
        BrandImage.Source=new BitmapImage(new Uri(System.IO.Path.Combine(AppContext.BaseDirectory,"Assets","logo.png")));
        Root.Loaded+=Loaded;AppWindow.Closing+=Closing;
        Closed+=(_,_)=>{windowClosed=true;cancellation.Dispose();};
    }
    // The content header moves the window; there is no native caption or caption buttons.
    private bool dragging;
    private int dragCursorX,dragCursorY,dragWindowX,dragWindowY;
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint{public int X;public int Y;}
    [DllImport("user32.dll")]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);
    private void DragHeader_PointerPressed(object sender,Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
    {
        if(activeDialog!=null)return;
        var point=args.GetCurrentPoint(Root);
        if(args.Pointer.PointerDeviceType==Microsoft.UI.Input.PointerDeviceType.Mouse&&!point.Properties.IsLeftButtonPressed)return;
        if(!GetCursorPos(out var cursor))return;
        dragging=true;dragCursorX=cursor.X;dragCursorY=cursor.Y;
        dragWindowX=AppWindow.Position.X;dragWindowY=AppWindow.Position.Y;
        ((UIElement)sender).CapturePointer(args.Pointer);args.Handled=true;
    }
    private void DragHeader_PointerMoved(object sender,Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
    {
        if(!dragging)return;
        if(GetCursorPos(out var cursor))AppWindow.Move(new Windows.Graphics.PointInt32(dragWindowX+(cursor.X-dragCursorX),dragWindowY+(cursor.Y-dragCursorY)));
        args.Handled=true;
    }
    private void DragHeader_PointerReleased(object sender,Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
    {
        if(!dragging)return;
        dragging=false;((UIElement)sender).ReleasePointerCapture(args.Pointer);args.Handled=true;
    }
    private async void Root_KeyDown(object sender,Microsoft.UI.Xaml.Input.KeyRoutedEventArgs args)
    {
        if(args.Key!=Windows.System.VirtualKey.Escape||activeDialog!=null)return;
        args.Handled=true;await CancelAsync(false);
    }
    private void Resize(bool configure)
    {
        ResizeToHeight(configure?560:380);
    }
    private void ResizeToContent()
    {
        Root.UpdateLayout();
        var contentHeight=TaskBody.DesiredSize.Height+ActionFooter.ActualHeight+2;
        if(double.IsFinite(contentHeight)&&contentHeight>0)ResizeToHeight(contentHeight);
    }
    private void ResizeToHeight(double heightDip)
    {
        var scale=Root.XamlRoot.RasterizationScale;
        var area=DisplayArea.GetFromWindowId(AppWindow.Id,DisplayAreaFallback.Primary).WorkArea;
        var width=Math.Min((int)(600*scale),area.Width-32);
        // MoveAndResize takes the outer window rect; add the non-client frame so the
        // client area is tall enough and the ScrollViewer does not show a scrollbar.
        var frame=Math.Max(0,AppWindow.Size.Height-AppWindow.ClientSize.Height);
        var height=Math.Min((int)Math.Ceiling(heightDip*scale)+frame,area.Height-32);
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(area.X+(area.Width-width)/2,area.Y+(area.Height-height)/2,width,height));
    }
    private async void Loaded(object sender,RoutedEventArgs args)
    {
        if(loaded)return;loaded=true;Resize(request?.Action is "create" or "extract-to");
        if(startupError!=null){ShowFailure(startupError);return;}
        if(request==null){ShowFailure(T("没有可执行的任务。","There is no task to run."));return;}
        var action=request.Action;
        Heading.Text=action.StartsWith("create")?T("压缩文件","Create Archive"):action=="test"?T("测试完整性","Test Integrity"):T("解压文件","Extract Files");
        Title="New7z · "+Heading.Text;
        SourceText.Text=action=="extract-entries"?T($"{System.IO.Path.GetFileName(request.Paths[0])} · {request.Entries?.Length??0} 项",$"{System.IO.Path.GetFileName(request.Paths[0])} · {request.Entries?.Length??0} items"):request.Paths.Length==1?System.IO.Path.GetFileName(request.Paths[0]):T($"已选择 {request.Paths.Length} 个项目",$"Selected {request.Paths.Length} items");
        ToolTipService.SetToolTip(SourceText,string.Join(Environment.NewLine,request.Paths.Take(20)));
        if(action is "create" or "extract-to" or "extract-entries"){Configure();return;}
        PrepareTargets();await RunAsync();
    }
    private void Configure()
    {
        TaskScrollViewer.VerticalAlignment=VerticalAlignment.Top;
        Configuration.Visibility=Visibility.Visible;ProgressSection.Visibility=Visibility.Collapsed;
        StartButton.Visibility=Visibility.Visible;CancelButton.Content=T("取消","Cancel");
        output=new TextBox{Header=request!.Action=="create"?T("保存为","Save as"):T("目标目录","Target folder"),Text=request.Action=="create"?ShellPaths.CreateTarget(request,UserSettings.NormalizeFormat(UserSettings.Load().DefaultFormat)):request.Action=="extract-entries"&&request.Target!=null?request.Target:System.IO.Path.Combine(System.IO.Path.GetDirectoryName(request.Paths[0])!,ShellPaths.ArchiveStem(request.Paths[0]))};
        if(request.Action=="extract-to"&&request.Paths.Length>1)output.Text=System.IO.Path.GetDirectoryName(request.Paths[0])!;
        Configuration.Children.Add(output);
        var browse=new Button{Content=T("选择文件夹","Choose Folder")};browse.Click+=Browse_Click;Configuration.Children.Add(browse);
        if(request.Action=="create")
        {
            StartButton.Content=T("开始压缩","Start Compression");
            format=new ComboBox{Header=T("格式","Format"),ItemsSource=new[]{"7z","zip","tar","zst","lz4"},SelectedIndex=Math.Max(0,Array.IndexOf(new[]{"7z","zip","tar","zst","lz4"},UserSettings.NormalizeFormat(UserSettings.Load().DefaultFormat))),HorizontalAlignment=HorizontalAlignment.Stretch};
            method=new ComboBox{Header=T("压缩方法","Compression method"),HorizontalAlignment=HorizontalAlignment.Stretch};
            level=new ComboBox{Header=T("压缩级别","Compression Level"),ItemsSource=new[]{T("标准","Standard"),T("更快","Faster"),T("更小","Smaller")},SelectedIndex=0,HorizontalAlignment=HorizontalAlignment.Stretch};
            secret=new PasswordBox{Header=T("密码（可选）","Password (optional)"),PasswordRevealMode=PasswordRevealMode.Peek};
            encryptNames=new CheckBox{Content=T("加密文件名（仅 7z）","Encrypt file names (7z only)"),IsEnabled=false};
            void UpdateMethods(){var code=format.SelectedItem?.ToString()??"7z";var choices=CompressionOptions.For(code);method.ItemsSource=choices;var preferred=CompressionOptions.Default(UserSettings.Load(),code);method.SelectedIndex=Math.Max(0,choices.ToList().FindIndex(item=>item.Value==preferred));method.IsEnabled=choices.Count>0;}
            var streamHint=new TextBlock{Text=T("Zstandard / LZ4 每次压缩一个文件；多个文件请先打包为 TAR。","Zstandard / LZ4 compress one file; pack multiple files into TAR first."),TextWrapping=TextWrapping.Wrap};
            void UpdateEncryption(){var noPassword=format.SelectedIndex>=2;if(noPassword)secret.Password="";secret.IsEnabled=!noPassword;level.IsEnabled=format.SelectedIndex!=2;method.IsEnabled=format.SelectedIndex<2;streamHint.Visibility=format.SelectedIndex>=3?Visibility.Visible:Visibility.Collapsed;encryptNames.IsEnabled=format.SelectedIndex==0&&secret.Password.Length>0;if(!encryptNames.IsEnabled)encryptNames.IsChecked=false;}
            format.SelectionChanged+=(_,_)=>{output.Text=System.IO.Path.ChangeExtension(output.Text,format.SelectedItem.ToString());UpdateMethods();UpdateEncryption();};
            secret.PasswordChanged+=(_,_)=>UpdateEncryption();
            Configuration.Children.Add(format);Configuration.Children.Add(method);Configuration.Children.Add(level);Configuration.Children.Add(secret);Configuration.Children.Add(encryptNames);Configuration.Children.Add(streamHint);UpdateMethods();UpdateEncryption();
        }
        else
        {
            StartButton.Content=T("开始解压","Start Extraction");
            conflict=new ComboBox{Header=T("遇到同名文件","On Existing Files"),ItemsSource=new[]{T("自动重命名，保留两者","Rename Automatically, Keep Both"),T("跳过已有文件","Skip Existing Files"),T("替换已有文件","Replace Existing Files")},SelectedIndex=0,HorizontalAlignment=HorizontalAlignment.Stretch};Configuration.Children.Add(conflict);
            Configuration.Children.Add(new TextBlock{Text=request.Paths.Length>1?T("每个压缩包解压到独立子文件夹，不删除源文件。","Each archive is extracted to its own subfolder; source files are kept."):T("不删除源压缩包。","The source archive is kept."),TextWrapping=TextWrapping.Wrap});
        }
        output.Focus(FocusState.Programmatic);Root.DispatcherQueue.TryEnqueue(ResizeToContent);
    }
    private async void Browse_Click(object sender,RoutedEventArgs args)
    {
        try
        {
            var picker=new FolderPicker();InitializeWithWindow.Initialize(picker,WindowNative.GetWindowHandle(this));picker.FileTypeFilter.Add("*");
            var folder=await picker.PickSingleFolderAsync();
            if(folder!=null)output!.Text=request!.Action=="create"?System.IO.Path.Combine(folder.Path,System.IO.Path.GetFileName(output.Text)):folder.Path;
        }
        catch(Exception ex){ShowMessage(T("无法选择文件夹","Cannot Choose Folder"),ex.Message,InfoBarSeverity.Error);}
    }
    private async void Start_Click(object sender,RoutedEventArgs args)
    {
        if(running||!StartButton.IsEnabled)return;StartButton.IsEnabled=false;
        try
        {
            if(string.IsNullOrWhiteSpace(output?.Text)||!System.IO.Path.IsPathFullyQualified(output.Text))throw new IOException(T("请输入完整的目标路径，或选择文件夹。","Enter a full target path, or choose a folder."));
            if(request!.Action=="create"&&(File.Exists(output.Text)||Directory.Exists(output.Text)))throw new IOException(T("输出名称已存在，请换一个名称。","The output name already exists. Choose another name."));
            if(conflict?.SelectedIndex==2&&!await ConfirmAsync(T("替换同名文件？","Replace Existing Files?"),T("目标目录中的同名文件将被替换：\n","Files with the same name in the target folder will be replaced:\n")+output.Text,T("替换并解压","Replace and Extract")))return;
            PrepareTargets();await RunAsync();
        }
        catch(Exception ex){ShowMessage(T("请检查目标位置","Check the Target Location"),ex.Message,InfoBarSeverity.Error);output?.Focus(FocusState.Programmatic);}
        finally{StartButton.IsEnabled=true;}
    }
    private static string ActionFormat(string action)=>action switch{"create-zip"=>"zip","create-tar"=>"tar",_=>"7z"};
    private void PrepareTargets()
    {
        if(request!.Action=="extract-entries")
        {
            destination=output?.Text??request.Target??System.IO.Path.Combine(System.IO.Path.GetDirectoryName(request.Paths[0])!,ShellPaths.ArchiveStem(request.Paths[0]));
            targets=[destination];return;
        }
        if(request!.Action.StartsWith("create"))
        {
            destination=output?.Text??ShellPaths.CreateTarget(request,ActionFormat(request.Action));targets=[destination];return;
        }
        var reserved=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        targets=request.Paths.Select(source=>
        {
            if(request.Action=="test")return source;
            if(request.Action=="extract-to"&&request.Paths.Length==1)return output!.Text;
            var parent=request.Action=="extract-to"?output!.Text:System.IO.Path.GetDirectoryName(source)!;
            var target=request.Action=="extract-here"?parent:System.IO.Path.Combine(parent,ShellPaths.ArchiveStem(source));
            return request.Paths.Length>1?ShellPaths.UniqueOutput(target,reserved):target;
        }).ToArray();
        destination=request.Action=="test"?System.IO.Path.GetDirectoryName(request.Paths[0]):request.Action=="extract-to"&&request.Paths.Length>1?output!.Text:targets[0];
    }
    private async Task RunAsync()
    {
        if(running)return;running=true;cancellation.Dispose();cancellation=new();
        TaskScrollViewer.VerticalAlignment=VerticalAlignment.Top;
        Configuration.Visibility=Visibility.Collapsed;StartButton.Visibility=Visibility.Collapsed;
        RetryButton.Visibility=Visibility.Collapsed;FolderButton.Visibility=Visibility.Collapsed;Message.IsOpen=false;
        ProgressSection.Visibility=Visibility.Visible;Progress.IsIndeterminate=true;Progress.Value=0;PercentText.Text="";
        CancelButton.Content=T("取消任务","Cancel Task");CancelButton.IsEnabled=true;
        DestinationText.Text=(request!.Action=="test"?T("位置：","Location: "):T("保存到：","Save to: "))+destination;
        PersistSharedTask(T("正在处理","Running"));
        Root.DispatcherQueue.TryEnqueue(ResizeToContent);
        bool succeeded=false;
        try
        {
            var creating=request.Action.StartsWith("create");var count=creating?1:request.Paths.Length;
            for(var i=0;i<count;i++)
            {
                cancellation.Token.ThrowIfCancellationRequested();batchIndex=i;
                StatusText.Text=T((creating?"正在压缩":request.Action=="test"?"正在测试":"正在解压")+(count>1?$" · {i+1}/{count}":"…"),(creating?"Compressing":request.Action=="test"?"Testing":"Extracting")+(count>1?$" · {i+1}/{count}":"…"));
                if(!creating)SourceText.Text=System.IO.Path.GetFileName(request.Paths[i]);
                var step=i;
                var progress=new Progress<EngineProgress>(p=>
                {
                    if(!running||batchIndex!=step||p.Total==0)return;
                    Progress.IsIndeterminate=false;
                    Progress.Value=Math.Max(Progress.Value,Math.Min(100,(step+Math.Clamp(p.Completed/(double)p.Total,0,1))*100/count));
                    PercentText.Text=$"{Progress.Value:0}%";
                    PersistSharedTask(StatusText.Text,p.CurrentItem??"");
                });
                if(creating)
                {
                    var kind=format?.SelectedItem?.ToString()??ActionFormat(request.Action);
                    var methodName=(method?.SelectedItem as CompressionChoice)?.Value;
                    if(string.IsNullOrEmpty(methodName))methodName=CompressionOptions.Default(UserSettings.Load(),kind);
                    var compression=level?.SelectedIndex switch{1=>1,2=>9,_=>5};
                    await engine.CreateAsync(targets[0],request.Paths,kind,compression,secret?.Password,encryptNames?.IsChecked==true,methodName,progress,cancellation.Token);
                }
                else await ProcessArchiveAsync(request.Paths[i],targets[i],progress);
            }
            Progress.IsIndeterminate=false;Progress.Value=100;PercentText.Text="100%";StatusText.Text=T("已完成","Completed");
            ShowMessage(request.Action=="test"?T("完整性测试通过","Integrity Test Passed"):creating?T("压缩完成","Compression Complete"):T("解压完成","Extraction Complete"),request.Action=="test"?T("所选压缩包未发现完整性错误。","No integrity errors were found in the selected archives."):T("文件已保存到目标位置。","Files were saved to the target location."),InfoBarSeverity.Success);
            succeeded=true;
            PersistSharedTask(T("已完成","Completed"));
        }
        catch(OperationCanceledException){Progress.IsIndeterminate=false;StatusText.Text=T("已取消","Cancelled");PersistSharedTask(StatusText.Text);ShowMessage(T("任务已取消","Task Cancelled"),T("已完成的输出保留，当前未完成的临时文件已清理。","Completed output is kept; unfinished temporary files have been cleaned up."),InfoBarSeverity.Informational);}
        catch(Exception ex){ShowFailure(ex.Message);PersistSharedTask(T("失败：","Failed: ")+ex.Message);RetryButton.Visibility=Visibility.Visible;}
        finally
        {
            running=false;secret?.SetValue(PasswordBox.PasswordProperty,"");
            CancelButton.Content=T("关闭","Close");CancelButton.IsEnabled=true;
            FolderButton.Visibility=destination!=null?Visibility.Visible:Visibility.Collapsed;
            bool autoClose=false;
            try{autoClose=succeeded&&new ExplorerPreferencesStore().Read().AutoCloseOnSuccess;}
            catch(Exception){/* A settings read failure keeps the result visible. */}
            await SharedTaskStore.FlushAsync();
            if(closeWhenDone||autoClose){activeDialog?.Hide();if(!windowClosed)Close();}
        }
    }
    private void PersistSharedTask(string status,string current="")
    {
        if(request==null||destination==null||request.Action=="test")return;
        SharedTaskStore.Save(new SharedTask(taskId,(request.Action.StartsWith("create")?T("压缩 ","Compress "):T("解压 ","Extract "))+string.Join(", ",request.Paths.Select(System.IO.Path.GetFileName)),destination,status,Progress.Value,current,DateTime.UtcNow));
    }
    private async Task ProcessArchiveAsync(string source,string target,IProgress<EngineProgress> progress)
    {
        string? key=request!.Password;
        while(true)
        {
            cancellation.Token.ThrowIfCancellationRequested();
            try
            {
                if(request!.Action=="test")await engine.TestAsync(source,key,progress,cancellation.Token);
                else
                {
                    var entries=await engine.ListAsync(source,key,cancellation.Token);
                    if(entries.Any(e=>e.Encrypted)&&string.IsNullOrEmpty(key))throw new ArchivePasswordException();
                    if(request.Action=="extract-entries"&&request.Entries is {Length:>0} wanted)
                    {
                        var roots=new HashSet<string>(wanted,StringComparer.Ordinal);
                        entries=entries.Where(e=>roots.Contains(e.NormalizedPath)||roots.Any(r=>e.NormalizedPath.StartsWith(r+"/",StringComparison.Ordinal))).ToList();
                    }
                    await engine.ExtractAsync(source,entries,target,(ConflictPolicy)(conflict?.SelectedIndex??0),key,progress,cancellation.Token);
                }
                return;
            }
            catch(ArchivePasswordException)
            {
                Progress.IsIndeterminate=true;StatusText.Text=T("等待输入密码","Waiting for password");
                var password=new PasswordBox{Header=T("压缩包密码","Archive Password"),PasswordRevealMode=PasswordRevealMode.Peek};
                var content=new StackPanel{Spacing=12};content.Children.Add(new TextBlock{Text=System.IO.Path.GetFileName(source),TextWrapping=TextWrapping.Wrap});content.Children.Add(password);
                if(key!=null)content.Children.Add(new TextBlock{Text=T("密码不正确，请重新输入。","Incorrect password. Try again."),TextWrapping=TextWrapping.Wrap});
                var dialog=CreateDialog(T("输入密码","Enter Password"),content,T("继续","Continue"));
                dialog.Opened+=(_,_)=>password.Focus(FocusState.Programmatic);
                if(await ShowDialogAsync(dialog)!=ContentDialogResult.Primary)throw new OperationCanceledException();
                key=password.Password;password.Password="";cancellation.Token.ThrowIfCancellationRequested();
                StatusText.Text=request!.Action=="test"?T("正在测试…","Testing…"):T("正在解压…","Extracting…");
            }
        }
    }
    private void ShowMessage(string title,string text,InfoBarSeverity severity)
    {
        Message.Title=title;Message.Message=text;Message.Severity=severity;Message.IsOpen=true;
        // A message added after the window was sized to its form lands below the visible
        // area; grow the configure window and make sure the message is on screen.
        var grow=TaskScrollViewer.VerticalAlignment==VerticalAlignment.Top;
        Root.DispatcherQueue.TryEnqueue(()=>{if(grow)ResizeToContent();Message.StartBringIntoView();});
    }
    private void ShowFailure(string error)
    {
        Progress.IsIndeterminate=false;StatusText.Text=T("未完成","Not Completed");CancelButton.Content=T("关闭","Close");
        ShowMessage(T("任务未完成","Task Not Completed"),error,InfoBarSeverity.Error);
    }
    private ContentDialog CreateDialog(string title,UIElement content,string primary)=>new(){Title=title,Content=content,PrimaryButtonText=primary,CloseButtonText=T("返回","Back"),DefaultButton=ContentDialogButton.Close,XamlRoot=Root.XamlRoot,RequestedTheme=Root.RequestedTheme};
    private async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog)
    {
        activeDialog=dialog;try{return await dialog.ShowAsync();}finally{activeDialog=null;}
    }
    private async Task<bool> ConfirmAsync(string title,string text,string primary)=>await ShowDialogAsync(CreateDialog(title,new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap},primary))==ContentDialogResult.Primary;
    private async Task CancelAsync(bool exit)
    {
        if(!running){Close();return;}if(closingDialog)return;closingDialog=true;
        try
        {
            if(activeDialog!=null){closeWhenDone=exit;activeDialog.Hide();return;}
            if(!await ConfirmAsync(T("取消任务？","Cancel Task?"),T("已完成的输出会保留，当前未完成的临时文件会清理。","Completed output will be kept; unfinished temporary files will be cleaned up."),T("取消任务","Cancel Task"))||windowClosed)return;
            if(!running){if(exit)Close();return;}
            closeWhenDone=exit;cancellation.Cancel();CancelButton.IsEnabled=false;StatusText.Text=T("正在取消…","Cancelling…");
        }
        finally{closingDialog=false;}
    }
    private async void Closing(AppWindow sender,AppWindowClosingEventArgs args){if(!running)return;args.Cancel=true;await CancelAsync(true);}
    private async void Cancel_Click(object sender,RoutedEventArgs args)=>await CancelAsync(false);
    private async void Retry_Click(object sender,RoutedEventArgs args)
    {
        if(running)return;
        if(request?.Action is "create" or "extract-to")
        {
            Message.IsOpen=false;RetryButton.Visibility=Visibility.Collapsed;FolderButton.Visibility=Visibility.Collapsed;
            TaskScrollViewer.VerticalAlignment=VerticalAlignment.Top;Configuration.Visibility=Visibility.Visible;ProgressSection.Visibility=Visibility.Collapsed;StartButton.Visibility=Visibility.Visible;Root.DispatcherQueue.TryEnqueue(ResizeToContent);return;
        }
        PrepareTargets();await RunAsync();
    }
    private void Folder_Click(object sender,RoutedEventArgs args)
    {
        try
        {
            var path=destination;
            if(path==null)return;if(!Directory.Exists(path))path=System.IO.Path.GetDirectoryName(path);
            if(path==null||!Directory.Exists(path))throw new IOException(T("目标文件夹尚未创建。","The target folder has not been created yet."));
            var info=new ProcessStartInfo("explorer.exe"){UseShellExecute=false};info.ArgumentList.Add(path);Process.Start(info);
        }
        catch(Exception ex){ShowMessage(T("无法打开文件夹","Cannot Open Folder"),ex.Message,InfoBarSeverity.Error);}
    }
}
