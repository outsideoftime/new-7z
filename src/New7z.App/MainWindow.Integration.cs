using System.Diagnostics;
using Microsoft.UI.Xaml.Controls;
using New7z.Core;
using static New7z.Core.Loc;
namespace New7z.App;

public sealed partial class MainWindow
{
    private async Task RunIntegrationAsync(string action)
    {
        var runtime=AppContext.BaseDirectory.TrimEnd('\\');
        var folder=new DirectoryInfo(runtime);
        var root=folder.Parent?.Name=="Versions"?folder.Parent.Parent!.FullName:runtime;
        var script=System.IO.Path.Combine(runtime,"Installer","Integrate-Shell.ps1");
        if(!File.Exists(script))throw new IOException(T("菜单和关联修复组件缺失，请重新安装 New7z。","The menu and association repair component is missing. Reinstall New7z."));
        var executable=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe");
        var info=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
        foreach(var value in new[]{"-NoProfile","-NonInteractive","-ExecutionPolicy","Bypass","-File",script,"-Action",action,"-InstallRoot",root,"-RuntimeDirectory",runtime})info.ArgumentList.Add(value);
        using var process=Process.Start(info)??throw new IOException(T("无法启动系统集成组件。","Cannot start the system integration component."));
        await process.WaitForExitAsync();
        if(process.ExitCode!=0)throw new IOException(T("注册未完成。请查看安装目录中的 integration-error.txt。","Registration did not complete. See integration-error.txt in the install folder."));
    }
    private void ShowPendingIntegrationError()
    {
        if(AppEnvironment.IsPackaged)return;
        var folder=new DirectoryInfo(AppContext.BaseDirectory.TrimEnd('\\'));
        var root=folder.Parent?.Name=="Versions"?folder.Parent.Parent!.FullName:folder.FullName;
        if(!File.Exists(System.IO.Path.Combine(root,"integration-error.txt")))return;
        Notify(T("右键菜单注册未完成，可在设置 → 资源管理器右键菜单中点击「修复右键菜单注册」。","Context menu registration did not complete. Repair it in Settings → Explorer context menu."),InfoBarSeverity.Warning);
    }
    private void AddPackagedAssociationSettings(StackPanel panel)
    {
        var card=Stack(14);
        card.Children.Add(Text(T("文件关联","File associations"),18,true));
        card.Children.Add(Text(T("Microsoft Store 版本在安装包里声明了 7z、ZIP、RAR、TAR、GZ、XZ、BZ2、ISO 的关联，它们会自动出现在「打开方式」列表中。","The Microsoft Store build declares associations for 7z, ZIP, RAR, TAR, GZ, XZ, BZ2 and ISO in its package, so they appear in the \"Open with\" list automatically."),12));
        card.Children.Add(Button(T("选择默认应用","Choose default apps"),(_,_)=>
        {
            try{Process.Start(new ProcessStartInfo("ms-settings:defaultapps"){UseShellExecute=true});}
            catch(Exception ex){Notify(T("无法打开 Windows 设置：","Cannot open Windows Settings: ")+ex.Message,InfoBarSeverity.Error);}
        },true));
        card.Children.Add(Text(T("默认程序由你在 Windows 设置中确认，不会强制替换现有选择。","You confirm default apps in Windows Settings; New7z never forces a replacement."),12));
        panel.Children.Add(Card(card));
    }
    private void AddAssociationSettings(StackPanel panel)
    {
        var card=Stack(14);card.Children.Add(Text(T("文件关联","File associations"),18,true));
        card.Children.Add(Text(T("支持 7z、ZIP、RAR、TAR、GZ、XZ、BZ2、ISO。注册后可在 Windows「默认应用」中选择 New7z。","Supports 7z, ZIP, RAR, TAR, GZ, XZ, BZ2 and ISO. After registering, choose New7z in Windows Default apps."),12));
        var feedback=new InfoBar{IsOpen=false,IsClosable=false};
        var register=Button(T("注册 / 修复文件关联","Register / repair associations"),(_,_)=>{});
        register.Click+=async(_,_)=>
        {
            register.IsEnabled=false;feedback.IsOpen=false;
            try{await RunIntegrationAsync("Associate");feedback.Severity=InfoBarSeverity.Success;feedback.Title=T("已加入默认应用列表","Added to the Default apps list");feedback.Message=T("点击「选择默认应用」，在 Windows 中选择要由 New7z 打开的格式。","Click \"Choose default apps\" and pick the formats New7z should open in Windows.");}
            catch(Exception ex){feedback.Severity=InfoBarSeverity.Error;feedback.Title=T("文件关联未完成","File associations were not registered");feedback.Message=ex.Message;}
            finally{register.IsEnabled=true;feedback.IsOpen=true;}
        };
        card.Children.Add(register);
        card.Children.Add(Button(T("选择默认应用","Choose default apps"),(_,_)=>
        {
            try{Process.Start(new ProcessStartInfo("ms-settings:defaultapps?registeredAppUser="+Uri.EscapeDataString("New7z")){UseShellExecute=true});}
            catch(Exception ex){feedback.Severity=InfoBarSeverity.Error;feedback.Title=T("无法打开 Windows 设置","Cannot open Windows Settings");feedback.Message=ex.Message;feedback.IsOpen=true;}
        },true));
        card.Children.Add(Text(T("默认程序由你在 Windows 设置中确认，不会强制替换现有选择。","You confirm default apps in Windows Settings; New7z never forces a replacement."),12));
        card.Children.Add(feedback);panel.Children.Add(Card(card));
    }
}
