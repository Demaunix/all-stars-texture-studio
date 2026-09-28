#pragma once
#include "files.h"
#include "json.h"
#include "rtsc.h"
#include "virtual_xpac.h"
#include <map>
#include <set>
namespace ssasr {
struct Redirect {std::wstring logical,physical;bool writable=false;std::shared_ptr<VirtualArchive>archive;};
inline uint32_t key(const Json&j){if(j.kind==Json::Number)return j.u32();auto s=j.str();require(s.size()==8,"Invalid XPAC key");uint32_t k=0;for(char c:s){unsigned v=c>='0'&&c<='9'?c-'0':c>='a'&&c<='f'?c-'a'+10:c>='A'&&c<='F'?c-'A'+10:99;require(v<16,"Invalid XPAC key");k=(k<<4)|v;}return k;}
inline uint32_t aligned(uint64_t n){uint64_t a=(n+2047)&~uint64_t(2047);require(a<=UINT32_MAX,"XPAC exceeds 32-bit offsets");return (uint32_t)a;}
struct XRow {uint32_t key,off,size2,stored,flags;};
static_assert(sizeof(XRow)==20,"XPAC row layout");
struct XMember {XRow row{};std::wstring payload;};
inline void zeros(HANDLE f,uint64_t n){unsigned char z[2048]{};while(n){DWORD k=(DWORD)(std::min)(n,(uint64_t)sizeof(z));write(f,z,k);n-=k;}}
inline std::wstring payload_path(const std::wstring&mod,const Json&o){auto p=child(mod,wide(o.at("payload").str()));no_reparse(mod,p);hash_matches(p,o.at("payload_sha256").str());return p;}
inline void xpac(const std::wstring&source,const std::wstring&mod,const Json&group,const std::wstring&out){
    Handle in(CreateFileW(source.c_str(),GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr));require(in.h!=INVALID_HANDLE_VALUE,"Cannot open original archive");auto length=size(in.h);require(length>=24&&length<=UINT32_MAX,"Invalid original archive length");
    uint32_t h[6];read(in.h,h,24);uint32_t count=h[3];require(h[0]==0&&h[1]==0&&h[4]==0&&h[5]==0&&count>0&&count<1000000&&24ull+20ull*count<=h[2]&&h[2]<=length,"Invalid XPAC header");
    std::vector<XRow>rows(count);read(in.h,rows.data(),count*20);std::map<uint32_t,XMember>members;std::vector<std::pair<uint64_t,uint64_t>>ranges;
    for(auto r:rows){require(r.off>=h[2]&&uint64_t(r.off)+r.stored<=length&&r.size2==r.stored&&r.flags==0,"Unsupported XPAC record");require(members.emplace(r.key,XMember{r,{}}).second,"Duplicate XPAC key");if(r.stored)ranges.emplace_back(r.off,uint64_t(r.off)+r.stored);}
    std::sort(ranges.begin(),ranges.end());for(size_t i=1;i<ranges.size();++i)require(ranges[i].first>=ranges[i-1].second,"Overlapping XPAC records");
    std::set<uint32_t>changed;
    for(auto&o:group.at("operations").list()){uint32_t k=key(o.at("key"));require(changed.insert(k).second,"Duplicate member operation");auto kind=o.at("kind").str();auto it=members.find(k);
        if(kind=="replace"){require(it!=members.end(),"Replacement member absent");auto&r=it->second.row;require(hash_range(in.h,r.off,r.stored)==o.at("base_stored_sha256").str(),"Original member hash mismatch");}
        else require(kind=="add"&&it==members.end(),"Added member already exists");
        auto p=payload_path(mod,o);Handle pf(CreateFileW(p.c_str(),GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr));require(pf.h!=INVALID_HANDLE_VALUE,"Payload open failed");auto n=size(pf.h);require(n<=UINT32_MAX&&n==o.at("stored_size").u32(),"Payload length mismatch");uint32_t flags=o.has("entry_flags")?o.at("entry_flags").u32():0;require(flags==0,"Unsupported member flags");members[k]=XMember{{k,0,(uint32_t)n,(uint32_t)n,flags},p};}
    count=(uint32_t)members.size();h[2]=aligned(24ull+20ull*count);h[3]=count;uint64_t cursor=h[2];rows.clear();for(auto&item:members){auto r=item.second.row;r.off=(uint32_t)cursor;rows.push_back(r);cursor=aligned(cursor+r.stored);}
    Handle dest(CreateFileW(out.c_str(),GENERIC_WRITE,0,nullptr,CREATE_NEW,FILE_ATTRIBUTE_NORMAL,nullptr));require(dest.h!=INVALID_HANDLE_VALUE,"Cannot create archive cache");write(dest.h,h,24);write(dest.h,rows.data(),count*20);zeros(dest.h,h[2]-24-20ull*count);
    size_t i=0;for(auto&item:members){auto&m=item.second;auto&r=rows[i++];if(m.payload.empty()){seek(in.h,m.row.off);copy(in.h,dest.h,r.stored);}else{Handle pf(CreateFileW(m.payload.c_str(),GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr));require(pf.h!=INVALID_HANDLE_VALUE,"Payload disappeared");copy(pf.h,dest.h,r.stored);}zeros(dest.h,aligned(uint64_t(r.off)+r.stored)-r.off-r.stored);}require(FlushFileBuffers(dest.h)!=0,"Archive flush failed");
}
inline std::vector<Redirect> prepare(const std::wstring&game,const std::wstring&mod,const Json&plan){
    require(plan.at("schema").u32()==1,"Unsupported plan version");std::vector<Redirect>result;std::set<std::wstring>seen;
    for(auto&g:plan.at("archive_groups").list()){
        auto rel=relative(wide(g.at("path").str()));require(seen.insert(fold(rel)).second,"Duplicate logical file");auto logical=child(game,rel);no_reparse(game,logical);auto format=g.at("format").str();bool cache=format=="copy";
        if(format=="rtsc_merge"){require(fold(rel).rfind(L"shadercache\\",0)==0&&fold(rel).size()>5&&fold(rel).substr(rel.size()-5)==L".rtsc","Invalid shader cache path");auto p=payload_path(mod,g);unsigned keyBytes=g.at("key_size").u32();auto target=prepare_shaders(game,mod,logical,p,keyBytes,g.at("payload_sha256").str());result.push_back({logical,target,true});continue;}
        require(fold(rel).rfind(L"resource\\",0)==0||fold(rel).rfind(L"scripts\\",0)==0,"Only Resource and Scripts files may be redirected");
        if(format=="xpac"){auto archive=virtual_xpac(logical,mod,g);result.push_back({logical,logical,false,archive});continue;}
        if(!cache)hash_matches(logical,g.at("base_sha256").str());
        std::string signature;if(g.has("expected_composed_sha256"))signature=g.at("expected_composed_sha256").str();
        if(format=="file"||cache) {auto p=payload_path(mod,g);signature=g.at("payload_sha256").str();if(!cache){result.push_back({logical,p,false});continue;}}
        else require(format=="xpac","Unsupported archive format");
        require(signature.size()==64,"Expected composed SHA-256 required");
        // A release-specific cache namespace prevents old mutable shader caches
        // from being mistaken for a cache prepared for another asset build.
        auto directory=child(mod,L".cache\\"+wide(signature));mkdirs(mod,directory);auto target=child(directory,L"data.bin");no_reparse(mod,target);
        DWORD attr=GetFileAttributesW(target.c_str());bool ready=attr!=INVALID_FILE_ATTRIBUTES&&!(attr&FILE_ATTRIBUTE_DIRECTORY);
        if(ready&&!cache)ready=hash_file(target)==signature;
        if(!ready){auto temp=target+L".tmp-"+std::to_wstring(GetCurrentProcessId())+L"-"+std::to_wstring(GetTickCount64());no_reparse(mod,temp);
            try{if(cache){auto p=payload_path(mod,g);require(CopyFileW(p.c_str(),temp.c_str(),TRUE)!=0,"Shader cache copy failed");}else xpac(logical,mod,g,temp);hash_matches(temp,signature);atomic_publish(temp,target);}catch(...){DeleteFileW(temp.c_str());throw;}}
        result.push_back({logical,target,cache});
    }return result;
}
}
