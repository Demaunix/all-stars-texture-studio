#pragma once
#include "files.h"
#include "json.h"
#include <memory>
#include <mutex>
#include <map>
#include <set>
#include <cstring>

namespace ssasr {
struct VirtualSource {
    std::wstring path;
    Handle sync,async;
    std::mutex mutex;
    explicit VirtualSource(const std::wstring&p):path(p),
        sync(CreateFileW(p.c_str(),GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr)),
        async(CreateFileW(p.c_str(),GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_FLAG_OVERLAPPED,nullptr)) {
        require(sync.h!=INVALID_HANDLE_VALUE&&async.h!=INVALID_HANDLE_VALUE,"Cannot open virtual archive source");
    }
};
struct VirtualSpan {uint64_t start,length,offset;uint32_t source;};
struct VirtualArchive {
    static constexpr uint32_t Zero=UINT32_MAX,Metadata=UINT32_MAX-1;
    uint64_t bytes=0;
    std::string base_hash;
    std::set<uint32_t> modified_keys;
    std::vector<unsigned char>metadata;
    std::vector<VirtualSpan>spans;
    std::vector<std::shared_ptr<VirtualSource>>sources;
    size_t locate(uint64_t position)const {
        auto i=std::upper_bound(spans.begin(),spans.end(),position,[](uint64_t p,const VirtualSpan&s){return p<s.start;});
        require(i!=spans.begin(),"Virtual archive position outside layout");return size_t(--i-spans.begin());
    }
    DWORD readAt(uint64_t position,void*buffer,DWORD count)const {
        if(!count||position>=bytes)return 0;
        DWORD remaining=(DWORD)(std::min)(uint64_t(count),bytes-position),total=remaining;
        auto out=(unsigned char*)buffer;size_t index=locate(position);
        while(remaining){
            const auto&s=spans.at(index++);auto within=position-s.start;
            DWORD n=(DWORD)(std::min)(uint64_t(remaining),s.length-within);
            require(n!=0,"Empty virtual archive span");
            if(s.source==Zero)memset(out,0,n);
            else if(s.source==Metadata)memcpy(out,metadata.data()+size_t(s.offset+within),n);
            else {auto&source=*sources.at(s.source);std::lock_guard<std::mutex>lock(source.mutex);seek(source.sync.h,s.offset+within);read(source.sync.h,out,n);}
            out+=n;position+=n;remaining-=n;
        }
        return total;
    }
};
inline uint32_t virtual_key(const Json&j){
    if(j.kind==Json::Number)return j.u32();auto s=j.str();require(s.size()==8,"Invalid XPAC key");uint32_t k=0;
    for(char c:s){unsigned v=c>='0'&&c<='9'?c-'0':c>='a'&&c<='f'?c-'a'+10:c>='A'&&c<='F'?c-'A'+10:99;require(v<16,"Invalid XPAC key");k=(k<<4)|v;}return k;
}
inline uint64_t virtual_align(uint64_t n){auto a=(n+2047)&~uint64_t(2047);require(a<=UINT32_MAX,"XPAC exceeds 32-bit offsets");return a;}
inline std::shared_ptr<VirtualArchive> virtual_xpac(const std::wstring&source,const std::wstring&mod,const Json&g){
    auto a=std::make_shared<VirtualArchive>();
    a->sources.push_back(std::make_shared<VirtualSource>(source));auto file=a->sources[0]->sync.h;
    auto length=size(file);require(length>=24&&length<=UINT32_MAX,"Invalid original archive length");
    // These same open handles remain read-only and deny writers for the layout's lifetime.
    require(hash_range(file,0,length)==g.at("base_sha256").str(),"Original archive differs from required unmodified game file");
    a->base_hash=g.at("base_sha256").str();
    uint32_t h[6];seek(file,0);read(file,h,sizeof(h));
    require(h[0]==0&&h[1]==0&&h[4]==0&&h[5]==0&&h[3]>0&&h[3]<1000000&&24ull+20ull*h[3]<=h[2]&&h[2]<=length,"Invalid XPAC header");
    struct Row{uint32_t key,off,size2,stored,flags;};static_assert(sizeof(Row)==20,"XPAC layout");
    struct Member{uint32_t source;uint64_t offset;uint32_t length;};
    std::vector<Row>rows(h[3]);read(file,rows.data(),(DWORD)(rows.size()*sizeof(Row)));
    std::map<uint32_t,Member>members;std::vector<std::pair<uint64_t,uint64_t>>ranges;
    for(auto&r:rows){require(r.off>=h[2]&&uint64_t(r.off)+r.stored<=length&&r.size2==r.stored&&r.flags==0,"Unsupported XPAC record");require(members.emplace(r.key,Member{0,r.off,r.stored}).second,"Duplicate XPAC key");if(r.stored)ranges.emplace_back(r.off,uint64_t(r.off)+r.stored);}
    std::sort(ranges.begin(),ranges.end());for(size_t i=1;i<ranges.size();++i)require(ranges[i-1].second<=ranges[i].first,"Overlapping XPAC records");
    std::set<uint32_t>changed;std::map<std::wstring,uint32_t>payloads;
    for(auto&o:g.at("operations").list()){
        uint32_t k=virtual_key(o.at("key"));require(changed.insert(k).second,"Duplicate member operation");auto kind=o.at("kind").str();auto it=members.find(k);
        if(kind=="replace"){require(it!=members.end(),"Replacement member absent");auto&m=it->second;require(hash_range(file,m.offset,m.length)==o.at("base_stored_sha256").str(),"Original member hash mismatch");}
        else require(kind=="add"&&it==members.end(),"Added member already exists");
        auto p=child(mod,wide(o.at("payload").str()));no_reparse(mod,p);uint32_t index;
        auto seen=payloads.find(fold(p));
        if(seen==payloads.end()){index=(uint32_t)a->sources.size();a->sources.push_back(std::make_shared<VirtualSource>(p));payloads.emplace(fold(p),index);}else index=seen->second;
        auto ph=a->sources[index]->sync.h;auto n=size(ph);
        require(n<=UINT32_MAX&&n==o.at("stored_size").u32(),"Payload length mismatch");
        require(hash_range(ph,0,n)==o.at("payload_sha256").str(),"Payload hash mismatch");
        require(!o.has("entry_flags")||o.at("entry_flags").u32()==0,"Unsupported member flags");members[k]={index,0,(uint32_t)n};
    }
    h[3]=(uint32_t)members.size();h[2]=(uint32_t)virtual_align(24ull+20ull*members.size());
    a->metadata.resize(h[2],0);memcpy(a->metadata.data(),h,sizeof(h));
    a->spans.push_back({0,h[2],0,VirtualArchive::Metadata});uint64_t cursor=h[2];size_t i=0;
    for(auto&item:members){auto&m=item.second;Row r{item.first,(uint32_t)cursor,m.length,m.length,0};memcpy(a->metadata.data()+24+20*i++,&r,20);
        if(m.length)a->spans.push_back({cursor,m.length,m.offset,m.source});
        auto end=virtual_align(cursor+m.length);if(end>cursor+m.length)a->spans.push_back({cursor+m.length,end-cursor-m.length,0,VirtualArchive::Zero});cursor=end;
    }
    a->bytes=cursor;require(a->bytes==g.at("expected_composed_size").u32()&&members.size()==g.at("expected_member_count").u32(),"Virtual archive dimensions mismatch");
    Hash hash;std::vector<unsigned char>buffer(1<<20);
    for(uint64_t off=0;off<a->bytes;){DWORD n=a->readAt(off,buffer.data(),(DWORD)buffer.size());require(n>0,"Virtual archive hash stalled");hash.add(buffer.data(),n);off+=n;}
    require(hash.finish()==g.at("expected_composed_sha256").str(),"Virtual archive output hash mismatch");
    a->modified_keys=std::move(changed);return a;
}
inline std::shared_ptr<VirtualArchive> merge_virtual_xpac(const std::shared_ptr<VirtualArchive>&left,const std::shared_ptr<VirtualArchive>&right) {
    require(left&&right&&left->base_hash==right->base_hash,"Mods require different original archives");
    for(auto k:right->modified_keys)require(!left->modified_keys.count(k),"Two mods change the same archive member");
    auto out=std::make_shared<VirtualArchive>();out->base_hash=left->base_hash;
    out->modified_keys=left->modified_keys;out->modified_keys.insert(right->modified_keys.begin(),right->modified_keys.end());
    out->sources=left->sources;uint32_t sourceShift=(uint32_t)out->sources.size();out->sources.insert(out->sources.end(),right->sources.begin(),right->sources.end());
    struct Row {uint32_t key,off,size2,stored,flags;};
    struct Member {uint32_t source;uint64_t offset;uint32_t bytes;};
    std::map<uint32_t,Member>members;
    auto collect=[&](const std::shared_ptr<VirtualArchive>&a,bool incoming) {
        require(a->metadata.size()>=24,"Missing archive metadata");uint32_t count;memcpy(&count,a->metadata.data()+12,4);
        require(24ull+20ull*count<=a->metadata.size(),"Invalid archive directory");
        for(uint32_t i=0;i<count;++i) {
            Row r{};memcpy(&r,a->metadata.data()+24+20*i,20);
            if(incoming&&!a->modified_keys.count(r.key))continue;
            require(!r.flags&&r.size2==r.stored,"Unsupported composed entry");
            if(!r.stored){members[r.key]={VirtualArchive::Zero,0,0};continue;}
            const auto&span=a->spans.at(a->locate(r.off));
            require(span.start==r.off&&span.length==r.stored&&span.source<a->sources.size(),"Invalid member source span");
            members[r.key]={span.source+(incoming?sourceShift:0),span.offset,r.stored};
        }
    };
    collect(left,false);collect(right,true);
    uint32_t header[6]={0,0,(uint32_t)virtual_align(24ull+20ull*members.size()),(uint32_t)members.size(),0,0};
    out->metadata.resize(header[2],0);memcpy(out->metadata.data(),header,24);
    out->spans.push_back({0,header[2],0,VirtualArchive::Metadata});uint64_t cursor=header[2];size_t i=0;
    for(const auto&entry:members) {
        const auto&m=entry.second;Row r{entry.first,(uint32_t)cursor,m.bytes,m.bytes,0};memcpy(out->metadata.data()+24+20*i++,&r,20);
        if(m.bytes)out->spans.push_back({cursor,m.bytes,m.offset,m.source});
        auto end=virtual_align(cursor+m.bytes);if(end>cursor+m.bytes)out->spans.push_back({cursor+m.bytes,end-cursor-m.bytes,0,VirtualArchive::Zero});cursor=end;
    }
    out->bytes=cursor;return out;
}
}
