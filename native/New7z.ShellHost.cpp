#define UNICODE
#define _UNICODE
#include <windows.h>
#include <shlwapi.h>
#include <shellapi.h>
#include <string>
// Keep the Explorer COM server independent of the WinUI application's lifetime.
int WINAPI wWinMain(HINSTANCE,HINSTANCE,PWSTR,int){
 wchar_t path[32768];GetModuleFileNameW(nullptr,path,32768);PathRemoveFileSpecW(path);std::wstring executable=std::wstring(path)+L"\\New7z.App.exe",command=L"\""+executable+L"\"";
 int count=0;auto args=CommandLineToArgvW(GetCommandLineW(),&count);if(count==3&&std::wstring(args[1])==L"--shell-request")command+=L" --shell-request \""+std::wstring(args[2])+L"\"";LocalFree(args);
 SIZE_T bytes=0;InitializeProcThreadAttributeList(nullptr,1,0,&bytes);auto attributes=static_cast<LPPROC_THREAD_ATTRIBUTE_LIST>(HeapAlloc(GetProcessHeap(),0,bytes));if(!attributes)return ERROR_OUTOFMEMORY;
 if(!InitializeProcThreadAttributeList(attributes,1,0,&bytes)){auto e=GetLastError();HeapFree(GetProcessHeap(),0,attributes);return int(e);}
 DWORD policy=PROCESS_CREATION_DESKTOP_APP_BREAKAWAY_ENABLE_PROCESS_TREE;
 if(!UpdateProcThreadAttribute(attributes,0,PROC_THREAD_ATTRIBUTE_DESKTOP_APP_POLICY,&policy,sizeof(policy),nullptr,nullptr)){auto e=GetLastError();DeleteProcThreadAttributeList(attributes);HeapFree(GetProcessHeap(),0,attributes);return int(e);}
 STARTUPINFOEXW startup{};startup.StartupInfo.cb=sizeof(startup);startup.lpAttributeList=attributes;PROCESS_INFORMATION process{};
 BOOL ok=CreateProcessW(executable.c_str(),command.data(),nullptr,nullptr,FALSE,EXTENDED_STARTUPINFO_PRESENT,nullptr,nullptr,&startup.StartupInfo,&process);auto error=GetLastError();DeleteProcThreadAttributeList(attributes);HeapFree(GetProcessHeap(),0,attributes);
 if(!ok)return int(error);CloseHandle(process.hThread);CloseHandle(process.hProcess);return 0;
}
