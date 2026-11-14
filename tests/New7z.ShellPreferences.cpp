#define UNICODE
#define _UNICODE
#include <windows.h>
#include <shobjidl.h>
#include <shlobj.h>
#include <string>
#include <vector>
#include <iostream>
#include <stdexcept>
#include <algorithm>

static void check(bool condition,const char* message){if(!condition)throw std::runtime_error(message);std::cout<<"PASS "<<message<<"\n";}
static const CLSID rootId={0x642cf40a,0x831e,0x42a9,{0xac,0x63,0xf5,0xa0,0xab,0xdd,0x5c,0x17}};
static const CLSID classicId={0x642cf40a,0x831e,0x42a9,{0xac,0x63,0xf5,0xa0,0xab,0xdd,0x5c,0x18}};
static IShellItemArray* items(const std::wstring& path){IShellItem* item=nullptr;IShellItemArray* result=nullptr;auto hr=SHCreateItemFromParsingName(path.c_str(),nullptr,IID_PPV_ARGS(&item));if(FAILED(hr)){std::wcerr<<path<<L" HRESULT "<<std::hex<<hr<<L"\n";throw std::runtime_error("shell item");}hr=SHCreateShellItemArrayFromShellItem(item,IID_PPV_ARGS(&result));item->Release();if(FAILED(hr))throw std::runtime_error("shell array");return result;}
static int count(IExplorerCommand* command){IEnumExplorerCommand* values=nullptr;if(FAILED(command->EnumSubCommands(&values)))throw std::runtime_error("enumeration");int total=0;IExplorerCommand* child=nullptr;while(values->Next(1,&child,nullptr)==S_OK){++total;child->Release();}values->Release();return total;}
static std::vector<std::wstring> titles(IExplorerCommand* command,IShellItemArray* selection){
 IEnumExplorerCommand* values=nullptr;if(FAILED(command->EnumSubCommands(&values)))throw std::runtime_error("title enumeration");
 std::vector<std::wstring> result;IExplorerCommand* child=nullptr;
 while(values->Next(1,&child,nullptr)==S_OK){PWSTR text=nullptr;auto hr=child->GetTitle(selection,&text);result.emplace_back(SUCCEEDED(hr)&&text?text:L"");if(text)CoTaskMemFree(text);child->Release();}
 values->Release();return result;
}
static bool titlesAre(IExplorerCommand* command,IShellItemArray* selection,std::initializer_list<const wchar_t*> expected){
 auto actual=titles(command,selection);return actual.size()==expected.size()&&std::equal(actual.begin(),actual.end(),expected.begin());
}
static std::wstring tooltip(IExplorerCommand* command){PWSTR text=nullptr;auto hr=command->GetToolTip(nullptr,&text);std::wstring result=SUCCEEDED(hr)&&text?text:L"";if(text)CoTaskMemFree(text);return result;}
static bool visible(IExplorerCommand* command,IShellItemArray* selection){EXPCMDSTATE state=ECS_HIDDEN;return SUCCEEDED(command->GetState(selection,FALSE,&state))&&state==ECS_ENABLED;}
int wmain(int argc,wchar_t** argv){
 if(argc!=3)return 2;
 CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED);
 if(std::wstring(argv[1])==L"--registered"||std::wstring(argv[1])==L"--modern"){
  IExplorerCommand* registered=nullptr;
  auto modern=std::wstring(argv[1])==L"--modern";
  auto hr=CoCreateInstance(modern?rootId:classicId,nullptr,modern?CLSCTX_ALL:CLSCTX_INPROC_SERVER,IID_PPV_ARGS(&registered));
  if(FAILED(hr)){std::cerr<<"CoCreateInstance HRESULT "<<std::hex<<hr<<"\n";CoUninitialize();return 1;}
  PWSTR icon=nullptr;hr=registered->GetIcon(nullptr,&icon);
  bool matches=SUCCEEDED(hr)&&icon&&std::wstring(icon).rfind(argv[2],0)==0;
  wchar_t working[32768];GetCurrentDirectoryW(32768,working);
  auto selection=items(std::wstring(working)+L"\\artifacts\\preferences-fixture\\plain.txt");
  EXPCMDSTATE state=ECS_HIDDEN;auto stateResult=registered->GetState(selection,FALSE,&state);
  std::cout<<"Registered state HRESULT "<<std::hex<<stateResult<<" value "<<state<<"; commands "<<std::dec<<count(registered)<<"\n";
  selection->Release();
  if(icon)CoTaskMemFree(icon);registered->Release();CoUninitialize();
  if(!matches){std::cerr<<"Registered COM icon path mismatch\n";return 1;}
  std::cout<<"PASS registered Explorer COM handler activation and icon resource\n";return 0;
 }
 HKEY sandbox=nullptr,preferences=nullptr;HMODULE module=nullptr;IClassFactory* factory=nullptr;IExplorerCommand* command=nullptr;
 IShellItemArray* file=nullptr;IShellItemArray* archive=nullptr;
 auto sandboxPath=L"Software\\New7z.Tests\\Native-"+std::to_wstring(GetCurrentProcessId());
 try{
  auto folder=std::wstring(argv[2]);std::replace(folder.begin(),folder.end(),L'/',L'\\');CreateDirectoryW(folder.c_str(),nullptr);
  for(auto name:{L"plain.txt",L"sample.zip"}){auto path=folder+L"\\"+name;auto output=CreateFileW(path.c_str(),GENERIC_WRITE,0,nullptr,CREATE_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr);if(output==INVALID_HANDLE_VALUE)throw std::runtime_error("fixture creation");CloseHandle(output);}
  file=items(folder+L"\\plain.txt");archive=items(folder+L"\\sample.zip");
  if(RegCreateKeyExW(HKEY_CURRENT_USER,sandboxPath.c_str(),0,nullptr,0,KEY_ALL_ACCESS,nullptr,&sandbox,nullptr)!=ERROR_SUCCESS)throw std::runtime_error("sandbox");
  if(RegOverridePredefKey(HKEY_CURRENT_USER,sandbox)!=ERROR_SUCCESS)throw std::runtime_error("registry override");
  module=LoadLibraryW(argv[1]);if(!module)throw std::runtime_error("load extension");
  auto get=reinterpret_cast<HRESULT(WINAPI*)(REFCLSID,REFIID,void**)>(GetProcAddress(module,"DllGetClassObject"));
  if(!get||FAILED(get(rootId,IID_PPV_ARGS(&factory)))||FAILED(factory->CreateInstance(nullptr,IID_PPV_ARGS(&command))))throw std::runtime_error("factory");
  check(visible(command,file)&&count(command)==4,"default file commands");
  check(visible(command,archive)&&count(command)==5,"default archive commands");
  if(RegCreateKeyExW(HKEY_CURRENT_USER,L"Software\\New7z\\Explorer",0,nullptr,0,KEY_ALL_ACCESS,nullptr,&preferences,nullptr)!=ERROR_SUCCESS)throw std::runtime_error("preferences");
  auto write=[&](const wchar_t* name,DWORD value){if(RegSetValueExW(preferences,name,0,REG_DWORD,reinterpret_cast<BYTE*>(&value),sizeof(value))!=ERROR_SUCCESS)throw std::runtime_error("write settings");};
  auto writeLanguage=[&](const wchar_t* value){if(RegSetValueExW(preferences,L"Language",0,REG_SZ,reinterpret_cast<const BYTE*>(value),DWORD((wcslen(value)+1)*sizeof(wchar_t)))!=ERROR_SUCCESS)throw std::runtime_error("write language");};
  writeLanguage(L"zh");
  check(visible(command,file)&&titlesAre(command,file,{L"新建压缩包…",L"压缩为 7Z",L"压缩为 ZIP",L"压缩为 TAR"}),"zh menu titles follow app language");
  check(tooltip(command)==L"使用 New7z 压缩或解压文件","zh menu tooltip follows app language");
  check(visible(command,archive)&&titlesAre(command,archive,{L"用 New7z 打开",L"智能解压",L"解压到…",L"解压到当前文件夹",L"测试完整性"}),"zh archive menu exposes Smart Extract");
  writeLanguage(L"en");
  check(visible(command,file)&&titlesAre(command,file,{L"New archive…",L"Compress to 7Z",L"Compress to ZIP",L"Compress to TAR"}),"en menu titles follow app language");
  check(tooltip(command)==L"Compress or extract files with New7z","en menu tooltip follows app language");
  check(visible(command,archive)&&titlesAre(command,archive,{L"Open with New7z",L"Smart Extract",L"Extract to…",L"Extract here",L"Test archive"}),"en archive menu titles follow app language");
  write(L"MenuEnabled",0);check(!visible(command,file)&&count(command)==0,"master switch hides menu");
  write(L"MenuEnabled",1);write(L"Commands",128);check(visible(command,file)&&count(command)==1,"file command filtering");
  check(!visible(command,archive)&&count(command)==0,"no relevant archive command hides root");
  write(L"Commands",2|16);check(visible(command,archive)&&count(command)==2,"archive command filtering");
  check(!visible(command,file),"no relevant file command hides root");
  write(L"Commands",0);check(!visible(command,file)&&!visible(command,archive),"empty command set hides root");
  write(L"CommandsVersion",1);write(L"Commands",255);check(visible(command,file)&&count(command)==4,"legacy 255 expands to include TAR");
  write(L"CommandsVersion",2);write(L"Commands",255);check(visible(command,file)&&count(command)==3,"explicit 255 without TAR stays filtered");
  command->Release();command=nullptr;factory->Release();factory=nullptr;FreeLibrary(module);module=nullptr;
  RegCloseKey(preferences);preferences=nullptr;RegOverridePredefKey(HKEY_CURRENT_USER,nullptr);RegCloseKey(sandbox);sandbox=nullptr;
  RegDeleteTreeW(HKEY_CURRENT_USER,sandboxPath.c_str());file->Release();archive->Release();CoUninitialize();return 0;
 }catch(const std::exception& e){std::cerr<<e.what()<<"\n";if(command)command->Release();if(factory)factory->Release();if(module)FreeLibrary(module);if(preferences)RegCloseKey(preferences);RegOverridePredefKey(HKEY_CURRENT_USER,nullptr);if(sandbox)RegCloseKey(sandbox);RegDeleteTreeW(HKEY_CURRENT_USER,sandboxPath.c_str());if(file)file->Release();if(archive)archive->Release();CoUninitialize();return 1;}
}
