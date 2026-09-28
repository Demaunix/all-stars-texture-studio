#pragma once
#include "files.h"
namespace ssasr {
struct LegacyCache {std::wstring mod;std::string hash;uint64_t bytes=0;};
enum class CacheCleanup {Missing,Removed,Kept};
inline CacheCleanup remove_legacy_cache(const LegacyCache&c) noexcept {
    // Delete only the previous host's exact, authenticated generated archive.
    // Shader caches, unexpected contents, and files in use are retained.
    try {
        require(c.hash.size()==64&&c.hash.find_first_not_of("0123456789abcdef")==std::string::npos,"Invalid cache signature");
        auto dir=child(c.mod,L".cache\\"+wide(c.hash));auto file=child(dir,L"data.bin");no_reparse(c.mod,file);
        DWORD attr=GetFileAttributesW(file.c_str());
        if(attr==INVALID_FILE_ATTRIBUTES){DWORD e=GetLastError();return e==ERROR_FILE_NOT_FOUND||e==ERROR_PATH_NOT_FOUND?CacheCleanup::Missing:CacheCleanup::Kept;}
        if(attr&(FILE_ATTRIBUTE_DIRECTORY|FILE_ATTRIBUTE_REPARSE_POINT))return CacheCleanup::Kept;
        {
            // Deny concurrent writers/deleters and delete the SAME handle that
            // was hashed, avoiding a pathname substitution after verification.
            Handle f(CreateFileW(file.c_str(),GENERIC_READ|DELETE,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr));
            if(f.h==INVALID_HANDLE_VALUE||size(f.h)!=c.bytes||hash_range(f.h,0,c.bytes)!=c.hash)return CacheCleanup::Kept;
            FILE_DISPOSITION_INFO disposition{TRUE};
            if(!SetFileInformationByHandle(f.h,FileDispositionInfo,&disposition,sizeof(disposition)))return CacheCleanup::Kept;
        }
        RemoveDirectoryW(dir.c_str()); // succeeds only if this exact directory is empty
        return CacheCleanup::Removed;
    }catch(...){return CacheCleanup::Kept;}
}
}
