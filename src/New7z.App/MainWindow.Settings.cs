using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using New7z.Core;
namespace New7z.App;

public sealed partial class MainWindow
{
    private void AddExplorerSettings(StackPanel panel)
    {
        var store=new ExplorerPreferencesStore();ExplorerPreferences saved;
        try{saved=store.Read();}
        catch(Exception ex){panel.Children.Add(Card(Text("无法读取右键菜单设置："+ex.Message)));return;}
        var section=Stack(14);section.Children.Add(Text("资源管理器右键菜单",18,true));
        var enabled=new ToggleSwitch{Header="显示 New7z 右键菜单",IsOn=saved.MenuEnabled,OnContent="显示",OffContent="不显示"};section.Children.Add(enabled);
        section.Children.Add(Text("选择命令后点击「应用菜单设置」。菜单会根据文件类型自动筛选。",12));
        var commands=Stack(6);section.Children.Add(commands);
        var choices=new List<(ExplorerCommands Flag,CheckBox Box)>();
        foreach(var (flag,label) in new[]{
            (ExplorerCommands.Open,"用 New7z 打开"),(ExplorerCommands.ExtractNamed,"解压到同名文件夹 / 分别解压"),
            (ExplorerCommands.ExtractTo,"解压到…"),(ExplorerCommands.ExtractHere,"解压到当前文件夹"),(ExplorerCommands.Test,"测试完整性"),
            (ExplorerCommands.Create,"新建压缩包…"),(ExplorerCommands.Create7z,"压缩为 7z"),(ExplorerCommands.CreateZip,"压缩为 ZIP")})
        {
            var box=new CheckBox{Content=label,IsChecked=saved.Commands.HasFlag(flag)};commands.Children.Add(box);choices.Add((flag,box));
        }
        var save=Button("应用菜单设置",(_,_)=>{} ,true);section.Children.Add(save);
        var repair=Button("修复右键菜单注册",(_,_)=>{});section.Children.Add(repair);
        var feedback=new InfoBar{IsOpen=false,IsClosable=false};section.Children.Add(feedback);
        var popup=Stack(14);popup.Children.Add(Text("任务弹出窗口",18,true));
        var closeMode=new ComboBox{Header="任务成功后",ItemsSource=new[]{"手动关闭（默认）","自动关闭"},SelectedIndex=saved.AutoCloseOnSuccess?1:0,HorizontalAlignment=HorizontalAlignment.Stretch};popup.Children.Add(closeMode);
        popup.Children.Add(Text("自动关闭仅用于成功完成的任务。失败或取消时保留窗口，便于查看原因。",12));
        var popupFeedback=new InfoBar{IsOpen=false,IsClosable=false};popup.Children.Add(popupFeedback);
        bool applying=false;
        repair.Click+=async(_,_)=>
        {
            repair.IsEnabled=false;feedback.IsOpen=false;
            try{await RunIntegrationAsync("Repair");feedback.Severity=InfoBarSeverity.Success;feedback.Title="兼容菜单已注册";feedback.Message="右键文件或文件夹 → 显示更多选项 → New7z。已通知资源管理器刷新。";}
            catch(Exception ex){feedback.Severity=InfoBarSeverity.Error;feedback.Title="无法修复菜单";feedback.Message=ex.Message;}
            finally{repair.IsEnabled=true;feedback.IsOpen=true;}
        };
        void UpdateDirty()
        {
            foreach(var item in choices)item.Box.IsEnabled=enabled.IsOn;
            var mask=choices.Aggregate(ExplorerCommands.None,(value,item)=>item.Box.IsChecked==true?value|item.Flag:value);
            save.IsEnabled=!applying&&(enabled.IsOn!=saved.MenuEnabled||mask!=saved.Commands);
        }
        enabled.Toggled+=(_,_)=>UpdateDirty();foreach(var item in choices){item.Box.Checked+=(_,_)=>UpdateDirty();item.Box.Unchecked+=(_,_)=>UpdateDirty();}
        save.Click+=(_,_)=>
        {
            if(applying)return;applying=true;save.IsEnabled=false;feedback.IsOpen=false;
            try
            {
                var mask=choices.Aggregate(ExplorerCommands.None,(value,item)=>item.Box.IsChecked==true?value|item.Flag:value);
                if(enabled.IsOn&&mask==ExplorerCommands.None)throw new IOException("请至少勾选一个命令，或关闭 New7z 右键菜单。");
                // Preserve a popup policy that may have been changed in another window.
                var updated=store.Read() with{MenuEnabled=enabled.IsOn,Commands=mask};store.Save(updated);saved=updated;
                feedback.Severity=InfoBarSeverity.Success;feedback.Title="菜单设置已保存";feedback.Message="下次打开右键菜单时生效，部分窗口可能需要重新打开。";
            }
            catch(Exception ex){feedback.Severity=InfoBarSeverity.Error;feedback.Title="未能应用菜单设置";feedback.Message=ex.Message;}
            finally{applying=false;feedback.IsOpen=true;UpdateDirty();}
        };
        bool restoring=false;
        closeMode.SelectionChanged+=(_,_)=>
        {
            if(restoring)return;
            try
            {
                var current=store.Read() with{AutoCloseOnSuccess=closeMode.SelectedIndex==1};store.Save(current);saved=saved with{AutoCloseOnSuccess=current.AutoCloseOnSuccess};
                popupFeedback.Severity=InfoBarSeverity.Success;popupFeedback.Title="关闭方式已保存";popupFeedback.Message=current.AutoCloseOnSuccess?"成功后自动关闭；失败和取消时保留。":"任务结束后保留窗口，由你手动关闭。";
            }
            catch(Exception ex)
            {
                restoring=true;closeMode.SelectedIndex=saved.AutoCloseOnSuccess?1:0;restoring=false;
                popupFeedback.Severity=InfoBarSeverity.Error;popupFeedback.Title="未能保存关闭方式";popupFeedback.Message=ex.Message;
            }
            popupFeedback.IsOpen=true;
        };
        UpdateDirty();panel.Children.Add(Card(section));panel.Children.Add(Card(popup));
        var system=Stack(14);system.Children.Add(Text("Windows 自带压缩菜单",18,true));
        var systemStore=new WindowsCompressMenuSettingsStore();
        var hideSystemMenu=new ToggleSwitch{Header="隐藏 Windows「压缩到」菜单",IsOn=saved.HideWindowsCompressMenu,OnContent="隐藏",OffContent="显示"};system.Children.Add(hideSystemMenu);
        var systemMenuSupported=OperatingSystem.IsWindowsVersionAtLeast(10,0,26100);
        hideSystemMenu.IsEnabled=systemMenuSupported;
        system.Children.Add(Text(systemMenuSupported
            ?"适用于 Windows 11 24H2 及更新版本，仅影响当前用户。保存后会通知资源管理器刷新；如菜单仍显示，请重新启动资源管理器。"
            :"此选项需要 Windows 11 24H2 或更新版本。",12));
        var systemFeedback=new InfoBar{IsOpen=false,IsClosable=false};system.Children.Add(systemFeedback);
        bool changingSystemMenu=false,restoringSystemMenu=false;
        hideSystemMenu.Toggled+=(_,_)=>
        {
            if(restoringSystemMenu||changingSystemMenu)return;
            changingSystemMenu=true;hideSystemMenu.IsEnabled=false;systemFeedback.IsOpen=false;
            try
            {
                var restored=systemStore.SetHidden(hideSystemMenu.IsOn);saved=saved with{HideWindowsCompressMenu=hideSystemMenu.IsOn};
                if(restored)
                {
                    systemFeedback.Severity=InfoBarSeverity.Success;systemFeedback.Title=hideSystemMenu.IsOn?"Windows 压缩菜单已隐藏":"New7z 已恢复修改前的 Windows 菜单设置";
                    systemFeedback.Message="已通知资源管理器刷新。重新打开右键菜单；如仍未更新，请重新启动资源管理器。";
                }
                else
                {
                    systemFeedback.Severity=InfoBarSeverity.Warning;systemFeedback.Title="已保留其他程序的设置";
                    systemFeedback.Message="检测到 Windows 菜单设置已被其他程序修改，New7z 保留了该值；菜单最终显示状态可能受其他设置影响。";
                }
            }
            catch(Exception ex)
            {
                restoringSystemMenu=true;hideSystemMenu.IsOn=saved.HideWindowsCompressMenu;restoringSystemMenu=false;
                systemFeedback.Severity=InfoBarSeverity.Error;systemFeedback.Title="未能更新 Windows 压缩菜单设置";systemFeedback.Message=ex.Message;
            }
            finally{changingSystemMenu=false;hideSystemMenu.IsEnabled=true;systemFeedback.IsOpen=true;}
        };
        panel.Children.Add(Card(system));
        AddAssociationSettings(panel);
    }
}
