using Microsoft.Win32;
using System.Runtime.Versioning;

namespace New7z.Core;

[Flags]
public enum ExplorerCommands
{
    None=0,Open=1,ExtractNamed=2,ExtractTo=4,ExtractHere=8,Test=16,
    Create=32,Create7z=64,CreateZip=128,All=255
}

public sealed record ExplorerPreferences(bool MenuEnabled=true,ExplorerCommands Commands=ExplorerCommands.All,bool AutoCloseOnSuccess=false,bool HideWindowsCompressMenu=false);

// Both the application and the native Explorer extension read this per-user key.
[SupportedOSPlatform("windows")]
public sealed class ExplorerPreferencesStore
{
    public const string RegistryPath=@"Software\New7z\Explorer";
    private readonly RegistryKey hive;
    private readonly string path;
    public ExplorerPreferencesStore():this(Registry.CurrentUser,RegistryPath){}
    public ExplorerPreferencesStore(RegistryKey hive,string path){this.hive=hive;this.path=path;}
    public ExplorerPreferences Read()
    {
        using var key=hive.OpenSubKey(path);
        static int Value(RegistryKey? key,string name,int fallback)=>key?.GetValue(name) is int value?value:fallback;
        return new(Value(key,"MenuEnabled",1)!=0,(ExplorerCommands)(Value(key,"Commands",255)&255),Value(key,"AutoCloseOnSuccess",0)!=0,Value(key,"HideWindowsCompressMenu",0)!=0);
    }
    public void Save(ExplorerPreferences value)
    {
        using var key=hive.CreateSubKey(path,true)??throw new IOException(Loc.T("无法保存资源管理器设置。", "Could not save Explorer settings."));
        key.SetValue("Commands",(int)value.Commands&255,RegistryValueKind.DWord);
        key.SetValue("AutoCloseOnSuccess",value.AutoCloseOnSuccess?1:0,RegistryValueKind.DWord);
        key.SetValue("MenuEnabled",value.MenuEnabled?1:0,RegistryValueKind.DWord);
        key.SetValue("HideWindowsCompressMenu",value.HideWindowsCompressMenu?1:0,RegistryValueKind.DWord);
    }
}

// Blocks only the current user's Windows 11 "Compress to" shell command. The
// previous Blocked value is backed up so the user's registry state can be restored.
[SupportedOSPlatform("windows")]
public sealed class WindowsCompressMenuSettingsStore
{
    public const string BlockedRegistryPath=@"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";
    public const string CompressToClsid="{7AE6900F-6EB0-44A2-9CA1-DB2F7EF352AF}";
    // Prefix of the block entry value data. Its presence proves New7z wrote the entry,
    // so an orphaned entry can be removed even when the backup key was lost.
    private const string MarkerPrefix="New7z:";
    private const string BackupKeyName="WindowsCompressMenuBackup";
    private const string HadPreviousValueName="HadPreviousValue";
    private const string PreviousValueName="PreviousValue";
    private const string PreviousKindName="PreviousKind";
    private const string MarkerName="Marker";
    private readonly RegistryKey hive;
    private readonly string preferencesPath;
    private readonly string blockedPath;

    public WindowsCompressMenuSettingsStore():this(Registry.CurrentUser,ExplorerPreferencesStore.RegistryPath,BlockedRegistryPath){}
    public WindowsCompressMenuSettingsStore(RegistryKey hive,string preferencesPath,string blockedPath)
    {
        this.hive=hive;this.preferencesPath=preferencesPath;this.blockedPath=blockedPath;
    }

