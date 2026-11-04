using System.Diagnostics;
using Microsoft.UI.Xaml.Controls;
namespace New7z.App;

public sealed partial class MainWindow
{
    private async Task RunIntegrationAsync(string action)
    {
        var runtime=AppContext.BaseDirectory.TrimEnd('\\');
        var folder=new DirectoryInfo(runtime);
        var root=folder.Parent?.Name=="Versions"?folder.Parent.Parent!.FullName:runtime;
        var script=System.IO.Path.Combine(runtime,"Installer","Integrate-Shell.ps1");
        if(!File.Exists(script))throw new IOException("菜单和关联修复组件缺失，请重新安装 New7z。");
        var executable=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe");
        var info=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
        foreach(var value in new[]{"-NoProfile","-NonInteractive","-ExecutionPolicy","Bypass","-File",script,"-Action",action,"-InstallRoot",root,"-RuntimeDirectory",runtime})info.ArgumentList.Add(value);
        using var process=Process.Start(info)??throw new IOException("无法启动系统集成组件。");
        await process.WaitForExitAsync();
        if(process.ExitCode!=0)throw new IOException("注册未完成。请查看安装目录中的 integration-error.txt。");
    }
    private void AddAssociationSettings(StackPanel panel)
    {
        var card=Stack(14);card.Children.Add(Text("文件关联",18,true));
        card.Children.Add(Text("支持 7z、ZIP、RAR、TAR、GZ、XZ、BZ2、ISO。注册后可在 Windows「默认应用」中选择 New7z。",12));
        var feedback=new InfoBar{IsOpen=false,IsClosable=false};
        var register=Button("注册 / 修复文件关联",(_,_)=>{});
        register.Click+=async(_,_)=>
        {
            register.IsEnabled=false;feedback.IsOpen=false;
            try{await RunIntegrationAsync("Associate");feedback.Severity=InfoBarSeverity.Success;feedback.Title="已加入默认应用列表";feedback.Message="点击「选择默认应用」，在 Windows 中选择要由 New7z 打开的格式。";}
            catch(Exception ex){feedback.Severity=InfoBarSeverity.Error;feedback.Title="文件关联未完成";feedback.Message=ex.Message;}
            finally{register.IsEnabled=true;feedback.IsOpen=true;}
        };
        card.Children.Add(register);
        card.Children.Add(Button("选择默认应用",(_,_)=>
        {
            try{Process.Start(new ProcessStartInfo("ms-settings:defaultapps?registeredAppUser="+Uri.EscapeDataString("New7z")){UseShellExecute=true});}
            catch(Exception ex){feedback.Severity=InfoBarSeverity.Error;feedback.Title="无法打开 Windows 设置";feedback.Message=ex.Message;feedback.IsOpen=true;}
        },true));
        card.Children.Add(Text("默认程序由你在 Windows 设置中确认，不会强制替换现有选择。",12));
        card.Children.Add(feedback);panel.Children.Add(Card(card));
    }
}
