#include "mod_api.h"
#include "resources.h"
#include "virtual_io.h"
#include "legacy_cache.h"
#include <cwctype>
using namespace ssasr;
namespace {
constexpr HRESULT Unrecoverable=(HRESULT)0x8004A001;
std::wstring gameRoot,logPath;
volatile LONG hostEntered=0;
std::map<std::wstring,Redirect> redirects;
struct Hook{uintptr_t address;void* before;void* after;bool applied=false;DWORD originalProtection=0;bool protectionDirty=false;};
std::vector<Hook> hooks;
struct Mod{std::wstring dir,runtime;HMODULE module=nullptr;SsasrModInitializeFn initialize=nullptr;SsasrModRollbackFn rollback=nullptr;bool started=false;std::wstring id;};
std::vector<Mod>mods;
std::vector<LegacyCache>legacyCaches;
void __cdecl log(const wchar_t*message){if(logPath.empty())return;HANDLE h=CreateFileW(logPath.c_str(),FILE_APPEND_DATA,FILE_SHARE_READ,nullptr,OPEN_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr);if(h==INVALID_HANDLE_VALUE)return;SYSTEMTIME t;GetSystemTime(&t);wchar_t prefix[64];swprintf_s(prefix,L"%04u-%02u-%02u %02u:%02u:%02u UTC  ",t.wYear,t.wMonth,t.wDay,t.wHour,t.wMinute,t.wSecond);std::wstring w=prefix;w+=message;w+=L"\r\n";int n=WideCharToMultiByte(CP_UTF8,0,w.data(),(int)w.size(),nullptr,0,nullptr,nullptr);std::string s(n,0);WideCharToMultiByte(CP_UTF8,0,w.data(),(int)w.size(),s.data(),n,nullptr,nullptr);DWORD k;WriteFile(h,s.data(),n,&k,nullptr);CloseHandle(h);}
std::wstring ansi(const char*s){if(!s)return {};int n=MultiByteToWideChar(CP_ACP,0,s,-1,nullptr,0);if(!n)return {};std::wstring w(n,0);MultiByteToWideChar(CP_ACP,0,s,-1,w.data(),n);w.resize(n-1);return w;}
const Redirect*lookup(const char*path){struct PreserveError{DWORD saved=GetLastError();~PreserveError(){SetLastError(saved);}}error;if(!path||!*path)return nullptr;try{auto p=fold(full(ansi(path)));auto i=redirects.find(p);return i==redirects.end()?nullptr:&i->second;}catch(...){return nullptr;}}
HANDLE WINAPI openFile(LPCSTR p,DWORD access,DWORD sharing,LPSECURITY_ATTRIBUTES sa,DWORD creation,DWORD flags,HANDLE templ){
    if(auto r=lookup(p)){if(r->archive)return virtual_io::open(r->archive,r->logical,access,sharing,sa,creation,flags,templ);if(!r->writable&&(creation!=OPEN_EXISTING||(access&(GENERIC_WRITE|GENERIC_ALL|DELETE|WRITE_DAC|WRITE_OWNER|FILE_WRITE_DATA|FILE_APPEND_DATA|FILE_WRITE_EA|FILE_WRITE_ATTRIBUTES)))){SetLastError(ERROR_ACCESS_DENIED);return INVALID_HANDLE_VALUE;}return CreateFileW(r->physical.c_str(),access,sharing,sa,creation,flags,templ);}return CreateFileA(p,access,sharing,sa,creation,flags,templ);}
DWORD WINAPI attributes(LPCSTR p){if(auto r=lookup(p))return GetFileAttributesW(r->physical.c_str());return GetFileAttributesA(p);}
BOOL WINAPI deleteFile(LPCSTR p){if(auto r=lookup(p)){if(!r->writable){SetLastError(ERROR_ACCESS_DENIED);return FALSE;}return DeleteFileW(r->physical.c_str());}return DeleteFileA(p);}
BOOL WINAPI moveFile(LPCSTR a,LPCSTR b){auto ar=lookup(a),br=lookup(b);if(ar||br){if(!ar||!br||!ar->writable||!br->writable){SetLastError(ERROR_ACCESS_DENIED);return FALSE;}return MoveFileW(ar->physical.c_str(),br->physical.c_str());}return MoveFileA(a,b);}
bool change(Hook&h, bool installing){auto slot=(void*volatile*)h.address;bool exchanged=true;DWORD old=0;
    if(installing||h.applied){if(!VirtualProtect((void*)h.address,sizeof(void*),PAGE_READWRITE,&old))return false;if(installing)h.originalProtection=old;h.protectionDirty=true;
        void*was=InterlockedCompareExchangePointer(slot,installing?h.after:h.before,installing?h.before:h.after);exchanged=was==(installing?h.before:h.after);if(exchanged)h.applied=installing;}
    if(h.protectionDirty){DWORD ignored=0;if(!VirtualProtect((void*)h.address,sizeof(void*),h.originalProtection,&ignored))return false;h.protectionDirty=false;}return exchanged;}
bool undo(){bool ok=true;for(auto i=mods.rbegin();i!=mods.rend();++i)if(i->started){if(FAILED(i->rollback()))ok=false;else i->started=false;}for(auto i=hooks.rbegin();i!=hooks.rend();++i)if((i->applied||i->protectionDirty)&&!change(*i,false))ok=false;return ok;}
std::wstring ini(const std::wstring&p,const wchar_t*k){wchar_t s[2048]{};DWORD n=GetPrivateProfileStringW(L"mod",k,L"",s,2048,p.c_str());require(n&&n<2047,"Missing or oversized mod.ini field");return s;}
std::string narrowAscii(const std::wstring&s){std::string n;for(auto c:s){require(c>0&&c<128,"Expected ASCII manifest field");n+=(char)c;}return n;}
void discover(){auto root=child(gameRoot,L"mods");DWORD a=GetFileAttributesW(root.c_str());if(a==INVALID_FILE_ATTRIBUTES)return;require((a&FILE_ATTRIBUTE_DIRECTORY)&&!(a&FILE_ATTRIBUTE_REPARSE_POINT),"mods must be a real directory");logPath=root+L"\\SsasrMods.log";
    WIN32_FIND_DATAW data;HANDLE f=FindFirstFileW((root+L"\\*").c_str(),&data);if(f==INVALID_HANDLE_VALUE)return;std::vector<std::wstring>dirs;do{std::wstring name=data.cFileName;if(name==L"."||name==L".."||!(data.dwFileAttributes&FILE_ATTRIBUTE_DIRECTORY))continue;require(!(data.dwFileAttributes&FILE_ATTRIBUTE_REPARSE_POINT),"Mod junctions are not supported");dirs.push_back(root+L"\\"+name);}while(FindNextFileW(f,&data));FindClose(f);std::sort(dirs.begin(),dirs.end());std::set<std::wstring>ids;
    for(auto&dir:dirs){auto config=child(dir,L"mod.ini");if(GetFileAttributesW(config.c_str())==INVALID_FILE_ATTRIBUTES)continue;no_reparse(root,config);require(ini(config,L"schema")==L"1","Unsupported mod.ini version");auto id=ini(config,L"id");require(ids.insert(id).second,"Duplicate mod ID");auto plan=child(dir,ini(config,L"plan"));no_reparse(root,plan);hash_matches(plan,narrowAscii(ini(config,L"plan_sha256")));auto dataPlan=JsonParser(text_file(plan)).parse();auto paths=prepare(gameRoot,dir,dataPlan);for(auto&g:dataPlan.at("archive_groups").list())if(g.at("format").str()=="xpac")legacyCaches.push_back({dir,g.at("expected_composed_sha256").str(),g.at("expected_composed_size").u32()});for(auto&r:paths){auto found=redirects.find(fold(r.logical));if(found==redirects.end())redirects.emplace(fold(r.logical),r);else {require(found->second.archive&&r.archive,"Two mods replace the same resource; combination needs a merge plan");found->second.archive=merge_virtual_xpac(found->second.archive,r.archive);}}auto runtime=child(dir,ini(config,L"runtime"));no_reparse(root,runtime);hash_matches(runtime,narrowAscii(ini(config,L"runtime_sha256")));Mod m;m.dir=dir;m.runtime=runtime;m.id=id;mods.push_back(m);}
    std::stable_sort(mods.begin(),mods.end(),[](const Mod&a,const Mod&b){return (a.id==L"metal-sonic")>(b.id==L"metal-sonic");});
}
void installRedirects(HMODULE game){HMODULE kernel=GetModuleHandleW(L"kernel32.dll");require(kernel!=nullptr,"Kernel module missing");struct Target{uintptr_t va;const char*name;void*replacement;};Target targets[]={{0x67D04C,"CreateFileA",(void*)&openFile},{0x67D0E0,"GetFileAttributesA",(void*)&attributes},{0x67D0E4,"DeleteFileA",(void*)&deleteFile},{0x67D0E8,"MoveFileA",(void*)&moveFile},
        {0x67D060,"ReadFile",(void*)&virtual_io::readFile},{0x67D198,"ReadFileEx",(void*)&virtual_io::readFileEx},
        {0x67D070,"SetFilePointer",(void*)&virtual_io::seekFile},{0x67D064,"GetFileSize",(void*)&virtual_io::getSize},
        {0x67D09C,"GetFileSizeEx",(void*)&virtual_io::getSizeEx},{0x67D03C,"CloseHandle",(void*)&virtual_io::close}};
    for(auto&t:targets){auto expected=(void*)GetProcAddress(kernel,t.name);require(expected!=nullptr,"System function unavailable");uintptr_t address=(uintptr_t)game+t.va-0x400000;require(*(void**)address==expected,"File API already hooked; refusing conflicting loader");hooks.push_back({address,expected,t.replacement,false});}
    for(auto&h:hooks)require(change(h,true),"Resource redirection publication failed");
}
}
extern "C" HRESULT __cdecl SsasrModsHostInitialize(const SsasrBootstrapV1*b){
    if(!b||b->size!=sizeof(*b)||b->version!=1||!b->initialization_is_active||!b->initialization_is_active()||b->initializing_thread_id!=GetCurrentThreadId()||b->native_gate_rva!=0x248F45||b->flags!=7)return E_INVALIDARG;
    if(InterlockedCompareExchange(&hostEntered,1,0))return HRESULT_FROM_WIN32(ERROR_ALREADY_INITIALIZED);
    bool publishing=false;
    try{require(mods.empty()&&hooks.empty(),"Host initialization was invoked twice");require((uintptr_t)b->game_image==0x400000,"Unsupported game image base");gameRoot=full(b->game_directory);discover();if(mods.empty())return S_FALSE;
        // Load/resolve all components before any hook publication. Failure here
        // cannot expose Metal assets to a vanilla runtime.
        for(auto&m:mods){m.module=LoadLibraryExW(m.runtime.c_str(),nullptr,LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_SYSTEM32);require(m.module!=nullptr,"Mod runtime failed to load");m.initialize=(SsasrModInitializeFn)GetProcAddress(m.module,"SsasrModInitialize");m.rollback=(SsasrModRollbackFn)GetProcAddress(m.module,"SsasrModRollback");require(m.initialize&&m.rollback,"Mod runtime ABI is incomplete");}
        require(b->initialization_is_active()&&GetCurrentThreadId()==b->initializing_thread_id,"Early initialization gate expired");publishing=true;installRedirects(b->game_image);
        SsasrHostApiV1 api{sizeof(api),1,b->game_image,gameRoot.c_str(),SSASR_HOST_NATIVE_CRT_GATE,b->initializing_thread_id,b->native_gate_rva,b->initialization_is_active,&log};
        for(auto&m:mods){m.started=true;HRESULT hr=m.initialize(&api,m.dir.c_str());if(FAILED(hr)){wchar_t detail[128];swprintf_s(detail,L"Runtime initialization returned HRESULT 0x%08lX",(unsigned long)hr);log(detail);}if(hr==Unrecoverable)return Unrecoverable;require(SUCCEEDED(hr),"Mod initialization rejected this session");}
        for(auto&c:legacyCaches){auto result=remove_legacy_cache(c);if(result==CacheCleanup::Removed)log((L"Removed obsolete generated archive cache: "+wide(c.hash)+L" ("+std::to_wstring(c.bytes)+L" bytes)").c_str());else if(result==CacheCleanup::Kept)log((L"Retained legacy cache because it is changed, locked, or inaccessible: "+wide(c.hash)).c_str());}
        log(L"READY: mod resources and runtime initialized before game startup. Virtual archives enabled; no full XPAC disk copies.");return S_OK;
    }catch(const std::exception&e){if(publishing&&!undo()){log(L"FATAL: rollback failed; game startup must stop.");return Unrecoverable;}redirects.clear();auto message=wide(e.what());log(message.c_str());
#ifndef SSASR_FIXTURE
        MessageBoxW(nullptr,(L"The mods could not be enabled. The game will continue without them.\n\n"+message+L"\n\nSee mods\\SsasrMods.log.").c_str(),L"All-Stars mods",MB_OK|MB_ICONWARNING);
#endif
        return E_FAIL;}catch(...){if(publishing&&!undo())return Unrecoverable;return E_FAIL;}
}
extern "C" __declspec(dllexport) const ssasr::virtual_io::Counters* ChaoGarageVirtualCounters=&ssasr::virtual_io::counters;
