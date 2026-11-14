#define UNICODE
#define _UNICODE
#define NOMINMAX
#include <windows.h>
#include <shobjidl.h>
#include <shlobj.h>
#include <shlwapi.h>
#include <string>
#include <vector>
#include <atomic>
#include <algorithm>

// {642CF40A-831E-42A9-AC63-F5A0ABDD5C17}
const CLSID CLSID_New7z={0x642cf40a,0x831e,0x42a9,{0xac,0x63,0xf5,0xa0,0xab,0xdd,0x5c,0x17}};
const CLSID CLSID_New7zClassic={0x642cf40a,0x831e,0x42a9,{0xac,0x63,0xf5,0xa0,0xab,0xdd,0x5c,0x18}};
static HMODULE moduleHandle;
static std::atomic<long> objects{0},locks{0};
// Keep bit assignments in sync with New7z.Core/ExplorerPreferences.cs.
static DWORD commandBit(const std::wstring& action){
 if(action==L"open")return 1;if(action==L"extract-named")return 2;if(action==L"extract-to")return 4;if(action==L"extract-here")return 8;
 if(action==L"test")return 16;if(action==L"create")return 32;if(action==L"create-7z")return 64;if(action==L"create-zip")return 128;if(action==L"create-tar")return 256;return 0;
}
static DWORD preference(const wchar_t* name,DWORD fallback){DWORD value=0,bytes=sizeof(value);return RegGetValueW(HKEY_CURRENT_USER,L"Software\\New7z\\Explorer",name,RRF_RT_REG_DWORD,nullptr,&value,&bytes)==ERROR_SUCCESS?value:fallback;}
static constexpr DWORD kAllCommands=511;
// 255 was "all commands" before the TAR command existed; expand it once.
static DWORD commands(){DWORD value=preference(L"Commands",kAllCommands),version=preference(L"CommandsVersion",0);if(value==255&&version<2)value=kAllCommands;return value&kAllCommands;}
static bool english(){wchar_t language[8]{};DWORD bytes=sizeof(language);if(RegGetValueW(HKEY_CURRENT_USER,L"Software\\New7z\\Explorer",L"Language",RRF_RT_REG_SZ,nullptr,language,&bytes)==ERROR_SUCCESS)return _wcsicmp(language,L"en")==0;return PRIMARYLANGID(GetUserDefaultUILanguage())!=LANG_CHINESE;}
static std::wstring appPath(){wchar_t path[32768];GetModuleFileNameW(moduleHandle,path,32768);PathRemoveFileSpecW(path);return std::wstring(path)+L"\\New7z.App.exe";}
static std::vector<std::wstring> getPaths(IShellItemArray* items){std::vector<std::wstring> paths;if(!items)return paths;DWORD count=0;items->GetCount(&count);if(count>10000)return paths;for(DWORD i=0;i<count;i++){IShellItem* item=nullptr;PWSTR path=nullptr;if(SUCCEEDED(items->GetItemAt(i,&item))){if(SUCCEEDED(item->GetDisplayName(SIGDN_FILESYSPATH,&path))){paths.emplace_back(path);CoTaskMemFree(path);}item->Release();}}return paths;}
static bool archive(const std::wstring& path){auto attributes=GetFileAttributesW(path.c_str());if(attributes!=INVALID_FILE_ATTRIBUTES&&(attributes&FILE_ATTRIBUTE_DIRECTORY))return false;auto ext=std::wstring(PathFindExtensionW(path.c_str()));std::transform(ext.begin(),ext.end(),ext.begin(),towlower);return ext==L".7z"||ext==L".zip"||ext==L".rar"||ext==L".tar"||ext==L".gz"||ext==L".xz"||ext==L".bz2"||ext==L".iso"||ext==L".zst"||ext==L".zstd"||ext==L".tzst"||ext==L".lz4";}
static bool allArchives(const std::vector<std::wstring>& paths){return !paths.empty()&&std::all_of(paths.begin(),paths.end(),archive);}
static DWORD selectionMask(const std::vector<std::wstring>& paths){if(paths.empty())return 0;return allArchives(paths)?(paths.size()==1?31:22):480;}
static std::wstring quoteJson(const std::wstring& s){std::wstring out=L"\"";for(auto c:s){if(c==L'"')out+=L"\\\"";else if(c==L'\\')out+=L"\\\\";else if(c<32){wchar_t b[8];swprintf_s(b,L"\\u%04x",unsigned(c));out+=b;}else out+=c;}return out+L"\"";}
static HRESULT dispatch(const std::wstring& action,const std::vector<std::wstring>& paths){
 if(paths.empty())return E_INVALIDARG;
 wchar_t base[32768];if(!GetEnvironmentVariableW(L"LOCALAPPDATA",base,32768))return E_FAIL;
 auto directory=std::wstring(base)+L"\\New7z\\Requests";int made=SHCreateDirectoryExW(nullptr,directory.c_str(),nullptr);if(made!=ERROR_SUCCESS&&made!=ERROR_ALREADY_EXISTS&&made!=ERROR_FILE_EXISTS)return HRESULT_FROM_WIN32(made);
 GUID guid;CoCreateGuid(&guid);wchar_t id[40];StringFromGUID2(guid,id,40);auto file=directory+L"\\"+id+L".json";
 std::wstring json=L"{\"Action\":"+quoteJson(action)+L",\"Paths\":[";for(size_t i=0;i<paths.size();i++){if(i)json+=L",";json+=quoteJson(paths[i]);}json+=L"]}";
 int length=WideCharToMultiByte(CP_UTF8,WC_ERR_INVALID_CHARS,json.c_str(),int(json.size()),nullptr,0,nullptr,nullptr);if(length<=0||length>1024*1024)return E_INVALIDARG;std::string utf8(length,0);WideCharToMultiByte(CP_UTF8,WC_ERR_INVALID_CHARS,json.c_str(),int(json.size()),utf8.data(),length,nullptr,nullptr);
 HANDLE output=CreateFileW(file.c_str(),GENERIC_WRITE,0,nullptr,CREATE_NEW,FILE_ATTRIBUTE_NORMAL,nullptr);if(output==INVALID_HANDLE_VALUE)return HRESULT_FROM_WIN32(GetLastError());DWORD written=0;BOOL ok=WriteFile(output,utf8.data(),DWORD(utf8.size()),&written,nullptr);CloseHandle(output);if(!ok||written!=utf8.size()){DeleteFileW(file.c_str());return E_FAIL;}
 auto executable=appPath();executable=executable.substr(0,executable.find_last_of(L'\\')+1)+L"New7z.ShellHost.exe";auto command=L"\""+executable+L"\" --shell-request \""+file+L"\"";
 SIZE_T attributeBytes=0;InitializeProcThreadAttributeList(nullptr,1,0,&attributeBytes);auto attributes=static_cast<LPPROC_THREAD_ATTRIBUTE_LIST>(HeapAlloc(GetProcessHeap(),0,attributeBytes));if(!attributes)return E_OUTOFMEMORY;
 if(!InitializeProcThreadAttributeList(attributes,1,0,&attributeBytes)){auto error=GetLastError();HeapFree(GetProcessHeap(),0,attributes);return HRESULT_FROM_WIN32(error);}
 DWORD policy=PROCESS_CREATION_DESKTOP_APP_BREAKAWAY_ENABLE_PROCESS_TREE;
 if(!UpdateProcThreadAttribute(attributes,0,PROC_THREAD_ATTRIBUTE_DESKTOP_APP_POLICY,&policy,sizeof(policy),nullptr,nullptr)){auto error=GetLastError();DeleteProcThreadAttributeList(attributes);HeapFree(GetProcessHeap(),0,attributes);return HRESULT_FROM_WIN32(error);}
 STARTUPINFOEXW startup{};startup.StartupInfo.cb=sizeof(startup);startup.lpAttributeList=attributes;PROCESS_INFORMATION process{};
 BOOL created=CreateProcessW(executable.c_str(),command.data(),nullptr,nullptr,FALSE,EXTENDED_STARTUPINFO_PRESENT,nullptr,nullptr,&startup.StartupInfo,&process);auto error=GetLastError();DeleteProcThreadAttributeList(attributes);HeapFree(GetProcessHeap(),0,attributes);
 if(!created){DeleteFileW(file.c_str());return HRESULT_FROM_WIN32(error);}CloseHandle(process.hThread);CloseHandle(process.hProcess);return S_OK;
}
class Command;
class Commands final:public IEnumExplorerCommand{
 std::atomic<ULONG> refs{1};std::vector<IExplorerCommand*> values;ULONG position=0;
public:
 explicit Commands(std::vector<IExplorerCommand*> list):values(std::move(list)){++objects;}
 ~Commands(){for(auto p:values)p->Release();--objects;}
 HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id,void** out)override{*out=nullptr;if(id==IID_IUnknown||id==__uuidof(IEnumExplorerCommand))*out=this;else return E_NOINTERFACE;AddRef();return S_OK;}
 ULONG STDMETHODCALLTYPE AddRef()override{return ++refs;}ULONG STDMETHODCALLTYPE Release()override{auto n=--refs;if(!n)delete this;return n;}
 HRESULT STDMETHODCALLTYPE Next(ULONG count,IExplorerCommand** out,ULONG* fetched)override{if(!out||(!fetched&&count!=1))return E_POINTER;ULONG n=0;while(n<count&&position<values.size()){out[n]=values[position++];out[n++]->AddRef();}if(fetched)*fetched=n;return n==count?S_OK:S_FALSE;}
 HRESULT STDMETHODCALLTYPE Skip(ULONG count)override{auto available=values.size()-position;position+=ULONG(std::min<size_t>(available,count));return available>=count?S_OK:S_FALSE;}
 HRESULT STDMETHODCALLTYPE Reset()override{position=0;return S_OK;}
 HRESULT STDMETHODCALLTYPE Clone(IEnumExplorerCommand** out)override{for(auto p:values)p->AddRef();auto clone=new Commands(values);clone->position=position;*out=clone;return S_OK;}
};
class Command final:public IExplorerCommand{
 std::atomic<ULONG> refs{1};std::wstring action;std::vector<std::wstring> cached;CLSID canonicalBase;
public:
 explicit Command(std::wstring verb=L"",std::vector<std::wstring> paths={},CLSID base=CLSID_New7z):action(std::move(verb)),cached(std::move(paths)),canonicalBase(base){++objects;}
 ~Command(){--objects;}
 HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id,void** out)override{*out=nullptr;if(id==IID_IUnknown||id==__uuidof(IExplorerCommand))*out=this;else return E_NOINTERFACE;AddRef();return S_OK;}
 ULONG STDMETHODCALLTYPE AddRef()override{return ++refs;}ULONG STDMETHODCALLTYPE Release()override{auto n=--refs;if(!n)delete this;return n;}
 HRESULT STDMETHODCALLTYPE GetTitle(IShellItemArray* items,PWSTR* title)override{
  if(items)cached=getPaths(items);const bool en=english();std::wstring text=L"New7z";
  if(action==L"open")text=en?L"Open with New7z":L"用 New7z 打开";else if(action==L"extract-named")text=en?L"Smart Extract":L"智能解压";
  else if(action==L"extract-to")text=en?L"Extract to…":L"解压到…";else if(action==L"extract-here")text=en?L"Extract here":L"解压到当前文件夹";else if(action==L"test")text=en?L"Test archive":L"测试完整性";
  else if(action==L"create")text=en?L"New archive…":L"新建压缩包…";else if(action==L"create-7z")text=en?L"Compress to 7Z":L"压缩为 7Z";else if(action==L"create-zip")text=en?L"Compress to ZIP":L"压缩为 ZIP";else if(action==L"create-tar")text=en?L"Compress to TAR":L"压缩为 TAR";
  return SHStrDupW(text.c_str(),title);
 }
 HRESULT STDMETHODCALLTYPE GetIcon(IShellItemArray*,PWSTR* icon)override{wchar_t path[32768];GetModuleFileNameW(moduleHandle,path,32768);return SHStrDupW((std::wstring(path)+L",-101").c_str(),icon);}
 HRESULT STDMETHODCALLTYPE GetToolTip(IShellItemArray*,PWSTR* tooltip)override{
  if(action==L"extract-named")return SHStrDupW(english()?L"Extract a single top-level item here; put multiple items in a named folder":L"单个顶层项目直接解压；多个顶层项目解压到同名文件夹",tooltip);
  return SHStrDupW(english()?L"Compress or extract files with New7z":L"使用 New7z 压缩或解压文件",tooltip);
 }
 HRESULT STDMETHODCALLTYPE GetCanonicalName(GUID* id)override{*id=canonicalBase;id->Data1^=commandBit(action);return S_OK;}
 HRESULT STDMETHODCALLTYPE GetState(IShellItemArray* items,BOOL,EXPCMDSTATE* state)override{if(items)cached=getPaths(items);auto mask=commands()&selectionMask(cached);*state=preference(L"MenuEnabled",1)&&(action.empty()?mask!=0:(mask&commandBit(action))!=0)?ECS_ENABLED:ECS_HIDDEN;return S_OK;}
 HRESULT STDMETHODCALLTYPE Invoke(IShellItemArray* items,IBindCtx*)override{try{if(!preference(L"MenuEnabled",1)||!(commands()&commandBit(action)))return E_ACCESSDENIED;return dispatch(action,getPaths(items));}catch(...){return E_OUTOFMEMORY;}}
 HRESULT STDMETHODCALLTYPE GetFlags(EXPCMDFLAGS* flags)override{*flags=action.empty()?ECF_HASSUBCOMMANDS:ECF_DEFAULT;return S_OK;}
 HRESULT STDMETHODCALLTYPE EnumSubCommands(IEnumExplorerCommand** result)override{
  *result=nullptr;if(!action.empty())return E_NOTIMPL;std::vector<IExplorerCommand*> list;
  auto mask=preference(L"MenuEnabled",1)?commands():0;
  auto add=[&](const wchar_t* verb){if(mask&commandBit(verb))list.push_back(new Command(verb,cached,canonicalBase));};
  if(allArchives(cached)){if(cached.size()==1)add(L"open");for(auto verb:{L"extract-named",L"extract-to"})add(verb);if(cached.size()==1)add(L"extract-here");add(L"test");}
  else for(auto verb:{L"create",L"create-7z",L"create-zip",L"create-tar"})add(verb);
  *result=new Commands(std::move(list));return S_OK;
 }
};
class Factory final:public IClassFactory{
 std::atomic<ULONG> refs{1};
 CLSID canonicalBase;
public:
 explicit Factory(CLSID base):canonicalBase(base){++objects;}~Factory(){--objects;}
 HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id,void** out)override{*out=nullptr;if(id==IID_IUnknown||id==IID_IClassFactory)*out=this;else return E_NOINTERFACE;AddRef();return S_OK;}
 ULONG STDMETHODCALLTYPE AddRef()override{return ++refs;}ULONG STDMETHODCALLTYPE Release()override{auto n=--refs;if(!n)delete this;return n;}
 HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer,REFIID id,void** out)override{if(outer)return CLASS_E_NOAGGREGATION;auto command=new Command(L"",{},canonicalBase);auto hr=command->QueryInterface(id,out);command->Release();return hr;}
 HRESULT STDMETHODCALLTYPE LockServer(BOOL lock)override{if(lock)++locks;else --locks;return S_OK;}
};
STDAPI DllGetClassObject(REFCLSID cls,REFIID id,void** out){*out=nullptr;if(cls!=CLSID_New7z&&cls!=CLSID_New7zClassic)return CLASS_E_CLASSNOTAVAILABLE;auto factory=new Factory(cls);auto hr=factory->QueryInterface(id,out);factory->Release();return hr;}
STDAPI DllCanUnloadNow(){return objects==0&&locks==0?S_OK:S_FALSE;}
BOOL WINAPI DllMain(HINSTANCE instance,DWORD reason,LPVOID){if(reason==DLL_PROCESS_ATTACH){moduleHandle=instance;DisableThreadLibraryCalls(instance);}return TRUE;}
