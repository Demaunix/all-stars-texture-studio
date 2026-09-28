#pragma once
#include "virtual_xpac.h"
#include <atomic>
#include <new>

// Only the admitted game's IAT uses these wrappers. Windows calls made from
// this DLL retain their original imports, including the real ReadFileEx.
namespace ssasr::virtual_io {
struct Counters {uint32_t magic=0x31504743,version=1;volatile LONG opens=0,closes=0,syncReads=0,asyncReads=0,nativeChunks=0,memoryCompletions=0,errors=0,pending=0;};
inline Counters counters;
struct File {std::shared_ptr<VirtualArchive>archive;uint64_t cursor=0;bool overlapped=false,canRead=false;std::atomic<bool>closed{false};std::mutex mutex;};
inline std::mutex filesMutex;
inline std::map<HANDLE,std::shared_ptr<File>>files;
struct PreserveError {DWORD saved=GetLastError();~PreserveError(){SetLastError(saved);}};
inline std::shared_ptr<File>lookup(HANDLE h){PreserveError error;std::lock_guard<std::mutex>lock(filesMutex);auto i=files.find(h);return i==files.end()?nullptr:i->second;}
inline BOOL fail(DWORD error){InterlockedIncrement(&counters.errors);SetLastError(error);return FALSE;}
inline HANDLE open(const std::shared_ptr<VirtualArchive>&a,const std::wstring&base,DWORD access,DWORD sharing,LPSECURITY_ATTRIBUTES sa,DWORD creation,DWORD flags,HANDLE templ){
    const DWORD writes=GENERIC_WRITE|GENERIC_ALL|DELETE|WRITE_DAC|WRITE_OWNER|FILE_WRITE_DATA|FILE_APPEND_DATA|FILE_WRITE_EA|FILE_WRITE_ATTRIBUTES;
    if(creation!=OPEN_EXISTING||(access&writes)||(flags&FILE_FLAG_DELETE_ON_CLOSE)){fail(ERROR_ACCESS_DENIED);return INVALID_HANDLE_VALUE;}
    // The public handle still identifies a real read-only disk file. Its data
    // and cursor are supplied by the wrappers, not by mutating that file.
    HANDLE h=CreateFileW(base.c_str(),access,sharing,sa,creation,flags,templ);if(h==INVALID_HANDLE_VALUE)return h;
    try{auto f=std::make_shared<File>();f->archive=a;f->overlapped=(flags&FILE_FLAG_OVERLAPPED)!=0;f->canRead=(access&(GENERIC_READ|FILE_READ_DATA))!=0;std::lock_guard<std::mutex>lock(filesMutex);require(files.emplace(h,f).second,"Virtual handle collision");}
    catch(...){CloseHandle(h);fail(ERROR_NOT_ENOUGH_MEMORY);return INVALID_HANDLE_VALUE;}
    InterlockedIncrement(&counters.opens);return h;
}
inline BOOL WINAPI close(HANDLE h){
    DWORD incoming=GetLastError();std::unique_lock<std::mutex>lock(filesMutex);auto i=files.find(h);if(i==files.end()){lock.unlock();SetLastError(incoming);return CloseHandle(h);}
    BOOL ok=CloseHandle(h);DWORD error=GetLastError();if(ok){i->second->closed=true;files.erase(i);InterlockedIncrement(&counters.closes);}SetLastError(error);return ok;
}
inline DWORD WINAPI getSize(HANDLE h,LPDWORD high){auto f=lookup(h);if(!f)return GetFileSize(h,high);if(high)*high=(DWORD)(f->archive->bytes>>32);SetLastError(ERROR_SUCCESS);return (DWORD)f->archive->bytes;}
inline BOOL WINAPI getSizeEx(HANDLE h,PLARGE_INTEGER size){auto f=lookup(h);if(!f)return GetFileSizeEx(h,size);if(!size)return fail(ERROR_INVALID_PARAMETER);size->QuadPart=f->archive->bytes;return TRUE;}
inline DWORD WINAPI seekFile(HANDLE h,LONG low,PLONG high,DWORD method){
    auto f=lookup(h);if(!f)return SetFilePointer(h,low,high,method);
    std::lock_guard<std::mutex>lock(f->mutex);
    int64_t distance=high?(int64_t(*high)*0x100000000ll+uint32_t(low)):int64_t(low);
    if(method>FILE_END){fail(ERROR_INVALID_PARAMETER);return INVALID_SET_FILE_POINTER;}
    uint64_t origin=method==FILE_BEGIN?0:method==FILE_CURRENT?f->cursor:f->archive->bytes;
    if((distance<0&&uint64_t(-(distance+1))+1>origin)||(distance>=0&&uint64_t(distance)>uint64_t(INT64_MAX)-origin)){fail(ERROR_NEGATIVE_SEEK);return INVALID_SET_FILE_POINTER;}
    uint64_t position=distance<0?origin-uint64_t(-(distance+1))-1:origin+uint64_t(distance);
    if(!high&&position>UINT32_MAX){fail(ERROR_INVALID_PARAMETER);return INVALID_SET_FILE_POINTER;}
    f->cursor=position;if(high)*high=(LONG)(position>>32);SetLastError(ERROR_SUCCESS);return (DWORD)position;
}
inline BOOL WINAPI readFile(HANDLE h,LPVOID buffer,DWORD count,LPDWORD got,LPOVERLAPPED ov){
    auto f=lookup(h);if(!f)return ReadFile(h,buffer,count,got,ov);
    if(got)*got=0;
    if(!f->canRead)return fail(ERROR_ACCESS_DENIED);
    if((count&&!buffer)||(!got&&!ov)||(f->overlapped&&!ov))return fail(ERROR_INVALID_PARAMETER);
    std::lock_guard<std::mutex>lock(f->mutex);if(f->closed)return fail(ERROR_INVALID_HANDLE);
    uint64_t position=ov?(uint64_t(ov->OffsetHigh)<<32)|ov->Offset:f->cursor;
    InterlockedIncrement(&counters.syncReads);
    try{
        DWORD n=f->archive->readAt(position,buffer,count);
        if(ov){ov->Internal=(!n&&count&&position>=f->archive->bytes)?0xC0000011ul:0;ov->InternalHigh=n;if(ov->hEvent)SetEvent((HANDLE)((uintptr_t)ov->hEvent&~uintptr_t(1)));}
        if(got)*got=n;
        if(!f->overlapped)f->cursor=position+n;
        if(ov&&!n&&count&&position>=f->archive->bytes){SetLastError(ERROR_HANDLE_EOF);return FALSE;}
        return TRUE;
    }catch(...){if(ov){ov->Internal=0xC0000185ul;ov->InternalHigh=0;if(ov->hEvent)SetEvent((HANDLE)((uintptr_t)ov->hEvent&~uintptr_t(1)));}return fail(ERROR_READ_FAULT);}
}

struct Request {
    OVERLAPPED native{};
    std::shared_ptr<File>file;
    LPOVERLAPPED user=nullptr;
    LPOVERLAPPED_COMPLETION_ROUTINE callback=nullptr;
    unsigned char*buffer=nullptr;
    uint64_t position=0;
    DWORD total=0,done=0,chunk=0;
    size_t span=0;
    DWORD terminalError=0;
};
inline void finish(Request*r,DWORD error){
    if(r->file->closed)error=ERROR_OPERATION_ABORTED;
    auto ov=r->user;auto cb=r->callback;DWORD done=r->done;
    ov->Internal=error==ERROR_SUCCESS?0:error==ERROR_HANDLE_EOF?0xC0000011ul:error==ERROR_OPERATION_ABORTED?0xC0000120ul:0xC0000185ul;
    ov->InternalHigh=done;
    if(error!=ERROR_SUCCESS&&error!=ERROR_HANDLE_EOF)InterlockedIncrement(&counters.errors);
    delete r;InterlockedDecrement(&counters.pending);cb(error,done,ov);
}
inline void CALLBACK memoryDone(ULONG_PTR data){auto r=(Request*)data;finish(r,r->terminalError);}
inline bool next(Request*r,bool initial);
inline void CALLBACK nativeDone(DWORD error,DWORD bytes,LPOVERLAPPED ov){
    auto r=(Request*)ov->hEvent;
    if(error){finish(r,error);return;}
    if(bytes!=r->chunk){finish(r,ERROR_READ_FAULT);return;}
    r->done+=bytes;r->position+=bytes;r->span++;
    if(!next(r,false))finish(r,GetLastError());
}
inline bool next(Request*r,bool initial){
    auto&a=*r->file->archive;
    if(r->file->closed){SetLastError(ERROR_OPERATION_ABORTED);return false;}
    while(r->done<r->total){
        auto&s=a.spans[r->span];auto within=r->position-s.start;
        DWORD n=(DWORD)(std::min)(uint64_t(r->total-r->done),s.length-within);
        if(!n){SetLastError(ERROR_INVALID_DATA);return false;}
        if(s.source==VirtualArchive::Zero)memset(r->buffer+r->done,0,n);
        else if(s.source==VirtualArchive::Metadata)memcpy(r->buffer+r->done,a.metadata.data()+size_t(s.offset+within),n);
        else {
            r->native={};uint64_t off=s.offset+within;r->native.Offset=(DWORD)off;r->native.OffsetHigh=(DWORD)(off>>32);r->native.hEvent=(HANDLE)r;r->chunk=n;
            InterlockedIncrement(&counters.nativeChunks);
            return ReadFileEx(a.sources[s.source]->async.h,r->buffer+r->done,n,&r->native,&nativeDone)!=FALSE;
        }
        r->done+=n;r->position+=n;r->span++;
    }
    if(!initial){finish(r,r->terminalError);return true;}
    // In-memory header/zero/EOF completion must also wait for the caller's
    // alertable state. Never call the game's completion routine inline.
    InterlockedIncrement(&counters.memoryCompletions);
    return QueueUserAPC(&memoryDone,GetCurrentThread(),(ULONG_PTR)r)!=0;
}
inline BOOL WINAPI readFileEx(HANDLE h,LPVOID buffer,DWORD count,LPOVERLAPPED ov,LPOVERLAPPED_COMPLETION_ROUTINE callback){
    auto f=lookup(h);if(!f)return ReadFileEx(h,buffer,count,ov,callback);
    if(!f->canRead)return fail(ERROR_ACCESS_DENIED);
    if(!f->overlapped||!ov||!callback||(count&&!buffer))return fail(ERROR_INVALID_PARAMETER);
    if(f->closed)return fail(ERROR_INVALID_HANDLE);
    auto r=new(std::nothrow)Request;if(!r)return fail(ERROR_NOT_ENOUGH_MEMORY);
    r->file=f;r->buffer=(unsigned char*)buffer;r->user=ov;r->callback=callback;r->position=(uint64_t(ov->OffsetHigh)<<32)|ov->Offset;
    r->total=r->position<f->archive->bytes?(DWORD)(std::min)(uint64_t(count),f->archive->bytes-r->position):0;
    if(count&&r->position>=f->archive->bytes)r->terminalError=ERROR_HANDLE_EOF;
    if(r->total)r->span=f->archive->locate(r->position);
    ov->Internal=0x103;ov->InternalHigh=0;
    InterlockedIncrement(&counters.asyncReads);InterlockedIncrement(&counters.pending);
    if(!next(r,true)){DWORD error=GetLastError();delete r;InterlockedDecrement(&counters.pending);ov->Internal=0xC0000185ul;return fail(error);}
    SetLastError(ERROR_SUCCESS);return TRUE;
}
}
