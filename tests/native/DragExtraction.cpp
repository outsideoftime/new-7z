#include "../../native/New7z.Native.cpp"
#include <filesystem>
#include <fstream>
#include <iostream>
#include <stdexcept>
#include <roapi.h>
#include <winstring.h>

namespace fs=std::filesystem;
static unsigned checks=0;
static void check(bool condition,const char* message){if(!condition)throw std::runtime_error(message);++checks;}
static std::vector<std::wstring> readDrop(IDataObject& data){
  FORMATETC format{CF_HDROP,nullptr,DVASPECT_CONTENT,-1,TYMED_HGLOBAL};STGMEDIUM medium{};
  HRESULT hr=data.GetData(&format,&medium);
  if(FAILED(hr))throw std::runtime_error("GetData failed: "+std::to_string(static_cast<unsigned>(hr)));
  std::vector<std::wstring> paths;
  const auto drop=static_cast<HDROP>(medium.hGlobal);UINT count=DragQueryFileW(drop,UINT(-1),nullptr,0);
  for(UINT i=0;i<count;++i){std::wstring path(DragQueryFileW(drop,i,nullptr,0)+1,L'\0');DragQueryFileW(drop,i,path.data(),static_cast<UINT>(path.size()));path.pop_back();paths.push_back(path);}
  ReleaseStgMedium(&medium);return paths;
}
static void write(const fs::path& path,const std::string& value){std::ofstream file(path,std::ios::binary);file<<value;}
static std::string read(const fs::path& path){std::ifstream file(path,std::ios::binary);return {std::istreambuf_iterator<char>(file),std::istreambuf_iterator<char>()};}
struct IsolatedState{fs::path output;unsigned calls=0,released=0;};
static int __cdecl isolatedPrepare(void* context,Progress progress,void* state){
  auto& request=*static_cast<IsolatedState*>(context);++request.calls;
  if(progress(1,0,state))return E_ABORT;
  write(request.output,"isolated worker output");return progress(1,1,state)?E_ABORT:S_OK;
}
static void __cdecl isolatedRelease(void* context){++static_cast<IsolatedState*>(context)->released;}
static void shellCopy(IDataObject* data,const fs::path& destination){
  fs::create_directories(destination);
  Ptr<IShellItem> folder;check(SUCCEEDED(SHCreateItemFromParsingName(destination.c_str(),nullptr,IID_PPV_ARGS(folder.out()))),"resolve Shell destination");
  Ptr<IFileOperation> operation;check(SUCCEEDED(CoCreateInstance(CLSID_FileOperation,nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(operation.out()))),"create Shell copy operation");
  check(SUCCEEDED(operation->SetOperationFlags(FOF_NO_UI)),"disable copy dialogs for isolated test files");
  check(SUCCEEDED(operation->CopyItems(data,folder.p)),"Shell accepts archive drag data object");
  check(SUCCEEDED(operation->PerformOperations()),"Shell copies archive drag data");
  BOOL aborted=TRUE;check(SUCCEEDED(operation->GetAnyOperationsAborted(&aborted))&&!aborted,"Shell copy finishes without cancellation");
}
static void shellDrop(DragDataObject* data,const fs::path& destination){
  fs::create_directories(destination);
  Ptr<IShellItem> folder;check(SUCCEEDED(SHCreateItemFromParsingName(destination.c_str(),nullptr,IID_PPV_ARGS(folder.out()))),"resolve Shell drop destination");
  Ptr<IDropTarget> target;check(SUCCEEDED(folder->BindToHandler(nullptr,BHID_SFUIObject,IID_PPV_ARGS(target.out()))),"bind actual filesystem Shell drop target");
  DWORD effect=DROPEFFECT_COPY;POINTL point{};
  check(SUCCEEDED(target->DragEnter(data,MK_LBUTTON,point,&effect))&&(effect&DROPEFFECT_COPY),"actual Shell target accepts archive drag on entry");
  data->commitDrop();
  effect=DROPEFFECT_COPY;check(SUCCEEDED(target->Drop(data,0,point,&effect))&&(effect&DROPEFFECT_COPY),"actual Shell target accepts and copies archive drop");
}
int wmain(int argc,wchar_t** argv){
  if(argc!=2)return 2;
  const HRESULT initialized=OleInitialize(nullptr);if(FAILED(initialized))return 2;
  struct OleCleanup{~OleCleanup(){OleUninitialize();}} oleCleanup;
  const fs::path root=fs::temp_directory_path()/(L"New7z-drag-tests-"+std::to_wstring(GetCurrentProcessId()));
  try{
    fs::create_directories(root/L"source"/L"empty");
    write(root/L"source"/L"root.txt","root contents");write(root/L"source"/L"nested.txt","nested contents");write(root/L"source"/L"zero.txt","");
    check(SUCCEEDED(NzInitialize(argv[1])),"initialize engine");
    for(int zip=0;zip<=1;++zip){
      const auto archive=(root/(zip?L"fixture.zip":L"fixture.7z")).wstring();
      const std::wstring sourceRoot=(root/L"source"/L"root.txt").wstring(),sourceNested=(root/L"source"/L"nested.txt").wstring(),sourceEmpty=(root/L"source"/L"empty").wstring(),sourceZero=(root/L"source"/L"zero.txt").wstring();
      const wchar_t* sources[]={sourceRoot.c_str(),sourceNested.c_str(),sourceEmpty.c_str(),sourceZero.c_str()};
      // Deliberately omit explicit parent entries, as many ZIP files do.
      const wchar_t* names[]={L"根文件.txt",L"目录/子目录/文件.txt",L"空目录",L"零字节.txt"};
      check(SUCCEEDED(NzCreate(archive.c_str(),zip,5,nullptr,nullptr,0,sources,names,4,nullptr,nullptr)),"create archive fixture");
      UInt32 archiveIndices[4]{};
      {Archive opened(nullptr,nullptr,nullptr);check(SUCCEEDED(opened.open(archive.c_str())),"open drag fixture");UInt32 count=0;opened.archive->GetNumberOfItems(&count);
        for(UInt32 i=0;i<count;++i){Variant name;opened.archive->GetProperty(i,kpidPath,&name.v);if(name.v.vt!=VT_BSTR)continue;std::wstring path=name.v.bstrVal;std::replace(path.begin(),path.end(),L'\\',L'/');for(size_t n=0;n<4;++n)if(path==names[n])archiveIndices[n]=i;}}
      UInt32 index=archiveIndices[0];const std::wstring temp=(root/(zip?L"zip-root":L"7z-root")).wstring(),output=(fs::path(temp)/L"根文件.txt").wstring();fs::create_directories(temp);
      const wchar_t* outputs[]={output.c_str()};
      DragDataObject file(archive.c_str(),nullptr,temp.c_str(),&index,outputs,1,outputs,1,nullptr,nullptr);
      // Match the upstream 7-Zip lifecycle, including multiple hover probes.
      check(readDrop(file)==std::vector<std::wstring>{temp}&&readDrop(file)==std::vector<std::wstring>{temp},"hover exposes existing temporary directory without extraction");
      check(!fs::exists(output),"hover does not decompress the archive");
      file.commitDrop();check(!fs::exists(output),"release commits the drop without extracting before GetData");
      auto paths=readDrop(file);check(paths==std::vector<std::wstring>{output},"drop returns final selected paths");
      check(read(output)=="root contents","root file exists with complete contents before Drop GetData returns");
      check(readDrop(file)==paths,"repeated data request keeps the same paths");
      HANDLE readable=CreateFileW(output.c_str(),GENERIC_READ,0,nullptr,OPEN_EXISTING,0,nullptr);
      check(readable!=INVALID_HANDLE_VALUE,"extraction releases file handles before shell reads");if(readable!=INVALID_HANDLE_VALUE)CloseHandle(readable);

      const std::wstring folderTemp=(root/(zip?L"zip-folders":L"7z-folders")).wstring();fs::create_directories(folderTemp);
      const std::wstring folderRoot=(fs::path(folderTemp)/L"目录").wstring(),nested=(fs::path(folderRoot)/L"子目录"/L"文件.txt").wstring(),empty=(fs::path(folderTemp)/L"空目录").wstring(),zero=(fs::path(folderTemp)/L"零字节.txt").wstring();
      std::vector<std::pair<UInt32,const wchar_t*>> selection={{archiveIndices[1],nested.c_str()},{archiveIndices[2],empty.c_str()},{archiveIndices[3],zero.c_str()}};std::sort(selection.begin(),selection.end());
      UInt32 indices[3];const wchar_t* folderOutputs[3];for(size_t i=0;i<3;++i){indices[i]=selection[i].first;folderOutputs[i]=selection[i].second;}const wchar_t* roots[]={folderRoot.c_str(),empty.c_str(),zero.c_str()};
      DragDataObject folders(archive.c_str(),nullptr,folderTemp.c_str(),indices,folderOutputs,3,roots,3,nullptr,nullptr);
      check(readDrop(folders)==std::vector<std::wstring>{folderTemp}&&!fs::exists(nested),"folder hover does not extract nested contents");
      folders.commitDrop();
      check(readDrop(folders)==std::vector<std::wstring>{folderRoot,empty,zero},"multi-selection exposes inferred folder, empty folder and file");
      check(read(nested)=="nested contents"&&fs::is_directory(empty)&&fs::file_size(zero)==0,"folder extraction preserves nesting, empty folder and zero-byte file");

      const fs::path destination=root/(zip?L"zip-destination":L"7z-destination");
      const std::wstring shellTemp=(root/(zip?L"zip-shell":L"7z-shell")).wstring(),shellOutput=(fs::path(shellTemp)/L"根文件.txt").wstring();fs::create_directories(shellTemp);const wchar_t* shellOutputs[]={shellOutput.c_str()};
      DragDataObject shellFile(archive.c_str(),nullptr,shellTemp.c_str(),&index,shellOutputs,1,shellOutputs,1,nullptr,nullptr);
      shellFile.commitDrop();
      shellCopy(&shellFile,destination);shellCopy(&folders,destination);
      check(read(destination/L"根文件.txt")=="root contents"&&read(destination/L"目录"/L"子目录"/L"文件.txt")=="nested contents","real Shell copy produces matching file and nested folder contents");
      check(fs::is_directory(destination/L"空目录")&&fs::file_size(destination/L"零字节.txt")==0,"real Shell copy keeps empty folders and zero-byte files");
      check(std::distance(fs::directory_iterator(destination),fs::directory_iterator{})==4,"real Shell copy creates only the four selected roots");
      const std::wstring bridgeTemp=(root/(zip?L"zip-winrt":L"7z-winrt")).wstring(),bridgeOutput=(fs::path(bridgeTemp)/L"根文件.txt").wstring();fs::create_directories(bridgeTemp);const wchar_t* bridgeOutputs[]={bridgeOutput.c_str()};
      HSTRING runtimeName=nullptr;const wchar_t* className=L"Windows.ApplicationModel.DataTransfer.DataPackage";
      check(SUCCEEDED(WindowsCreateString(className,static_cast<UINT32>(wcslen(className)),&runtimeName)),"create DataPackage runtime class name");
      Ptr<IInspectable> package;HRESULT activated=RoActivateInstance(runtimeName,package.out());WindowsDeleteString(runtimeName);check(SUCCEEDED(activated),"activate real Windows DataPackage");
      DragDataObject* session=nullptr;
      check(SUCCEEDED(NzCreateArchiveDragData(package.p,nullptr,archive.c_str(),nullptr,&index,bridgeOutputs,1,bridgeOutputs,1,bridgeTemp.c_str(),L"New7z test",&session)),"attach native delayed archive data through IDataObjectProvider");
      Ptr<IDataObjectProvider> provider;check(SUCCEEDED(package->QueryInterface(IID_PPV_ARGS(provider.out()))),"query real DataPackage interop provider");
      Ptr<IDataObject> bridged;check(SUCCEEDED(provider->GetDataObject(bridged.out())),"retrieve native archive object through DataPackage");
      session->commitDrop(); // The test must not depend on the user's physical mouse state.
      NzReleaseArchiveDragData(session);
      check(readDrop(*bridged.p)==std::vector<std::wstring>{bridgeOutput}&&read(bridgeOutput)=="root contents","DataPackage retains native data after source releases its reference");
      FORMATETC performed{static_cast<CLIPFORMAT>(RegisterClipboardFormatW(CFSTR_PERFORMEDDROPEFFECT)),nullptr,DVASPECT_CONTENT,-1,TYMED_HGLOBAL};
      STGMEDIUM effectMedium{};effectMedium.tymed=TYMED_HGLOBAL;effectMedium.hGlobal=GlobalAlloc(GMEM_MOVEABLE,sizeof(DWORD));
      auto effectValue=static_cast<DWORD*>(GlobalLock(effectMedium.hGlobal));*effectValue=DROPEFFECT_MOVE;GlobalUnlock(effectMedium.hGlobal);
      check(SUCCEEDED(file.SetData(&performed,&effectMedium,FALSE)),"retain independent copy of Shell performed-effect metadata");ReleaseStgMedium(&effectMedium);
      check(file.wasTransferred(DROPEFFECT_NONE),"optimized move is recognized even when final effect is zero");
      FORMATETC streamFormat{static_cast<CLIPFORMAT>(RegisterClipboardFormatW(L"New7z.Test.DragContext")),nullptr,DVASPECT_CONTENT,-1,TYMED_ISTREAM};STGMEDIUM streamMedium{};streamMedium.tymed=TYMED_ISTREAM;
      check(SUCCEEDED(CreateStreamOnHGlobal(nullptr,TRUE,&streamMedium.pstm)),"create private drag metadata stream");DWORD marker=12345;streamMedium.pstm->Write(&marker,sizeof(marker),nullptr);
      check(SUCCEEDED(file.SetData(&streamFormat,&streamMedium,FALSE)),"accept Shell metadata carried in IStream");ReleaseStgMedium(&streamMedium);
      STGMEDIUM copiedStream{};check(SUCCEEDED(file.GetData(&streamFormat,&copiedStream)),"retrieve private metadata after caller releases its stream");
      LARGE_INTEGER beginning{};copiedStream.pstm->Seek(beginning,STREAM_SEEK_SET,nullptr);DWORD recovered=0;copiedStream.pstm->Read(&recovered,sizeof(recovered),nullptr);ReleaseStgMedium(&copiedStream);check(recovered==marker,"private IStream metadata survives roundtrip");
      const fs::path dropDestination=root/(zip?L"zip-drop":L"7z-drop");
      const std::wstring dropTemp=(root/(zip?L"zip-drop-source":L"7z-drop-source")).wstring(),dropOutput=(fs::path(dropTemp)/L"根文件.txt").wstring();fs::create_directories(dropTemp);const wchar_t* dropOutputs[]={dropOutput.c_str()};
      DragDataObject dropFile(archive.c_str(),nullptr,dropTemp.c_str(),&index,dropOutputs,1,dropOutputs,1,nullptr,nullptr);
      shellDrop(&dropFile,dropDestination);shellDrop(&folders,dropDestination);
      check(read(dropDestination/L"根文件.txt")=="root contents"&&read(dropDestination/L"目录"/L"子目录"/L"文件.txt")=="nested contents","real Shell Drop produces matching file and nested folder contents");
      check(fs::is_directory(dropDestination/L"空目录")&&fs::file_size(dropDestination/L"零字节.txt")==0,"real Shell Drop preserves empty folders and zero-byte files");

      const std::wstring cancelTemp=(root/(zip?L"zip-cancel":L"7z-cancel")).wstring(),cancelOutput=(fs::path(cancelTemp)/L"根文件.txt").wstring();fs::create_directories(cancelTemp);const wchar_t* cancelOutputs[]={cancelOutput.c_str()};
      DragDataObject cancelled(archive.c_str(),nullptr,cancelTemp.c_str(),&index,cancelOutputs,1,cancelOutputs,1,nullptr,nullptr);
      check(readDrop(cancelled)==std::vector<std::wstring>{cancelTemp},"cancelled drag may be inspected while hovering");
      check(!fs::exists(cancelOutput),"leaving or cancelling without a committed data request creates no files");
      DragDataObject interrupted(archive.c_str(),nullptr,cancelTemp.c_str(),&index,cancelOutputs,1,cancelOutputs,1,[](UInt64,UInt64,void*)->int{return 1;},nullptr);
      interrupted.commitDrop();FORMATETC interruptedFormat{CF_HDROP,nullptr,DVASPECT_CONTENT,-1,TYMED_HGLOBAL};STGMEDIUM interruptedMedium{};
      check(interrupted.GetData(&interruptedFormat,&interruptedMedium)==E_ABORT&&!fs::exists(cancelOutput),"cancellation while preparing the drop creates no output");
      check(interrupted.GetData(&interruptedFormat,&interruptedMedium)==E_ABORT,"repeat requests preserve the cancelled preparation result");

      const std::wstring isolatedTemp=(root/(zip?L"zip-isolated":L"7z-isolated")).wstring();fs::create_directories(isolatedTemp);
      const std::wstring isolatedOutput=(fs::path(isolatedTemp)/L"worker.txt").wstring();const wchar_t* isolatedOutputs[]={isolatedOutput.c_str()};
      IsolatedState worker{fs::path(isolatedOutput)};
      {
        DragDataObject isolated(L"missing-native-archive.7z",nullptr,isolatedTemp.c_str(),&index,isolatedOutputs,1,isolatedOutputs,1,nullptr,nullptr);
        isolated.setIsolatedPrepare(isolatedPrepare,&worker,isolatedRelease);
        check(readDrop(isolated)==std::vector<std::wstring>{isolatedTemp}&&worker.calls==0,"isolated drag hover never invokes the worker");
        isolated.commitDrop();check(readDrop(isolated)==std::vector<std::wstring>{isolatedOutput}&&read(isolatedOutput)=="isolated worker output","isolated drag uses worker callback without opening a native archive");
        check(readDrop(isolated).size()==1&&worker.calls==1,"isolated drag prepares exactly once");
      }
      check(worker.released==1,"isolated callback lifetime ends with the native data object");

      const std::wstring outside=(root/L"outside.txt").wstring();const wchar_t* badOutputs[]={outside.c_str()};
      DragDataObject invalid(archive.c_str(),nullptr,cancelTemp.c_str(),&index,badOutputs,1,badOutputs,1,nullptr,nullptr);
      invalid.commitDrop();
      FORMATETC format{CF_HDROP,nullptr,DVASPECT_CONTENT,-1,TYMED_HGLOBAL};STGMEDIUM medium{};SetLastError(ERROR_SUCCESS);
      check(FAILED(invalid.GetData(&format,&medium))&&!fs::exists(outside),"invalid output fails even when last error is zero");
    }
    std::cout<<"PASS: "<<checks<<" native drag extraction checks\n";fs::remove_all(root);return 0;
  }catch(const std::exception& error){std::cerr<<"FAIL: "<<error.what()<<"\nFixtures: "<<root<<"\n";return 1;}
}
