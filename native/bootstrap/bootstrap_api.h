#pragma once
#include <windows.h>
#include <stdint.h>

/* Called synchronously outside DllMain, before the game's own CRT setup.
   No native game function may be called yet. Steam/system threads may exist.
   All strings/bytes remain valid for the duration of this call only. */
#define SSASR_BOOTSTRAP_ABI_V1 1u
#define SSASR_BOOTSTRAP_DISK_AUTHENTICATED 1u
#define SSASR_BOOTSTRAP_NATIVE_CRT_GATE 2u
#define SSASR_BOOTSTRAP_GAME_INITIALIZATION_NOT_STARTED 4u
#define SSASR_HOST_ROLLBACK_FAILED ((HRESULT)0x8004A001L)

struct SsasrBootstrapV1 {
    uint32_t size;
    uint32_t version;
    HMODULE game_image;
    HMODULE bootstrap_image;
    const wchar_t* game_directory;
    const uint8_t* executable_sha256; /* exactly 32 bytes */
    uint32_t flags;
    uint32_t initializing_thread_id;
    uint32_t native_gate_rva;
    BOOL (WINAPI* initialization_is_active)();
};

/* Host must validate ABI and initialization_is_active(). On failure it must
   undo all of its own publication. The proxy retains the host DLL until exit.
   Export this exact undecorated name from the x86 host. */
typedef HRESULT (__cdecl* SsasrModsHostInitializeFn)(const SsasrBootstrapV1*);
