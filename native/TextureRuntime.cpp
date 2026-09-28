#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>
struct HostApi {
    uint32_t size,version;
    HMODULE game_image;
    const wchar_t* game_directory;
    uint32_t flags,initializing_thread_id,native_gate_rva;
    BOOL (WINAPI* initialization_is_active)();
    void (__cdecl* log)(const wchar_t*);
};
// Texture resources are supplied by the archive host. This module changes no
// gameplay, executable bytes, shader programs, saves, or settings.
extern "C" HRESULT __cdecl SsasrModInitialize(const HostApi* api,const wchar_t*) {
    if(!api||api->size!=sizeof(HostApi)||api->version!=1||api->flags!=1||
       api->initializing_thread_id!=GetCurrentThreadId()||!api->initialization_is_active||
       !api->initialization_is_active())return E_INVALIDARG;
    if(api->log)api->log(L"Texture Studio: texture replacements ready.");
    return S_OK;
}
extern "C" HRESULT __cdecl SsasrModRollback(){return S_OK;}
