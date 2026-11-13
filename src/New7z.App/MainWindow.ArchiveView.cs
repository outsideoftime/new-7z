using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Markup;
using New7z.Core;
using static New7z.Core.Loc;

namespace New7z.App;

public sealed partial class MainWindow
{
    private ArchiveSortColumn sortColumn=ArchiveSortColumn.Name;
    private bool sortDescending,searchComposing;
    private int visibleRowCount;
    private bool updatingRows;
    private bool selectionUpdateQueued;
    private string? cachedFolder;
    private IReadOnlyList<ArchiveEntry>? cachedEntries;
    private IReadOnlyList<ArchiveEntry> cachedFolderRows=[];
    private readonly Dictionary<ArchiveSortColumn,Button> sortHeaders=[];
    private TextBlock? folderLabel,archiveFileCountText,archiveOriginalSizeText,archivePackedSizeText;
    private Border? archivePropertiesPanel;
    private ScrollViewer? archiveTableScroll;
    private DispatcherTimer? searchTimer;
    private void SetFolder(string value)
    {
        folder=value;query="";
        if(folderLabel!=null)folderLabel.Text=T("根目录","Root")+(folder.Length>0?" / "+folder:"");
        if(searchBox!=null)searchBox.Text="";
        RefreshRows();
    }
    private void ScheduleSearch()
    {
        searchTimer?.Stop();
        if(searchComposing)return;
        if(searchBox?.Text.Length==0){query="";RefreshRows();return;}
        searchTimer??=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(180)};
        searchTimer.Tick-=_SearchTick;searchTimer.Tick+=_SearchTick;searchTimer.Start();
    }
    private void _SearchTick(object? sender,object e){searchTimer?.Stop();query=searchBox?.Text??"";RefreshRows();}
    private void UpdateSortHeaders()
    {
        foreach(var (column,button) in sortHeaders)
        {
            var label=column switch{ArchiveSortColumn.Size=>T("大小","Size"),ArchiveSortColumn.PackedSize=>T("压缩后","Packed"),ArchiveSortColumn.Modified=>T("修改时间","Modified"),_=>T("名称","Name")};
            button.Content=label+(column==sortColumn?(sortDescending?" ↓":" ↑"):"");
            AutomationProperties.SetName(button,label+(column==sortColumn?(sortDescending?T("，降序"," descending"):T("，升序"," ascending")):T("，排序"," sort")));
        }
    }
    private void UpdateSelectionDetails()
    {
        if(fileList==null||updatingRows)return;
        countLabel!.Text=visibleRowCount==0?T("当前目录没有匹配的项目。","No matching items in this folder."):T($"{visibleRowCount} 个项目 · 已选择 {fileList.SelectedItems.Count} 项",$"{visibleRowCount} items · {fileList.SelectedItems.Count} selected");
    }
    private Border CreateArchivePropertiesPanel()
    {
        // These totals belong to the archive, independent of the visible folder,
        // search and selection. Recalculate only when the archive view is rebuilt.
        var panel=(Border)XamlReader.Load("""
        <Border xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" BorderBrush="{ThemeResource DividerStrokeColorDefaultBrush}" BorderThickness="1,0,0,0" Padding="20,4,0,0">
          <ScrollViewer HorizontalScrollMode="Disabled" HorizontalScrollBarVisibility="Disabled" VerticalScrollBarVisibility="Auto">
            <StackPanel Spacing="24" Padding="0,0,12,16">
              <StackPanel Spacing="8">
                <TextBlock x:Name="PropertiesHeading" FontSize="16" FontWeight="SemiBold" TextWrapping="Wrap"/>
                <TextBlock x:Name="ArchiveName" FontSize="12" Foreground="{ThemeResource TextFillColorSecondaryBrush}" TextWrapping="Wrap" IsTextSelectionEnabled="True"/>
              </StackPanel>
              <StackPanel Spacing="4">
                <TextBlock x:Name="FileCountLabel" FontSize="12" Foreground="{ThemeResource TextFillColorSecondaryBrush}" TextWrapping="Wrap"/>
                <TextBlock x:Name="FileCount" FontSize="18" FontWeight="SemiBold" TextWrapping="Wrap" IsTextSelectionEnabled="True"/>
              </StackPanel>
              <StackPanel Spacing="4">
                <TextBlock x:Name="OriginalSizeLabel" FontSize="12" Foreground="{ThemeResource TextFillColorSecondaryBrush}" TextWrapping="Wrap"/>
                <TextBlock x:Name="OriginalSize" FontSize="18" FontWeight="SemiBold" TextWrapping="Wrap" IsTextSelectionEnabled="True"/>
              </StackPanel>
              <StackPanel Spacing="4">
                <TextBlock x:Name="PackedSizeLabel" FontSize="12" Foreground="{ThemeResource TextFillColorSecondaryBrush}" TextWrapping="Wrap"/>
                <TextBlock x:Name="PackedSize" FontSize="18" FontWeight="SemiBold" TextWrapping="Wrap" IsTextSelectionEnabled="True"/>
              </StackPanel>
            </StackPanel>
          </ScrollViewer>
        </Border>
        """);
        TextBlock Label(string name,string value){var label=(TextBlock)panel.FindName(name);label.Text=value;return label;}
        var heading=Label("PropertiesHeading",T("压缩包属性","Archive properties"));
        AutomationProperties.SetHeadingLevel(heading,Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2);
        Label("ArchiveName",System.IO.Path.GetFileName(archive)!);
        var countLabel=Label("FileCountLabel",T("文件数量","Files"));
        var originalLabel=Label("OriginalSizeLabel",T("原始大小","Original size"));
        var packedLabel=Label("PackedSizeLabel",T("压缩后","Packed"));
        BigInteger size=0;var files=0;
        foreach(var entry in entries.Where(e=>!e.IsDirectory)){files++;size+=entry.Size;}
        archiveFileCountText=Label("FileCount",files.ToString("N0"));
        archiveOriginalSizeText=Label("OriginalSize",size<=ulong.MaxValue?ByteSize.Format((ulong)size):size.ToString()+" B");
        // The whole archive size includes headers and works for solid archives,
        // whose individual entries may not have a packed-size value.
        string packedSize;
        try{packedSize=ByteSize.Format((ulong)new FileInfo(archive!).Length);}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){packedSize=T("无法读取","Unavailable");}
        archivePackedSizeText=Label("PackedSize",packedSize);
        AutomationProperties.SetLabeledBy(archiveFileCountText,countLabel);
        AutomationProperties.SetLabeledBy(archiveOriginalSizeText,originalLabel);
        AutomationProperties.SetLabeledBy(archivePackedSizeText,packedLabel);
        return panel;
    }
    private void QueueSelectionDetails()
    {
        if(updatingRows||selectionUpdateQueued)return;selectionUpdateQueued=true;
        DispatcherQueue.TryEnqueue(()=>{selectionUpdateQueued=false;UpdateSelectionDetails();});
    }
}
