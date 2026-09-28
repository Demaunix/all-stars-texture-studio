#define WIN32_LEAN_AND_MEAN
#define DIRECTINPUT_VERSION 0x0800
#include <windows.h>
#include <dinput.h>
#include <bcrypt.h>
#include <intrin.h>
#include <string>
#include "bootstrap_policy.h"

namespace {
HMODULE g_self = nullptr;
BYTE* g_game = nullptr;
void* g_startup_original = nullptr;
volatile LONG g_phase = 0; // 0 waiting,1 initializing,2 complete,3 disabled
DWORD g_initializing_thread = 0;
ssasr::InitResult g_result = ssasr::InitResult::NeverCalled;
INIT_ONCE g_system_once = INIT_ONCE_STATIC_INIT;
HMODULE g_system = nullptr;
FARPROC g_system_exports[6]{};

__declspec(noreturn) void Unrecoverable() {
    OutputDebugStringW(L"SSASR loader: host could not restore partial changes; refusing to start the game.\n");
    TerminateProcess(GetCurrentProcess(),static_cast<UINT>(SSASR_HOST_ROLLBACK_FAILED));
    __fastfail(7);
}

class WindowsIat final : public ssasr::IatOperations {
    bool Writable(void* address,DWORD& old) override {
        return VirtualProtect(address,sizeof(void*),PAGE_READWRITE,&old)!=FALSE;
    }
    void* CompareExchange(void* volatile* address,void* replacement,void* expected) override {
        return InterlockedCompareExchangePointer(address,replacement,expected);
    }
    bool RestoreProtection(void* address,DWORD old) override {
        DWORD ignored=0;return VirtualProtect(address,sizeof(void*),old,&ignored)!=FALSE;
    }
};

BOOL WINAPI InitializationActive() {
    return InterlockedCompareExchange(&g_phase,0,0)==1 && GetCurrentThreadId()==g_initializing_thread;
}

bool ModulePath(HMODULE module,std::wstring& path) {
    wchar_t buffer[32768];
    DWORD n=GetModuleFileNameW(module,buffer,32768);
    if (!n || n>=32768) return false;
    path.assign(buffer,n);return true;
}
bool ParentPath(const std::wstring& path,std::wstring& parent) {
    auto index=path.find_last_of(L"\\/");
    if (index==std::wstring::npos || index==0) return false;
    parent=path.substr(0,index);return true;
}

class WindowsEnvironment final : public ssasr::InitEnvironment {
    std::wstring executable_,directory_;
    HMODULE host_=nullptr;
    SsasrModsHostInitializeFn initialize_=nullptr;
public:
    bool ResolveLocation() override {
        std::wstring proxy,parent;
        return ModulePath(nullptr,executable_) && ParentPath(executable_,directory_) &&
            ModulePath(g_self,proxy) && ParentPath(proxy,parent) &&
            CompareStringOrdinal(directory_.c_str(),-1,parent.c_str(),-1,TRUE)==CSTR_EQUAL;
    }
    bool HasCandidateMod() override {
        const std::wstring mods=directory_+L"\\mods";
        WIN32_FIND_DATAW data{};
        HANDLE search=FindFirstFileW((mods+L"\\*").c_str(),&data);
        if (search==INVALID_HANDLE_VALUE) return false;
        bool found=false;
        do {
            if (!(data.dwFileAttributes&FILE_ATTRIBUTE_DIRECTORY) ||
                wcscmp(data.cFileName,L".")==0 || wcscmp(data.cFileName,L"..")==0) continue;
            const std::wstring manifest=mods+L"\\"+data.cFileName+L"\\mod.ini";
            DWORD attributes=GetFileAttributesW(manifest.c_str());
            if (attributes!=INVALID_FILE_ATTRIBUTES && !(attributes&FILE_ATTRIBUTE_DIRECTORY)) {found=true;break;}
        } while (FindNextFileW(search,&data));
        FindClose(search);return found;
    }
    bool HashExecutable(uint8_t hash[32]) override {
        HANDLE file=CreateFileW(executable_.c_str(),GENERIC_READ,FILE_SHARE_READ|FILE_SHARE_DELETE,
            nullptr,OPEN_EXISTING,FILE_FLAG_SEQUENTIAL_SCAN,nullptr);
        if (file==INVALID_HANDLE_VALUE) return false;
        BCRYPT_ALG_HANDLE algorithm=nullptr;BCRYPT_HASH_HANDLE digest=nullptr;
        bool okay=BCryptOpenAlgorithmProvider(&algorithm,BCRYPT_SHA256_ALGORITHM,nullptr,0)>=0;
        if (okay) okay=BCryptCreateHash(algorithm,&digest,nullptr,0,nullptr,0,0)>=0;
        BYTE data[16384];DWORD count=0;
        while (okay) {
            if (!ReadFile(file,data,sizeof(data),&count,nullptr)) {okay=false;break;}
            if (!count) break;
            if (BCryptHashData(digest,data,count,0)<0) {okay=false;break;}
        }
        if (okay) okay=BCryptFinishHash(digest,hash,32,0)>=0;
        if (digest) BCryptDestroyHash(digest);
        if (algorithm) BCryptCloseAlgorithmProvider(algorithm,0);
        CloseHandle(file);return okay;
    }
    bool LoadHost() override {
        const std::wstring path=directory_+L"\\ChaoGarage.dll";
        host_=LoadLibraryExW(path.c_str(),nullptr,LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_SYSTEM32);
        if (!host_) return false;
        initialize_=reinterpret_cast<SsasrModsHostInitializeFn>(GetProcAddress(host_,"SsasrModsHostInitialize"));
        /* Even a rejected host stays resident: unloading an extension that
           might have initialized callback state would be less predictable. */
        return initialize_!=nullptr;
    }
    HRESULT InitializeHost(const uint8_t hash[32]) override {
        SsasrBootstrapV1 context{};
        context.size=sizeof(context);context.version=SSASR_BOOTSTRAP_ABI_V1;
        context.game_image=reinterpret_cast<HMODULE>(g_game);context.bootstrap_image=g_self;
        context.game_directory=directory_.c_str();context.executable_sha256=hash;
        context.flags=SSASR_BOOTSTRAP_DISK_AUTHENTICATED|SSASR_BOOTSTRAP_NATIVE_CRT_GATE|
            SSASR_BOOTSTRAP_GAME_INITIALIZATION_NOT_STARTED;
        context.initializing_thread_id=g_initializing_thread;context.native_gate_rva=ssasr::kStartupCallRva;
        context.initialization_is_active=&InitializationActive;
        try { return initialize_(&context); }
        catch (...) { Unrecoverable(); }
    }
};

bool ReadMetadata(BYTE* image) {
    __try { return reinterpret_cast<uintptr_t>(image)==ssasr::kGameBase && ssasr::SupportedMetadata(image,0x1000); }
    __except(EXCEPTION_EXECUTE_HANDLER) { return false; }
}
bool ReadStartupCaller(uintptr_t caller) {
    __try { return ssasr::SupportedStartupCaller(g_game,ssasr::kImageSize,caller); }
    __except(EXCEPTION_EXECUTE_HANDLER) { return false; }
}

void WINAPI StartupInformation(LPSTARTUPINFOA info) {
    const uintptr_t caller=reinterpret_cast<uintptr_t>(_ReturnAddress());
    reinterpret_cast<decltype(&GetStartupInfoA)>(g_startup_original)(info);
    const DWORD native_error=GetLastError();
    if (ReadStartupCaller(caller) && InterlockedCompareExchange(&g_phase,1,0)==0) {
        g_initializing_thread=GetCurrentThreadId();
        WindowsIat operations;
        auto* slot=reinterpret_cast<void* volatile*>(g_game+ssasr::kGetStartupInfoIatRva);
        const auto result=ssasr::ReplaceIat(operations,slot,reinterpret_cast<void*>(&StartupInformation),g_startup_original);
        if (result==ssasr::IatResult::Replaced) {
            try {
                WindowsEnvironment environment;
                g_result=ssasr::Initialize(environment);
                if (g_result==ssasr::InitResult::UnrecoverablePartialPublication) Unrecoverable();
            } catch (...) {
                /* Host ABI requires errors as HRESULT and complete rollback.
                   This catches C++ allocation failures in bootstrap setup. */
                g_result=ssasr::InitResult::HostRejected;
                OutputDebugStringW(L"SSASR loader: initialization failed; no late retry.\n");
            }
            InterlockedExchange(&g_phase,2);
        } else {
            InterlockedExchange(&g_phase,3);
            OutputDebugStringW(L"SSASR loader: startup gate restoration failed; mods disabled.\n");
        }
    }
    SetLastError(native_error);
}

/* No allocation, paths, hashing, LoadLibrary, worker thread, hook builder,
   host initialization, file IO or game function calls occur here. */
void Attach(HMODULE module) {
    g_self=module;
    g_game=reinterpret_cast<BYTE*>(GetModuleHandleW(nullptr));
    if (!ReadMetadata(g_game)) {g_phase=3;return;}
    WindowsIat operations;
    g_startup_original=reinterpret_cast<void*>(&GetStartupInfoA);
    auto* slot=reinterpret_cast<void* volatile*>(g_game+ssasr::kGetStartupInfoIatRva);
    if (ssasr::ReplaceIat(operations,slot,g_startup_original,reinterpret_cast<void*>(&StartupInformation))
        !=ssasr::IatResult::Replaced) g_phase=3;
}

BOOL CALLBACK LoadSystemDinput(PINIT_ONCE,PVOID,PVOID*) {
    wchar_t path[MAX_PATH+32];
    const UINT length=GetSystemDirectoryW(path,MAX_PATH);
    if (!length || length>=MAX_PATH) return TRUE;
    wcscpy_s(path+length,_countof(path)-length,L"\\dinput8.dll");
    HMODULE module=LoadLibraryExW(path,nullptr,LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (!module || module==g_self) return TRUE;
    static const char* names[]={"DirectInput8Create","DllCanUnloadNow","DllGetClassObject",
        "DllRegisterServer","DllUnregisterServer","GetdfDIJoystick"};
    FARPROC addresses[6]{};
    for (int i=0;i<6;++i) {
        addresses[i]=GetProcAddress(module,names[i]);
        if (!addresses[i]) return TRUE;
    }
    for (int i=0;i<6;++i) g_system_exports[i]=addresses[i];
    g_system=module;return TRUE;
}
FARPROC SystemExport(int index) {
    const DWORD native_error=GetLastError();
    InitOnceExecuteOnce(&g_system_once,LoadSystemDinput,nullptr,nullptr);
    SetLastError(native_error);
    return g_system?g_system_exports[index]:nullptr;
}
}

extern "C" HRESULT WINAPI ProxyDirectInput8Create(HINSTANCE instance,DWORD version,REFIID iid,LPVOID* output,LPUNKNOWN outer) {
    auto f=reinterpret_cast<decltype(&DirectInput8Create)>(SystemExport(0));
    if (!f) {if (output)*output=nullptr;return E_FAIL;}
    return f(instance,version,iid,output,outer);
}
extern "C" HRESULT WINAPI ProxyDllCanUnloadNow() {
    auto f=reinterpret_cast<HRESULT(WINAPI*)()>(SystemExport(1));return f?f():S_FALSE;
}
extern "C" HRESULT WINAPI ProxyDllGetClassObject(REFCLSID id,REFIID iid,LPVOID* output) {
    auto f=reinterpret_cast<HRESULT(WINAPI*)(REFCLSID,REFIID,LPVOID*)>(SystemExport(2));
    if (!f) {if(output)*output=nullptr;return CLASS_E_CLASSNOTAVAILABLE;}return f(id,iid,output);
}
extern "C" HRESULT WINAPI ProxyDllRegisterServer() {
    auto f=reinterpret_cast<HRESULT(WINAPI*)()>(SystemExport(3));return f?f():E_FAIL;
}
extern "C" HRESULT WINAPI ProxyDllUnregisterServer() {
    auto f=reinterpret_cast<HRESULT(WINAPI*)()>(SystemExport(4));return f?f():E_FAIL;
}
extern "C" const DIDATAFORMAT* WINAPI ProxyGetdfDIJoystick() {
    auto f=reinterpret_cast<const DIDATAFORMAT*(WINAPI*)()>(SystemExport(5));return f?f():nullptr;
}
BOOL WINAPI DllMain(HINSTANCE instance,DWORD reason,LPVOID) {
    if (reason==DLL_PROCESS_ATTACH) Attach(instance);
    return TRUE;
}
