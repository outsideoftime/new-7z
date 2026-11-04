#define NOMINMAX
#include <windows.h>
#include <propidl.h>
#include <oleauto.h>
#include <string>
#include <vector>
#include <algorithm>
#include <atomic>
#include <mutex>
#include "../7zSdk/Source/CPP/Common/MyInitGuid.h"
#include "../7zSdk/Source/CPP/7zip/Archive/IArchive.h"
#include "../7zSdk/Source/CPP/7zip/IPassword.h"

using Progress = int(__cdecl*)(unsigned long long, unsigned long long, void*);
constexpr HRESULT PasswordRequired=HRESULT(0x80040201), SizeLimit=HRESULT(0x80040202), DataInvalid=HRESULT(0x80040203);
using Creator=HRESULT(WINAPI*)(const GUID*,const GUID*,void**);
using HandlerProp=HRESULT(WINAPI*)(UInt32,PROPID,PROPVARIANT*);
static HMODULE engine=nullptr;
static Creator createObject=nullptr;
static HandlerProp getHandlerProp=nullptr;
static std::mutex initMutex;
thread_local std::wstring errorText;
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
  METHOD(GetProperty,(UInt32 i,PROPID prop,PROPVARIANT* value)){PropVariantInit(value);WIN32_FILE_ATTRIBUTE_DATA info;if(!GetFileAttributesExW(paths[i],GetFileExInfoStandard,&info))return HRESULT_FROM_WIN32(GetLastError());switch(prop){case kpidPath:value->vt=VT_BSTR;value->bstrVal=SysAllocString(names[i]);break;case kpidIsDir:value->vt=VT_BOOL;value->boolVal=(info.dwFileAttributes&FILE_ATTRIBUTE_DIRECTORY)?VARIANT_TRUE:VARIANT_FALSE;break;case kpidSize:value->vt=VT_UI8;value->uhVal.QuadPart=(UInt64(info.nFileSizeHigh)<<32)|info.nFileSizeLow;break;case kpidAttrib:value->vt=VT_UI4;value->ulVal=info.dwFileAttributes;break;case kpidMTime:value->vt=VT_FILETIME;value->filetime=info.ftLastWriteTime;break;}return S_OK;}
  METHOD(GetStream,(UInt32 i,ISequentialInStream** stream)){*stream=nullptr;if(FAILED(status.report(0)))return E_ABORT;if(GetFileAttributesW(paths[i])&FILE_ATTRIBUTE_DIRECTORY)return S_OK;auto input=new Input(paths[i]);if(!input->valid()){input->Release();return HRESULT_FROM_WIN32(GetLastError());}*stream=input;return S_OK;}
  METHOD(SetOperationResult,(Int32 r)){if(r!=0)result=E_FAIL;return result;}
  METHOD(CryptoGetTextPassword2,(Int32* defined,BSTR* pw)){*defined=status.password.empty()?0:1;*pw=*defined?SysAllocString(status.password.c_str()):nullptr;return S_OK;}
};
API int __cdecl NzInitialize(const wchar_t* path){std::lock_guard<std::mutex> lock(initMutex);if(engine)return S_OK;engine=LoadLibraryExW(path,nullptr,LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);if(!engine)return HRESULT_FROM_WIN32(GetLastError());createObject=reinterpret_cast<Creator>(GetProcAddress(engine,"CreateObject"));getHandlerProp=reinterpret_cast<HandlerProp>(GetProcAddress(engine,"GetHandlerProperty2"));return createObject&&getHandlerProp?S_OK:E_FAIL;}
API void __cdecl NzFree(void* ptr){CoTaskMemFree(ptr);}
API int __cdecl NzList(const wchar_t* path,const wchar_t* password,wchar_t** json,Progress progress,void* ctx){*json=nullptr;try{Archive a(password,progress,ctx);HRESULT hr=a.open(path);if(FAILED(hr))return hr;UInt32 count=0;if(FAILED(hr=a.archive->GetNumberOfItems(&count)))return hr;if(count>1000000)return SizeLimit;std::wstring text=L"[";for(UInt32 i=0;i<count;i++){if(FAILED(a.status.report(i)))return E_ABORT;Variant name,size,packed,dir,encrypted,time,link,hard,attrs;a.archive->GetProperty(i,kpidPath,&name.v);a.archive->GetProperty(i,kpidSize,&size.v);a.archive->GetProperty(i,kpidPackSize,&packed.v);a.archive->GetProperty(i,kpidIsDir,&dir.v);a.archive->GetProperty(i,kpidEncrypted,&encrypted.v);a.archive->GetProperty(i,kpidMTime,&time.v);a.archive->GetProperty(i,kpidSymLink,&link.v);a.archive->GetProperty(i,kpidHardLink,&hard.v);a.archive->GetProperty(i,kpidAttrib,&attrs.v);if(i)text+=L",";UInt64 ft=time.v.vt==VT_FILETIME?(UInt64(time.v.filetime.dwHighDateTime)<<32)|time.v.filetime.dwLowDateTime:0;text+=L"{\"Index\":"+std::to_wstring(i)+L",\"Path\":"+quote(name.v.vt==VT_BSTR?name.v.bstrVal:L"content")+L",\"Size\":"+std::to_wstring(number(size.v))+L",\"PackedSize\":"+(packed.v.vt==VT_EMPTY?L"null":std::to_wstring(number(packed.v)))+L",\"IsDirectory\":"+(propBool(dir.v)?L"true":L"false")+L",\"Encrypted\":"+(propBool(encrypted.v)?L"true":L"false")+L",\"IsLink\":"+((link.v.vt==VT_BSTR||hard.v.vt==VT_BSTR||(number(attrs.v)&FILE_ATTRIBUTE_REPARSE_POINT))?L"true":L"false")+L",\"FileTime\":"+std::to_wstring(ft)+L"}";}text+=L"]";const auto n=(text.size()+1)*sizeof(wchar_t);*json=static_cast<wchar_t*>(CoTaskMemAlloc(n));if(!*json)return E_OUTOFMEMORY;memcpy(*json,text.c_str(),n);return S_OK;}catch(...){return E_OUTOFMEMORY;}}
API int __cdecl NzExtract(const wchar_t* path,const wchar_t* password,UInt32 index,const wchar_t* output,UInt64 limit,unsigned char** data,UInt64* size,Progress progress,void* ctx){if(data)*data=nullptr;if(size)*size=0;try{Archive a(password,progress,ctx);HRESULT hr=a.open(path);if(FAILED(hr))return hr;Variant property;a.archive->GetProperty(index,kpidSize,&property.v);if(number(property.v)>limit)return SizeLimit;Ptr<Output> stream;stream.p=new Output(output,limit);if(!stream->valid())return HRESULT_FROM_WIN32(GetLastError());Ptr<ExtractCallback> callback;callback.p=new ExtractCallback(a.status,stream.p);hr=a.archive->Extract(&index,1,0,callback.p);if(FAILED(hr))return a.status.passwordAsked&&hr!=E_ABORT?PasswordRequired:hr;if(FAILED(callback->result))return callback->result;if(!output&&data&&size){*size=stream->bytes.size();*data=static_cast<unsigned char*>(CoTaskMemAlloc(size_t(*size?*size:1)));if(!*data)return E_OUTOFMEMORY;if(*size)memcpy(*data,stream->bytes.data(),size_t(*size));}return S_OK;}catch(...){return E_OUTOFMEMORY;}}
API int __cdecl NzTest(const wchar_t* path,const wchar_t* password,Progress progress,void* ctx){try{Archive a(password,progress,ctx);HRESULT hr=a.open(path);if(FAILED(hr))return hr;Ptr<ExtractCallback> callback;callback.p=new ExtractCallback(a.status,nullptr);hr=a.archive->Extract(nullptr,UInt32(-1),1,callback.p);return FAILED(hr)?hr:callback->result;}catch(...){return E_FAIL;}}
API int __cdecl NzCreate(const wchar_t* path,int zip,int level,const wchar_t* password,int encryptNames,const wchar_t* const* paths,const wchar_t* const* names,UInt32 count,Progress progress,void* ctx){try{if(!createObject)return E_UNEXPECTED;GUID cls={0x23170F69,0x40C1,0x278A,{0x10,0,0,1,0x10,BYTE(zip?1:7),0,0}};Ptr<IOutArchive> archive;HRESULT hr=createObject(&cls,&IID_IOutArchive,reinterpret_cast<void**>(archive.out()));if(FAILED(hr))return hr;Ptr<ISetProperties> props;if(SUCCEEDED(archive->QueryInterface(IID_ISetProperties,reinterpret_cast<void**>(props.out())))){const wchar_t* keys[]={L"x",L"he",L"em"};PROPVARIANT values[3]{};values[0].vt=VT_UI4;values[0].ulVal=level;values[1].vt=VT_BOOL;values[1].boolVal=encryptNames?VARIANT_TRUE:VARIANT_FALSE;values[2].vt=VT_BSTR;values[2].bstrVal=SysAllocString(L"AES256");if(zip){const wchar_t* zipKeys[]={keys[0],keys[2]};PROPVARIANT zipValues[]={values[0],values[2]};hr=props->SetProperties(zipKeys,zipValues,password&&*password?2:1);}else hr=props->SetProperties(keys,values,password&&*password?2:1);PropVariantClear(&values[2]);if(FAILED(hr))return hr;}Status status(password,progress,ctx);Ptr<Output> output;output.p=new Output(path,UINT64_MAX);if(!output->valid())return HRESULT_FROM_WIN32(GetLastError());Ptr<UpdateCallback> callback;callback.p=new UpdateCallback(status,paths,names);hr=archive->UpdateItems(output.p,count,callback.p);return FAILED(hr)?hr:callback->result;}catch(...){return E_FAIL;}}

