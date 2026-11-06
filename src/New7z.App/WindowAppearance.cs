using Microsoft.UI.Xaml;
namespace New7z.App;

internal static class WindowAppearance
{
    public static void ApplyIcon(Window window)=>window.AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory,"Assets","New7z.ico"));
    public static void ApplyTheme(FrameworkElement root)=>root.RequestedTheme=UserSettings.Load().Theme switch
    {"浅色" or "light"=>ElementTheme.Light,"深色" or "dark"=>ElementTheme.Dark,_=>ElementTheme.Default};
}
