#define NOMINMAX
#include <windows.h>
#include <propidl.h>
#include <oleauto.h>
#include <shellapi.h>
#include <shlobj.h>
#include <urlmon.h>
#include <string>
#include <vector>
#include <algorithm>
#include <atomic>
#include <mutex>
#include <unordered_map>
#include "../7zSdk/Source/CPP/Common/MyInitGuid.h"
#include "../7zSdk/Source/CPP/7zip/Archive/IArchive.h"
#include "../7zSdk/Source/CPP/7zip/IPassword.h"

using Progress = int(__cdecl*)(unsigned long long, unsigned long long, void*);
constexpr HRESULT PasswordRequired=HRESULT(0x80040201), SizeLimit=HRESULT(0x80040202), DataInvalid=HRESULT(0x80040203), UpdateUnsupported=HRESULT(0x80040204);
using Creator=HRESULT(WINAPI*)(const GUID*,const GUID*,void**);
using HandlerProp=HRESULT(WINAPI*)(UInt32,PROPID,PROPVARIANT*);
static HMODULE engine=nullptr;
static Creator createObject=nullptr;
static HandlerProp getHandlerProp=nullptr;
static std::mutex initMutex;
thread_local std::wstring errorText;
thread_local std::wstring dragDiagnostics;

static void traceDrag(const wchar_t* event,DWORD value) noexcept{try{if(dragDiagnostics.size()<4096)dragDiagnostics+=std::wstring(event)+L"="+std::to_wstring(value)+L"\n";}catch(...){}}
#define API extern "C" __declspec(dllexport)
#define METHOD(name, args) HRESULT STDMETHODCALLTYPE name args noexcept override
#define REFS \
 std::atomic<ULONG> refs{1}; \
 ULONG STDMETHODCALLTYPE AddRef() noexcept override{return ++refs;} \
 ULONG STDMETHODCALLTYPE Release() noexcept override{auto n=--refs;if(!n)delete this;return n;}
