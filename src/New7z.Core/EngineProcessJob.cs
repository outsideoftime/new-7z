using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace New7z.Core;

// Process isolation is crash containment, not a low-integrity security sandbox.
internal sealed class EngineProcessJob:IDisposable
{
    private readonly SafeFileHandle handle;
    internal EngineProcessJob(Process process)
    {
        handle=CreateJobObject(IntPtr.Zero,null);
        try
        {
            if(handle.IsInvalid)throw new Win32Exception(Marshal.GetLastWin32Error());
            var info=new ExtendedLimits();
            info.Basic.LimitFlags=0x2000|0x100; // KILL_ON_JOB_CLOSE | PROCESS_MEMORY
            info.ProcessMemoryLimit=(nuint)(2UL*1024*1024*1024);
            if(!SetInformationJobObject(handle,9,ref info,(uint)Marshal.SizeOf<ExtendedLimits>())||!AssignProcessToJobObject(handle,process.Handle))throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        catch{try{process.Kill(true);}catch{}handle.Dispose();throw;}
    }
    public void Dispose()=>handle.Dispose();
    [StructLayout(LayoutKind.Sequential)]private struct BasicLimits{public long PerProcessTime,PerJobTime;public uint LimitFlags;public nuint MinWorkingSet,MaxWorkingSet;public uint ActiveProcesses;public nuint Affinity;public uint Priority,Scheduling;}
    [StructLayout(LayoutKind.Sequential)]private struct IoCounters{public ulong ReadOperations,WriteOperations,OtherOperations,ReadBytes,WriteBytes,OtherBytes;}
    [StructLayout(LayoutKind.Sequential)]private struct ExtendedLimits{public BasicLimits Basic;public IoCounters Io;public nuint ProcessMemoryLimit,JobMemoryLimit,PeakProcessMemory,PeakJobMemory;}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern SafeFileHandle CreateJobObject(IntPtr attributes,string? name);
    [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool SetInformationJobObject(SafeFileHandle job,int infoClass,ref ExtendedLimits info,uint length);
    [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool AssignProcessToJobObject(SafeFileHandle job,IntPtr process);
}
