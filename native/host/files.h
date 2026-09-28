#pragma once
#include <windows.h>
#include <bcrypt.h>
#include <string>
#include <vector>
#include <algorithm>
#include <stdexcept>
#include <cstdint>
namespace ssasr {
inline void require(bool ok,const char* msg){if(!ok)throw std::runtime_error(msg);}
struct Handle {HANDLE h=INVALID_HANDLE_VALUE;explicit Handle(HANDLE v=INVALID_HANDLE_VALUE):h(v){}~Handle(){if(h!=INVALID_HANDLE_VALUE)CloseHandle(h);}Handle(const Handle&)=delete;Handle& operator=(const Handle&)=delete;};
inline std::wstring wide(const std::string&s){if(s.empty())return {};int n=MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,s.data(),(int)s.size(),nullptr,0);require(n>0,"Invalid UTF-8 path");std::wstring w(n,0);MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,s.data(),(int)s.size(),w.data(),n);return w;}
inline std::wstring full(const std::wstring&s){DWORD n=GetFullPathNameW(s.c_str(),0,nullptr,nullptr);require(n>0&&n<32768,"Invalid full path");std::wstring v(n,0);DWORD k=GetFullPathNameW(s.c_str(),n,v.data(),nullptr);require(k>0&&k<n,"Invalid full path");v.resize(k);std::replace(v.begin(),v.end(),L'/',L'\\');return v;}
inline std::wstring fold(std::wstring s){for(auto&c:s)c=(wchar_t)towlower(c);return s;}
inline std::wstring relative(std::wstring s){require(!s.empty()&&s.size()<2000,"Invalid relative path");std::replace(s.begin(),s.end(),L'/',L'\\');require(s[0]!=L'\\'&&s.find(L':')==s.npos&&s.find(L'\0')==s.npos,"Absolute mod path forbidden");size_t p=0;while(p<s.size()){size_t e=s.find(L'\\',p);if(e==s.npos)e=s.size();auto part=s.substr(p,e-p);require(!part.empty()&&part!=L"."&&part!=L".."&&part.back()!=L'.'&&part.back()!=L' ',"Invalid path segment");require(part.find_first_of(L"*?\"<>|") == part.npos,"Invalid path character");p=e+1;}return s;}
inline std::wstring child(const std::wstring&root,const std::wstring&rel){return full(root+L"\\"+relative(rel));}
inline void no_reparse(const std::wstring& root,const std::wstring& path){auto r=full(root),p=full(path);require(fold(p).compare(0,r.size()+1,fold(r)+L"\\")==0,"Path escapes root");size_t pos=r.size();for(;;){pos=p.find(L'\\',pos+1);auto part=pos==p.npos?p:p.substr(0,pos);DWORD a=GetFileAttributesW(part.c_str());require(a==INVALID_FILE_ATTRIBUTES||!(a&FILE_ATTRIBUTE_REPARSE_POINT),"Reparse points are not supported in mod paths");if(pos==p.npos)break;}}
inline void mkdirs(const std::wstring&root,const std::wstring&path){no_reparse(root,path);auto r=full(root),p=full(path);size_t pos=r.size();for(;;){pos=p.find(L'\\',pos+1);auto part=pos==p.npos?p:p.substr(0,pos);if(!CreateDirectoryW(part.c_str(),nullptr))require(GetLastError()==ERROR_ALREADY_EXISTS,"Cannot create mod cache directory");DWORD a=GetFileAttributesW(part.c_str());require(a!=INVALID_FILE_ATTRIBUTES&&(a&FILE_ATTRIBUTE_DIRECTORY)&&!(a&FILE_ATTRIBUTE_REPARSE_POINT),"Cache path is not a real directory");if(pos==p.npos)break;}}
inline uint64_t size(HANDLE h){LARGE_INTEGER n{};require(GetFileSizeEx(h,&n)&&n.QuadPart>=0,"Cannot get file size");return (uint64_t)n.QuadPart;}
inline void seek(HANDLE h,uint64_t p){LARGE_INTEGER n;n.QuadPart=p;require(SetFilePointerEx(h,n,nullptr,FILE_BEGIN)!=0,"File seek failed");}
inline void read(HANDLE h,void*p,DWORD n){DWORD got=0;require(ReadFile(h,p,n,&got,nullptr)&&got==n,"File read failed");}
inline void write(HANDLE h,const void*p,DWORD n){DWORD got=0;require(WriteFile(h,p,n,&got,nullptr)&&got==n,"File write failed");}
inline void copy(HANDLE from,HANDLE to,uint64_t n){std::vector<unsigned char>b(1<<20);while(n){DWORD k=(DWORD)(std::min)(n,(uint64_t)b.size());read(from,b.data(),k);write(to,b.data(),k);n-=k;}}
struct Hash {BCRYPT_ALG_HANDLE alg=nullptr;BCRYPT_HASH_HANDLE h=nullptr;std::vector<unsigned char>obj;Hash(){require(BCryptOpenAlgorithmProvider(&alg,BCRYPT_SHA256_ALGORITHM,nullptr,0)>=0,"SHA provider failed");DWORD n=0,k=0;require(BCryptGetProperty(alg,BCRYPT_OBJECT_LENGTH,(PUCHAR)&n,sizeof(n),&k,0)>=0,"SHA object failed");obj.resize(n);require(BCryptCreateHash(alg,&h,obj.data(),n,nullptr,0,0)>=0,"SHA init failed");}~Hash(){if(h)BCryptDestroyHash(h);if(alg)BCryptCloseAlgorithmProvider(alg,0);}void add(const void*p,DWORD n){require(BCryptHashData(h,(PUCHAR)p,n,0)>=0,"SHA update failed");}std::string finish(){unsigned char b[32];require(BCryptFinishHash(h,b,32,0)>=0,"SHA finish failed");std::string s;const char*x="0123456789abcdef";for(auto c:b){s+=x[c>>4];s+=x[c&15];}return s;}};
inline std::string hash_range(HANDLE h,uint64_t off,uint64_t n){seek(h,off);Hash hash;std::vector<unsigned char>b(1<<20);while(n){DWORD k=(DWORD)(std::min)(n,(uint64_t)b.size());read(h,b.data(),k);hash.add(b.data(),k);n-=k;}return hash.finish();}
inline std::string hash_file(const std::wstring&p){Handle f(CreateFileW(p.c_str(),GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr));require(f.h!=INVALID_HANDLE_VALUE,"Cannot read required file");return hash_range(f.h,0,size(f.h));}
inline void hash_matches(const std::wstring&p,const std::string&hash){require(hash.size()==64&&hash_file(p)==hash,"Required file hash mismatch");}
inline std::string text_file(const std::wstring&p,size_t limit=16*1024*1024){Handle f(CreateFileW(p.c_str(),GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr));require(f.h!=INVALID_HANDLE_VALUE,"Cannot open plan");auto n=size(f.h);require(n<=limit,"Plan is too large");std::string s((size_t)n,0);read(f.h,s.data(),(DWORD)n);return s;}
inline void atomic_publish(const std::wstring&temporary,const std::wstring&target){require(MoveFileExW(temporary.c_str(),target.c_str(),MOVEFILE_REPLACE_EXISTING|MOVEFILE_WRITE_THROUGH)!=0,"Cannot publish cache file");}
}