template<class T>struct Ptr{T* p=nullptr;~Ptr(){if(p)p->Release();}T* operator->()const{return p;}T** out(){return &p;}};
struct Variant{PROPVARIANT v{};~Variant(){PropVariantClear(&v);}};
struct Status {
  Progress progress;void* context;std::wstring password;UInt64 total=0;bool passwordAsked=false;
  Status(const wchar_t* pw,Progress cb,void* ctx):progress(cb),context(ctx),password(pw?pw:L""){}
  HRESULT report(UInt64 done){return progress&&progress(total,done,context)?E_ABORT:S_OK;}
  HRESULT getPassword(BSTR* pw){passwordAsked=true;if(password.empty())return PasswordRequired;*pw=SysAllocString(password.c_str());return *pw?S_OK:E_OUTOFMEMORY;}
};
class Input final:public IInStream {
  HANDLE file;
public:
  explicit Input(const wchar_t* path):file(CreateFileW(path,GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr)){}
  ~Input(){if(file!=INVALID_HANDLE_VALUE)CloseHandle(file);}
  bool valid()const{return file!=INVALID_HANDLE_VALUE;}
  REFS
  METHOD(QueryInterface,(REFIID iid,void** out)){*out=nullptr;if(iid==IID_IUnknown||iid==IID_ISequentialInStream||iid==IID_IInStream)*out=static_cast<IInStream*>(this);if(!*out)return E_NOINTERFACE;AddRef();return S_OK;}
  METHOD(Read,(void* data,UInt32 size,UInt32* processed)){DWORD read=0;const BOOL ok=ReadFile(file,data,size,&read,nullptr);if(processed)*processed=read;return ok?S_OK:HRESULT_FROM_WIN32(GetLastError());}
  METHOD(Seek,(Int64 offset,UInt32 origin,UInt64* pos)){LARGE_INTEGER move{},result{};move.QuadPart=offset;if(!SetFilePointerEx(file,move,&result,origin))return HRESULT_FROM_WIN32(GetLastError());if(pos)*pos=result.QuadPart;return S_OK;}
};
class Output final:public IOutStream {
  HANDLE file=INVALID_HANDLE_VALUE;
public:
  std::vector<unsigned char> bytes;UInt64 limit,written=0;bool memory;
  Output(const wchar_t* path,UInt64 cap):limit(cap),memory(!path){if(path)file=CreateFileW(path,GENERIC_WRITE,0,nullptr,CREATE_NEW,FILE_ATTRIBUTE_NORMAL,nullptr);}
  ~Output(){if(file!=INVALID_HANDLE_VALUE)CloseHandle(file);}
  bool valid()const{return memory||file!=INVALID_HANDLE_VALUE;}
  REFS
  METHOD(QueryInterface,(REFIID iid,void** out)){*out=nullptr;if(iid==IID_IUnknown||iid==IID_ISequentialOutStream||iid==IID_IOutStream)*out=static_cast<IOutStream*>(this);if(!*out)return E_NOINTERFACE;AddRef();return S_OK;}
  METHOD(Write,(const void* data,UInt32 size,UInt32* processed)){if(processed)*processed=0;if(written>limit||size>limit-written)return SizeLimit;try{if(memory){const auto p=static_cast<const unsigned char*>(data);bytes.insert(bytes.end(),p,p+size);}else{DWORD n=0;if(!WriteFile(file,data,size,&n,nullptr))return HRESULT_FROM_WIN32(GetLastError());if(n!=size)return E_FAIL;}written+=size;if(processed)*processed=size;return S_OK;}catch(...){return E_OUTOFMEMORY;}}
  METHOD(Seek,(Int64 offset,UInt32 origin,UInt64* pos)){if(memory)return E_NOTIMPL;LARGE_INTEGER move{},result{};move.QuadPart=offset;if(!SetFilePointerEx(file,move,&result,origin))return HRESULT_FROM_WIN32(GetLastError());if(pos)*pos=result.QuadPart;return S_OK;}
  METHOD(SetSize,(UInt64 size)){if(memory)return E_NOTIMPL;LARGE_INTEGER zero{},old{},target{};target.QuadPart=size;if(!SetFilePointerEx(file,zero,&old,FILE_CURRENT)||!SetFilePointerEx(file,target,nullptr,FILE_BEGIN)||!SetEndOfFile(file)||!SetFilePointerEx(file,old,nullptr,FILE_BEGIN))return HRESULT_FROM_WIN32(GetLastError());return S_OK;}
};
class OpenCallback final:public IArchiveOpenCallback,public ICryptoGetTextPassword {
  Status& status;
public:
  explicit OpenCallback(Status& s):status(s){}
  REFS
  METHOD(QueryInterface,(REFIID iid,void** out)){*out=nullptr;if(iid==IID_IUnknown||iid==IID_IArchiveOpenCallback)*out=static_cast<IArchiveOpenCallback*>(this);else if(iid==IID_ICryptoGetTextPassword)*out=static_cast<ICryptoGetTextPassword*>(this);if(!*out)return E_NOINTERFACE;AddRef();return S_OK;}
  METHOD(SetTotal,(const UInt64*,const UInt64* bytes)){if(bytes)status.total=*bytes;return status.report(0);}
  METHOD(SetCompleted,(const UInt64*,const UInt64* bytes)){return status.report(bytes?*bytes:0);}
  METHOD(CryptoGetTextPassword,(BSTR* pw)){return status.getPassword(pw);}
};
struct Archive {
  Ptr<IInArchive> archive;Ptr<Input> input;Status status;
  Archive(const wchar_t* pw,Progress cb,void* ctx):status(pw,cb,ctx){}
  ~Archive(){if(archive.p)archive->Close();}
  HRESULT open(const wchar_t* path){
    if(!createObject)return E_UNEXPECTED;
    input.p=new Input(path);if(!input->valid())return HRESULT_FROM_WIN32(GetLastError());
    using Count=HRESULT(WINAPI*)(UInt32*);auto countFn=reinterpret_cast<Count>(GetProcAddress(engine,"GetNumberOfFormats"));UInt32 count=0;HRESULT result=countFn(&count);if(FAILED(result))return result;
    std::vector<UInt32> order;std::wstring ext=path;auto dot=ext.find_last_of(L'.');ext=dot==std::wstring::npos?L"":ext.substr(dot+1);std::transform(ext.begin(),ext.end(),ext.begin(),::towlower);
    for(UInt32 i=0;i<count;i++){Variant v;getHandlerProp(i,NArchive::NHandlerPropID::kExtension,&v.v);bool match=false;if(v.v.vt==VT_BSTR){std::wstring exts=v.v.bstrVal;size_t start=0;while(start<exts.size()){auto end=exts.find(L' ',start);if(exts.substr(start,end==std::wstring::npos?end:end-start)==ext){match=true;break;}if(end==std::wstring::npos)break;start=end+1;}}if(match)order.insert(order.begin(),i);else order.push_back(i);}
    Ptr<OpenCallback> callback;callback.p=new OpenCallback(status);
    for(auto i:order){if(FAILED(status.report(0)))return E_ABORT;Variant v;if(FAILED(getHandlerProp(i,NArchive::NHandlerPropID::kClassID,&v.v))||v.v.vt!=VT_BSTR||SysStringByteLen(v.v.bstrVal)!=sizeof(GUID))continue;GUID id;memcpy(&id,v.v.bstrVal,sizeof(id));Ptr<IInArchive> candidate;if(FAILED(createObject(&id,&IID_IInArchive,reinterpret_cast<void**>(candidate.out()))))continue;input->Seek(0,FILE_BEGIN,nullptr);const UInt64 check=1<<23;result=candidate->Open(input.p,&check,callback.p);if(result==S_OK){archive.p=candidate.p;candidate.p=nullptr;return S_OK;}candidate->Close();if(status.passwordAsked)return PasswordRequired;}
    errorText=L"Unsupported or damaged archive";return DataInvalid;
  }
};
std::wstring quote(const wchar_t* s){std::wstring out=L"\"";for(;*s;s++){switch(*s){case L'"':out+=L"\\\"";break;case L'\\':out+=L"\\\\";break;case L'\n':out+=L"\\n";break;case L'\r':out+=L"\\r";break;case L'\t':out+=L"\\t";break;default:if(*s<32){wchar_t b[8];swprintf_s(b,L"\\u%04x",unsigned(*s));out+=b;}else out+=*s;}}return out+L"\"";}
UInt64 number(const PROPVARIANT& v){return v.vt==VT_UI8?v.uhVal.QuadPart:v.vt==VT_UI4?v.ulVal:0;}
bool propBool(const PROPVARIANT& v){return v.vt==VT_BOOL&&v.boolVal!=VARIANT_FALSE;}
class ExtractCallback final:public IArchiveExtractCallback,public ICryptoGetTextPassword {
  Status& status;Output* output;
public:
  HRESULT result=S_OK;
  ExtractCallback(Status& s,Output* o):status(s),output(o){}
  REFS
  METHOD(QueryInterface,(REFIID iid,void** out)){*out=nullptr;if(iid==IID_IUnknown||iid==IID_IArchiveExtractCallback||iid==IID_IProgress)*out=static_cast<IArchiveExtractCallback*>(this);else if(iid==IID_ICryptoGetTextPassword)*out=static_cast<ICryptoGetTextPassword*>(this);if(!*out)return E_NOINTERFACE;AddRef();return S_OK;}
  METHOD(SetTotal,(UInt64 value)){status.total=value;return status.report(0);}
  METHOD(SetCompleted,(const UInt64* value)){return status.report(value?*value:0);}
  METHOD(GetStream,(UInt32,ISequentialOutStream** stream,Int32 mode)){*stream=nullptr;if(mode==NArchive::NExtract::NAskMode::kExtract&&output){output->AddRef();*stream=output;}return status.report(0);}
  METHOD(PrepareOperation,(Int32)){return S_OK;}
  METHOD(SetOperationResult,(Int32 r)){if(r!=NArchive::NExtract::NOperationResult::kOK)result=(r==NArchive::NExtract::NOperationResult::kWrongPassword||status.passwordAsked)?PasswordRequired:DataInvalid;return result;}
  METHOD(CryptoGetTextPassword,(BSTR* pw)){return status.getPassword(pw);}
};
struct DragItem{UInt32 index;std::wstring path;bool directory;UInt64 limit;};
static bool ensureDirectory(const std::wstring& value){
  if(value.empty())return false;
  DWORD attributes=GetFileAttributesW(value.c_str());
  if(attributes!=INVALID_FILE_ATTRIBUTES)return (attributes&FILE_ATTRIBUTE_DIRECTORY)!=0;
  size_t end=value.size();while(end>3&&(value[end-1]==L'\\'||value[end-1]==L'/'))--end;
  std::wstring path=value.substr(0,end);size_t slash=path.find_last_of(L"\\/");
  if(slash==std::wstring::npos)return false;
  std::wstring parent;
  if(slash==2&&path.size()>2&&path[1]==L':')parent=path.substr(0,3);
  else if(slash==0)parent=path.substr(0,1);
  else parent=path.substr(0,slash);
  if(parent==path||!ensureDirectory(parent))return false;
  if(CreateDirectoryW(path.c_str(),nullptr))return true;
  return GetLastError()==ERROR_ALREADY_EXISTS&&(GetFileAttributesW(path.c_str())&FILE_ATTRIBUTE_DIRECTORY)!=0;
}
static bool isWithinDirectory(const std::wstring& root,const std::wstring& path){
  size_t length=root.size();while(length>3&&(root[length-1]==L'\\'||root[length-1]==L'/'))--length;
  if(path.size()<=length||_wcsnicmp(root.c_str(),path.c_str(),length)!=0)return false;
  return path[length]==L'\\'||path[length]==L'/';
}
static HRESULT makeDropFiles(const std::vector<std::wstring>& paths,STGMEDIUM* medium){
  try{
    size_t chars=1;for(const auto& path:paths){if(path.size()>SIZE_MAX-chars-1)return E_OUTOFMEMORY;chars+=path.size()+1;}
    const SIZE_T bytes=sizeof(DROPFILES)+chars*sizeof(wchar_t);
    HGLOBAL global=GlobalAlloc(GMEM_MOVEABLE|GMEM_ZEROINIT,bytes);if(!global)return E_OUTOFMEMORY;
    auto drop=static_cast<DROPFILES*>(GlobalLock(global));if(!drop){GlobalFree(global);return E_OUTOFMEMORY;}
    drop->pFiles=sizeof(DROPFILES);drop->fWide=TRUE;
    auto destination=reinterpret_cast<wchar_t*>(reinterpret_cast<BYTE*>(drop)+sizeof(DROPFILES));
    for(const auto& path:paths){memcpy(destination,path.c_str(),path.size()*sizeof(wchar_t));destination+=path.size()+1;}
    *destination=L'\0';GlobalUnlock(global);medium->tymed=TYMED_HGLOBAL;medium->hGlobal=global;medium->pUnkForRelease=nullptr;return S_OK;
  }catch(...){return E_OUTOFMEMORY;}
}
class DragExtractCallback final:public IArchiveExtractCallback,public ICryptoGetTextPassword {
  Status& status;std::unordered_map<UInt32,DragItem>& items;std::vector<Output*> outputs;
public:
  HRESULT result=S_OK;
  DragExtractCallback(Status& s,std::unordered_map<UInt32,DragItem>& i):status(s),items(i){}
  ~DragExtractCallback(){for(auto output:outputs)output->Release();}
  REFS
  METHOD(QueryInterface,(REFIID iid,void** out)){*out=nullptr;if(iid==IID_IUnknown||iid==IID_IArchiveExtractCallback||iid==IID_IProgress)*out=static_cast<IArchiveExtractCallback*>(this);else if(iid==IID_ICryptoGetTextPassword)*out=static_cast<ICryptoGetTextPassword*>(this);if(!*out)return E_NOINTERFACE;AddRef();return S_OK;}
  METHOD(SetTotal,(UInt64 value)){status.total=value;return status.report(0);}
  METHOD(SetCompleted,(const UInt64* value)){return status.report(value?*value:0);}
  METHOD(GetStream,(UInt32 index,ISequentialOutStream** stream,Int32 mode)){
    *stream=nullptr;if(mode!=NArchive::NExtract::NAskMode::kExtract)return S_OK;
    auto found=items.find(index);if(found==items.end())return E_INVALIDARG;
    if(found->second.directory)return S_OK;
    HRESULT progressResult=status.report(0);if(FAILED(progressResult))return progressResult;
    auto output=new(std::nothrow) Output(found->second.path.c_str(),found->second.limit);if(!output)return E_OUTOFMEMORY;
    if(!output->valid()){HRESULT hr=HRESULT_FROM_WIN32(GetLastError());output->Release();return hr;}
    try{outputs.push_back(output);}catch(...){output->Release();return E_OUTOFMEMORY;}
    output->AddRef();*stream=output;return S_OK;
  }
  METHOD(PrepareOperation,(Int32)){return S_OK;}
  METHOD(SetOperationResult,(Int32 value)){if(value!=NArchive::NExtract::NOperationResult::kOK)result=(value==NArchive::NExtract::NOperationResult::kWrongPassword||status.passwordAsked)?PasswordRequired:DataInvalid;return result;}
  METHOD(CryptoGetTextPassword,(BSTR* pw)){return status.getPassword(pw);}
};
class DragDataObject final:public IDataObject {
  std::atomic<ULONG> refs{1};
  std::wstring archivePath,password,tempPath;std::vector<UInt32> indices;std::vector<std::wstring> outputPaths,rootPaths;
  Progress progress;void* context;bool dropCommitted=false,prepared=false,frameworkDrag;HRESULT preparationResult=S_OK;
  HWND owner;std::wstring preparationTitle;std::unordered_map<CLIPFORMAT,STGMEDIUM> shellFormats;
  struct PreparationProgress {
    Progress callback;void* context;Ptr<IProgressDialog> dialog;
    ~PreparationProgress(){if(dialog.p)dialog->StopProgressDialog();}
    static int __cdecl report(UInt64 total,UInt64 done,void* context){
      auto& state=*static_cast<PreparationProgress*>(context);
      if(state.dialog.p){state.dialog->SetProgress64(done,total);if(state.dialog->HasUserCancelled())return 1;}
      return state.callback?state.callback(total,done,state.context):0;
    }
  };
  HRESULT prepare(){
    if(prepared)return preparationResult;prepared=true;preparationResult=E_PENDING;
    try{
      PreparationProgress state{progress,context};
      if(owner&&SUCCEEDED(CoCreateInstance(CLSID_ProgressDialog,nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(state.dialog.out())))){
        state.dialog->SetTitle(preparationTitle.c_str());state.dialog->SetLine(1,archivePath.c_str(),TRUE,nullptr);
        state.dialog->StartProgressDialog(owner,nullptr,PROGDLG_AUTOTIME|PROGDLG_NOMINIMIZE,nullptr);
      }
      Archive a(password.c_str(),PreparationProgress::report,&state);HRESULT hr=a.open(archivePath.c_str());if(FAILED(hr))return preparationResult=hr;
      UInt32 itemCount=0;if(FAILED(hr=a.archive->GetNumberOfItems(&itemCount)))return preparationResult=hr;
      if(indices.empty()||indices.size()!=outputPaths.size()||indices.size()>1000000||rootPaths.empty())return preparationResult=E_INVALIDARG;
      std::unordered_map<UInt32,DragItem> items;items.reserve(indices.size());
      for(size_t i=0;i<indices.size();i++){
        if(indices[i]>=itemCount||!isWithinDirectory(tempPath,outputPaths[i]))return preparationResult=E_INVALIDARG;
        Variant size,directory;a.archive->GetProperty(indices[i],kpidSize,&size.v);a.archive->GetProperty(indices[i],kpidIsDir,&directory.v);
        DragItem item{indices[i],outputPaths[i],propBool(directory.v),std::max<UInt64>(number(size.v),1)};
        if(!items.emplace(item.index,item).second)return preparationResult=E_INVALIDARG;
        const std::wstring directoryPath=item.directory?item.path:item.path.substr(0,item.path.find_last_of(L"\\/"));
        // A file at the archive root has tempPath itself as its parent.
        // Path validation errors must not use a stale (possibly zero) GetLastError.
        if(directoryPath.empty()||(_wcsicmp(tempPath.c_str(),directoryPath.c_str())!=0&&!isWithinDirectory(tempPath,directoryPath)))return preparationResult=E_INVALIDARG;
        if(!ensureDirectory(directoryPath)){DWORD error=GetLastError();return preparationResult=error?HRESULT_FROM_WIN32(error):E_FAIL;}
      }
      for(const auto& root:rootPaths)if(!isWithinDirectory(tempPath,root))return preparationResult=E_INVALIDARG;
      Ptr<DragExtractCallback> callback;callback.p=new DragExtractCallback(a.status,items);
      hr=a.archive->Extract(indices.data(),static_cast<UInt32>(indices.size()),0,callback.p);
      if(FAILED(hr))return preparationResult=a.status.passwordAsked&&hr!=E_ABORT?PasswordRequired:hr;
      if(FAILED(callback->result))return preparationResult=callback->result;
      for(const auto& root:rootPaths){DWORD attributes=GetFileAttributesW(root.c_str());if(attributes==INVALID_FILE_ATTRIBUTES)return preparationResult=HRESULT_FROM_WIN32(GetLastError());}
      return preparationResult=S_OK;
    }catch(...){return preparationResult=E_OUTOFMEMORY;}
  }
public:
  DragDataObject(const wchar_t* archive,const wchar_t* password,const wchar_t* temp,const UInt32* itemIndices,const wchar_t* const* outputs,UInt32 count,const wchar_t* const* roots,UInt32 rootCount,Progress cb,void* ctx,HWND window=nullptr,const wchar_t* title=L"New7z",bool useFrameworkDrag=false)
    :archivePath(archive),password(password?password:L""),tempPath(temp),indices(itemIndices,itemIndices+count),progress(cb),context(ctx),frameworkDrag(useFrameworkDrag),owner(window),preparationTitle(title?title:L"New7z"){
    outputPaths.reserve(count);for(UInt32 i=0;i<count;i++)outputPaths.emplace_back(outputs[i]);
    rootPaths.reserve(rootCount);for(UInt32 i=0;i<rootCount;i++)rootPaths.emplace_back(roots[i]);
  }
  ~DragDataObject(){for(auto& item:shellFormats)ReleaseStgMedium(&item.second);}
  ULONG STDMETHODCALLTYPE AddRef() noexcept override{return ++refs;}
  ULONG STDMETHODCALLTYPE Release() noexcept override{auto n=--refs;if(!n)delete this;return n;}
  HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid,void** out) noexcept override{*out=nullptr;if(iid==IID_IUnknown||iid==IID_IDataObject)*out=static_cast<IDataObject*>(this);if(!*out)return E_NOINTERFACE;AddRef();return S_OK;}
  HRESULT STDMETHODCALLTYPE GetData(FORMATETC* format,STGMEDIUM* medium) noexcept override{
    if(!format||!medium)return E_INVALIDARG;*medium={};HRESULT hr=QueryGetData(format);if(FAILED(hr))return hr;
    if(format->cfFormat!=CF_HDROP){
      return CopyStgMedium(&shellFormats.at(format->cfFormat),medium);
    }
    // Match 7-Zip PanelDrag.cpp: hover exposes an existing temporary directory,
    // without opening/decompressing the archive. Upstream commits final names
    // from QueryContinueDrag on release. WinUI owns that source here, so physical
    // release gates delayed rendering while it tracks the pointer and cursor.
    if(frameworkDrag&&!(GetAsyncKeyState(VK_LBUTTON)&0x8000))dropCommitted=true;
    traceDrag(L"GetData.committed",dropCommitted);
    if(!dropCommitted)return makeDropFiles({tempPath},medium);
    hr=prepare();if(FAILED(hr))return hr;return makeDropFiles(rootPaths,medium);
  }
  HRESULT STDMETHODCALLTYPE GetDataHere(FORMATETC*,STGMEDIUM*) noexcept override{return DATA_E_FORMATETC;}
  HRESULT STDMETHODCALLTYPE QueryGetData(FORMATETC* format) noexcept override{
    if(!format)return E_INVALIDARG;if(format->dwAspect!=DVASPECT_CONTENT)return DV_E_DVASPECT;
    if(format->cfFormat==CF_HDROP)return (format->tymed&TYMED_HGLOBAL)?S_OK:DV_E_TYMED;
    auto item=shellFormats.find(format->cfFormat);if(item==shellFormats.end())return DV_E_FORMATETC;
    return (format->tymed&item->second.tymed)?S_OK:DV_E_TYMED;
  }
  HRESULT STDMETHODCALLTYPE GetCanonicalFormatEtc(FORMATETC*,FORMATETC* output) noexcept override{if(!output)return E_INVALIDARG;output->ptd=nullptr;return DATA_S_SAMEFORMATETC;}
  HRESULT STDMETHODCALLTYPE SetData(FORMATETC* format,STGMEDIUM* medium,BOOL release) noexcept override{
    if(!format||!medium)return E_INVALIDARG;
    if(format->cfFormat==CF_HDROP)return DV_E_FORMATETC;
    if(format->dwAspect!=DVASPECT_CONTENT)return DV_E_DVASPECT;
    if(!(format->tymed&medium->tymed)||!medium->hGlobal)return DV_E_TYMED;
    STGMEDIUM stored=*medium;
    if(!release){stored={};HRESULT hr=CopyStgMedium(medium,&stored);if(FAILED(hr))return hr;}
    try{auto item=shellFormats.try_emplace(format->cfFormat,STGMEDIUM{}).first;ReleaseStgMedium(&item->second);item->second=stored;return S_OK;}
    catch(...){if(!release)ReleaseStgMedium(&stored);return E_OUTOFMEMORY;}
  }
  HRESULT STDMETHODCALLTYPE EnumFormatEtc(DWORD direction,IEnumFORMATETC** out) noexcept override;
  HRESULT STDMETHODCALLTYPE DAdvise(FORMATETC*,DWORD,IAdviseSink*,DWORD*) noexcept override{return OLE_E_ADVISENOTSUPPORTED;}
  HRESULT STDMETHODCALLTYPE DUnadvise(DWORD) noexcept override{return OLE_E_ADVISENOTSUPPORTED;}
  HRESULT STDMETHODCALLTYPE EnumDAdvise(IEnumSTATDATA** out) noexcept override{if(out)*out=nullptr;return OLE_E_ADVISENOTSUPPORTED;}
  HRESULT result()const{return preparationResult;}
  void commitDrop(){dropCommitted=true;}
  bool wasTransferred(DWORD effect)const{
    if(effect&(DROPEFFECT_COPY|DROPEFFECT_MOVE))return true;
    // Explorer can report an optimized move through SetData and return effect 0.
    for(auto name:{CFSTR_PERFORMEDDROPEFFECT,CFSTR_LOGICALPERFORMEDDROPEFFECT}){
      auto item=shellFormats.find(static_cast<CLIPFORMAT>(RegisterClipboardFormatW(name)));
      if(item==shellFormats.end()||item->second.tymed!=TYMED_HGLOBAL||GlobalSize(item->second.hGlobal)<sizeof(DWORD))continue;
      auto value=static_cast<const DWORD*>(GlobalLock(item->second.hGlobal));if(!value)continue;
      bool transferred=(*value&(DROPEFFECT_COPY|DROPEFFECT_MOVE))!=0;GlobalUnlock(item->second.hGlobal);if(transferred)return true;
    }
    return false;
  }
};
class DragFormatEnumerator final:public IEnumFORMATETC {
  std::atomic<ULONG> refs{1};UINT position=0;FORMATETC format{CF_HDROP,nullptr,DVASPECT_CONTENT,-1,TYMED_HGLOBAL};
public:
  ULONG STDMETHODCALLTYPE AddRef() noexcept override{return ++refs;}
  ULONG STDMETHODCALLTYPE Release() noexcept override{auto n=--refs;if(!n)delete this;return n;}
  HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid,void** out) noexcept override{*out=nullptr;if(iid==IID_IUnknown||iid==IID_IEnumFORMATETC)*out=static_cast<IEnumFORMATETC*>(this);if(!*out)return E_NOINTERFACE;AddRef();return S_OK;}
  HRESULT STDMETHODCALLTYPE Next(ULONG count,FORMATETC* out,ULONG* fetched) noexcept override{if(fetched)*fetched=0;if(count&& !out)return E_POINTER;ULONG copied=0;while(copied<count&&position==0){out[copied++]=format;position=1;}if(fetched)*fetched=copied;return copied==count?S_OK:S_FALSE;}
  HRESULT STDMETHODCALLTYPE Skip(ULONG count) noexcept override{if(position==0&&count){position=1;return count==1?S_OK:S_FALSE;}return count==0?S_OK:S_FALSE;}
  HRESULT STDMETHODCALLTYPE Reset() noexcept override{position=0;return S_OK;}
  HRESULT STDMETHODCALLTYPE Clone(IEnumFORMATETC** out) noexcept override{if(!out)return E_POINTER;auto clone=new(std::nothrow) DragFormatEnumerator();if(!clone)return E_OUTOFMEMORY;clone->position=position;*out=clone;return S_OK;}
};
HRESULT DragDataObject::EnumFormatEtc(DWORD direction,IEnumFORMATETC** out) noexcept{if(!out)return E_POINTER;*out=nullptr;if(direction!=DATADIR_GET)return E_NOTIMPL;auto enumerator=new(std::nothrow) DragFormatEnumerator();if(!enumerator)return E_OUTOFMEMORY;*out=enumerator;return S_OK;}
static HRESULT fillNewItemProperty(const wchar_t* path,const wchar_t* name,PROPID prop,PROPVARIANT* value){
  PropVariantInit(value);WIN32_FILE_ATTRIBUTE_DATA info;
  if(!GetFileAttributesExW(path,GetFileExInfoStandard,&info))return HRESULT_FROM_WIN32(GetLastError());
  switch(prop){case kpidPath:value->vt=VT_BSTR;value->bstrVal=SysAllocString(name);break;case kpidIsDir:value->vt=VT_BOOL;value->boolVal=(info.dwFileAttributes&FILE_ATTRIBUTE_DIRECTORY)?VARIANT_TRUE:VARIANT_FALSE;break;case kpidSize:value->vt=VT_UI8;value->uhVal.QuadPart=(UInt64(info.nFileSizeHigh)<<32)|info.nFileSizeLow;break;case kpidAttrib:value->vt=VT_UI4;value->ulVal=info.dwFileAttributes;break;case kpidMTime:value->vt=VT_FILETIME;value->filetime=info.ftLastWriteTime;break;}
  return S_OK;
}
class UpdateCallback final:public IArchiveUpdateCallback,public ICryptoGetTextPassword2 {
  Status& status;const wchar_t* const* paths;const wchar_t* const* names;
public:
  HRESULT result=S_OK;
  UpdateCallback(Status& s,const wchar_t* const* p,const wchar_t* const* n):status(s),paths(p),names(n){}
  REFS
  METHOD(QueryInterface,(REFIID iid,void** out)){*out=nullptr;if(iid==IID_IUnknown||iid==IID_IArchiveUpdateCallback||iid==IID_IProgress)*out=static_cast<IArchiveUpdateCallback*>(this);else if(iid==IID_ICryptoGetTextPassword2)*out=static_cast<ICryptoGetTextPassword2*>(this);if(!*out)return E_NOINTERFACE;AddRef();return S_OK;}
  METHOD(SetTotal,(UInt64 value)){status.total=value;return status.report(0);}
  METHOD(SetCompleted,(const UInt64* value)){return status.report(value?*value:0);}
  METHOD(GetUpdateItemInfo,(UInt32,Int32* data,Int32* props,UInt32* index)){*data=1;*props=1;*index=UInt32(-1);return status.report(0);}
  METHOD(GetProperty,(UInt32 i,PROPID prop,PROPVARIANT* value)){return fillNewItemProperty(paths[i],names[i],prop,value);}
  METHOD(GetStream,(UInt32 i,ISequentialInStream** stream)){*stream=nullptr;if(FAILED(status.report(0)))return E_ABORT;if(GetFileAttributesW(paths[i])&FILE_ATTRIBUTE_DIRECTORY)return S_OK;auto input=new Input(paths[i]);if(!input->valid()){input->Release();return HRESULT_FROM_WIN32(GetLastError());}*stream=input;return S_OK;}
  METHOD(SetOperationResult,(Int32 r)){if(r!=0)result=E_FAIL;return result;}
  METHOD(CryptoGetTextPassword2,(Int32* defined,BSTR* pw)){*defined=status.password.empty()?0:1;*pw=*defined?SysAllocString(status.password.c_str()):nullptr;return S_OK;}
};
// Rewrites the archive as a new file, copying every entry except the removed indices and
// appending new sources. The caller replaces the original only after this succeeds.
struct UpdateItem{int isNew;UInt32 indexInArchive;const wchar_t* path;const wchar_t* name;};
class RewriteCallback final:public IArchiveUpdateCallback,public ICryptoGetTextPassword2 {
  Status& status;const UpdateItem* items;UInt32 count;
public:
  HRESULT result=S_OK;
  RewriteCallback(Status& s,const UpdateItem* i,UInt32 c):status(s),items(i),count(c){}
  REFS
  METHOD(QueryInterface,(REFIID iid,void** out)){*out=nullptr;if(iid==IID_IUnknown||iid==IID_IArchiveUpdateCallback||iid==IID_IProgress)*out=static_cast<IArchiveUpdateCallback*>(this);else if(iid==IID_ICryptoGetTextPassword2)*out=static_cast<ICryptoGetTextPassword2*>(this);if(!*out)return E_NOINTERFACE;AddRef();return S_OK;}
  METHOD(SetTotal,(UInt64 value)){status.total=value;return status.report(0);}
  METHOD(SetCompleted,(const UInt64* value)){return status.report(value?*value:0);}
  METHOD(GetUpdateItemInfo,(UInt32 i,Int32* data,Int32* props,UInt32* index)){if(i>=count)return E_INVALIDARG;const auto& item=items[i];if(item.isNew){*data=1;*props=1;*index=UInt32(-1);}else{*data=0;*props=0;*index=item.indexInArchive;}return status.report(0);}
  METHOD(GetProperty,(UInt32 i,PROPID prop,PROPVARIANT* value)){if(i>=count||!items[i].isNew){PropVariantInit(value);return S_OK;}return fillNewItemProperty(items[i].path,items[i].name,prop,value);}
  METHOD(GetStream,(UInt32 i,ISequentialInStream** stream)){*stream=nullptr;if(i>=count)return E_INVALIDARG;if(!items[i].isNew)return S_OK;if(FAILED(status.report(0)))return E_ABORT;const wchar_t* path=items[i].path;if(GetFileAttributesW(path)&FILE_ATTRIBUTE_DIRECTORY)return S_OK;auto input=new Input(path);if(!input->valid()){input->Release();return HRESULT_FROM_WIN32(GetLastError());}*stream=input;return S_OK;}
  METHOD(SetOperationResult,(Int32 r)){if(r!=0)result=E_FAIL;return result;}
  METHOD(CryptoGetTextPassword2,(Int32* defined,BSTR* pw)){*defined=status.password.empty()?0:1;*pw=*defined?SysAllocString(status.password.c_str()):nullptr;return S_OK;}
};
API int __cdecl NzInitialize(const wchar_t* path){std::lock_guard<std::mutex> lock(initMutex);if(engine)return S_OK;engine=LoadLibraryExW(path,nullptr,LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);if(!engine)return HRESULT_FROM_WIN32(GetLastError());createObject=reinterpret_cast<Creator>(GetProcAddress(engine,"CreateObject"));getHandlerProp=reinterpret_cast<HandlerProp>(GetProcAddress(engine,"GetHandlerProperty2"));return createObject&&getHandlerProp?S_OK:E_FAIL;}
API void __cdecl NzFree(void* ptr){CoTaskMemFree(ptr);}
API void __cdecl NzGetDragDiagnostics(wchar_t** output){*output=nullptr;size_t bytes=(dragDiagnostics.size()+1)*sizeof(wchar_t);*output=static_cast<wchar_t*>(CoTaskMemAlloc(bytes));if(*output)memcpy(*output,dragDiagnostics.c_str(),bytes);}
API int __cdecl NzCreateArchiveDragData(IUnknown* package,HWND owner,const wchar_t* archive,const wchar_t* password,const UInt32* indices,const wchar_t* const* outputPaths,UInt32 count,const wchar_t* const* roots,UInt32 rootCount,const wchar_t* temp,const wchar_t* preparationTitle,DragDataObject** session){
  if(!session)return E_POINTER;*session=nullptr;
  if(!package||!archive||!indices||!outputPaths||!count||count>1000000||!roots||!rootCount||rootCount>1000000||!temp)return E_INVALIDARG;
  dragDiagnostics.clear();
  try{
    Ptr<IDataObjectProvider> provider;HRESULT hr=package->QueryInterface(IID_PPV_ARGS(provider.out()));if(FAILED(hr))return hr;
    Ptr<DragDataObject> data;data.p=new DragDataObject(archive,password,temp,indices,outputPaths,count,roots,rootCount,nullptr,nullptr,owner,preparationTitle,true);
    hr=provider->SetDataObject(data.p);if(FAILED(hr))return hr;
    *session=data.p;data.p=nullptr;return S_OK;
  }catch(...){return E_OUTOFMEMORY;}
}
API int __cdecl NzArchiveDragResult(DragDataObject* session){return session?session->result():E_POINTER;}
API int __cdecl NzArchiveDragWasTransferred(DragDataObject* session){return session&&session->wasTransferred(DROPEFFECT_NONE)?1:0;}
API void __cdecl NzReleaseArchiveDragData(DragDataObject* session){if(session)session->Release();}
API int __cdecl NzList(const wchar_t* path,const wchar_t* password,wchar_t** json,Progress progress,void* ctx){*json=nullptr;try{Archive a(password,progress,ctx);HRESULT hr=a.open(path);if(FAILED(hr))return hr;UInt32 count=0;if(FAILED(hr=a.archive->GetNumberOfItems(&count)))return hr;if(count>1000000)return SizeLimit;std::wstring text=L"[";for(UInt32 i=0;i<count;i++){if(FAILED(a.status.report(i)))return E_ABORT;Variant name,size,packed,dir,encrypted,time,link,hard,attrs;a.archive->GetProperty(i,kpidPath,&name.v);a.archive->GetProperty(i,kpidSize,&size.v);a.archive->GetProperty(i,kpidPackSize,&packed.v);a.archive->GetProperty(i,kpidIsDir,&dir.v);a.archive->GetProperty(i,kpidEncrypted,&encrypted.v);a.archive->GetProperty(i,kpidMTime,&time.v);a.archive->GetProperty(i,kpidSymLink,&link.v);a.archive->GetProperty(i,kpidHardLink,&hard.v);a.archive->GetProperty(i,kpidAttrib,&attrs.v);if(i)text+=L",";UInt64 ft=time.v.vt==VT_FILETIME?(UInt64(time.v.filetime.dwHighDateTime)<<32)|time.v.filetime.dwLowDateTime:0;text+=L"{\"Index\":"+std::to_wstring(i)+L",\"Path\":"+quote(name.v.vt==VT_BSTR?name.v.bstrVal:L"content")+L",\"Size\":"+std::to_wstring(number(size.v))+L",\"PackedSize\":"+(packed.v.vt==VT_EMPTY?L"null":std::to_wstring(number(packed.v)))+L",\"IsDirectory\":"+(propBool(dir.v)?L"true":L"false")+L",\"Encrypted\":"+(propBool(encrypted.v)?L"true":L"false")+L",\"IsLink\":"+((link.v.vt==VT_BSTR||hard.v.vt==VT_BSTR||(number(attrs.v)&FILE_ATTRIBUTE_REPARSE_POINT))?L"true":L"false")+L",\"FileTime\":"+std::to_wstring(ft)+L"}";}text+=L"]";const auto n=(text.size()+1)*sizeof(wchar_t);*json=static_cast<wchar_t*>(CoTaskMemAlloc(n));if(!*json)return E_OUTOFMEMORY;memcpy(*json,text.c_str(),n);return S_OK;}catch(...){return E_OUTOFMEMORY;}}
API int __cdecl NzExtract(const wchar_t* path,const wchar_t* password,UInt32 index,const wchar_t* output,UInt64 limit,unsigned char** data,UInt64* size,Progress progress,void* ctx){if(data)*data=nullptr;if(size)*size=0;try{Archive a(password,progress,ctx);HRESULT hr=a.open(path);if(FAILED(hr))return hr;Variant property;a.archive->GetProperty(index,kpidSize,&property.v);if(number(property.v)>limit)return SizeLimit;Ptr<Output> stream;stream.p=new Output(output,limit);if(!stream->valid())return HRESULT_FROM_WIN32(GetLastError());Ptr<ExtractCallback> callback;callback.p=new ExtractCallback(a.status,stream.p);hr=a.archive->Extract(&index,1,0,callback.p);if(FAILED(hr))return a.status.passwordAsked&&hr!=E_ABORT?PasswordRequired:hr;if(FAILED(callback->result))return callback->result;if(!output&&data&&size){*size=stream->bytes.size();*data=static_cast<unsigned char*>(CoTaskMemAlloc(size_t(*size?*size:1)));if(!*data)return E_OUTOFMEMORY;if(*size)memcpy(*data,stream->bytes.data(),size_t(*size));}return S_OK;}catch(...){return E_OUTOFMEMORY;}}
API int __cdecl NzTest(const wchar_t* path,const wchar_t* password,Progress progress,void* ctx){try{Archive a(password,progress,ctx);HRESULT hr=a.open(path);if(FAILED(hr))return hr;Ptr<ExtractCallback> callback;callback.p=new ExtractCallback(a.status,nullptr);hr=a.archive->Extract(nullptr,UInt32(-1),1,callback.p);return FAILED(hr)?hr:callback->result;}catch(...){return E_FAIL;}}
API int __cdecl NzCreate(const wchar_t* path,int format,int level,const wchar_t* method,const wchar_t* password,int encryptNames,const wchar_t* const* paths,const wchar_t* const* names,UInt32 count,Progress progress,void* ctx){try{if(!createObject)return E_UNEXPECTED;if(format<0||format>2)return E_INVALIDARG;const BYTE handler=format==1?1:format==2?0xEE:7;GUID cls={0x23170F69,0x40C1,0x278A,{0x10,0,0,1,0x10,handler,0,0}};Ptr<IOutArchive> archive;HRESULT hr=createObject(&cls,&IID_IOutArchive,reinterpret_cast<void**>(archive.out()));if(FAILED(hr))return hr;Ptr<ISetProperties> props;if(SUCCEEDED(archive->QueryInterface(IID_ISetProperties,reinterpret_cast<void**>(props.out())))){const wchar_t* keys[4];PROPVARIANT values[4]{};UInt32 n=0;keys[n]=L"x";values[n].vt=VT_UI4;values[n].ulVal=level;n++;if(format!=2&&method&&*method){keys[n]=format==1?L"m":L"0";if(format==1&&_wcsicmp(method,L"Copy")==0){values[n].vt=VT_UI4;values[n].ulVal=0;}else{values[n].vt=VT_BSTR;values[n].bstrVal=SysAllocString(method);}n++;}if(password&&*password){if(format==1){keys[n]=L"em";values[n].vt=VT_BSTR;values[n].bstrVal=SysAllocString(L"AES256");n++;}else if(format==0){keys[n]=L"he";values[n].vt=VT_BOOL;values[n].boolVal=encryptNames?VARIANT_TRUE:VARIANT_FALSE;n++;}}hr=props->SetProperties(keys,values,n);for(UInt32 i=0;i<n;i++)PropVariantClear(&values[i]);if(FAILED(hr))return hr;}Status status(password,progress,ctx);Ptr<Output> output;output.p=new Output(path,UINT64_MAX);if(!output->valid())return HRESULT_FROM_WIN32(GetLastError());Ptr<UpdateCallback> callback;callback.p=new UpdateCallback(status,paths,names);hr=archive->UpdateItems(output.p,count,callback.p);return FAILED(hr)?hr:callback->result;}catch(...){return E_FAIL;}}
API int __cdecl NzUpdate(const wchar_t* path,const wchar_t* output,const wchar_t* password,int zip,const UInt32* removeIndices,UInt32 removeCount,const wchar_t* const* addPaths,const wchar_t* const* addNames,UInt32 addCount,Progress progress,void* ctx){
  try{
    if(!createObject)return E_UNEXPECTED;
    Archive a(password,progress,ctx);
    HRESULT hr=a.open(path);
    if(FAILED(hr))return hr;
    Ptr<IOutArchive> out;
    if(FAILED(a.archive->QueryInterface(IID_IOutArchive,reinterpret_cast<void**>(out.out()))))return UpdateUnsupported;
    // Keep an encrypted 7z header encrypted when the opened archive required the password.
    if(!a.status.password.empty()&&a.status.passwordAsked){
      Ptr<ISetProperties> props;
      if(SUCCEEDED(out->QueryInterface(IID_ISetProperties,reinterpret_cast<void**>(props.out())))){
        const wchar_t* keys[]={L"he"};PROPVARIANT value{};value.vt=VT_BOOL;value.boolVal=VARIANT_TRUE;props->SetProperties(keys,&value,1);
      }
    }
    // ZIP defaults to ZipCrypto when only a password is given; keep AES-256 like archive creation.
    if(zip&&password&&*password){
      Ptr<ISetProperties> props;
      if(SUCCEEDED(out->QueryInterface(IID_ISetProperties,reinterpret_cast<void**>(props.out())))){
        const wchar_t* keys[]={L"em"};PROPVARIANT value{};value.vt=VT_BSTR;value.bstrVal=SysAllocString(L"AES256");
        if(value.bstrVal){props->SetProperties(keys,&value,1);PropVariantClear(&value);}
      }
    }
    UInt32 existing=0;
    if(FAILED(hr=a.archive->GetNumberOfItems(&existing)))return hr;
    if(existing>1000000||addCount>1000000)return SizeLimit;
    std::vector<char> removed(existing,0);
    for(UInt32 i=0;i<removeCount;i++){
      if(removeIndices[i]>=existing)return E_INVALIDARG;
      removed[removeIndices[i]]=1;
    }
    std::vector<UpdateItem> items;
    items.reserve(static_cast<size_t>(existing)+addCount);
    for(UInt32 i=0;i<existing;i++)if(!removed[i])items.push_back({0,i,nullptr,nullptr});
    for(UInt32 i=0;i<addCount;i++)items.push_back({1,UInt32(-1),addPaths[i],addNames[i]});
    Status status(password,progress,ctx);
    Ptr<Output> outputStream;
    outputStream.p=new Output(output,UINT64_MAX);
    if(!outputStream->valid())return HRESULT_FROM_WIN32(GetLastError());
    Ptr<RewriteCallback> callback;
    callback.p=new RewriteCallback(status,items.data(),static_cast<UInt32>(items.size()));
    hr=out->UpdateItems(outputStream.p,static_cast<UInt32>(items.size()),callback.p);
    return FAILED(hr)?hr:callback->result;
  }catch(...){return E_OUTOFMEMORY;}
}

