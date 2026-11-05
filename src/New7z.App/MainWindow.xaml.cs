using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using New7z.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace New7z.App;

public sealed partial class MainWindow:Window
{
    private readonly ArchiveEngine engine=new();
    private readonly UserSettings settings=UserSettings.Load();
    private readonly SemaphoreSlim queue=new(1,1);
    private readonly ObservableCollection<ArchiveJob> jobs=[];
    private IReadOnlyList<ArchiveEntry> entries=[];
    private string? archive,password;
    private string folder="",query="",page="home";
    private ListView? fileList;
    private TextBlock? countLabel;
    private TextBox? searchBox;
    private bool initialized;
    private CancellationTokenSource? openCancellation;
    private long openVersion;
    private const string InternalDragFormat="New7z.EntryDrag";
    private string? dragTemp;
    private readonly List<string> startupFiles=[];
    private nint Hwnd=>WindowNative.GetWindowHandle(this);
    private static SolidColorBrush Secondary=>new(Colors.Gray);
    public MainWindow(string? startupArchive=null)
    {
        InitializeComponent();Title="New7z";ExtendsContentIntoTitleBar=true;SetTitleBar(TitleBar);
        SystemBackdrop=new MicaBackdrop();AppWindow.Resize(new Windows.Graphics.SizeInt32(1320,880));
        AppWindow.Closing+=OnClosing;
        initialized=true;ApplyTheme();ApplyIcon();WindowAppearance.ApplyIcon(this);ShowHome();
        Root.AllowDrop=true;Root.DragOver+=(_,e)=>{if(e.DataView.Contains(InternalDragFormat)){e.AcceptedOperation=DataPackageOperation.None;return;}if(e.DataView.Contains(StandardDataFormats.StorageItems))e.AcceptedOperation=DataPackageOperation.Copy;};Root.Drop+=FilesDropped;
        foreach(var stale in Directory.EnumerateDirectories(System.IO.Path.GetTempPath(),"New7z-drag-*"))
        {
            try{if(Directory.GetLastWriteTimeUtc(stale)<DateTime.UtcNow.AddDays(-2))Directory.Delete(stale,true);}catch{}
        }
        Root.Loaded+=async(_,_)=>{
            var scale=Root.XamlRoot.RasterizationScale;var area=DisplayArea.GetFromWindowId(AppWindow.Id,DisplayAreaFallback.Primary).WorkArea;
            var width=Math.Min((int)(1240*scale),area.Width-60);var height=Math.Min((int)(820*scale),area.Height-60);
            AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(area.X+(area.Width-width)/2,area.Y+(area.Height-height)/2,width,height));
            var launchArgs=Environment.GetCommandLineArgs().Skip(1).ToArray();
            if(launchArgs.Length==3&&launchArgs[0]=="--verify-ui"){await VerifyUiAsync(launchArgs[1],launchArgs[2]);return;}
            var arg=startupArchive??launchArgs.FirstOrDefault(File.Exists);if(arg!=null)await OpenArchiveAsync(arg);
            if(settings.AutoCheckUpdates)_=CheckForUpdatesAsync(false);
        };
    }
    private static Image? FormatIcon(string path)
    {
        var name=System.IO.Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        var file=System.IO.Path.Combine(AppContext.BaseDirectory,"Assets","Formats",name+".png");
        return File.Exists(file)?new Image{Width=32,Height=32,Source=new BitmapImage(new Uri(file))}:null;
    }
    private static TextBlock Text(string text,int size=14,bool bold=false)=>new(){Text=text,FontSize=size,FontWeight=bold?Microsoft.UI.Text.FontWeights.SemiBold:Microsoft.UI.Text.FontWeights.Normal,TextWrapping=TextWrapping.Wrap};
    private static StackPanel Stack(int space=16)=>new(){Spacing=space};
    private static Button Button(string label,RoutedEventHandler action,bool primary=false)
    {
        var b=new Button{Content=label,Padding=new Thickness(14,7,14,7)};if(primary)b.Style=(Style)Application.Current.Resources["AccentButtonStyle"];b.Click+=action;return b;
    }
    private static Border Card(UIElement child)=>new(){Child=child,Padding=new Thickness(22),CornerRadius=new CornerRadius(8),BorderThickness=new Thickness(1),BorderBrush=(Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],Background=(Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"]};
    private static TextBox Input(string label,string value="")=>new(){Header=label,Text=value,HorizontalAlignment=HorizontalAlignment.Stretch};
    private void ShowContent(UIElement content){ContentHost.Children.Clear();ContentHost.Children.Add(content);}
    private void Notify(string text,InfoBarSeverity severity=InfoBarSeverity.Informational){Message.Message=text;Message.Severity=severity;Message.ActionButton=null;Message.IsOpen=true;}
    private void SelectPage(string name)
    {
        page=name;
        if(name=="settings")Navigation.SelectedItem=Navigation.SettingsItem;
        else Navigation.SelectedItem=Navigation.MenuItems.Cast<NavigationViewItem>().First(i=>i.Tag?.ToString()==name);
        RenderPage();
    }
    private void NavigationChanged(NavigationView sender,NavigationViewSelectionChangedEventArgs args)
    {
        if(!initialized)return;
        page=args.IsSettingsSelected?"settings":(args.SelectedItem as NavigationViewItem)?.Tag?.ToString()??"home";RenderPage();
    }
    private void RenderPage(){if(page=="archive")ShowArchive();else if(page=="tasks")ShowTasks();else if(page=="settings")ShowSettings();else ShowHome();}
    private void ShowHome()
    {
        var content=Stack(24);content.Children.Add(Text("主页",28,true));content.Children.Add(Text("打开压缩包，或将文件打包分享。"));
        var actions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=10};actions.Children.Add(Button("打开压缩包",async(_,_)=>{var f=await PickArchiveAsync();if(f!=null)await OpenArchiveAsync(f);}));actions.Children.Add(Button("新建压缩",async(_,_)=>await CreateDialogAsync(),true));content.Children.Add(actions);
        var drop=Stack(10);drop.Children.Add(Text("把文件或文件夹拖到窗口",22,true));drop.Children.Add(Text("压缩包可查看内容，普通文件可打包为 7z 或 ZIP。无需另装 7-Zip。"));var pick=new StackPanel{Orientation=Orientation.Horizontal,Spacing=10};pick.Children.Add(Button("选择文件",async(_,_)=>{var paths=await PickFilesAsync();if(paths.Count>0)await CreateDialogAsync(paths);}));pick.Children.Add(Button("选择文件夹",async(_,_)=>{var path=await PickFolderAsync();if(path!=null)await CreateDialogAsync([path]);}));drop.Children.Add(pick);content.Children.Add(Card(drop));
        var recent=Stack(8);recent.Children.Add(Text("最近使用",18,true));
        foreach(var path in settings.Recent.Take(12)){var row=new Grid{ColumnSpacing=12};row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});var icon=FormatIcon(path);if(icon!=null){icon.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(icon);}var name=Text(System.IO.Path.GetFileName(path),14,true);var labels=Stack(3);labels.Children.Add(name);var location=Text(path,12);location.Foreground=(Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];labels.Children.Add(location);Grid.SetColumn(labels,1);row.Children.Add(labels);var open=Button("打开",async(_,_)=>await OpenArchiveAsync(path));Grid.SetColumn(open,2);row.Children.Add(open);recent.Children.Add(Card(row));}
        if(settings.Recent.Count==0)recent.Children.Add(Text("还没有最近记录。打开一份压缩包开始。"));
        else recent.Children.Add(Button("清空最近记录",async(_,_)=>{var d=Dialog("清空最近记录？",Text("仅移除应用内记录，不删除文件。"),"清空记录");if(await d.ShowAsync()==ContentDialogResult.Primary){settings.Recent.Clear();settings.Save();ShowHome();}}));
        content.Children.Add(recent);ShowContent(new ScrollViewer{Content=content,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
    }
    private async Task<string?> PickArchiveAsync()
    {
        var picker=new FileOpenPicker();InitializeWithWindow.Initialize(picker,Hwnd);picker.FileTypeFilter.Add("*");return (await picker.PickSingleFileAsync())?.Path;
    }
    private async Task<List<string>> PickFilesAsync()
    {
        var picker=new FileOpenPicker();InitializeWithWindow.Initialize(picker,Hwnd);picker.FileTypeFilter.Add("*");return (await picker.PickMultipleFilesAsync()).Select(f=>f.Path).ToList();
    }
    private async Task<string?> PickFolderAsync(){var picker=new FolderPicker();InitializeWithWindow.Initialize(picker,Hwnd);picker.FileTypeFilter.Add("*");return (await picker.PickSingleFolderAsync())?.Path;}
    private async void FilesDropped(object sender,DragEventArgs e)
    {
        if(e.DataView.Contains(InternalDragFormat))return;
        if(!e.DataView.Contains(StandardDataFormats.StorageItems))return;var deferral=e.GetDeferral();
        try{var items=await e.DataView.GetStorageItemsAsync();var paths=items.Select(i=>i.Path).ToList();if(paths.Count==0)return;if(page=="archive"&&archive!=null){await AddSourcesAsync(paths);return;}if(paths.Count==1&&File.Exists(paths[0])&&new[]{".7z",".zip",".rar",".tar",".gz",".xz",".bz2",".iso"}.Contains(System.IO.Path.GetExtension(paths[0]).ToLowerInvariant()))await OpenArchiveAsync(paths[0]);else if(paths.Count>0)await CreateDialogAsync(paths);}catch(Exception ex){Notify(ex.Message,InfoBarSeverity.Error);}finally{deferral.Complete();}
    }
    private async Task<string?> AskPasswordAsync()
    {
        var box=new PasswordBox{Header="压缩包密码",PasswordRevealMode=PasswordRevealMode.Peek};var d=Dialog("输入密码",box,"打开");return await d.ShowAsync()==ContentDialogResult.Primary?box.Password:null;
    }
    private async Task OpenArchiveAsync(string path,bool remember=true)
    {
        openCancellation?.Cancel();var version=++openVersion;var tokenSource=new CancellationTokenSource();openCancellation=tokenSource;var token=tokenSource.Token;
        StatusText.Text="正在读取压缩包…";Message.IsOpen=false;string? key=null;
        try
        {
            IReadOnlyList<ArchiveEntry> list;
            while(true){try{list=await engine.ListAsync(path,key,token);break;}catch(ArchivePasswordException){if(version!=openVersion)return;key=await AskPasswordAsync();if(key==null)return;}}
            if(version!=openVersion)return;
            archive=System.IO.Path.GetFullPath(path);password=key;entries=list;folder="";query="";
            if(remember){settings.Recent.RemoveAll(p=>string.Equals(p,archive,StringComparison.OrdinalIgnoreCase));settings.Recent.Insert(0,archive);settings.Recent=settings.Recent.Take(20).ToList();settings.Save();}
            SelectPage("archive");Title=System.IO.Path.GetFileName(archive)+" · New7z";
        }
        catch(OperationCanceledException){}catch(Exception ex){if(version==openVersion)Notify(ex.Message,InfoBarSeverity.Error);}
        finally{if(version==openVersion)StatusText.Text="就绪 · 本地 7-Zip 26.05 引擎";tokenSource.Dispose();if(ReferenceEquals(openCancellation,tokenSource))openCancellation=null;}
    }
    private IReadOnlyList<ArchiveEntry> CurrentRows()
    {
        var prefix=folder.Length==0?"":folder+"/";var rows=new Dictionary<string,ArchiveEntry>(StringComparer.Ordinal);
        foreach(var entry in entries){var path=entry.NormalizedPath;if(!path.StartsWith(prefix,StringComparison.Ordinal)||path==folder)continue;var relative=path[prefix.Length..];var slash=relative.IndexOf('/');if(slash>=0){var name=relative[..slash];var full=prefix+name;if(!rows.ContainsKey(full))rows[full]=new(){Index=uint.MaxValue,Path=full,IsDirectory=true};}else rows[path]=entry;}
        return rows.Values.Where(e=>e.Name.Contains(query,StringComparison.CurrentCultureIgnoreCase)).OrderByDescending(e=>e.IsDirectory).ThenBy(e=>e.Name,StringComparer.CurrentCultureIgnoreCase).ToList();
    }
    private void RefreshRows(){if(fileList==null)return;fileList.ItemsSource=CurrentRows().Select(e=>new ArchiveRow(e)).ToList();countLabel!.Text=$"{CurrentRows().Count} 个项目";}
    private void ShowArchive()
    {
        if(archive==null){var empty=Stack();empty.Children.Add(Text("打开一份压缩包",28,true));empty.Children.Add(Text("支持 7z、ZIP、RAR 等格式读取。"));empty.Children.Add(Button("打开压缩包",async(_,_)=>{var f=await PickArchiveAsync();if(f!=null)await OpenArchiveAsync(f);},true));ShowContent(empty);return;}
        var grid=new Grid{RowSpacing=16};grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});grid.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var heading=Stack(6);heading.Children.Add(Text(System.IO.Path.GetFileName(archive),26,true));var location=Text(archive,12);location.Foreground=(Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];heading.Children.Add(location);grid.Children.Add(heading);
        var actions=new Grid{ColumnSpacing=12};
        actions.ColumnDefinitions.Add(new(){Width=GridLength.Auto});actions.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});actions.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var primaryActions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};
        primaryActions.Children.Add(Button("全部解压",async(_,_)=>await ExtractDialogAsync(entries),true));primaryActions.Children.Add(Button("解压选中",async(_,_)=>{var chosen=SelectedEntries();if(chosen.Count>0)await ExtractDialogAsync(chosen);else Notify("请先选择文件或文件夹。");}));
        var canModify=ArchiveEngine.SupportsUpdate(archive);
        var addFiles=Button("添加文件",async(_,_)=>await AddToArchiveAsync());addFiles.IsEnabled=canModify;if(!canModify)ToolTipService.SetToolTip(addFiles,"当前格式只能读取；仅 7z 和 ZIP 支持修改。");primaryActions.Children.Add(addFiles);
        var deleteEntries=Button("删除",async(_,_)=>await DeleteSelectedAsync());deleteEntries.IsEnabled=canModify;if(!canModify)ToolTipService.SetToolTip(deleteEntries,"当前格式只能读取；仅 7z 和 ZIP 支持修改。");primaryActions.Children.Add(deleteEntries);
        actions.Children.Add(primaryActions);
        var secondaryActions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8,HorizontalAlignment=HorizontalAlignment.Right};
        secondaryActions.Children.Add(Button("预览",async(_,_)=>{if(fileList?.SelectedItems.Count==1)await ActivateRowAsync(((ArchiveRow)fileList.SelectedItem).Entry);else Notify("请选择一个文件以预览。");}));secondaryActions.Children.Add(Button("测试完整性",(_,_)=>{var current=archive;var pw=password;QueueJob("测试 "+System.IO.Path.GetFileName(current),current,(p,ct)=>engine.TestAsync(current,pw,p,ct));}));secondaryActions.Children.Add(Button("新建压缩",async(_,_)=>await CreateDialogAsync()));
        Grid.SetColumn(secondaryActions,2);actions.Children.Add(secondaryActions);Grid.SetRow(actions,1);grid.Children.Add(actions);
        var pathbar=new Grid{ColumnSpacing=10};pathbar.ColumnDefinitions.Add(new(){Width=GridLength.Auto});pathbar.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});pathbar.ColumnDefinitions.Add(new(){Width=new GridLength(260)});
        pathbar.Children.Add(Button("↑ 上一级",(_,_)=>{folder=folder.Contains('/')?folder[..folder.LastIndexOf('/')]:"";query="";ShowArchive();}));var pathText=Text("根目录"+(folder.Length>0?" / "+folder:""));pathText.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(pathText,1);pathbar.Children.Add(pathText);
        searchBox=new TextBox{PlaceholderText="搜索当前目录",Text=query};Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(searchBox,"搜索当前目录");searchBox.TextChanged+=(_,_)=>{query=searchBox.Text;RefreshRows();};Grid.SetColumn(searchBox,2);pathbar.Children.Add(searchBox);Grid.SetRow(pathbar,2);grid.Children.Add(pathbar);
        var table=new Grid();table.RowDefinitions.Add(new(){Height=GridLength.Auto});table.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        var header=Text("       名称                                                     大小                 压缩后               修改时间",12);header.Margin=new Thickness(12,10,12,10);header.Foreground=(Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];table.Children.Add(header);
        fileList=new ListView{SelectionMode=ListViewSelectionMode.Multiple,IsMultiSelectCheckBoxEnabled=true,HorizontalContentAlignment=HorizontalAlignment.Stretch,CanDragItems=true,AllowDrop=false};
        fileList.ItemTemplate=(DataTemplate)XamlReader.Load("""
        <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"><Grid MinHeight="48" ColumnSpacing="12" Padding="4,5"><Grid.ColumnDefinitions><ColumnDefinition Width="26"/><ColumnDefinition Width="*"/><ColumnDefinition Width="100"/><ColumnDefinition Width="100"/><ColumnDefinition Width="145"/></Grid.ColumnDefinitions><FontIcon Glyph="{Binding Glyph}" FontSize="20" Foreground="{ThemeResource AccentTextFillColorPrimaryBrush}"/><TextBlock Grid.Column="1" Text="{Binding Name}" ToolTipService.ToolTip="{Binding FullPath}" VerticalAlignment="Center" TextTrimming="CharacterEllipsis"/><TextBlock Grid.Column="2" Text="{Binding SizeText}" VerticalAlignment="Center" FontSize="12"/><TextBlock Grid.Column="3" Text="{Binding PackedSizeText}" VerticalAlignment="Center" FontSize="12"/><TextBlock Grid.Column="4" Text="{Binding ModifiedText}" VerticalAlignment="Center" FontSize="12"/></Grid></DataTemplate>
        """);
        fileList.DoubleTapped+=async(_,e)=>{
            var elements=VisualTreeHelper.FindElementsInHostCoordinates(e.GetPosition(null),fileList).ToList();
            if(elements.Any(x=>x is CheckBox))return;
            if(elements.OfType<FrameworkElement>().Select(x=>x.DataContext).OfType<ArchiveRow>().FirstOrDefault() is { } row)await ActivateRowAsync(row.Entry);
        };fileList.KeyDown+=async(_,e)=>{if(e.Key==VirtualKey.Enter&&fileList.SelectedItem is ArchiveRow row){e.Handled=true;await ActivateRowAsync(row.Entry);}else if(e.Key==VirtualKey.Delete){e.Handled=true;await DeleteSelectedAsync();}};
        fileList.DragStarting+=FileListDragStarting;fileList.DropCompleted+=(_,_)=>CleanupDragTemp();
        fileList.SelectionChanged+=(_,_)=>{countLabel!.Text=$"{CurrentRows().Count} 个项目 · 已选择 {fileList.SelectedItems.Count} 项";};Grid.SetRow(fileList,1);table.Children.Add(fileList);Grid.SetRow(table,3);grid.Children.Add(table);
        countLabel=Text("",12);Grid.SetRow(countLabel,4);grid.Children.Add(countLabel);ShowContent(grid);RefreshRows();
    }
    private List<ArchiveEntry> SelectedEntries()
    {
        var selected=fileList?.SelectedItems.Cast<ArchiveRow>().Select(r=>r.Entry).ToList()??[];
        return entries.Where(e=>selected.Any(s=>s.IsDirectory?(e.NormalizedPath==s.NormalizedPath||e.NormalizedPath.StartsWith(s.NormalizedPath+"/",StringComparison.Ordinal)):e.Index==s.Index)).ToList();
    }
    private async Task ActivateRowAsync(ArchiveEntry entry){if(entry.IsDirectory){folder=entry.NormalizedPath;query="";ShowArchive();}else await ShowPreviewAsync(entry);}
    private ContentDialog Dialog(string title,UIElement content,string primary="确定")=>new(){Title=title,Content=content,PrimaryButtonText=primary,CloseButtonText="取消",DefaultButton=ContentDialogButton.Primary,XamlRoot=Root.XamlRoot,RequestedTheme=Root.RequestedTheme};
    private async Task CreateDialogAsync(List<string>? sources=null)
    {
        sources??=[];var panel=Stack(14);var list=Text(sources.Count>0?string.Join(Environment.NewLine,sources):"请选择来源文件或文件夹",12);panel.Children.Add(list);
        var sourceActions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};sourceActions.Children.Add(Button("添加文件",async(_,_)=>{sources.AddRange(await PickFilesAsync());list.Text=string.Join(Environment.NewLine,sources);}));sourceActions.Children.Add(Button("添加文件夹",async(_,_)=>{var f=await PickFolderAsync();if(f!=null)sources.Add(f);list.Text=string.Join(Environment.NewLine,sources);}));panel.Children.Add(sourceActions);
        var output=Input("保存为",System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"归档.7z"));panel.Children.Add(output);
        panel.Children.Add(Button("选择保存文件夹",async(_,_)=>{var path=await PickFolderAsync();if(path!=null)output.Text=System.IO.Path.Combine(path,System.IO.Path.GetFileName(output.Text));}));
        var format=new ComboBox{Header="格式",ItemsSource=new[]{"7z","zip"},SelectedIndex=0,HorizontalAlignment=HorizontalAlignment.Stretch};var level=new ComboBox{Header="压缩级别",ItemsSource=new[]{"标准","更快","更小"},SelectedIndex=0,HorizontalAlignment=HorizontalAlignment.Stretch};panel.Children.Add(format);panel.Children.Add(level);
        var secret=new PasswordBox{Header="密码（可选）",PasswordRevealMode=PasswordRevealMode.Peek};var encrypt=new CheckBox{Content="加密文件名（仅 7z）",IsEnabled=false};secret.PasswordChanged+=(_,_)=>{encrypt.IsEnabled=secret.Password.Length>0&&format.SelectedIndex==0;if(!encrypt.IsEnabled)encrypt.IsChecked=false;};format.SelectionChanged+=(_,_)=>{output.Text=System.IO.Path.ChangeExtension(output.Text,format.SelectedItem.ToString());encrypt.IsEnabled=secret.Password.Length>0&&format.SelectedIndex==0;if(!encrypt.IsEnabled)encrypt.IsChecked=false;};panel.Children.Add(secret);panel.Children.Add(encrypt);
        var error=Text("",12);error.Foreground=new SolidColorBrush(Colors.Firebrick);panel.Children.Add(error);
        var dialog=Dialog("新建压缩包",new ScrollViewer{Content=panel,MaxHeight=560,MinWidth=480},"开始压缩");dialog.PrimaryButtonClick+=(_,e)=>{if(sources.Count==0||string.IsNullOrWhiteSpace(output.Text)){error.Text="请选择来源并填写输出路径。";e.Cancel=true;}else if(File.Exists(output.Text)){error.Text="输出文件已存在，请改用一个新名称。";e.Cancel=true;}};
        if(await dialog.ShowAsync()!=ContentDialogResult.Primary)return;
        var target=output.Text;var inputs=sources.ToArray();var kind=format.SelectedItem.ToString()!;var compression=level.SelectedIndex switch{1=>1,2=>9,_=>5};var key=secret.Password;var header=encrypt.IsChecked==true;
        QueueJob("压缩 "+System.IO.Path.GetFileName(target),target,(p,ct)=>engine.CreateAsync(target,inputs,kind,compression,key,header,p,ct));
    }
    private async Task ExtractDialogAsync(IReadOnlyList<ArchiveEntry> selected)
    {
        if(archive==null||selected.Count==0){Notify("没有可以解压的条目。");return;}var panel=Stack(14);panel.Children.Add(Text($"{System.IO.Path.GetFileName(archive)} · {selected.Count} 项"));var dest=Input("目标目录",System.IO.Path.Combine(System.IO.Path.GetDirectoryName(archive)!,System.IO.Path.GetFileNameWithoutExtension(archive)));panel.Children.Add(dest);panel.Children.Add(Button("选择文件夹",async(_,_)=>{var path=await PickFolderAsync();if(path!=null)dest.Text=path;}));var conflict=new ComboBox{Header="遇到同名文件",ItemsSource=new[]{"自动重命名，保留两者","跳过已有文件","替换已有文件"},SelectedIndex=0,HorizontalAlignment=HorizontalAlignment.Stretch};panel.Children.Add(conflict);var note=Text("只写入选定目录；不删除源压缩包。",12);panel.Children.Add(note);var d=Dialog("解压到",panel,"开始解压");d.PrimaryButtonClick+=(_,e)=>{if(string.IsNullOrWhiteSpace(dest.Text)){e.Cancel=true;note.Text="请输入目标目录。";}};if(await d.ShowAsync()!=ContentDialogResult.Primary)return;
        if(conflict.SelectedIndex==2){var confirm=Dialog("替换同名文件？",Text($"本次解压遇到同名文件时，将替换目标目录中的现有文件：\n{dest.Text}"),"替换并解压");confirm.DefaultButton=ContentDialogButton.Close;if(await confirm.ShowAsync()!=ContentDialogResult.Primary)return;}
        var current=archive;var key=password;
        if(selected.Any(e=>e.Encrypted)&&string.IsNullOrEmpty(key)){key=await AskPasswordAsync();if(key==null)return;password=key;}
        var target=dest.Text;var policy=(ConflictPolicy)conflict.SelectedIndex;
        QueueJob("解压 "+System.IO.Path.GetFileName(current),target,async(p,ct)=>{var r=await engine.ExtractAsync(current,selected,target,policy,key,p,ct);DispatcherQueue.TryEnqueue(()=>Notify($"已解压 {r.Extracted} 个文件，跳过 {r.Skipped} 个。",InfoBarSeverity.Success));});
    }
    private async Task AddToArchiveAsync()
    {
        if(archive==null)return;
        if(!ArchiveEngine.SupportsUpdate(archive)){Notify("当前只支持修改 7z 和 ZIP 压缩包。",InfoBarSeverity.Warning);return;}
        var sources=new List<string>();
        var panel=Stack(14);
        panel.Children.Add(Text($"添加到 {System.IO.Path.GetFileName(archive)} · {(folder.Length==0?"根目录":folder)}",12));
        var summary=Text("还没有选择文件。",12);summary.TextWrapping=TextWrapping.Wrap;panel.Children.Add(summary);
        var rows=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};
        rows.Children.Add(Button("添加文件",async(_,_)=>{var picked=await PickFilesAsync();if(picked.Count>0){sources.AddRange(picked);summary.Text=string.Join(Environment.NewLine,sources);}}));
        rows.Children.Add(Button("添加文件夹",async(_,_)=>{var picked=await PickFolderAsync();if(picked!=null){sources.Add(picked);summary.Text=string.Join(Environment.NewLine,sources);}}));
        panel.Children.Add(rows);
        var error=Text("",12);error.Foreground=new SolidColorBrush(Colors.Firebrick);panel.Children.Add(error);
        var dialog=Dialog("向压缩包添加文件",new ScrollViewer{Content=panel,MaxHeight=520,MinWidth=480},"添加");
        dialog.PrimaryButtonClick+=(_,e)=>{if(sources.Count==0){error.Text="请先选择文件或文件夹。";e.Cancel=true;}};
        if(await dialog.ShowAsync()!=ContentDialogResult.Primary)return;
        await AddSourcesAsync(sources);
    }
    private async Task AddSourcesAsync(IReadOnlyList<string> sources)
    {
        if(archive==null||sources.Count==0)return;
        var current=archive;
        if(!ArchiveEngine.SupportsUpdate(current)){Notify("当前只支持修改 7z 和 ZIP 压缩包。",InfoBarSeverity.Warning);return;}
        IReadOnlyList<SourceItem> additions;
        try{additions=ArchiveEngine.ExpandSources(sources,folder);}catch(Exception ex){Notify(ex.Message,InfoBarSeverity.Error);return;}
        if(additions.Count==0){Notify("没有可以添加的文件。");return;}
        var existing=new Dictionary<string,ArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach(var entry in entries)existing.TryAdd(entry.NormalizedPath,entry);
        var conflicts=additions.Select(a=>a.Name).Where(existing.ContainsKey).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if(conflicts.Count>0)
        {
            var preview=string.Join(Environment.NewLine,conflicts.Take(10));
            var more=conflicts.Count>10?$"\n…共 {conflicts.Count} 项":"";
            var confirm=Dialog("替换压缩包内的同名条目？",Text($"压缩包中已存在以下同名条目，继续将用新文件替换：\n\n{preview}{more}"),"替换并添加");
            confirm.DefaultButton=ContentDialogButton.Close;
            if(await confirm.ShowAsync()!=ContentDialogResult.Primary)return;
        }
        var key=await EnsurePasswordAsync();
        if(key is null)return;
        var approved=new HashSet<string>(conflicts,StringComparer.OrdinalIgnoreCase);
        var additionsSnapshot=additions.ToArray();
        QueueJob("添加文件 "+System.IO.Path.GetFileName(current),current,async(p,ct)=>
        {
            // Re-read the archive so removal indexes match the current file, not the earlier listing.
            var fresh=await engine.ListAsync(current,key,ct);
            var freshByPath=new Dictionary<string,ArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            foreach(var entry in fresh)freshByPath.TryAdd(entry.NormalizedPath,entry);
            var currentConflicts=additionsSnapshot.Select(a=>a.Name).Where(freshByPath.ContainsKey).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if(currentConflicts.Exists(name=>!approved.Contains(name)))throw new IOException("压缩包内容已在外部发生变化，请重新添加。");
            var remove=new List<uint>();
            foreach(var name in currentConflicts)
            {
                var entry=freshByPath[name];var prefix=entry.NormalizedPath+"/";
                foreach(var item in fresh)
                    if((item.NormalizedPath==entry.NormalizedPath||item.NormalizedPath.StartsWith(prefix,StringComparison.Ordinal))&&!remove.Contains(item.Index))remove.Add(item.Index);
            }
            await engine.ModifyAsync(current,remove,additionsSnapshot,key,p,ct);
        },false,ReloadArchiveAsync);
    }
    private async Task DeleteSelectedAsync()
    {
        if(archive==null)return;
        if(!ArchiveEngine.SupportsUpdate(archive)){Notify("当前只支持修改 7z 和 ZIP 压缩包。",InfoBarSeverity.Warning);return;}
        var selected=SelectedEntries().Where(e=>e.Index!=uint.MaxValue).ToList();
        if(selected.Count==0){Notify("请先选择要删除的文件或文件夹。");return;}
        var preview=string.Join(Environment.NewLine,selected.Take(10).Select(e=>e.NormalizedPath));
        var more=selected.Count>10?$"\n…共 {selected.Count} 项":"";
        var confirm=Dialog("删除压缩包内的条目？",Text($"将从 {System.IO.Path.GetFileName(archive)} 删除 {selected.Count} 项：\n\n{preview}{more}\n\n压缩包会被重新生成，删除的内容不会进入回收站，也无法撤销。"),"删除");
        confirm.DefaultButton=ContentDialogButton.Close;
        if(await confirm.ShowAsync()!=ContentDialogResult.Primary)return;
        var key=await EnsurePasswordAsync();
        if(key is null)return;
        var current=archive;var paths=selected.Select(e=>e.NormalizedPath).Distinct(StringComparer.Ordinal).ToArray();
        QueueJob("删除条目 "+System.IO.Path.GetFileName(current),current,async(p,ct)=>
        {
            // Resolve the selected paths against a fresh listing so a changed archive is never
            // rewritten with stale entry indexes.
            var fresh=await engine.ListAsync(current,key,ct);
            var lookup=new Dictionary<string,uint>(StringComparer.Ordinal);
            foreach(var entry in fresh)lookup.TryAdd(entry.NormalizedPath,entry.Index);
            var indices=new List<uint>();
            foreach(var path in paths)
            {
                if(!lookup.TryGetValue(path,out var index))throw new IOException("压缩包内容已在外部发生变化，请重新打开后再删除。");
                indices.Add(index);
            }
            await engine.ModifyAsync(current,indices,[],key,p,ct);
        },false,ReloadArchiveAsync);
    }
    // Rewriting an archive reads every kept entry, so an encrypted archive needs the password up front.
    private async Task<string?> EnsurePasswordAsync()
    {
        if(entries.Any(e=>e.Encrypted)&&string.IsNullOrEmpty(password)){var key=await AskPasswordAsync();if(key==null)return null;password=key;}
        return password;
    }
    private async Task ReloadArchiveAsync()
    {
        if(archive==null)return;
        try
        {
            entries=await engine.ListAsync(archive,password);
            if(folder.Length>0&&!entries.Any(e=>e.NormalizedPath.StartsWith(folder+"/",StringComparison.Ordinal)))folder="";
            if(page=="archive")ShowArchive();
        }
        catch(ArchivePasswordException){Notify("压缩包现在需要密码，请重新打开。",InfoBarSeverity.Warning);}
        catch(Exception ex){Notify(ex.Message,InfoBarSeverity.Error);}
    }
    // DragStarting (unlike DragItemsStarting) supports a deferral, so the entries can be
    // extracted to a temporary folder before Explorer receives real files to copy.
    private async void FileListDragStarting(UIElement sender,DragStartingEventArgs e)
    {
        var deferral=e.GetDeferral();string? temp=null;
        try
        {
            if(archive==null||page!="archive"||previewOpen){e.Cancel=true;return;}
            var rows=fileList?.SelectedItems.OfType<ArchiveRow>().Select(r=>r.Entry).ToList()??[];
            if(rows.Count==0&&fileList!=null)
            {
                // Dragging an unselected row should still drag that row, like Explorer.
                var element=VisualTreeHelper.FindElementsInHostCoordinates(e.GetPosition(null),fileList).OfType<FrameworkElement>().Select(x=>x.DataContext).OfType<ArchiveRow>().FirstOrDefault();
                if(element!=null){fileList.SelectedItem=element;rows=[element.Entry];}
            }
            var selected=SelectedEntries();
            if(rows.Count==0||selected.Count==0){e.Cancel=true;return;}
            var current=archive;
            var key=await EnsurePasswordAsync();
            if(key is null){e.Cancel=true;return;}
            StatusText.Text=rows.Count==1?"正在准备拖动 "+rows[0].Name+" …":$"正在准备拖动 {rows.Count} 项 …";
            temp=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"New7z-drag-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            await engine.ExtractAsync(current,selected,temp,ConflictPolicy.Rename,key,null,CancellationToken.None);
            var storage=new List<IStorageItem>();
            foreach(var row in rows)
            {
                var full=System.IO.Path.Combine(temp,row.NormalizedPath.Replace('/',System.IO.Path.DirectorySeparatorChar));
                if(Directory.Exists(full))storage.Add(await StorageFolder.GetFolderFromPathAsync(full));
                else if(File.Exists(full))storage.Add(await StorageFile.GetFileFromPathAsync(full));
            }
            if(storage.Count==0){e.Cancel=true;return;}
            if(dragTemp!=null)ScheduleTempCleanup(dragTemp);
            dragTemp=temp;temp=null;
            e.AllowedOperations=DataPackageOperation.Copy;
            e.Data.RequestedOperation=DataPackageOperation.Copy;
            e.Data.SetData(InternalDragFormat,"1");
            e.Data.SetStorageItems(storage,false);
            e.Data.Properties.Title=System.IO.Path.GetFileName(current);
        }
        catch(Exception ex){e.Cancel=true;Notify(ex.Message,InfoBarSeverity.Error);}
        finally
        {
            if(temp!=null){try{Directory.Delete(temp,true);}catch{}}
            StatusText.Text="就绪";
            deferral.Complete();
        }
    }
    private void CleanupDragTemp()
    {
        var path=dragTemp;dragTemp=null;if(path==null)return;
        ScheduleTempCleanup(path);
    }
    private static void ScheduleTempCleanup(string path)
    {
        // Explorer may finish copying after the drop returns; retry later and keep files on sharing violations.
        _=Task.Run(async()=>{
            for(var attempt=0;attempt<3;attempt++){await Task.Delay(TimeSpan.FromMinutes(2));try{if(!Directory.Exists(path))return;Directory.Delete(path,true);return;}catch{}}
        });
    }
    private void QueueJob(string name,string destination,Func<IProgress<EngineProgress>,CancellationToken,Task> operation,bool openTasks=true,Func<Task>? completed=null)
    {
        var job=new ArchiveJob(name,destination);jobs.Insert(0,job);if(openTasks)SelectPage("tasks");
        _=Run();async Task Run(){bool acquired=false;try{await queue.WaitAsync(job.Cancellation.Token);acquired=true;job.Status="正在处理";var last=Stopwatch.StartNew();var progress=new Progress<EngineProgress>(p=>{if(last.ElapsedMilliseconds<100)return;last.Restart();if(p.Total>0)job.Percent=Math.Clamp(p.Completed*100.0/p.Total,0,100);});await operation(progress,job.Cancellation.Token);if(completed!=null){try{await completed();}catch(Exception ex){Notify(ex.Message,InfoBarSeverity.Error);}}job.Percent=100;job.Status="已完成";Notify(name+" 已完成。",InfoBarSeverity.Success);}catch(OperationCanceledException){job.Status="已取消";}catch(Exception ex){job.Status="失败："+ex.Message;Notify(ex.Message,InfoBarSeverity.Error);}finally{if(acquired)queue.Release();StatusText.Text=jobs.Any(j=>j.CanCancel)?"有任务正在处理或排队":"就绪";}}
    }
    private void ShowTasks()
    {
        var content=Stack(18);content.Children.Add(Text("任务中心",28,true));content.Children.Add(Text("任务在后台排队运行，切换页面不会中断。"));
        if(jobs.Count==0)content.Children.Add(Card(Text("还没有任务。新建压缩或解压后，可在这里查看进度。")));
        foreach(var job in jobs){var card=Stack(10);card.DataContext=job;card.Children.Add(Text(job.Title,16,true));card.Children.Add(Text(job.Destination,12));var status=Text("");status.SetBinding(TextBlock.TextProperty,new Microsoft.UI.Xaml.Data.Binding{Path=new PropertyPath("Status")});card.Children.Add(status);var bar=new ProgressBar{Minimum=0,Maximum=100};bar.SetBinding(ProgressBar.ValueProperty,new Microsoft.UI.Xaml.Data.Binding{Path=new PropertyPath("Percent")});card.Children.Add(bar);var actions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};var cancel=Button("取消任务",async(_,_)=>{var d=Dialog("取消任务？",Text("已完成的文件将保留，当前未完成的临时文件会清理。"),"取消任务");d.DefaultButton=ContentDialogButton.Close;if(await d.ShowAsync()==ContentDialogResult.Primary)job.Cancellation.Cancel();});cancel.SetBinding(Control.IsEnabledProperty,new Microsoft.UI.Xaml.Data.Binding{Path=new PropertyPath("CanCancel")});actions.Children.Add(cancel);actions.Children.Add(Button("打开文件夹",(_,_)=>{var path=Directory.Exists(job.Destination)?job.Destination:System.IO.Path.GetDirectoryName(job.Destination);if(path!=null&&Directory.Exists(path)){var info=new ProcessStartInfo("explorer.exe"){UseShellExecute=false};info.ArgumentList.Add(path);Process.Start(info);}}));card.Children.Add(actions);content.Children.Add(Card(card));}
        ShowContent(new ScrollViewer{Content=content});
    }
    private void ApplyTheme(){Root.RequestedTheme=settings.Theme switch{"浅色"=>ElementTheme.Light,"深色"=>ElementTheme.Dark,_=>ElementTheme.Default};}
    private void ApplyIcon(){try{var path=settings.IconPath??System.IO.Path.Combine(AppContext.BaseDirectory,"Assets/logo.png");BrandImage.Source=new BitmapImage(new Uri(path));}catch{settings.IconPath=null;}}
    private void ShowSettings()
    {
        var panel=Stack(22);panel.Children.Add(Text("设置",28,true));AddExplorerSettings(panel);AddUpdateSettings(panel);var theme=new ComboBox{Header="应用主题",ItemsSource=new[]{"系统","浅色","深色"},SelectedItem=settings.Theme,MinWidth=240};theme.SelectionChanged+=(_,_)=>{settings.Theme=theme.SelectedItem.ToString()!;settings.Save();ApplyTheme();};panel.Children.Add(Card(theme));
        var icon=Stack(12);icon.Children.Add(Text("应用内图标",18,true));icon.Children.Add(Text("替换应用内品牌图标。不会修改 EXE、任务栏或系统文件关联。",12));var preview=new Image{Width=96,Height=96,HorizontalAlignment=HorizontalAlignment.Left,Source=BrandImage.Source};icon.Children.Add(preview);icon.Children.Add(Button("替换 PNG 图标",async(_,_)=>{try{var picker=new FileOpenPicker();InitializeWithWindow.Initialize(picker,Hwnd);picker.FileTypeFilter.Add(".png");var file=await picker.PickSingleFileAsync();if(file==null)return;var props=await file.GetBasicPropertiesAsync();if(props.Size>10*1024*1024)throw new IOException("图片不能超过 10 MiB。");using var stream=await file.OpenReadAsync();var decoder=await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);if(decoder.PixelWidth>4096||decoder.PixelHeight>4096)throw new IOException("图片尺寸不能超过 4096 × 4096。");Directory.CreateDirectory(UserSettings.Folder);var target=System.IO.Path.Combine(UserSettings.Folder,"custom-icon-"+Guid.NewGuid().ToString("N")+".png");File.Copy(file.Path,target);var previous=settings.IconPath;settings.IconPath=target;settings.Save();ApplyIcon();preview.Source=BrandImage.Source;if(previous!=null&&previous.StartsWith(UserSettings.Folder,StringComparison.OrdinalIgnoreCase)&&File.Exists(previous))File.Delete(previous);}catch(Exception ex){Notify(ex.Message,InfoBarSeverity.Error);}}));icon.Children.Add(Button("恢复默认",(_,_)=>{settings.IconPath=null;settings.Save();ApplyIcon();preview.Source=BrandImage.Source;}));panel.Children.Add(Card(icon));
        var about=Stack(8);about.Children.Add(Text("New7z · "+typeof(App).Assembly.GetName().Version?.ToString(3),18,true));about.Children.Add(Text("作者：王国梁 · MIT 开源许可",14));about.Children.Add(Text("本地 7-Zip 26.05 完整格式引擎 · 不依赖系统安装 7-Zip\n支持 7z/ZIP 创建，其他格式读取按引擎能力处理。RAR 仅解压。\n预览为只读；首版图片最大 32 MiB，文本最大 2 MiB。",12));panel.Children.Add(Card(about));ShowContent(new ScrollViewer{Content=panel});
    }
    private bool closingApproved;
    private async void OnClosing(AppWindow sender,AppWindowClosingEventArgs args)
    {
        if(closingApproved||!jobs.Any(j=>j.CanCancel))return;args.Cancel=true;
        try{var d=Dialog("任务尚未完成",Text("取消任务后退出？已完成输出会保留。"),"取消任务并退出");d.DefaultButton=ContentDialogButton.Close;if(await d.ShowAsync()!=ContentDialogResult.Primary)return;foreach(var job in jobs.Where(j=>j.CanCancel))job.Cancellation.Cancel();while(jobs.Any(j=>j.CanCancel))await Task.Delay(100);closingApproved=true;Close();}catch{args.Cancel=true;}
    }
}

