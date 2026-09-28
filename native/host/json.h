#pragma once
#include <string>
#include <map>
#include <vector>
#include <stdexcept>
#include <cstdint>
namespace ssasr {
struct Json {
    enum Kind {Null, String, Number, Bool, Object, Array} kind=Null;
    std::string s; uint64_t n=0; bool b=false;
    std::map<std::string,Json> o; std::vector<Json> a;
    const Json& at(const std::string& k) const {if(kind!=Object||!o.count(k))throw std::runtime_error("Missing plan field: "+k);return o.at(k);}
    bool has(const std::string& k) const {return kind==Object&&o.count(k);}
    std::string str() const {if(kind!=String)throw std::runtime_error("Expected string");return s;}
    uint32_t u32() const {if(kind!=Number||n>UINT32_MAX)throw std::runtime_error("Expected unsigned 32-bit number");return (uint32_t)n;}
    const std::vector<Json>& list() const {if(kind!=Array)throw std::runtime_error("Expected array");return a;}
};
class JsonParser {
    const std::string& s; size_t p=0;
    [[noreturn]] void bad(){throw std::runtime_error("Invalid JSON plan at byte "+std::to_string(p));}
    void ws(){while(p<s.size()&&(s[p]==' '||s[p]=='\r'||s[p]=='\n'||s[p]=='\t'))++p;}
    unsigned hex(){if(p>=s.size())bad();char c=s[p++];if(c>='0'&&c<='9')return c-'0';if(c>='a'&&c<='f')return c-'a'+10;if(c>='A'&&c<='F')return c-'A'+10;bad();}
    std::string string(){if(p>=s.size()||s[p++]!='"')bad();std::string r;
        while(p<s.size()){unsigned char c=s[p++];if(c=='"')return r;if(c<32)bad();if(c!='\\'){r+=(char)c;continue;}
            if(p>=s.size())bad();c=s[p++];switch(c){case '"':case '\\':case '/':r+=(char)c;break;case 'b':r+='\b';break;case 'f':r+='\f';break;case 'n':r+='\n';break;case 'r':r+='\r';break;case 't':r+='\t';break;
            case 'u':{unsigned u=0;for(int i=0;i<4;++i)u=u*16+hex();if(u>=0xD800&&u<=0xDFFF)bad();if(u<128)r+=(char)u;else if(u<2048){r+=(char)(0xC0|(u>>6));r+=(char)(0x80|(u&63));}else{r+=(char)(0xE0|(u>>12));r+=(char)(0x80|((u>>6)&63));r+=(char)(0x80|(u&63));}break;}default:bad();}}
        bad();}
    Json value(unsigned depth){if(depth>32)bad();ws();if(p>=s.size())bad();Json j;char c=s[p];
        if(c=='"'){j.kind=Json::String;j.s=string();return j;}
        if(c=='{'||c=='['){bool obj=c=='{';++p;j.kind=obj?Json::Object:Json::Array;ws();if(p<s.size()&&s[p]==(obj?'}':']')){++p;return j;}
            for(;;){ws();if(obj){std::string k=string();ws();if(p>=s.size()||s[p++]!=':')bad();if(!j.o.emplace(k,value(depth+1)).second)bad();}else j.a.push_back(value(depth+1));ws();if(p>=s.size())bad();char t=s[p++];if(t==(obj?'}':']'))return j;if(t!=',')bad();}}
        if(c>='0'&&c<='9'){j.kind=Json::Number;size_t begin=p;while(p<s.size()&&s[p]>='0'&&s[p]<='9'){unsigned d=s[p++]-'0';if(j.n>(UINT64_MAX-d)/10)bad();j.n=j.n*10+d;}if(p-begin>1&&s[begin]=='0')bad();return j;}
        if(s.compare(p,4,"null")==0){p+=4;return j;}if(s.compare(p,4,"true")==0){p+=4;j.kind=Json::Bool;j.b=true;return j;}if(s.compare(p,5,"false")==0){p+=5;j.kind=Json::Bool;return j;}bad();}
public:explicit JsonParser(const std::string& text):s(text){}Json parse(){Json j=value(0);ws();if(p!=s.size())bad();return j;}
};
}
