using System.Runtime.InteropServices;

namespace New7z.Core;

internal static class NativeMethods
{
    private const string Dll = "New7z.Native.dll";
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int ProgressCallback(ulong total, ulong done, IntPtr context);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl, CharSet=CharSet.Unicode)] internal static extern int NzInitialize(string path);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void NzFree(IntPtr data);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void NzGetDragDiagnostics(out IntPtr text);

    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl, CharSet=CharSet.Unicode)] internal static extern int NzCreateArchiveDragData(IntPtr package,IntPtr owner,string archive,string? password,
        [MarshalAs(UnmanagedType.LPArray,ArraySubType=UnmanagedType.U4,SizeParamIndex=6)] uint[] indices,
        [MarshalAs(UnmanagedType.LPArray,ArraySubType=UnmanagedType.LPWStr,SizeParamIndex=6)] string[] outputs,uint count,
        [MarshalAs(UnmanagedType.LPArray,ArraySubType=UnmanagedType.LPWStr,SizeParamIndex=8)] string[] roots,uint rootCount,string tempPath,
        string title,out IntPtr session);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern int NzArchiveDragResult(IntPtr session);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern int NzArchiveDragWasTransferred(IntPtr session);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void NzReleaseArchiveDragData(IntPtr session);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl, CharSet=CharSet.Unicode)] internal static extern int NzList(string path, string? password, out IntPtr json, ProgressCallback progress, IntPtr context);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl, CharSet=CharSet.Unicode)] internal static extern int NzExtract(string path, string? password, uint index, string? output, ulong limit, out IntPtr data, out ulong size, ProgressCallback progress, IntPtr context);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl, CharSet=CharSet.Unicode)] internal static extern int NzTest(string path, string? password, ProgressCallback progress, IntPtr context);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl, CharSet=CharSet.Unicode)] internal static extern int NzCreate(string path, int format, int level, string? method, string? password, int encryptNames,
        [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.LPWStr,SizeParamIndex=7)] string[] paths,
        [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.LPWStr,SizeParamIndex=7)] string[] names, uint count, ProgressCallback progress, IntPtr context);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl, CharSet=CharSet.Unicode)] internal static extern int NzUpdate(string path, string output, string? password, int zip,
        [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.U4,SizeParamIndex=5)] uint[] removeIndices, uint removeCount,
        [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.LPWStr,SizeParamIndex=8)] string[] addPaths,
        [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.LPWStr,SizeParamIndex=8)] string[] addNames, uint addCount, ProgressCallback progress, IntPtr context);
    [DllImport("shell32.dll")] internal static extern void SHChangeNotify(uint eventId,uint flags,IntPtr item1,IntPtr item2);

    internal static void NotifyShellAssociationsChanged()=>SHChangeNotify(0x08000000,0x1000,IntPtr.Zero,IntPtr.Zero);
}
