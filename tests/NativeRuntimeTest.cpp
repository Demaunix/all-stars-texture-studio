#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>
#include <stdio.h>
struct HostApi {uint32_t size,version;HMODULE game;const wchar_t* directory;uint32_t flags,thread,gate;BOOL(WINAPI* active)();void(__cdecl* log)(const wchar_t*);};
BOOL WINAPI Active(){return TRUE;}
BOOL WINAPI Inactive(){return FALSE;}
int wmain(int argc,wchar_t**argv){
    if(argc!=2)return 2;
    HMODULE module=LoadLibraryW(argv[1]);if(!module)return 3;
    auto init=(HRESULT(__cdecl*)(const HostApi*,const wchar_t*))GetProcAddress(module,"SsasrModInitialize");
    auto rollback=(HRESULT(__cdecl*)())GetProcAddress(module,"SsasrModRollback");
    if(!init||!rollback)return 4;
    HostApi api{sizeof(HostApi),1,nullptr,L"fixture",1,GetCurrentThreadId(),0,&Active,nullptr};
    if(init(&api,L"fixture")!=S_OK||rollback()!=S_OK)return 5;
    if(SUCCEEDED(init(nullptr,L"fixture")))return 6;
    api.active=&Inactive;if(SUCCEEDED(init(&api,L"fixture")))return 7;
    api.active=&Active;api.version=2;if(SUCCEEDED(init(&api,L"fixture")))return 8;
    api.version=1;api.thread=0;if(SUCCEEDED(init(&api,L"fixture")))return 9;
    FreeLibrary(module);puts("PASS runtime exports, valid initialization, rollback and invalid host rejection.");return 0;
}
