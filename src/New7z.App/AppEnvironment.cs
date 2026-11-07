namespace New7z.App;

/// <summary>Distinguishes the Microsoft Store (MSIX) build from the standalone build.</summary>
internal static class AppEnvironment
{
#if STORE_PACKAGE
    // The Store build must never self-update, even when launched directly by the shell host.
    public static bool IsPackaged=>true;
#else
    private static readonly bool packaged=Detect();
    public static bool IsPackaged=>packaged;

    private static bool Detect()
    {
        try{return Windows.ApplicationModel.Package.Current!=null;}
        catch{return false;}
    }
#endif
}
