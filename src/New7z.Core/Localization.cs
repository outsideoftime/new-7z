using System.Globalization;

namespace New7z.Core;

// Minimal two-language text source shared by the app and the engine errors.
public static class Loc
{
    public static string Language { get; set; } = "zh";
    public static bool English => Language == "en";
    public static string T(string zh, string en) => English ? en : zh;
    public static string Resolve(string? setting)
    {
        if (setting is "zh" or "en") return setting;
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase) ? "zh" : "en";
    }
}
