using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using New7z.Core;
using static New7z.Core.Loc;

namespace New7z.App;

public sealed partial class MainWindow
{
    private bool updateBusy;
    private static Version AppVersion=>typeof(App).Assembly.GetName().Version??new Version(0,1,0);
    private void AddUpdateSettings(StackPanel panel)
    {
        var section=Stack(12);
        section.Children.Add(Text(T("应用更新","App updates"),18,true));
        section.Children.Add(Text(T($"当前版本 {AppVersion.ToString(3)} · 从 GitHub Releases 检查最新版本，下载后校验 SHA-256 再安装。",$"Current version {AppVersion.ToString(3)} · checks GitHub Releases, verifies SHA-256, then installs."),12));
        var auto=new ToggleSwitch{Header=T("启动时自动检查并下载更新","Check and download updates at startup"),IsOn=settings.AutoCheckUpdates,OnContent=T("开","On"),OffContent=T("关","Off")};
        auto.Toggled+=(_,_)=>{settings.AutoCheckUpdates=auto.IsOn;settings.Save();};
        section.Children.Add(auto);
        section.Children.Add(Button(T("检查更新","Check for updates"),async(_,_)=>await CheckForUpdatesAsync(true)));
        panel.Children.Add(Card(section));
    }
    private async Task CheckForUpdatesAsync(bool interactive)
    {
        if(updateBusy)return;
        updateBusy=true;
        try
        {
            if(interactive)StatusText.Text=T("正在检查更新…","Checking for updates…");
            var update=await GitHubUpdateChecker.CheckAsync(AppVersion);
            if(update==null){if(interactive)Notify(T($"已是最新版本（{AppVersion.ToString(3)}）。",$"You have the latest version ({AppVersion.ToString(3)})."),InfoBarSeverity.Success);return;}
            if(!interactive){await DownloadAndPromptAsync(update,true);return;}
            var panel=Stack(12);
            panel.Children.Add(Text(T($"当前版本 {AppVersion.ToString(3)} → 最新版本 {update.Version.ToString(3)}",$"Current {AppVersion.ToString(3)} → latest {update.Version.ToString(3)}"),12));
            var notes=Text(string.IsNullOrWhiteSpace(update.Notes)?T("（该版本没有发布说明）","(No release notes for this version)"):update.Notes,12);
            notes.TextWrapping=TextWrapping.Wrap;
            panel.Children.Add(new ScrollViewer{Content=notes,MaxHeight=260,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
            panel.Children.Add(Text(T("安装包从 GitHub Releases 下载，运行前会与同版本发布的 SHA256SUMS.txt 比对。","The installer is downloaded from GitHub Releases and verified against SHA256SUMS.txt before running."),12));
            var dialog=Dialog(T("发现新版本 ","New version ")+update.Version.ToString(3),new ScrollViewer{Content=panel,MaxWidth=560},T("下载并安装","Download and install"));
            if(await ShowDialogAsync(dialog)!=ContentDialogResult.Primary)return;
            await DownloadAndPromptAsync(update,false);
        }
        catch(Exception ex){if(interactive)Notify(T("检查更新失败：","Update check failed: ")+ex.Message,InfoBarSeverity.Warning);}
        finally{updateBusy=false;if(interactive)StatusText.Text=T("就绪 · 本地 7-Zip 26.05 引擎","Ready · local 7-Zip 26.05 engine");}
    }
    private async Task DownloadAndPromptAsync(UpdateInfo update,bool silent)
    {
        var target=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"New7z-update",update.InstallerName);
        try
        {
            if(jobs.Any(j=>j.CanCancel)){if(!silent)Notify(T("有任务正在运行，完成后再安装更新。","Tasks are running; install the update after they finish."),InfoBarSeverity.Warning);return;}
            var verified=false;
            if(File.Exists(target)){try{verified=await GitHubUpdateChecker.VerifyChecksumAsync(update,target);}catch{}}
            if(!verified)
            {
                var progress=new Progress<double>(percent=>{Message.Message=T($"正在下载 New7z {update.Version.ToString(3)} · {percent:0}%",$"Downloading New7z {update.Version.ToString(3)} · {percent:0}%");Message.Severity=InfoBarSeverity.Informational;Message.ActionButton=null;Message.IsOpen=true;});
                await GitHubUpdateChecker.DownloadAsync(update,target,progress);
                if(!await GitHubUpdateChecker.VerifyChecksumAsync(update,target))
                {
                    try{File.Delete(target);}catch{}
                    Notify(T("更新包校验失败，已丢弃下载内容。","Update verification failed; the download was discarded."),InfoBarSeverity.Error);
                    return;
                }
            }
            Message.IsOpen=false;
            var confirm=Dialog(T("安装新版本？","Install the new version?"),Text(T($"New7z {update.Version.ToString(3)} 已下载并通过 SHA-256 校验。\n\n点击「立即安装」后应用会退出并运行安装程序；完成后可从开始菜单再次打开 New7z。",$"New7z {update.Version.ToString(3)} has been downloaded and passed SHA-256 verification.\n\nInstall now to exit the app and run the installer; reopen New7z from the Start menu afterwards.")),T("立即安装","Install now"));
            confirm.DefaultButton=ContentDialogButton.Close;
            if(await ShowDialogAsync(confirm)!=ContentDialogResult.Primary)return;
            LaunchInstaller(target);
        }
        catch(Exception ex){if(silent)Message.IsOpen=false;else Notify(T("下载更新失败：","Update download failed: ")+ex.Message,InfoBarSeverity.Error);}
    }
    private void LaunchInstaller(string installer)
    {
        // Inno Setup cannot replace files this process still maps, so let New7z exit first.
        var command=$"/c ping 127.0.0.1 -n 3 > nul & \"{installer}\" /SILENT /NORESTART /SUPPRESSMSGBOXES";
        Process.Start(new ProcessStartInfo("cmd.exe",command){CreateNoWindow=true,UseShellExecute=false});
        closingApproved=true;
        Application.Current.Exit();
    }
}
