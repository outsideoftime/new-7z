using System.Runtime.InteropServices;

namespace New7z.Core;

internal static class NativeMethods
{
    private const string Dll = "New7z.Native.dll";
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int ProgressCallback(ulong total, ulong done, IntPtr context);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl, CharSet=CharSet.Unicode)] internal static extern int NzInitialize(string path);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void NzFree(IntPtr data);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl, CharSet=CharSet.Unicode)] internal static extern int NzList(string path, string? password, out IntPtr json, ProgressCallback progress, IntPtr context);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl, CharSet=CharSet.Unicode)] internal static extern int NzExtract(string path, string? password, uint index, string? output, ulong limit, out IntPtr data, out ulong size, ProgressCallback progress, IntPtr context);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl, CharSet=CharSet.Unicode)] internal static extern int NzTest(string path, string? password, ProgressCallback progress, IntPtr context);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl, CharSet=CharSet.Unicode)] internal static extern int NzCreate(string path, int zip, int level, string? password, int encryptNames,
        [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.LPWStr,SizeParamIndex=7)] string[] paths,
        [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.LPWStr,SizeParamIndex=7)] string[] names, uint count, ProgressCallback progress, IntPtr context);
}
