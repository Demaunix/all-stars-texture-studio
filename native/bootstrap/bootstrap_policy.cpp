#include "bootstrap_policy.h"
#include <string.h>

namespace ssasr {
const uint8_t kGameSha256[32] = {
    0xc1,0x10,0xb4,0xd3,0xb9,0x72,0x93,0x17,0xa9,0x3e,0x95,0x6f,0x1a,0x49,0xbe,0x98,
    0x9c,0x7a,0xd3,0xc2,0xaa,0xba,0x7c,0x54,0xc4,0x13,0x19,0xc1,0x04,0x38,0x1b,0x03
};
bool SupportedMetadata(const BYTE* image, size_t size) {
    if (!image || size < sizeof(IMAGE_DOS_HEADER)) return false;
    const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(image);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE || dos->e_lfanew < 0x40 || dos->e_lfanew > 0x400) return false;
    const size_t offset = static_cast<size_t>(dos->e_lfanew);
    if (offset + sizeof(IMAGE_NT_HEADERS32) > size) return false;
    const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS32*>(image + offset);
    return nt->Signature == IMAGE_NT_SIGNATURE && nt->FileHeader.Machine == IMAGE_FILE_MACHINE_I386 &&
        nt->FileHeader.TimeDateStamp == 0x4B700995 && nt->FileHeader.NumberOfSections == 6 &&
        nt->FileHeader.SizeOfOptionalHeader == sizeof(IMAGE_OPTIONAL_HEADER32) &&
        nt->OptionalHeader.Magic == IMAGE_NT_OPTIONAL_HDR32_MAGIC &&
        nt->OptionalHeader.ImageBase == kGameBase && nt->OptionalHeader.SizeOfImage == kImageSize &&
        nt->OptionalHeader.AddressOfEntryPoint == 0xAF82ED &&
        nt->OptionalHeader.NumberOfRvaAndSizes == IMAGE_NUMBEROF_DIRECTORY_ENTRIES &&
        nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_TLS].VirtualAddress == 0 &&
        nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT].VirtualAddress == 0x2E71C4 &&
        nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT].Size == 0x118 &&
        nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IAT].VirtualAddress == 0x27D000 &&
        nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IAT].Size == 0x2CC;
}
bool SupportedStartupCaller(const BYTE* image, size_t size, uintptr_t caller) {
    if (reinterpret_cast<uintptr_t>(image) != kGameBase || caller != kGameBase+kStartupReturnRva ||
        size < kStartupReturnRva+7) return false;
    return SupportedStartupSpan(reinterpret_cast<uintptr_t>(image),caller,image+0x248F31,size-0x248F31);
}
bool SupportedStartupSpan(uintptr_t image_base,uintptr_t caller,const BYTE* span,size_t size) {
    if (image_base!=kGameBase || caller!=kGameBase+kStartupReturnRva || !span) return false;
    /* Full contiguous native call/prologue and continuation, after Steam has
       decrypted the game .text. Never accept the later CRT IO helper caller. */
    static const BYTE before[] = {
        0x6a,0x60,0x68,0xc8,0x6b,0x6e,0x00,0xe8,0x1f,0x0c,0x00,0x00,
        0x83,0x65,0xfc,0x00,0x8d,0x45,0x90,0x50,0xff,0x15,0x28,0xd1,0x67,0x00,
        0xc7,0x45,0xfc,0xfe,0xff,0xff,0xff
    };
    return size>=sizeof(before) && memcmp(span,before,sizeof(before)) == 0;
}
bool EqualSha256(const uint8_t hash[32]) {
    unsigned difference = 0;
    for (size_t i=0;i<32;++i) difference |= hash[i]^kGameSha256[i];
    return difference == 0;
}
InitResult Initialize(InitEnvironment& environment) {
    if (!environment.ResolveLocation()) return InitResult::InvalidLocation;
    if (!environment.HasCandidateMod()) return InitResult::NoMods;
    uint8_t hash[32]{};
    if (!environment.HashExecutable(hash) || !EqualSha256(hash)) return InitResult::UnknownExecutable;
    if (!environment.LoadHost()) return InitResult::HostUnavailable;
    const HRESULT result=environment.InitializeHost(hash);
    if (result==SSASR_HOST_ROLLBACK_FAILED) return InitResult::UnrecoverablePartialPublication;
    if (result==S_FALSE) return InitResult::NoMods;
    return SUCCEEDED(result) ? InitResult::Loaded : InitResult::HostRejected;
}
IatResult ReplaceIat(IatOperations& operations, void* volatile* slot, void* expected, void* replacement) {
    DWORD protection = 0;
    if (!operations.Writable(const_cast<void*>(reinterpret_cast<const volatile void*>(slot)),protection))
        return IatResult::ProtectedFailure;
    void* previous = operations.CompareExchange(slot,replacement,expected);
    if (!operations.RestoreProtection(const_cast<void*>(reinterpret_cast<const volatile void*>(slot)),protection)) {
        /* While still writable, undo only our exact write. Never overwrite a
           foreign writer. A protection failure disables host initialization. */
        if (previous == expected) operations.CompareExchange(slot,expected,replacement);
        operations.RestoreProtection(const_cast<void*>(reinterpret_cast<const volatile void*>(slot)),protection);
        return IatResult::RestoreFailure;
    }
    return previous == expected ? IatResult::Replaced : IatResult::ForeignPointer;
}
}
