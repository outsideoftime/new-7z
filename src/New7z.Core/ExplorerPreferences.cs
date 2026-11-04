using Microsoft.Win32;
using System.Runtime.Versioning;

namespace New7z.Core;

[Flags]
public enum ExplorerCommands
{
    None=0,Open=1,ExtractNamed=2,ExtractTo=4,ExtractHere=8,Test=16,
    Create=32,Create7z=64,CreateZip=128,All=255
}

public sealed record ExplorerPreferences(bool MenuEnabled=true,ExplorerCommands Commands=ExplorerCommands.All,bool AutoCloseOnSuccess=false);

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
        return new(Value(key,"MenuEnabled",1)!=0,(ExplorerCommands)(Value(key,"Commands",255)&255),Value(key,"AutoCloseOnSuccess",0)!=0);
    }
    public void Save(ExplorerPreferences value)
    {
        using var key=hive.CreateSubKey(path,true)??throw new IOException("无法保存资源管理器设置。");
        key.SetValue("Commands",(int)value.Commands&255,RegistryValueKind.DWord);
        key.SetValue("AutoCloseOnSuccess",value.AutoCloseOnSuccess?1:0,RegistryValueKind.DWord);
        key.SetValue("MenuEnabled",value.MenuEnabled?1:0,RegistryValueKind.DWord);
    }
}
