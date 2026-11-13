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
using static New7z.Core.Loc;

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
    private string? settingsCategory;
    private ListView? fileList;
    private TextBlock? countLabel;
    private TextBox? searchBox;
    private bool initialized;
    private CancellationTokenSource? openCancellation;
    private long openVersion;
    private ArchiveEngine.ArchiveDragData? dragData;
    private string? dragTemp;
    private ArchiveJob? dragJob;
    private StackPanel? sharedTaskHost;
    private Border? tasksEmptyCard;
    private DispatcherTimer? taskRefreshTimer;
    private readonly List<(ArchiveJob Job,Action<ArchiveJob> Handler)> taskSubscriptions=[];
    private readonly List<string> startupFiles=[];
    private nint Hwnd=>WindowNative.GetWindowHandle(this);
    private static SolidColorBrush Secondary=>new(Colors.Gray);
    private static readonly string[] ThemeCodes=["system","light","dark"];
    private static readonly string[] LanguageCodes=["system","zh","en"];
    private static readonly string[] FormatNames=["7z","zip","rar","tar","gz","bz2","xz","iso"];
    public MainWindow(string? startupArchive=null)
    {
        Loc.Language=Loc.Resolve(settings.Language);
        InitializeComponent();Title="New7z";ExtendsContentIntoTitleBar=true;SetTitleBar(TitleBar);
        SystemBackdrop=new MicaBackdrop();AppWindow.Resize(new Windows.Graphics.SizeInt32(1320,880));
        AppWindow.Closing+=OnClosing;
        initialized=true;ApplyLanguage();SaveExplorerLanguage();ApplyTheme();ApplyIcon();WindowAppearance.ApplyIcon(this);ShowHome();
        Root.AllowDrop=true;Root.DragOver+=(_,e)=>UpdateDragOver(e);Root.Drop+=FilesDropped;
        foreach(var stale in Directory.EnumerateDirectories(System.IO.Path.GetTempPath(),"New7z-drag-*"))
        {
            try{if(Directory.GetLastWriteTimeUtc(stale)<DateTime.UtcNow.AddDays(-2))Directory.Delete(stale,true);}catch{}
        }
        Root.Loaded+=async(_,_)=>{
            // The built-in settings item only exists once the NavigationView has loaded.
            ApplyLanguage();
            var scale=Root.XamlRoot.RasterizationScale;var area=DisplayArea.GetFromWindowId(AppWindow.Id,DisplayAreaFallback.Primary).WorkArea;
            var width=Math.Min((int)(1240*scale),area.Width-60);var height=Math.Min((int)(820*scale),area.Height-60);
            AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(area.X+(area.Width-width)/2,area.Y+(area.Height-height)/2,width,height));
            var launchArgs=Environment.GetCommandLineArgs().Skip(1).ToArray();
            if(launchArgs.Length==3&&launchArgs[0]=="--verify-ui"){await VerifyUiAsync(launchArgs[1],launchArgs[2]);return;}
            if(launchArgs.Length==3&&launchArgs[0]=="--verify-drag-ui"){await PrepareDragVerificationAsync(launchArgs[1],launchArgs[2]);return;}
            if(launchArgs.Length==3&&launchArgs[0]=="--verify-drag-text"){await PrepareDragVerificationAsync(launchArgs[1],launchArgs[2],true);return;}
            ShowPendingIntegrationError();
            var arg=startupArchive??launchArgs.FirstOrDefault(File.Exists);if(arg!=null)await OpenArchiveAsync(arg);
            if(settings.AutoCheckUpdates&&!AppEnvironment.IsPackaged)_=CheckForUpdatesAsync(false);
        };
    }
    private void ApplyLanguage()
    {
        Loc.Language=Loc.Resolve(settings.Language);
        foreach(var item in Navigation.MenuItems.OfType<NavigationViewItem>())
            item.Content=item.Tag?.ToString() switch
            {
                "home"=>T("主页","Home"),
                "archive"=>T("压缩包","Archive"),
                "tasks"=>T("任务中心","Tasks"),
                _=>item.Content
            };
        if(Navigation.SettingsItem is NavigationViewItem settingsItem)settingsItem.Content=T("设置","Settings");
        StatusText.Text=T("就绪 · 本地 7-Zip 26.05 引擎","Ready · local 7-Zip 26.05 engine");
    }
    private void SaveExplorerLanguage()=>ExplorerPreferencesStore.SetLanguage(Loc.Language);
    private string FormatIconPath(string name)
    {
        name=name.TrimStart('.').ToLowerInvariant();
        if(settings.FormatIcons!=null&&settings.FormatIcons.TryGetValue(name,out var custom)&&File.Exists(custom))return custom;
        return System.IO.Path.Combine(AppContext.BaseDirectory,"Assets","Formats",name+".png");
    }
    private Image? FormatIcon(string path)
    {
        var name=System.IO.Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        var file=FormatIconPath(name);
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
    private void ShowContent(UIElement content)
    {
        searchTimer?.Stop();
        taskRefreshTimer?.Stop();taskRefreshTimer=null;sharedTaskHost=null;tasksEmptyCard=null;
        sharedTaskViews.Clear();
        foreach(var (job,handler) in taskSubscriptions)job.Updated-=handler;
        taskSubscriptions.Clear();ContentHost.Children.Clear();ContentHost.Children.Add(content);
    }
    private void Notify(string text,InfoBarSeverity severity=InfoBarSeverity.Informational){Message.Message=text;Message.Severity=severity;Message.ActionButton=null;Message.IsOpen=true;}
    private void SelectPage(string name)
    {
        if(page!=name)settingsCategory=null;
        var item=name=="settings"?Navigation.SettingsItem:Navigation.MenuItems.Cast<NavigationViewItem>().First(i=>i.Tag?.ToString()==name);
        if(ReferenceEquals(Navigation.SelectedItem,item)){page=name;RenderPage();}
        else Navigation.SelectedItem=item;
    }
    private void NavigationChanged(NavigationView sender,NavigationViewSelectionChangedEventArgs args)
    {
        if(!initialized)return;
        var next=args.IsSettingsSelected?"settings":(args.SelectedItem as NavigationViewItem)?.Tag?.ToString()??"home";
        if(next!=page)settingsCategory=null;
        page=next;RenderPage();
    }
    private void RenderPage(){if(page=="archive")ShowArchive();else if(page=="tasks")ShowTasks();else if(page=="settings")ShowSettings();else ShowHome();}
    private void ShowHome()
    {
        var content=Stack(24);content.Children.Add(Text(T("主页","Home"),28,true));content.Children.Add(Text(T("打开压缩包，或将文件打包分享。","Open an archive, or pack files to share.")));
        var actions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=10};actions.Children.Add(Button(T("打开压缩包","Open archive"),async(_,_)=>{var f=await PickArchiveAsync();if(f!=null)await OpenArchiveAsync(f);}));actions.Children.Add(Button(T("新建压缩","New archive"),async(_,_)=>await CreateDialogAsync(),true));content.Children.Add(actions);
        var drop=Stack(10);drop.Children.Add(Text(T("把文件或文件夹拖到窗口","Drop files or folders here"),22,true));drop.Children.Add(Text(T("压缩包可查看内容，普通文件可打包为 7z、ZIP 或 TAR。无需另装 7-Zip。","Open an archive to browse it, or pack files into 7z, ZIP or TAR. No separate 7-Zip needed.")));var pick=new StackPanel{Orientation=Orientation.Horizontal,Spacing=10};pick.Children.Add(Button(T("选择文件","Choose files"),async(_,_)=>{var paths=await PickFilesAsync();if(paths.Count>0)await CreateDialogAsync(paths);}));pick.Children.Add(Button(T("选择文件夹","Choose folder"),async(_,_)=>{var path=await PickFolderAsync();if(path!=null)await CreateDialogAsync([path]);}));drop.Children.Add(pick);content.Children.Add(Card(drop));
        var recent=Stack(8);recent.Children.Add(Text(T("最近使用","Recent"),18,true));
        var unavailable=settings.Recent.Count(p=>!File.Exists(p));
        foreach(var path in settings.Recent.Take(12).ToList())
        {
            var exists=File.Exists(path);
            var row=new Grid{ColumnSpacing=12};row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            var icon=FormatIcon(path);if(icon!=null){icon.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(icon);}
            var labels=Stack(3);labels.Children.Add(Text(System.IO.Path.GetFileName(path),14,true));
            var location=Text(exists?path:path+T("（文件不存在）"," (file not found)"),12);location.Foreground=(Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];labels.Children.Add(location);Grid.SetColumn(labels,1);row.Children.Add(labels);
            var open=Button(T("打开","Open"),async(_,_)=>await OpenArchiveAsync(path));open.IsEnabled=exists;Grid.SetColumn(open,2);row.Children.Add(open);
            var remove=new Button{Content=T("移除","Remove"),Padding=new Thickness(10,4,10,4),VerticalAlignment=VerticalAlignment.Center};ToolTipService.SetToolTip(remove,T("从最近使用中移除这一条","Remove this item from Recent"));remove.Click+=(_,_)=>{settings.Recent.Remove(path);settings.Save();ShowHome();};Grid.SetColumn(remove,3);row.Children.Add(remove);
            recent.Children.Add(Card(row));
        }
        if(settings.Recent.Count==0)recent.Children.Add(Text(T("还没有最近记录。打开一份压缩包开始。","No recent items yet. Open an archive to start.")));
        else
        {
            var actionsRow=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};
            if(unavailable>0)actionsRow.Children.Add(Button(T($"清理失效记录（{unavailable}）",$"Remove unavailable ({unavailable})"),(_,_)=>{settings.Recent.RemoveAll(p=>!File.Exists(p));settings.Save();ShowHome();}));
            actionsRow.Children.Add(Button(T("清空最近记录","Clear recent"),async(_,_)=>{var d=Dialog(T("清空最近记录？","Clear recent items?"),Text(T("仅移除应用内记录，不删除文件。","This only clears the in-app list; files are not deleted.")),T("清空记录","Clear"));if(await ShowDialogAsync(d)==ContentDialogResult.Primary){settings.Recent.Clear();settings.Save();ShowHome();}}));
            recent.Children.Add(actionsRow);
        }
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
        if(e.Handled)return;
        if(dragData!=null){e.Handled=true;return;}
        if(!e.DataView.Contains(StandardDataFormats.StorageItems))return;e.Handled=true;var deferral=e.GetDeferral();
        try{var items=await e.DataView.GetStorageItemsAsync();var paths=items.Select(i=>i.Path).ToList();if(paths.Count==0)return;if(page=="archive"&&archive!=null){await AddSourcesAsync(paths);return;}if(paths.Count==1&&File.Exists(paths[0])&&new[]{".7z",".zip",".rar",".tar",".gz",".xz",".bz2",".iso",".zst",".zstd",".tzst",".lz4"}.Contains(System.IO.Path.GetExtension(paths[0]).ToLowerInvariant()))await OpenArchiveAsync(paths[0]);else if(paths.Count>0)await CreateDialogAsync(paths);}catch(Exception ex){Notify(ex.Message,InfoBarSeverity.Error);}finally{deferral.Complete();}
    }
    private async Task<string?> AskPasswordAsync()
    {
        var box=new PasswordBox{Header=T("压缩包密码","Archive password"),PasswordRevealMode=PasswordRevealMode.Peek};var d=Dialog(T("输入密码","Enter password"),box,T("打开","Open"));return await ShowDialogAsync(d)==ContentDialogResult.Primary?box.Password:null;
    }
    private async Task OpenArchiveAsync(string path,bool remember=true)
    {
        openCancellation?.Cancel();var version=++openVersion;var tokenSource=new CancellationTokenSource();openCancellation=tokenSource;var token=tokenSource.Token;
        StatusText.Text=T("正在读取压缩包…","Reading archive…");Message.IsOpen=false;string? key=null;
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
        finally{if(version==openVersion)StatusText.Text=T("就绪 · 本地 7-Zip 26.05 引擎","Ready · local 7-Zip 26.05 engine");tokenSource.Dispose();if(ReferenceEquals(openCancellation,tokenSource))openCancellation=null;}
    }
    private IReadOnlyList<ArchiveEntry> CurrentRows()
    {
        if(ReferenceEquals(cachedEntries,entries)&&cachedFolder==folder)return ArchiveSorting.Sort(cachedFolderRows.Where(e=>e.Name.Contains(query,StringComparison.CurrentCultureIgnoreCase)),sortColumn,sortDescending);
        var prefix=folder.Length==0?"":folder+"/";var rows=new Dictionary<string,ArchiveEntry>(StringComparer.Ordinal);
        foreach(var entry in entries){var path=entry.NormalizedPath;if(!path.StartsWith(prefix,StringComparison.Ordinal)||path==folder)continue;var relative=path[prefix.Length..];var slash=relative.IndexOf('/');if(slash>=0){var name=relative[..slash];var full=prefix+name;if(!rows.ContainsKey(full))rows[full]=new(){Index=uint.MaxValue,Path=full,IsDirectory=true};}else rows[path]=entry;}
        cachedEntries=entries;cachedFolder=folder;cachedFolderRows=rows.Values.ToArray();
        return ArchiveSorting.Sort(cachedFolderRows.Where(e=>e.Name.Contains(query,StringComparison.CurrentCultureIgnoreCase)),sortColumn,sortDescending);
    }
    private void RefreshRows(){if(fileList==null)return;var selected=fileList.SelectedItems.OfType<ArchiveRow>().Select(r=>(r.Entry.Index,r.FullPath)).ToHashSet();var rows=CurrentRows().Select(e=>new ArchiveRow(e)).ToArray();visibleRowCount=rows.Length;updatingRows=true;try{fileList.ItemsSource=rows;foreach(var row in rows)if(selected.Contains((row.Entry.Index,row.FullPath)))fileList.SelectedItems.Add(row);}finally{updatingRows=false;}UpdateSelectionDetails();}
    private void ShowArchive()
    {
        if(archive==null){var empty=Stack();empty.Children.Add(Text(T("打开一份压缩包","Open an archive"),28,true));empty.Children.Add(Text(T("支持 7z、ZIP、RAR 等格式读取。","Reads 7z, ZIP, RAR and more.")));empty.Children.Add(Button(T("打开压缩包","Open archive"),async(_,_)=>{var f=await PickArchiveAsync();if(f!=null)await OpenArchiveAsync(f);},true));ShowContent(empty);return;}
        var grid=new Grid{RowSpacing=16};grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});grid.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var heading=Stack(6);heading.Children.Add(Text(System.IO.Path.GetFileName(archive),26,true));var location=Text(archive,12);location.Foreground=(Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];heading.Children.Add(location);grid.Children.Add(heading);
        var actions=new Grid{ColumnSpacing=12};
        actions.ColumnDefinitions.Add(new(){Width=GridLength.Auto});actions.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});actions.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var primaryActions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};
        primaryActions.Children.Add(Button(T("全部解压","Extract all"),async(_,_)=>await ExtractDialogAsync(entries),true));primaryActions.Children.Add(Button(T("解压选中","Extract selected"),async(_,_)=>{var chosen=SelectedEntries();if(chosen.Count>0)await ExtractDialogAsync(chosen);else Notify(T("请先选择文件或文件夹。","Select files or folders first."));}));
        var canModify=ArchiveEngine.SupportsUpdate(archive);
        var addFiles=Button(T("添加文件","Add files"),async(_,_)=>await AddToArchiveAsync());addFiles.IsEnabled=canModify;if(!canModify)ToolTipService.SetToolTip(addFiles,T("当前格式只能读取；仅 7z 和 ZIP 支持修改。","This format is read-only; only 7z and ZIP can be modified."));primaryActions.Children.Add(addFiles);
        var deleteEntries=Button(T("删除","Delete"),async(_,_)=>await DeleteSelectedAsync());deleteEntries.IsEnabled=canModify;if(!canModify)ToolTipService.SetToolTip(deleteEntries,T("当前格式只能读取；仅 7z 和 ZIP 支持修改。","This format is read-only; only 7z and ZIP can be modified."));primaryActions.Children.Add(deleteEntries);
        actions.Children.Add(primaryActions);
        var secondaryActions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8,HorizontalAlignment=HorizontalAlignment.Right};
        secondaryActions.Children.Add(Button(T("预览","Preview"),async(_,_)=>{if(fileList?.SelectedItems.Count==1)await ActivateRowAsync(((ArchiveRow)fileList.SelectedItem).Entry);else Notify(T("请选择一个文件以预览。","Select one file to preview."));}));secondaryActions.Children.Add(Button(T("测试完整性","Test archive"),(_,_)=>{var current=archive;var pw=password;QueueJob(T("测试 ","Test ")+System.IO.Path.GetFileName(current),current,(p,ct)=>engine.TestAsync(current,pw,p,ct));}));secondaryActions.Children.Add(Button(T("新建压缩","New archive"),async(_,_)=>await CreateDialogAsync()));
        Grid.SetColumn(secondaryActions,2);actions.Children.Add(secondaryActions);Grid.SetRow(actions,1);grid.Children.Add(actions);
        var pathbar=new Grid{ColumnSpacing=10};pathbar.ColumnDefinitions.Add(new(){Width=GridLength.Auto});pathbar.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});pathbar.ColumnDefinitions.Add(new(){Width=GridLength.Auto});pathbar.ColumnDefinitions.Add(new(){Width=new GridLength(260)});
        pathbar.Children.Add(Button(T("↑ 上一级","↑ Up"),(_,_)=>SetFolder(folder.Contains('/')?folder[..folder.LastIndexOf('/')]:"")));folderLabel=Text(T("根目录","Root")+(folder.Length>0?" / "+folder:""));folderLabel.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(folderLabel,1);pathbar.Children.Add(folderLabel);
        searchBox=new TextBox{PlaceholderText=T("搜索当前目录","Search this folder"),Text=query};Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(searchBox,T("搜索当前目录","Search this folder"));searchBox.TextCompositionStarted+=(_,_)=>{searchComposing=true;searchTimer?.Stop();};searchBox.TextCompositionEnded+=(_,_)=>{searchComposing=false;ScheduleSearch();};searchBox.TextChanged+=(_,_)=>ScheduleSearch();Grid.SetColumn(searchBox,3);pathbar.Children.Add(searchBox);
        var selectionActions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};
        var selectAll=Button(T("全选","Select all"),(_,_)=>SelectAllRows());ToolTipService.SetToolTip(selectAll,T("选中当前目录的所有条目（Ctrl+A）","Select every item in this folder (Ctrl+A)"));selectionActions.Children.Add(selectAll);
        Grid.SetColumn(selectionActions,2);pathbar.Children.Add(selectionActions);Grid.SetRow(pathbar,2);grid.Children.Add(pathbar);
        var table=new Grid{MinWidth=680};table.RowDefinitions.Add(new(){Height=GridLength.Auto});table.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        // Column-for-column copy of the row template, with a leading spacer for the
        // selection checkbox column that the ListView item container renders.
        var header=new Grid{ColumnSpacing=12,Margin=new Thickness(16,10,16,10)};
        foreach(var width in new GridLength[]{new(21),new(26),new(1,GridUnitType.Star),new(100),new(100),new(145)})header.ColumnDefinitions.Add(new(){Width=width});
        sortHeaders.Clear();
        void AddHeader(string text,int column,ArchiveSortColumn key)
        {
            var label=new Button{Content=text,FontSize=12,Padding=new Thickness(0,4,0,4),HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Left,Background=new SolidColorBrush(Colors.Transparent),BorderThickness=new Thickness(0)};
            label.Click+=(_,_)=>{if(sortColumn==key)sortDescending=!sortDescending;else{sortColumn=key;sortDescending=false;}UpdateSortHeaders();RefreshRows();};sortHeaders[key]=label;
            Grid.SetColumn(label,column);header.Children.Add(label);
        }
        AddHeader(T("名称","Name"),2,ArchiveSortColumn.Name);AddHeader(T("大小","Size"),3,ArchiveSortColumn.Size);AddHeader(T("压缩后","Packed"),4,ArchiveSortColumn.PackedSize);AddHeader(T("修改时间","Modified"),5,ArchiveSortColumn.Modified);UpdateSortHeaders();
        table.Children.Add(header);
        fileList=new ListView{SelectionMode=ListViewSelectionMode.Multiple,IsMultiSelectCheckBoxEnabled=true,HorizontalContentAlignment=HorizontalAlignment.Stretch,CanDragItems=true};
        fileList.ItemContainerTransitions=[];
        fileList.ItemsPanel=(ItemsPanelTemplate)XamlReader.Load("<ItemsPanelTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><ItemsStackPanel/></ItemsPanelTemplate>");
        fileList.ItemTemplate=(DataTemplate)XamlReader.Load("""
        <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"><Grid MinHeight="48" ColumnSpacing="12" Padding="4,5"><Grid.ColumnDefinitions><ColumnDefinition Width="26"/><ColumnDefinition Width="*"/><ColumnDefinition Width="100"/><ColumnDefinition Width="100"/><ColumnDefinition Width="145"/></Grid.ColumnDefinitions><FontIcon Glyph="{Binding Glyph}" Visibility="{Binding FolderIconVisibility}" FontSize="20" Foreground="{ThemeResource AccentTextFillColorPrimaryBrush}"/><Image Source="{Binding Icon}" Visibility="{Binding FileIconVisibility}" Width="24" Height="24" Stretch="Uniform"/><TextBlock Grid.Column="1" Text="{Binding Name}" ToolTipService.ToolTip="{Binding FullPath}" VerticalAlignment="Center" TextTrimming="CharacterEllipsis"/><TextBlock Grid.Column="2" Text="{Binding SizeText}" VerticalAlignment="Center" FontSize="12"/><TextBlock Grid.Column="3" Text="{Binding PackedSizeText}" VerticalAlignment="Center" FontSize="12"/><TextBlock Grid.Column="4" Text="{Binding ModifiedText}" VerticalAlignment="Center" FontSize="12"/></Grid></DataTemplate>
        """);
        fileList.DoubleTapped+=(_,e)=>{
            var elements=VisualTreeHelper.FindElementsInHostCoordinates(e.GetPosition(null),fileList).ToList();
            if(elements.Any(x=>x is CheckBox))return;
            if(elements.OfType<FrameworkElement>().Select(x=>x.DataContext).OfType<ArchiveRow>().FirstOrDefault() is { } row)
            {
                e.Handled=true;var entry=row.Entry;var sourceArchive=archive;var sourceFolder=folder;
                // Finish the routed gesture and ListView selection before the
                // preview takes focus. There is only one activation per open.
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,async()=>{if(page=="archive"&&archive==sourceArchive&&folder==sourceFolder)await ActivateRowAsync(entry);});
            }
        };fileList.KeyDown+=async(_,e)=>{if(e.Key==VirtualKey.Enter&&fileList.SelectedItem is ArchiveRow row){e.Handled=true;await ActivateRowAsync(row.Entry);}else if(e.Key==VirtualKey.Delete){e.Handled=true;await DeleteSelectedAsync();}else if(e.Key==VirtualKey.A&&IsControlDown()){e.Handled=true;SelectAllRows();}};
        // WinUI owns gesture recognition and input tracking. A native data object
        // supplies 7-Zip-style delayed rendering through IDataObjectProvider.
        fileList.DragItemsStarting+=FileListDragItemsStarting;
        fileList.DragItemsCompleted+=FileListDragItemsCompleted;
        fileList.DragStarting+=(_,e)=>{e.AllowedOperations=DataPackageOperation.Copy|DataPackageOperation.Move;};
        fileList.AllowDrop=true;
        fileList.AddHandler(UIElement.DragOverEvent,new DragEventHandler((_,e)=>UpdateDragOver(e)),true);
        fileList.AddHandler(UIElement.DropEvent,new DragEventHandler(FilesDropped),true);
        fileList.SelectionChanged+=(_,_)=>QueueSelectionDetails();Grid.SetRow(fileList,1);table.Children.Add(fileList);
        var workspace=new Grid{ColumnSpacing=16};workspace.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});workspace.ColumnDefinitions.Add(new(){Width=new GridLength(240)});
        archiveTableScroll=new ScrollViewer{Content=table,HorizontalScrollMode=ScrollMode.Enabled,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollMode=ScrollMode.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};workspace.Children.Add(archiveTableScroll);
        archivePropertiesPanel=CreateArchivePropertiesPanel();Grid.SetColumn(archivePropertiesPanel,1);workspace.Children.Add(archivePropertiesPanel);Grid.SetRow(workspace,3);grid.Children.Add(workspace);
        countLabel=Text("",12);Grid.SetRow(countLabel,4);grid.Children.Add(countLabel);ShowContent(grid);RefreshRows();
    }
    private List<ArchiveEntry> SelectedEntries()
    {
        return ExpandSelectedEntries(fileList?.SelectedItems.Cast<ArchiveRow>().Select(r=>r.Entry)??[]);
    }
    private List<ArchiveEntry> ExpandSelectedEntries(IEnumerable<ArchiveEntry> selectedRows)
    {
        var selected=selectedRows.ToList();
        var fileIndices=selected.Where(e=>!e.IsDirectory).Select(e=>e.Index).ToHashSet();
        var folders=selected.Where(e=>e.IsDirectory).Select(e=>e.NormalizedPath).ToArray();
        return entries.Where(e=>fileIndices.Contains(e.Index)||folders.Any(folder=>e.NormalizedPath==folder||e.NormalizedPath.StartsWith(folder+"/",StringComparison.Ordinal))).ToList();
    }
    private async Task ActivateRowAsync(ArchiveEntry entry){if(entry.IsDirectory)SetFolder(entry.NormalizedPath);else await ShowPreviewAsync(entry);}
    private ContentDialog Dialog(string title,UIElement content,string? primary=null)=>new(){Title=title,Content=content,PrimaryButtonText=primary??T("确定","OK"),CloseButtonText=T("取消","Cancel"),DefaultButton=ContentDialogButton.Primary,XamlRoot=Root.XamlRoot,RequestedTheme=Root.RequestedTheme};
    private readonly SemaphoreSlim dialogGate=new(1,1);
    // WinUI allows only one ContentDialog at a time; a second ShowAsync (for example from a
    // fast second click) throws and used to crash the app as an unhandled async exception.
    private async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog)
    {
        await dialogGate.WaitAsync();
        try
        {
            if(Root.XamlRoot==null)return ContentDialogResult.None;
            return await dialog.ShowAsync();
        }
        catch(Exception ex){Notify(T("无法显示对话框：","Cannot show the dialog: ")+ex.Message,InfoBarSeverity.Error);return ContentDialogResult.None;}
        finally{dialogGate.Release();}
    }
    private async Task CreateDialogAsync(List<string>? sources=null)
    {
        sources??=[];var panel=Stack(14);var list=Text(sources.Count>0?string.Join(Environment.NewLine,sources):T("请选择来源文件或文件夹","Choose source files or folders"),12);panel.Children.Add(list);
        var sourceActions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};sourceActions.Children.Add(Button(T("添加文件","Add files"),async(_,_)=>{sources.AddRange(await PickFilesAsync());list.Text=string.Join(Environment.NewLine,sources);}));sourceActions.Children.Add(Button(T("添加文件夹","Add folder"),async(_,_)=>{var f=await PickFolderAsync();if(f!=null)sources.Add(f);list.Text=string.Join(Environment.NewLine,sources);}));panel.Children.Add(sourceActions);
        var formats=new[]{"7z","zip","tar","zst","lz4"};var defaultFormat=UserSettings.NormalizeFormat(settings.DefaultFormat);
        var output=Input(T("保存为","Save as"),System.IO.Path.Combine(ShellPaths.SuggestedFolder(sources),ShellPaths.SuggestedFileName(sources,defaultFormat)));panel.Children.Add(output);
        panel.Children.Add(Button(T("选择保存文件夹","Choose folder"),async(_,_)=>{var path=await PickFolderAsync();if(path!=null)output.Text=System.IO.Path.Combine(path,System.IO.Path.GetFileName(output.Text));}));
        var format=new ComboBox{Header=T("格式","Format"),ItemsSource=formats,SelectedIndex=Math.Max(0,Array.IndexOf(formats,defaultFormat)),HorizontalAlignment=HorizontalAlignment.Stretch};
        var method=new ComboBox{Header=T("压缩方法","Compression method"),HorizontalAlignment=HorizontalAlignment.Stretch};
        var level=new ComboBox{Header=T("压缩级别","Compression level"),ItemsSource=new[]{T("标准","Normal"),T("更快","Faster"),T("更小","Smaller")},SelectedIndex=0,HorizontalAlignment=HorizontalAlignment.Stretch};
        void UpdateMethods(){var code=formats[format.SelectedIndex];var choices=CompressionOptions.For(code);method.ItemsSource=choices;var preferred=CompressionOptions.Default(settings,code);method.SelectedIndex=Math.Max(0,choices.ToList().FindIndex(item=>item.Value==preferred));method.IsEnabled=choices.Count>0;}
        panel.Children.Add(format);panel.Children.Add(method);panel.Children.Add(level);
        var secret=new PasswordBox{Header=T("密码（可选）","Password (optional)"),PasswordRevealMode=PasswordRevealMode.Peek};var encrypt=new CheckBox{Content=T("加密文件名（仅 7z）","Encrypt file names (7z only)"),IsEnabled=false};
        var streamHint=Text(T("Zstandard / LZ4 每次压缩一个文件；文件夹或多个文件请先打包为 TAR。","Zstandard / LZ4 compress one file; pack folders or multiple files into TAR first."),12);panel.Children.Add(streamHint);
        void UpdateFormatState(){var noPassword=format.SelectedIndex>=2;if(noPassword)secret.Password="";secret.IsEnabled=!noPassword;level.IsEnabled=format.SelectedIndex!=2;method.IsEnabled=format.SelectedIndex<2;streamHint.Visibility=format.SelectedIndex>=3?Visibility.Visible:Visibility.Collapsed;encrypt.IsEnabled=format.SelectedIndex==0&&secret.Password.Length>0;if(!encrypt.IsEnabled)encrypt.IsChecked=false;}
        secret.PasswordChanged+=(_,_)=>UpdateFormatState();format.SelectionChanged+=(_,_)=>{output.Text=formats[format.SelectedIndex] is "zst" or "lz4"?System.IO.Path.Combine(System.IO.Path.GetDirectoryName(output.Text)??"",ShellPaths.SuggestedFileName(sources,formats[format.SelectedIndex])):System.IO.Path.ChangeExtension(output.Text,format.SelectedItem.ToString());UpdateMethods();UpdateFormatState();};panel.Children.Add(secret);panel.Children.Add(encrypt);UpdateMethods();UpdateFormatState();
        var error=Text("",12);error.Foreground=new SolidColorBrush(Colors.Firebrick);panel.Children.Add(error);
        var dialog=Dialog(T("新建压缩包","New archive"),new ScrollViewer{Content=panel,MaxHeight=560,MinWidth=480},T("开始压缩","Create"));dialog.PrimaryButtonClick+=(_,e)=>{if(sources.Count==0||string.IsNullOrWhiteSpace(output.Text)){error.Text=T("请选择来源并填写输出路径。","Choose sources and an output path.");e.Cancel=true;}else if(format.SelectedIndex>=3&&(sources.Count!=1||!File.Exists(sources[0]))){error.Text=T("Zstandard 和 LZ4 每次压缩一个文件；多个文件请先打包为 TAR。","Zstandard and LZ4 compress one file; pack multiple files into TAR first.");e.Cancel=true;}else if(File.Exists(output.Text)){error.Text=T("输出文件已存在，请改用一个新名称。","The output file already exists. Choose another name.");e.Cancel=true;}};
        if(await ShowDialogAsync(dialog)!=ContentDialogResult.Primary)return;
        var target=output.Text;var inputs=sources.ToArray();var kind=format.SelectedItem.ToString()!;var compression=level.SelectedIndex switch{1=>1,2=>9,_=>5};var key=secret.Password;var header=encrypt.IsChecked==true;var selectedMethod=(method.SelectedItem as CompressionChoice)?.Value;
        QueueJob(T("压缩 ","Compress ")+System.IO.Path.GetFileName(target),target,(p,ct)=>engine.CreateAsync(target,inputs,kind,compression,key,header,selectedMethod,p,ct));
    }
    private async Task ExtractDialogAsync(IReadOnlyList<ArchiveEntry> selected)
    {
        if(archive==null||selected.Count==0){Notify(T("没有可以解压的条目。","Nothing to extract."));return;}
        var currentArchive=archive;var chosen=selected.ToArray();var key=password;
        var panel=Stack(14);panel.Children.Add(Text($"{System.IO.Path.GetFileName(currentArchive)} · {T(chosen.Length+" 项",chosen.Length+" items")}"));
        var destination=Input(T("目标目录","Destination folder"),System.IO.Path.Combine(System.IO.Path.GetDirectoryName(currentArchive)!,ShellPaths.ArchiveStem(currentArchive)));panel.Children.Add(destination);
        panel.Children.Add(Button(T("选择文件夹","Choose folder"),async(_,_)=>{var path=await PickFolderAsync();if(path!=null)destination.Text=path;}));
        var conflict=new ComboBox{Header=T("遇到同名文件","On name conflict"),ItemsSource=new[]{T("自动重命名，保留两者","Rename, keep both"),T("跳过已有文件","Skip existing files"),T("替换已有文件","Replace existing files")},SelectedIndex=0,HorizontalAlignment=HorizontalAlignment.Stretch};panel.Children.Add(conflict);
        var note=Text(T("只写入选定目录；不删除源压缩包。","Only writes to the chosen folder; the source archive is not deleted."),12);panel.Children.Add(note);
        var dialog=Dialog(T("解压到","Extract to"),new ScrollViewer{Content=panel,MaxHeight=560,MinWidth=480},T("开始解压","Start extraction"));
        dialog.PrimaryButtonClick+=(_,e)=>{if(string.IsNullOrWhiteSpace(destination.Text)){e.Cancel=true;note.Text=T("请输入目标目录。","Enter a destination folder.");}};
        if(await ShowDialogAsync(dialog)!=ContentDialogResult.Primary)return;
        if(conflict.SelectedIndex==2)
        {
            var confirm=Dialog(T("替换同名文件？","Replace existing files?"),Text(T($"目标目录中的同名文件将被替换：\n{destination.Text}",$"Files with the same name in the destination will be replaced:\n{destination.Text}")),T("替换并解压","Replace and extract"));confirm.DefaultButton=ContentDialogButton.Close;
            if(await ShowDialogAsync(confirm)!=ContentDialogResult.Primary)return;
        }
        if(chosen.Any(e=>e.Encrypted)&&string.IsNullOrEmpty(key)){key=await AskPasswordAsync();if(key==null)return;password=key;}
        var target=destination.Text;var policy=(ConflictPolicy)conflict.SelectedIndex;
        var name=T("解压 ","Extract ")+System.IO.Path.GetFileName(currentArchive)+T($" · {chosen.Length} 项",$" · {chosen.Length} items");
        QueueJob(name,target,(progress,cancellation)=>engine.ExtractAsync(currentArchive,chosen,target,policy,key,progress,cancellation));
    }
    private async Task AddToArchiveAsync()
    {
        if(archive==null)return;
        if(!ArchiveEngine.SupportsUpdate(archive)){Notify(T("当前只支持修改 7z 和 ZIP 压缩包。","Only 7z and ZIP archives can be modified."),InfoBarSeverity.Warning);return;}
        var sources=new List<string>();
        var panel=Stack(14);
        panel.Children.Add(Text(T($"添加到 {System.IO.Path.GetFileName(archive)} · {(folder.Length==0?"根目录":folder)}",$"Add to {System.IO.Path.GetFileName(archive)} · {(folder.Length==0?"root":folder)}"),12));
        var summary=Text(T("还没有选择文件。","No files selected yet."),12);summary.TextWrapping=TextWrapping.Wrap;panel.Children.Add(summary);
        var rows=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};
        rows.Children.Add(Button(T("添加文件","Add files"),async(_,_)=>{var picked=await PickFilesAsync();if(picked.Count>0){sources.AddRange(picked);summary.Text=string.Join(Environment.NewLine,sources);}}));
        rows.Children.Add(Button(T("添加文件夹","Add folder"),async(_,_)=>{var picked=await PickFolderAsync();if(picked!=null){sources.Add(picked);summary.Text=string.Join(Environment.NewLine,sources);}}));
        panel.Children.Add(rows);
        var error=Text("",12);error.Foreground=new SolidColorBrush(Colors.Firebrick);panel.Children.Add(error);
        var dialog=Dialog(T("向压缩包添加文件","Add files to archive"),new ScrollViewer{Content=panel,MaxHeight=520,MinWidth=480},T("添加","Add"));
        dialog.PrimaryButtonClick+=(_,e)=>{if(sources.Count==0){error.Text=T("请先选择文件或文件夹。","Choose files or folders first.");e.Cancel=true;}};
        if(await ShowDialogAsync(dialog)!=ContentDialogResult.Primary)return;
        await AddSourcesAsync(sources);
    }
    private async Task AddSourcesAsync(IReadOnlyList<string> sources)
    {
        if(archive==null||sources.Count==0)return;
        var current=archive;
        if(!ArchiveEngine.SupportsUpdate(current)){Notify(T("当前只支持修改 7z 和 ZIP 压缩包。","Only 7z and ZIP archives can be modified."),InfoBarSeverity.Warning);return;}
        IReadOnlyList<SourceItem> additions;
        try{additions=ArchiveEngine.ExpandSources(sources,folder);}catch(Exception ex){Notify(ex.Message,InfoBarSeverity.Error);return;}
        if(additions.Count==0){Notify(T("没有可以添加的文件。","Nothing to add."));return;}
        var existing=new Dictionary<string,ArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach(var entry in entries)existing.TryAdd(entry.NormalizedPath,entry);
        var conflicts=additions.Select(a=>a.Name).Where(existing.ContainsKey).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if(conflicts.Count>0)
        {
            var preview=string.Join(Environment.NewLine,conflicts.Take(10));
            var more=conflicts.Count>10?T($"\n…共 {conflicts.Count} 项",$"\n…{conflicts.Count} items in total"):"";
            var confirm=Dialog(T("替换压缩包内的同名条目？","Replace entries with the same name?"),Text(T($"压缩包中已存在以下同名条目，继续将用新文件替换：\n\n{preview}{more}",$"These entries already exist and will be replaced:\n\n{preview}{more}")),T("替换并添加","Replace and add"));
            confirm.DefaultButton=ContentDialogButton.Close;
            if(await ShowDialogAsync(confirm)!=ContentDialogResult.Primary)return;
        }
        var key=await EnsurePasswordAsync();
        if(key is null)return;
        var approved=new HashSet<string>(conflicts,StringComparer.OrdinalIgnoreCase);
        var additionsSnapshot=additions.ToArray();
        QueueJob(T("添加文件 ","Add files ")+System.IO.Path.GetFileName(current),current,async(p,ct)=>
        {
            // Re-read the archive so removal indexes match the current file, not the earlier listing.
            var fresh=await engine.ListAsync(current,key,ct);
            var freshByPath=new Dictionary<string,ArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            foreach(var entry in fresh)freshByPath.TryAdd(entry.NormalizedPath,entry);
            var currentConflicts=additionsSnapshot.Select(a=>a.Name).Where(freshByPath.ContainsKey).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if(currentConflicts.Exists(name=>!approved.Contains(name)))throw new IOException(T("压缩包内容已在外部发生变化，请重新添加。","The archive changed outside New7z. Add the files again."));
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
        if(!ArchiveEngine.SupportsUpdate(archive)){Notify(T("当前只支持修改 7z 和 ZIP 压缩包。","Only 7z and ZIP archives can be modified."),InfoBarSeverity.Warning);return;}
        var selected=SelectedEntries().Where(e=>e.Index!=uint.MaxValue).ToList();
        if(selected.Count==0){Notify(T("请先选择要删除的文件或文件夹。","Select files or folders to delete."));return;}
        var preview=string.Join(Environment.NewLine,selected.Take(10).Select(e=>e.NormalizedPath));
        var more=selected.Count>10?T($"\n…共 {selected.Count} 项",$"\n…{selected.Count} items in total"):"";
        var confirm=Dialog(T("删除压缩包内的条目？","Delete entries from the archive?"),Text(T($"将从 {System.IO.Path.GetFileName(archive)} 删除 {selected.Count} 项：\n\n{preview}{more}\n\n压缩包会被重新生成，删除的内容不会进入回收站，也无法撤销。",$"Delete {selected.Count} items from {System.IO.Path.GetFileName(archive)}:\n\n{preview}{more}\n\nThe archive will be rewritten; deleted content does not go to the Recycle Bin and cannot be undone.")),T("删除","Delete"));
        confirm.DefaultButton=ContentDialogButton.Close;
        if(await ShowDialogAsync(confirm)!=ContentDialogResult.Primary)return;
        var key=await EnsurePasswordAsync();
        if(key is null)return;
        var current=archive;var paths=selected.Select(e=>e.NormalizedPath).Distinct(StringComparer.Ordinal).ToArray();
        QueueJob(T("删除条目 ","Delete entries ")+System.IO.Path.GetFileName(current),current,async(p,ct)=>
        {
            // Resolve the selected paths against a fresh listing so a changed archive is never
            // rewritten with stale entry indexes.
            var fresh=await engine.ListAsync(current,key,ct);
            var lookup=new Dictionary<string,uint>(StringComparer.Ordinal);
            foreach(var entry in fresh)lookup.TryAdd(entry.NormalizedPath,entry.Index);
            var indices=new List<uint>();
            foreach(var path in paths)
            {
                if(!lookup.TryGetValue(path,out var index))throw new IOException(T("压缩包内容已在外部发生变化，请重新打开后再删除。","The archive changed outside New7z. Reopen it and try again."));
                indices.Add(index);
            }
            await engine.ModifyAsync(current,indices,[],key,p,ct);
        },false,ReloadArchiveAsync);
    }
    private static bool IsControlDown()=>(Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)&Windows.UI.Core.CoreVirtualKeyStates.Down)!=0;
    private void SelectAllRows()
    {
        if(fileList==null)return;
        fileList.SelectAll();
    }
    // Rewriting an archive reads every kept entry, so an encrypted archive needs the password up front.
    // Returns null only when the user cancelled; an empty string means no password is needed.
    private async Task<string?> EnsurePasswordAsync()
    {
        if(entries.Any(e=>e.Encrypted)&&string.IsNullOrEmpty(password)){var key=await AskPasswordAsync();if(key==null)return null;password=key;}
        return password??"";
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
        catch(ArchivePasswordException){Notify(T("压缩包现在需要密码，请重新打开。","The archive now requires a password. Reopen it."),InfoBarSeverity.Warning);}
        catch(Exception ex){Notify(ex.Message,InfoBarSeverity.Error);}
    }
    private void UpdateDragOver(DragEventArgs e)
    {
        if(dragData!=null){e.AcceptedOperation=DataPackageOperation.None;return;}
        if(e.DataView.Contains(StandardDataFormats.StorageItems))e.AcceptedOperation=DataPackageOperation.Copy;
    }
    private async void FileListDragItemsStarting(object sender,DragItemsStartingEventArgs e)
    {
        if(dragVerificationReport!=null)File.AppendAllText(dragVerificationReport+".trace","Starting items="+e.Items.Count+" page="+page+" archive="+(archive!=null)+" preview="+previewOpen+Environment.NewLine);
        if(dragData!=null||archive==null||page!="archive"||previewOpen){e.Cancel=true;return;}
        var rows=e.Items.OfType<ArchiveRow>().Select(row=>row.Entry).ToArray();
        if(rows.Length==0){e.Cancel=true;return;}
        if(dragVerificationTextOnly){e.Data.SetText("New7z drag baseline");e.Data.RequestedOperation=DataPackageOperation.Copy;return;}
        try
        {
            if(entries.Any(entry=>entry.Encrypted)&&string.IsNullOrEmpty(password))
            {
                e.Cancel=true;
                if(await EnsurePasswordAsync()!=null)Notify(T("密码已确认，请重新拖动条目。","Password confirmed. Drag the item again."),InfoBarSeverity.Informational);
                return;
            }
            var selected=ExpandSelectedEntries(rows);
            if(selected.Count==0){e.Cancel=true;return;}
            dragJob=new ArchiveJob(T("拖动解压 ","Drag extract ")+string.Join(", ",rows.Select(r=>r.Name)),T("等待目标接收文件","Waiting for the drop target to request files"));
            dragJob.Cancelable=false;
            dragJob.Status=T("等待目标接收文件…","Waiting for the drop target…");jobs.Insert(0,dragJob);
            dragTemp=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"New7z-drag-"+Guid.NewGuid().ToString("N"));
            var package=WinRT.MarshalInspectable<DataPackage>.FromManaged(e.Data);
            try{dragData=engine.CreateDragData(package,archive,selected,rows,dragTemp,Hwnd,password);}
            finally{System.Runtime.InteropServices.Marshal.Release(package);}
            e.Data.RequestedOperation=DataPackageOperation.Copy;
            e.Data.Properties.Title=rows.Length==1?rows[0].Name:T($"{rows.Length} 个项目",$"{rows.Length} items");
            if(dragVerificationReport!=null)File.AppendAllText(dragVerificationReport+".trace","Formats="+string.Join("|",e.Data.GetView().AvailableFormats)+Environment.NewLine);
            StatusText.Text=T("拖动以解压；松开鼠标后开始。","Drag to extract; extraction starts on release.");
        }
        catch(Exception ex)
        {
            e.Cancel=true;
            Notify(ex.Message,InfoBarSeverity.Error);
            FinishArchiveDrag();
        }
    }
    private void FileListDragItemsCompleted(ListViewBase sender,DragItemsCompletedEventArgs e)
    {
        if(dragVerificationReport!=null)File.AppendAllText(dragVerificationReport+".trace","Completed="+e.DropResult+Environment.NewLine);
        var completedDrag=dragData;if(completedDrag==null)return;
        try
        {
            completedDrag.ThrowIfFailed();
            var accepted=e.DropResult!=DataPackageOperation.None||completedDrag.WasTransferred;
            if(dragJob!=null){dragJob.Active=false;dragJob.Percent=accepted?100:0;dragJob.Status=accepted?T("已完成","Completed"):T("已取消","Cancelled");}
            Notify(accepted?T("目标已接收拖动内容。","The target received the dragged items."):T("拖动已取消，没有目标接收内容。","The drag was cancelled; no target accepted the items."),accepted?InfoBarSeverity.Success:InfoBarSeverity.Informational);
        }
        catch(ArchivePasswordException){if(dragJob!=null){dragJob.Active=false;dragJob.Status=T("失败：密码不正确","Failed: incorrect password");}password=null;Notify(T("密码不正确，请重新拖动并输入正确密码。","The password was incorrect. Drag again and enter the correct password."),InfoBarSeverity.Warning);}
        catch(OperationCanceledException){if(dragJob!=null){dragJob.Active=false;dragJob.Status=T("已取消","Cancelled");}Notify(T("拖动解压已取消。","Drag extraction was cancelled."),InfoBarSeverity.Informational);}
        catch(Exception ex){if(dragJob!=null){dragJob.Active=false;dragJob.Status=T("失败：","Failed: ")+ex.Message;}Notify(ex.Message,InfoBarSeverity.Error);}
        finally{FinishArchiveDrag();}
    }
    private void FinishArchiveDrag()
    {
        if(dragVerificationReport!=null)File.AppendAllText(dragVerificationReport+".trace",engine.GetDragDiagnostics()+Environment.NewLine);
        dragData?.Dispose();dragData=null;
        dragJob=null;
        if(dragTemp!=null){ScheduleTempCleanup(dragTemp);dragTemp=null;}
        StatusText.Text=T("就绪","Ready");
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
        var job=new ArchiveJob(name,destination);job.Status=T("等待中","Waiting");jobs.Insert(0,job);if(openTasks)SelectPage("tasks");
        _=Run();async Task Run(){bool acquired=false;try{await queue.WaitAsync(job.Cancellation.Token);acquired=true;job.Status=T("正在处理","Running");var last=Stopwatch.StartNew();var progress=new Progress<EngineProgress>(p=>{if(!job.Active)return;if(p.CurrentItem is {Length:>0})job.CurrentItem=p.CurrentItem;if(last.ElapsedMilliseconds<100)return;last.Restart();if(p.Total>0)job.Percent=Math.Clamp(p.Completed*100.0/p.Total,0,100);});await operation(progress,job.Cancellation.Token);if(completed!=null){try{await completed();}catch(Exception ex){Notify(ex.Message,InfoBarSeverity.Error);}}job.Active=false;job.Percent=100;job.Status=T("已完成","Completed");Notify(T(name+" 已完成。",name+" completed."),InfoBarSeverity.Success);}catch(OperationCanceledException){job.Status=T("已取消","Cancelled");job.Active=false;}catch(Exception ex){job.Status=T("失败：","Failed: ")+ex.Message;job.Active=false;Notify(ex.Message,InfoBarSeverity.Error);}finally{if(acquired)queue.Release();StatusText.Text=jobs.Any(j=>j.CanCancel)?T("有任务正在处理或排队","Tasks are running or queued"):T("就绪","Ready");}}
    }
    private void ShowTasks()
    {
        var content=Stack(18);content.Children.Add(Text(T("任务中心","Tasks"),28,true));content.Children.Add(Text(T("任务在后台排队运行，切换页面不会中断。","Tasks run in the background; switching pages does not interrupt them.")));
        var emptyCard=Card(Text(T("还没有任务。新建压缩或解压后，可在这里查看进度。","No tasks yet. Create or extract an archive to see progress here.")));
        if(jobs.Count==0)content.Children.Add(emptyCard);
        var views=new List<(ArchiveJob Job,Action<ArchiveJob> Handler)>();
        foreach(var job in jobs)
        {
            var card=Stack(10);card.Children.Add(Text(job.Title,16,true));card.Children.Add(Text(job.Destination,12));
            var current=Text(job.CurrentItem,12);current.Foreground=(Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];current.Visibility=string.IsNullOrEmpty(job.CurrentItem)?Visibility.Collapsed:Visibility.Visible;card.Children.Add(current);
            var statusLine=new Grid();var status=Text(job.Status);statusLine.Children.Add(status);var percent=Text(job.PercentText,12);percent.HorizontalAlignment=HorizontalAlignment.Right;percent.Foreground=(Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];statusLine.Children.Add(percent);card.Children.Add(statusLine);
            var bar=new ProgressBar{Minimum=0,Maximum=100,Value=job.Percent};card.Children.Add(bar);
            var actions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};var cancel=Button(T("取消任务","Cancel task"),async(_,_)=>{var d=Dialog(T("取消任务？","Cancel this task?"),Text(T("已完成的文件将保留，当前未完成的临时文件会清理。","Completed files are kept; unfinished temporary files are cleaned up.")),T("取消任务","Cancel task"));d.DefaultButton=ContentDialogButton.Close;if(await ShowDialogAsync(d)==ContentDialogResult.Primary)job.Cancellation.Cancel();});cancel.IsEnabled=job.CanCancel;actions.Children.Add(cancel);
            actions.Children.Add(Button(T("打开文件夹","Open folder"),(_,_)=>{var path=Directory.Exists(job.Destination)?job.Destination:System.IO.Path.GetDirectoryName(job.Destination);if(path!=null&&Directory.Exists(path)){var info=new ProcessStartInfo("explorer.exe"){UseShellExecute=false};info.ArgumentList.Add(path);Process.Start(info);}}));card.Children.Add(actions);content.Children.Add(Card(card));
            void Refresh(){status.Text=job.Status;percent.Text=job.PercentText;bar.Value=job.Percent;cancel.IsEnabled=job.CanCancel;current.Text=job.CurrentItem;current.Visibility=string.IsNullOrEmpty(job.CurrentItem)?Visibility.Collapsed:Visibility.Visible;}
            Action<ArchiveJob> handler=_=>DispatcherQueue.TryEnqueue(Refresh);views.Add((job,handler));
        }
        var sharedHost=Stack(12);content.Children.Add(sharedHost);
        ShowContent(new ScrollViewer{Content=content});
        sharedTaskHost=sharedHost;tasksEmptyCard=emptyCard;
        foreach(var view in views){view.Job.Updated+=view.Handler;taskSubscriptions.Add(view);}
        RefreshSharedTaskCards();taskRefreshTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};taskRefreshTimer.Tick+=(_,_)=>RefreshSharedTaskCards();taskRefreshTimer.Start();
    }
    private readonly Dictionary<string,(Border Card,Action<SharedTask> Update)> sharedTaskViews=[];
    private bool refreshingSharedTasks;
    private async void RefreshSharedTaskCards()
    {
        if(sharedTaskHost==null||refreshingSharedTasks)return;
        var host=sharedTaskHost;refreshingSharedTasks=true;
        IReadOnlyList<SharedTask> tasks;
        try{tasks=await Task.Run(SharedTaskStore.ReadAll);}finally{refreshingSharedTasks=false;}
        if(!ReferenceEquals(host,sharedTaskHost))return;
        if(tasksEmptyCard!=null)tasksEmptyCard.Visibility=jobs.Count==0&&tasks.Count==0?Visibility.Visible:Visibility.Collapsed;
        var ids=tasks.Select(t=>t.Id).ToHashSet();
        foreach(var id in sharedTaskViews.Keys.Where(id=>!ids.Contains(id)).ToArray()){host.Children.Remove(sharedTaskViews[id].Card);sharedTaskViews.Remove(id);}
        foreach(var shared in tasks)
        {
            if(sharedTaskViews.TryGetValue(shared.Id,out var view)&&host.Children.Contains(view.Card)){view.Update(shared);continue;}
            var card=Stack(8);var title=Text(shared.Title,16,true);var destination=Text(shared.Destination,12);card.Children.Add(title);card.Children.Add(destination);
            var current=Text("",12);current.MinHeight=18;current.MaxLines=1;current.TextTrimming=TextTrimming.CharacterEllipsis;card.Children.Add(current);
            var line=new Grid{ColumnSpacing=12};line.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});line.ColumnDefinitions.Add(new(){Width=new GridLength(52)});var status=Text("");line.Children.Add(status);var percent=Text("",12);percent.HorizontalAlignment=HorizontalAlignment.Right;Grid.SetColumn(percent,1);line.Children.Add(percent);card.Children.Add(line);
            var bar=new ProgressBar{Minimum=0,Maximum=100};card.Children.Add(bar);
            var border=Card(card);
            void Update(SharedTask value){title.Text=value.Title;destination.Text=value.Destination;current.Text=value.CurrentItem;status.Text=value.Status;percent.Text=$"{value.Percent:0}%";bar.Value=Math.Clamp(value.Percent,0,100);bar.IsIndeterminate=value.Percent<=0&&value.Status.Contains(T("正在","Running"),StringComparison.Ordinal);}
            sharedTaskViews[shared.Id]=(border,Update);host.Children.Add(border);Update(shared);
        }
    }
    private static string NormalizeTheme(string theme)=>theme switch{"浅色" or "light"=>"light","深色" or "dark"=>"dark","系统" or "system"=>"system",_=>"system"};
    private void ApplyTheme(){Root.RequestedTheme=NormalizeTheme(settings.Theme) switch{"light"=>ElementTheme.Light,"dark"=>ElementTheme.Dark,_=>ElementTheme.Default};}
    private void ApplyIcon(){try{var path=settings.IconPath??System.IO.Path.Combine(AppContext.BaseDirectory,"Assets/logo.png");BrandImage.Source=new BitmapImage(new Uri(path));}catch{settings.IconPath=null;}}
    private async Task<string> PickPngAsync(string prefix)
    {
        var picker=new FileOpenPicker();InitializeWithWindow.Initialize(picker,Hwnd);picker.FileTypeFilter.Add(".png");
        var file=await picker.PickSingleFileAsync();
        if(file==null)return "";
        var props=await file.GetBasicPropertiesAsync();
        if(props.Size>10*1024*1024)throw new IOException(T("图片不能超过 10 MiB。","The image must not exceed 10 MiB."));
        using var stream=await file.OpenReadAsync();
        var decoder=await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
        if(decoder.PixelWidth>4096||decoder.PixelHeight>4096)throw new IOException(T("图片尺寸不能超过 4096 × 4096。","The image must not exceed 4096 × 4096."));
        Directory.CreateDirectory(UserSettings.Folder);
        var target=System.IO.Path.Combine(UserSettings.Folder,prefix+"-"+Guid.NewGuid().ToString("N")+".png");
        File.Copy(file.Path,target);
        return target;
    }
    private static void DeleteCustomIcon(string? path)
    {
        if(path==null)return;
        try{if(path.StartsWith(UserSettings.Folder,StringComparison.OrdinalIgnoreCase)&&File.Exists(path))File.Delete(path);}catch{}
    }
    private void SetFormatPreview(Image preview,string ext)
    {
        try{var file=FormatIconPath(ext);preview.Source=File.Exists(file)?new BitmapImage(new Uri(file)):null;}catch{preview.Source=null;}
    }
    private void AddIconSettings(StackPanel panel)
    {
        var icon=Stack(12);
        icon.Children.Add(Text(T("应用内图标","App icon"),18,true));
        icon.Children.Add(Text(T("替换应用内品牌图标。不会修改 EXE、任务栏或系统文件关联。","Replace the brand icon inside the app. EXE, taskbar and file associations are not changed."),12));
        var preview=new Image{Width=96,Height=96,HorizontalAlignment=HorizontalAlignment.Left,Source=BrandImage.Source};
        icon.Children.Add(preview);
        icon.Children.Add(Button(T("替换 PNG 图标","Replace PNG icon"),async(_,_)=>{try{var target=await PickPngAsync("custom-icon");if(target.Length==0)return;var previous=settings.IconPath;settings.IconPath=target;settings.Save();ApplyIcon();preview.Source=BrandImage.Source;DeleteCustomIcon(previous);}catch(Exception ex){Notify(ex.Message,InfoBarSeverity.Error);}}));
        icon.Children.Add(Button(T("恢复默认","Restore default"),(_,_)=>{var previous=settings.IconPath;settings.IconPath=null;settings.Save();ApplyIcon();preview.Source=BrandImage.Source;DeleteCustomIcon(previous);}));
        panel.Children.Add(Card(icon));

        var formats=Stack(12);
        formats.Children.Add(Text(T("压缩文件图标","Archive icons"),18,true));
        formats.Children.Add(Text(T("替换「最近使用」等位置显示的 7z、ZIP 等格式图标。不会修改资源管理器中的文件关联图标。","Replace the 7z, ZIP and other format icons shown inside the app, such as in Recent. Explorer file-association icons are not changed."),12));
        foreach(var ext in FormatNames)
        {
            var row=new Grid{ColumnSpacing=12};
            row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(40)});
            row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
            row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            var formatPreview=new Image{Width=32,Height=32,VerticalAlignment=VerticalAlignment.Center};
            SetFormatPreview(formatPreview,ext);
            row.Children.Add(formatPreview);
            var label=Text(ext.ToUpperInvariant(),14,true);label.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(label,1);row.Children.Add(label);
            Button? reset=null;
            var replace=Button(T("替换","Replace"),async(_,_)=>{try{var target=await PickPngAsync("custom-"+ext);if(target.Length==0)return;settings.FormatIcons.TryGetValue(ext,out var previous);settings.FormatIcons[ext]=target;settings.Save();SetFormatPreview(formatPreview,ext);DeleteCustomIcon(previous);if(reset!=null)reset.IsEnabled=true;}catch(Exception ex){Notify(ex.Message,InfoBarSeverity.Error);}});
            Grid.SetColumn(replace,2);row.Children.Add(replace);
            reset=Button(T("恢复默认","Restore default"),(_,_)=>{if(settings.FormatIcons.Remove(ext,out var previous)){settings.Save();DeleteCustomIcon(previous);}SetFormatPreview(formatPreview,ext);if(reset!=null)reset.IsEnabled=false;});
            reset.IsEnabled=settings.FormatIcons.ContainsKey(ext);
            Grid.SetColumn(reset,3);row.Children.Add(reset);
            formats.Children.Add(row);
        }
        panel.Children.Add(Card(formats));
    }
    private void ShowSettings()
    {
        var panel=Stack(22);
        if(settingsCategory==null)ShowSettingsHub(panel);else ShowSettingsCategory(panel,settingsCategory);
        ShowContent(new ScrollViewer{Content=panel});
    }
    private void ShowSettingsHub(StackPanel panel)
    {
        panel.Children.Add(Text(T("设置","Settings"),28,true));
        panel.Children.Add(Text(T("选择要调整的设置类别。","Choose a category to adjust.")));
        var rows=Stack(8);
        foreach(var (category,glyph,title,description) in new[]{
            ("general","",T("常规","General"),T("界面语言、应用主题和应用更新","Language, theme and app updates")),
            ("compression","",T("压缩","Compression"),T("默认压缩格式和压缩方法","Default archive format and compression method")),
            ("explorer","",T("右键菜单","Context menu"),T("资源管理器菜单命令和任务弹出窗口","Explorer menu commands and the task popup")),
            ("appearance","",T("外观与图标","Appearance and icons"),T("应用内品牌图标和压缩格式图标","In-app brand and archive format icons")),
            ("associations","",T("文件关联","File associations"),T("默认应用和打开方式","Default apps and Open with")),
            ("about","",T("关于","About"),T("版本、许可和引擎信息","Version, license and engine"))})
            rows.Children.Add(SettingsCategoryRow(category,glyph,title,description));
        panel.Children.Add(rows);
    }
    private Button SettingsCategoryRow(string category,string glyph,string title,string description)
    {
        var content=new Grid{ColumnSpacing=16};
        content.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        content.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        content.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        content.Children.Add(new FontIcon{Glyph=glyph,FontSize=20,VerticalAlignment=VerticalAlignment.Center});
        var text=Stack(2);text.VerticalAlignment=VerticalAlignment.Center;
        text.Children.Add(Text(title,16,true));
        var detail=Text(description,12);detail.Foreground=(Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];text.Children.Add(detail);
        Grid.SetColumn(text,1);content.Children.Add(text);
        var chevron=new FontIcon{Glyph="",FontSize=12,VerticalAlignment=VerticalAlignment.Center};
        Grid.SetColumn(chevron,2);content.Children.Add(chevron);
        var row=new Button{Content=content,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(16,12,16,12),CornerRadius=new CornerRadius(8),BorderThickness=new Thickness(1),BorderBrush=(Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],Background=(Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"]};
        row.Click+=(_,_)=>{settingsCategory=category;RenderPage();};
        return row;
    }
    private void ShowSettingsCategory(StackPanel panel,string category)
    {
        var header=new StackPanel{Orientation=Orientation.Horizontal,Spacing=12,VerticalAlignment=VerticalAlignment.Center};
        var back=new Button{Content=new FontIcon{Glyph="",FontSize=14},Padding=new Thickness(10,8,10,8),CornerRadius=new CornerRadius(6)};
        ToolTipService.SetToolTip(back,T("返回设置","Back to settings"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(back,T("返回设置","Back to settings"));
        back.Click+=(_,_)=>{settingsCategory=null;RenderPage();};
        header.Children.Add(back);header.Children.Add(Text(SettingsCategoryTitle(category),28,true));panel.Children.Add(header);
        switch(category)
        {
            case "general":AddGeneralSettings(panel);AddUpdateSettings(panel);break;
            case "compression":AddCompressionSettings(panel);break;
            case "explorer":AddExplorerSettings(panel);break;
            case "appearance":AddIconSettings(panel);break;
            case "associations":if(AppEnvironment.IsPackaged)AddPackagedAssociationSettings(panel);else AddAssociationSettings(panel);break;
            case "about":AddAboutSettings(panel);break;
        }
    }
    private static string SettingsCategoryTitle(string category)=>category switch
    {
        "general"=>T("常规","General"),
        "compression"=>T("压缩","Compression"),
        "explorer"=>T("右键菜单","Context menu"),
        "appearance"=>T("外观与图标","Appearance and icons"),
        "associations"=>T("文件关联","File associations"),
        _=>T("关于","About")
    };
    private void AddGeneralSettings(StackPanel panel)
    {
        var language=new ComboBox{Header=T("界面语言","Language"),ItemsSource=new[]{T("跟随系统","Follow system"),"简体中文","English"},SelectedIndex=Math.Max(0,Array.IndexOf(LanguageCodes,settings.Language)),MinWidth=240};
        language.SelectionChanged+=(_,_)=>{settings.Language=LanguageCodes[language.SelectedIndex];settings.Save();ApplyLanguage();SaveExplorerLanguage();RenderPage();};
        panel.Children.Add(Card(language));
        var theme=new ComboBox{Header=T("应用主题","Theme"),ItemsSource=new[]{T("跟随系统","System"),T("浅色","Light"),T("深色","Dark")},SelectedIndex=Math.Max(0,Array.IndexOf(ThemeCodes,NormalizeTheme(settings.Theme))),MinWidth=240};theme.SelectionChanged+=(_,_)=>{settings.Theme=ThemeCodes[theme.SelectedIndex];settings.Save();ApplyTheme();};panel.Children.Add(Card(theme));
    }
    private void AddAboutSettings(StackPanel panel)
    {
        var about=Stack(8);about.Children.Add(Text("New7z · "+typeof(App).Assembly.GetName().Version?.ToString(3),18,true));about.Children.Add(Text(T("MIT 开源许可","MIT open-source license"),14));about.Children.Add(Text(T("本地 7-Zip 26.05 完整格式引擎 · 不依赖系统安装 7-Zip\n支持 7z/ZIP/TAR 创建，其他格式读取按引擎能力处理。RAR 仅解压。\n预览为只读；首版图片最大 32 MiB，文本最大 2 MiB。","Local 7-Zip 26.05 engine · no separate 7-Zip installation needed\nCreates 7z/ZIP/TAR; other formats are read according to engine support. RAR is extract-only.\nPreview is read-only; images up to 32 MiB and text up to 2 MiB."),12));panel.Children.Add(Card(about));
    }
    private bool closingApproved;
    private async void OnClosing(AppWindow sender,AppWindowClosingEventArgs args)
    {
        if(closingApproved||!jobs.Any(j=>j.CanCancel))return;args.Cancel=true;
        try{var d=Dialog(T("任务尚未完成","Tasks are still running"),Text(T("取消任务后退出？已完成输出会保留。","Cancel the tasks and exit? Completed output is kept.")),T("取消任务并退出","Cancel tasks and exit"));d.DefaultButton=ContentDialogButton.Close;if(await ShowDialogAsync(d)!=ContentDialogResult.Primary)return;foreach(var job in jobs.Where(j=>j.CanCancel))job.Cancellation.Cancel();while(jobs.Any(j=>j.CanCancel))await Task.Delay(100);closingApproved=true;Close();}catch{args.Cancel=true;}
    }
}
