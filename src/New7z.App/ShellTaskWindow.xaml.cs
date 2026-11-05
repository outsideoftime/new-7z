using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using New7z.Core;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace New7z.App;

public sealed partial class ShellTaskWindow:Window
{
    private readonly ShellRequest? request;
    private readonly string? startupError;
    private readonly ArchiveEngine engine=new();
    private CancellationTokenSource cancellation=new();
    private bool loaded,running,closingDialog,closeWhenDone,windowClosed;
    private int batchIndex;
    private string? destination;
    private string[] targets=[];
    private TextBox? output;
    private ComboBox? format,level,conflict;
    private PasswordBox? secret;
    private CheckBox? encryptNames;
    private ContentDialog? activeDialog;

    internal ShellTaskWindow(ShellRequest? shellRequest,string? error=null)
    {
        InitializeComponent();request=shellRequest;startupError=error;
        Title="New7z · 任务";SystemBackdrop=new MicaBackdrop();
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
    [DllImport("user32.dll")]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();
    [DllImport("user32.dll",EntryPoint="SendMessageW")]
    private static extern nint SendMessage(nint window,uint message,nint wParam,nint lParam);
    private void DragHeader_PointerPressed(object sender,Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
    {
        if(activeDialog!=null||!args.GetCurrentPoint(Root).Properties.IsLeftButtonPressed)return;
        args.Handled=true;ReleaseCapture();
        SendMessage(WindowNative.GetWindowHandle(this),0x00A1,2,0); // WM_NCLBUTTONDOWN / HTCAPTION
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
        var height=Math.Min((int)Math.Ceiling(heightDip*scale),area.Height-32);
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(area.X+(area.Width-width)/2,area.Y+(area.Height-height)/2,width,height));
    }
    private async void Loaded(object sender,RoutedEventArgs args)
    {
        if(loaded)return;loaded=true;Resize(request?.Action is "create" or "extract-to");
        if(startupError!=null){ShowFailure(startupError);return;}
        if(request==null){ShowFailure("没有可执行的任务。");return;}
        var action=request.Action;
        Heading.Text=action.StartsWith("create")?"压缩文件":action=="test"?"测试完整性":"解压文件";
        Title="New7z · "+Heading.Text;
        SourceText.Text=request.Paths.Length==1?System.IO.Path.GetFileName(request.Paths[0]):$"已选择 {request.Paths.Length} 个项目";
        ToolTipService.SetToolTip(SourceText,string.Join(Environment.NewLine,request.Paths.Take(20)));
        if(action is "create" or "extract-to"){Configure();return;}
        PrepareTargets();await RunAsync();
    }
    private void Configure()
    {
        TaskScrollViewer.VerticalAlignment=VerticalAlignment.Top;
        Configuration.Visibility=Visibility.Visible;ProgressSection.Visibility=Visibility.Collapsed;
        StartButton.Visibility=Visibility.Visible;CancelButton.Content="取消";
        output=new TextBox{Header=request!.Action=="create"?"保存为":"目标目录",Text=request.Action=="create"?ShellPaths.CreateTarget(request,"7z"):System.IO.Path.Combine(System.IO.Path.GetDirectoryName(request.Paths[0])!,ShellPaths.ArchiveStem(request.Paths[0]))};
        if(request.Action=="extract-to"&&request.Paths.Length>1)output.Text=System.IO.Path.GetDirectoryName(request.Paths[0])!;
        Configuration.Children.Add(output);
        var browse=new Button{Content="选择文件夹"};browse.Click+=Browse_Click;Configuration.Children.Add(browse);
        if(request.Action=="create")
        {
            StartButton.Content="开始压缩";
            format=new ComboBox{Header="格式",ItemsSource=new[]{"7z","zip"},SelectedIndex=0,HorizontalAlignment=HorizontalAlignment.Stretch};
            level=new ComboBox{Header="压缩级别",ItemsSource=new[]{"标准","更快","更小"},SelectedIndex=0,HorizontalAlignment=HorizontalAlignment.Stretch};
            secret=new PasswordBox{Header="密码（可选）",PasswordRevealMode=PasswordRevealMode.Peek};
            encryptNames=new CheckBox{Content="加密文件名（仅 7z）",IsEnabled=false};
            void UpdateEncryption(){encryptNames.IsEnabled=format.SelectedIndex==0&&secret.Password.Length>0;if(!encryptNames.IsEnabled)encryptNames.IsChecked=false;}
            format.SelectionChanged+=(_,_)=>{output.Text=System.IO.Path.ChangeExtension(output.Text,format.SelectedItem.ToString());UpdateEncryption();};
            secret.PasswordChanged+=(_,_)=>UpdateEncryption();
            Configuration.Children.Add(format);Configuration.Children.Add(level);Configuration.Children.Add(secret);Configuration.Children.Add(encryptNames);
        }
        else
        {
            StartButton.Content="开始解压";
            conflict=new ComboBox{Header="遇到同名文件",ItemsSource=new[]{"自动重命名，保留两者","跳过已有文件","替换已有文件"},SelectedIndex=0,HorizontalAlignment=HorizontalAlignment.Stretch};Configuration.Children.Add(conflict);
            Configuration.Children.Add(new TextBlock{Text=request.Paths.Length>1?"每个压缩包解压到独立子文件夹，不删除源文件。":"不删除源压缩包。",TextWrapping=TextWrapping.Wrap});
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
        catch(Exception ex){ShowMessage("无法选择文件夹",ex.Message,InfoBarSeverity.Error);}
    }
    private async void Start_Click(object sender,RoutedEventArgs args)
    {
        if(running||!StartButton.IsEnabled)return;StartButton.IsEnabled=false;
        try
        {
            if(string.IsNullOrWhiteSpace(output?.Text)||!System.IO.Path.IsPathFullyQualified(output.Text))throw new IOException("请输入完整的目标路径，或选择文件夹。");
            if(request!.Action=="create"&&(File.Exists(output.Text)||Directory.Exists(output.Text)))throw new IOException("输出名称已存在，请换一个名称。");
            if(conflict?.SelectedIndex==2&&!await ConfirmAsync("替换同名文件？","目标目录中的同名文件将被替换：\n"+output.Text,"替换并解压"))return;
            PrepareTargets();await RunAsync();
        }
        catch(Exception ex){ShowMessage("请检查目标位置",ex.Message,InfoBarSeverity.Error);output?.Focus(FocusState.Programmatic);}
        finally{StartButton.IsEnabled=true;}
    }
    private void PrepareTargets()
    {
        if(request!.Action.StartsWith("create"))
        {
            destination=output?.Text??ShellPaths.CreateTarget(request,request.Action=="create-zip"?"zip":"7z");targets=[destination];return;
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
        TaskScrollViewer.VerticalAlignment=VerticalAlignment.Stretch;
        Configuration.Visibility=Visibility.Collapsed;StartButton.Visibility=Visibility.Collapsed;
        RetryButton.Visibility=Visibility.Collapsed;FolderButton.Visibility=Visibility.Collapsed;Message.IsOpen=false;
        ProgressSection.Visibility=Visibility.Visible;Progress.IsIndeterminate=true;Progress.Value=0;PercentText.Text="";
        CancelButton.Content="取消任务";CancelButton.IsEnabled=true;Resize(false);
        DestinationText.Text=(request!.Action=="test"?"位置：":"保存到：")+destination;
        bool succeeded=false;
        try
        {
            var creating=request.Action.StartsWith("create");var count=creating?1:request.Paths.Length;
            for(var i=0;i<count;i++)
            {
                cancellation.Token.ThrowIfCancellationRequested();batchIndex=i;
                StatusText.Text=(creating?"正在压缩":request.Action=="test"?"正在测试":"正在解压")+(count>1?$" · {i+1}/{count}":"…");
                if(!creating)SourceText.Text=System.IO.Path.GetFileName(request.Paths[i]);
                var step=i;
                var progress=new Progress<EngineProgress>(p=>
                {
                    if(!running||batchIndex!=step||p.Total==0)return;
                    Progress.IsIndeterminate=false;
                    Progress.Value=Math.Max(Progress.Value,Math.Min(100,(step+Math.Clamp(p.Completed/(double)p.Total,0,1))*100/count));
                    PercentText.Text=$"{Progress.Value:0}%";
                });
                if(creating)
                {
                    var kind=format?.SelectedItem?.ToString()??(request.Action=="create-zip"?"zip":"7z");
                    var compression=level?.SelectedIndex switch{1=>1,2=>9,_=>5};
                    await engine.CreateAsync(targets[0],request.Paths,kind,compression,secret?.Password,encryptNames?.IsChecked==true,progress,cancellation.Token);
                }
                else await ProcessArchiveAsync(request.Paths[i],targets[i],progress);
            }
            Progress.IsIndeterminate=false;Progress.Value=100;PercentText.Text="100%";StatusText.Text="已完成";
            ShowMessage(request.Action=="test"?"完整性测试通过":creating?"压缩完成":"解压完成",request.Action=="test"?"所选压缩包未发现完整性错误。":"文件已保存到目标位置。",InfoBarSeverity.Success);
            succeeded=true;
        }
        catch(OperationCanceledException){Progress.IsIndeterminate=false;StatusText.Text="已取消";ShowMessage("任务已取消","已完成的输出保留，当前未完成的临时文件已清理。",InfoBarSeverity.Informational);}
        catch(Exception ex){ShowFailure(ex.Message);RetryButton.Visibility=Visibility.Visible;}
        finally
        {
            running=false;secret?.SetValue(PasswordBox.PasswordProperty,"");
            CancelButton.Content="关闭";CancelButton.IsEnabled=true;
            FolderButton.Visibility=destination!=null?Visibility.Visible:Visibility.Collapsed;
            bool autoClose=false;
            try{autoClose=succeeded&&new ExplorerPreferencesStore().Read().AutoCloseOnSuccess;}
            catch(Exception){/* A settings read failure keeps the result visible. */}
            if(closeWhenDone||autoClose){activeDialog?.Hide();if(!windowClosed)Close();}
        }
    }
    private async Task ProcessArchiveAsync(string source,string target,IProgress<EngineProgress> progress)
    {
        string? key=null;
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
                    await engine.ExtractAsync(source,entries,target,(ConflictPolicy)(conflict?.SelectedIndex??0),key,progress,cancellation.Token);
                }
                return;
            }
            catch(ArchivePasswordException)
            {
                Progress.IsIndeterminate=true;StatusText.Text="等待输入密码";
                var password=new PasswordBox{Header="压缩包密码",PasswordRevealMode=PasswordRevealMode.Peek};
                var content=new StackPanel{Spacing=12};content.Children.Add(new TextBlock{Text=System.IO.Path.GetFileName(source),TextWrapping=TextWrapping.Wrap});content.Children.Add(password);
                if(key!=null)content.Children.Add(new TextBlock{Text="密码不正确，请重新输入。",TextWrapping=TextWrapping.Wrap});
                var dialog=CreateDialog("输入密码",content,"继续");
                dialog.Opened+=(_,_)=>password.Focus(FocusState.Programmatic);
                if(await ShowDialogAsync(dialog)!=ContentDialogResult.Primary)throw new OperationCanceledException();
                key=password.Password;password.Password="";cancellation.Token.ThrowIfCancellationRequested();
                StatusText.Text=request!.Action=="test"?"正在测试…":"正在解压…";
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
        Progress.IsIndeterminate=false;StatusText.Text="未完成";CancelButton.Content="关闭";
        ShowMessage("任务未完成",error,InfoBarSeverity.Error);
    }
    private ContentDialog CreateDialog(string title,UIElement content,string primary)=>new(){Title=title,Content=content,PrimaryButtonText=primary,CloseButtonText="返回",DefaultButton=ContentDialogButton.Close,XamlRoot=Root.XamlRoot,RequestedTheme=Root.RequestedTheme};
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
            if(!await ConfirmAsync("取消任务？","已完成的输出会保留，当前未完成的临时文件会清理。","取消任务")||windowClosed)return;
            if(!running){if(exit)Close();return;}
            closeWhenDone=exit;cancellation.Cancel();CancelButton.IsEnabled=false;StatusText.Text="正在取消…";
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
            if(path==null||!Directory.Exists(path))throw new IOException("目标文件夹尚未创建。");
            var info=new ProcessStartInfo("explorer.exe"){UseShellExecute=false};info.ArgumentList.Add(path);Process.Start(info);
        }
        catch(Exception ex){ShowMessage("无法打开文件夹",ex.Message,InfoBarSeverity.Error);}
    }
}