    // Returns false when a different tool changed the block-list value while New7z owned it;
    // that external value is kept instead of being overwritten during restore.
    public bool SetHidden(bool hidden)
    {
        var preferences=new ExplorerPreferencesStore(hive,preferencesPath);
        var current=preferences.Read();
        using var settings=hive.CreateSubKey(preferencesPath,true)??throw new IOException(Loc.T("无法保存 Windows 压缩菜单设置。", "Could not save the Windows compression menu setting."));
        var hasBackup=settings.GetSubKeyNames().Any(name=>string.Equals(name,BackupKeyName,StringComparison.OrdinalIgnoreCase));

        if(!hidden)
        {
            if(current.HideWindowsCompressMenu&&!hasBackup)
            {
                // The backup was lost; only a marker this application wrote may be removed,
                // otherwise a foreign block entry could be destroyed.
                if(!RemoveOwnMarker())throw new IOException(Loc.T("Windows 菜单的恢复信息缺失，且当前屏蔽项不是 New7z 写入的；为避免覆盖其他注册表设置，New7z 未修改该项。", "Restore information for the Windows menu is missing and the current block entry was not written by New7z; New7z left it unchanged to avoid overwriting other registry settings."));
                preferences.Save(current with{HideWindowsCompressMenu=false});
                NotifyExplorer();
                return true;
            }
            var restored=true;
            if(hasBackup)restored=RestorePreviousValue(settings);
            if(current.HideWindowsCompressMenu)preferences.Save(current with{HideWindowsCompressMenu=false});
            NotifyExplorer();
            return restored;
        }

        if(current.HideWindowsCompressMenu&&hasBackup)return true;
        // Without a backup the previous value is unknown; re-hiding would record the current
        // marker as the original value and make a later restore keep the menu hidden.
        if(!hasBackup&&current.HideWindowsCompressMenu)throw new IOException(Loc.T("Windows 菜单的恢复信息缺失；为避免覆盖其他注册表设置，New7z 未修改该项。", "Restore information for the Windows menu is missing; New7z left the entry unchanged to avoid overwriting other registry settings."));
        if(hasBackup)RestorePreviousValue(settings); // Recover an interrupted earlier update.

        using var blocked=hive.CreateSubKey(blockedPath,true)??throw new IOException(Loc.T("无法写入当前用户的 Windows Shell 设置。", "Could not write the current user's Windows Shell settings."));
        var existingName=blocked.GetValueNames().FirstOrDefault(name=>string.Equals(name,CompressToClsid,StringComparison.OrdinalIgnoreCase));
        var hadPreviousValue=existingName is not null;
        object? previousValue=null;
        RegistryValueKind? previousKind=null;
        if(hadPreviousValue)
        {
            previousValue=blocked.GetValue(existingName!,null,RegistryValueOptions.DoNotExpandEnvironmentNames);
            previousKind=blocked.GetValueKind(existingName!);
        }

        var marker=MarkerPrefix+Guid.NewGuid().ToString("N");
        using(var backup=settings.CreateSubKey(BackupKeyName,true)??throw new IOException(Loc.T("无法备份 Windows Shell 设置。", "Could not back up the Windows Shell settings.")))
        {
            backup.SetValue(HadPreviousValueName,hadPreviousValue?1:0,RegistryValueKind.DWord);
            backup.SetValue(MarkerName,marker,RegistryValueKind.String);
            if(hadPreviousValue)
            {
                backup.SetValue(PreviousValueName,previousValue!,previousKind!.Value);
                backup.SetValue(PreviousKindName,(int)previousKind.Value,RegistryValueKind.DWord);
            }
        }

        try
        {
            blocked.SetValue(CompressToClsid,marker,RegistryValueKind.String);
            preferences.Save(current with{HideWindowsCompressMenu=true});
        }
        catch
        {
            RestorePreviousValue(settings);
            throw;
        }
        NotifyExplorer();
        return true;
    }

    private bool RemoveOwnMarker()
    {
        using var blocked=hive.OpenSubKey(blockedPath,true);
        if(blocked is null)return true;
        var name=blocked.GetValueNames().FirstOrDefault(candidate=>string.Equals(candidate,CompressToClsid,StringComparison.OrdinalIgnoreCase));
        if(name is null)return true;
        if(blocked.GetValueKind(name)!=RegistryValueKind.String||blocked.GetValue(name,null,RegistryValueOptions.DoNotExpandEnvironmentNames) is not string value||!value.StartsWith(MarkerPrefix,StringComparison.Ordinal))return false;
        blocked.DeleteValue(name,false);
        return true;
    }

    private bool RestorePreviousValue(RegistryKey settings)
    {
        string? marker;
        var restored=false;
        using(var backup=settings.OpenSubKey(BackupKeyName,false))
        {
            if(backup is null)return true;
            marker=backup.GetValue(MarkerName) as string;
            using var blocked=hive.OpenSubKey(blockedPath,true);
            if(marker is not null&&blocked is not null)
            {
                var name=blocked.GetValueNames().FirstOrDefault(candidate=>string.Equals(candidate,CompressToClsid,StringComparison.OrdinalIgnoreCase));
                var currentValue=name is null?null:blocked.GetValue(name,null,RegistryValueOptions.DoNotExpandEnvironmentNames);
                var currentKind=name is null?(RegistryValueKind?)null:blocked.GetValueKind(name);
                if(name is not null&&currentKind==RegistryValueKind.String&&string.Equals(currentValue as string,marker,StringComparison.Ordinal))
                {
                    var hadPrevious=backup.GetValue(HadPreviousValueName) is int value&&value!=0;
                    if(hadPrevious)
                    {
                        var previous=backup.GetValue(PreviousValueName,null,RegistryValueOptions.DoNotExpandEnvironmentNames);
                        var kind=(RegistryValueKind)(backup.GetValue(PreviousKindName) is int rawKind?rawKind:(int)RegistryValueKind.String);
                        if(previous is not null)blocked.SetValue(CompressToClsid,previous,kind);
                    }
                    else blocked.DeleteValue(CompressToClsid,false);
                    restored=true;
                }
            }
        }
        settings.DeleteSubKeyTree(BackupKeyName,false);
        return restored;
    }

    private static void NotifyExplorer()=>NativeMethods.NotifyShellAssociationsChanged();
}
