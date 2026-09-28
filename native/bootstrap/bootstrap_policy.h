#pragma once
#include "bootstrap_api.h"
#include <stddef.h>

namespace ssasr {
constexpr uintptr_t kGameBase = 0x400000;
constexpr DWORD kImageSize = 0xB4E000;
constexpr DWORD kGetStartupInfoIatRva = 0x27D128;
constexpr DWORD kStartupCallRva = 0x248F45;
constexpr DWORD kStartupReturnRva = 0x248F4B;
extern const uint8_t kGameSha256[32];

bool SupportedMetadata(const BYTE* image, size_t readable_size);
bool SupportedStartupCaller(const BYTE* image, size_t readable_size, uintptr_t caller);
bool SupportedStartupSpan(uintptr_t image_base,uintptr_t caller,const BYTE* span,size_t size);
bool EqualSha256(const uint8_t actual[32]);

enum class InitResult : DWORD {
    NeverCalled, NoMods, UnknownExecutable, InvalidLocation, HostUnavailable,
    HostRejected, Loaded, UnrecoverablePartialPublication
};

/* Dependency injection keeps the exact production decision order executable
   in native fixtures without loading the game or altering an installed file. */
struct InitEnvironment {
    virtual bool ResolveLocation() = 0;
    virtual bool HasCandidateMod() = 0;
    virtual bool HashExecutable(uint8_t hash[32]) = 0;
    virtual bool LoadHost() = 0;
    virtual HRESULT InitializeHost(const uint8_t hash[32]) = 0;
    virtual ~InitEnvironment() = default;
};
InitResult Initialize(InitEnvironment& environment);

struct IatOperations {
    virtual bool Writable(void* address, DWORD& old_protection) = 0;
    virtual void* CompareExchange(void* volatile* address, void* replacement, void* expected) = 0;
    virtual bool RestoreProtection(void* address, DWORD old_protection) = 0;
    virtual ~IatOperations() = default;
};
enum class IatResult { Replaced, ProtectedFailure, ForeignPointer, RestoreFailure };
IatResult ReplaceIat(IatOperations& operations, void* volatile* slot, void* expected, void* replacement);
}
