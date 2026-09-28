#include "resources.h"
#include <iostream>
int wmain(int argc,wchar_t**argv){try{ssasr::require(argc==4,"Usage: compose game-root mod-folder plan.json");auto plan=ssasr::JsonParser(ssasr::text_file(argv[3])).parse();auto r=ssasr::prepare(ssasr::full(argv[1]),ssasr::full(argv[2]),plan);for(auto&p:r)std::wcout<<p.logical<<L" -> "<<p.physical<<L"\n";return 0;}catch(const std::exception&e){std::cerr<<e.what()<<"\n";return 1;}}
