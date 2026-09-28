#pragma once
#include "files.h"
#include <map>
namespace ssasr {
inline uint32_t word(const std::string&s,size_t p){require(p+4<=s.size(),"Truncated shader cache word");uint32_t v;memcpy(&v,s.data()+p,4);return v;}
inline uint32_t checksum(const std::string&s,size_t start=4){uint32_t a=1,b=0;for(size_t i=start;i<s.size();++i){a=(a+(int32_t)(int8_t)s[i])%65521u;b=(b+a)%65521u;}return (b<<16)|a;}
using ShaderRecords=std::map<std::string,std::string>;
inline ShaderRecords shaders(const std::string&s,unsigned keyBytes){require((keyBytes==28||keyBytes==32)&&s.size()>=4&&s.size()<=64*1024*1024,"Invalid shader cache size");require(word(s,0)==checksum(s),"Shader cache checksum mismatch");size_t p=4;ShaderRecords records;
    unsigned count=0;while(p<s.size()){require(++count<=16384,"Shader cache record limit exceeded");size_t start=p;require(p+keyBytes+8<=s.size(),"Truncated shader record");std::string key=s.substr(p,keyBytes);p+=keyBytes;unsigned n=(uint8_t)s[p+2]|((uint8_t)s[p+3]<<8);p+=4;require(n<=4096&&p+4ull*n+4<=s.size(),"Invalid shader bindings");p+=4*n;unsigned bytes=word(s,p);p+=4;require(bytes>=8&&bytes<=65536&&bytes%4==0&&p+bytes<=s.size(),"Invalid shader bytecode length");require((word(s,p)>>16)==(keyBytes==32?0xffffu:0xfffeu)&&word(s,p+bytes-4)==0x0000ffff,"Invalid shader bytecode framing");p+=bytes;auto record=s.substr(start,p-start);auto i=records.find(key);require(i==records.end()||i->second==record,"Conflicting duplicate shader keys");records[key]=record;
    }return records;
}
inline std::string shader_bytes(const ShaderRecords&r){std::string b(4,0);for(auto&item:r)b+=item.second;auto sum=checksum(b);memcpy(b.data(),&sum,4);return b;}
inline std::wstring prepare_shaders(const std::wstring&game,const std::wstring&mod,const std::wstring&logical,const std::wstring&seed,unsigned keyBytes,const std::string&seedHash){
    auto incoming=shaders(text_file(seed,64*1024*1024),keyBytes);ShaderRecords original;std::string sourceHash="absent";
    if(GetFileAttributesW(logical.c_str())!=INVALID_FILE_ATTRIBUTES){try{auto data=text_file(logical,64*1024*1024);original=shaders(data,keyBytes);Hash h;h.add(data.data(),(DWORD)data.size());sourceHash=h.finish();}catch(const std::exception&){sourceHash="invalid";original.clear();}}
    std::string identity=seedHash+sourceHash;Hash h;h.add(identity.data(),(DWORD)identity.size());auto scene=fold(logical.substr(game.size()));h.add(scene.data(),(DWORD)(scene.size()*sizeof(wchar_t)));auto directory=child(mod,L".cache\\shaders\\"+wide(h.finish()));mkdirs(mod,directory);auto target=child(directory,L"data.rtsc");no_reparse(mod,target);
    if(GetFileAttributesW(target.c_str())!=INVALID_FILE_ATTRIBUTES){try{shaders(text_file(target,64*1024*1024),keyBytes);return target;}catch(const std::exception&){}}
    // Keep existing same-key native records, including scene-specific constants.
    // Seeding adds only absent records; it never silently overrides a conflict.
    for(auto&item:incoming)original.emplace(item.first,item.second);auto data=shader_bytes(original);shaders(data,keyBytes);
    auto temp=target+L".tmp-"+std::to_wstring(GetCurrentProcessId())+L"-"+std::to_wstring(GetTickCount64());no_reparse(mod,temp);
    try{{Handle out(CreateFileW(temp.c_str(),GENERIC_WRITE,0,nullptr,CREATE_NEW,FILE_ATTRIBUTE_NORMAL,nullptr));require(out.h!=INVALID_HANDLE_VALUE,"Cannot create private shader cache");write(out.h,data.data(),(DWORD)data.size());require(FlushFileBuffers(out.h)!=0,"Shader cache flush failed");}atomic_publish(temp,target);}catch(...){DeleteFileW(temp.c_str());throw;}return target;
}
}
