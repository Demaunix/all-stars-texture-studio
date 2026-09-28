@echo off
setlocal
for /f "usebackq tokens=*" %%i in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "TEXTURE_VS=%%i"
if not defined TEXTURE_VS exit /b 1
call "%TEXTURE_VS%\VC\Auxiliary\Build\vcvars32.bat" >nul
if errorlevel 1 exit /b %errorlevel%
pushd "%~dp0"
if not exist "..\runtime" mkdir "..\runtime"
if not exist "..\bin" mkdir "..\bin"
pushd host
cl /nologo /std:c++17 /EHsc /O2 /MT /GS /W4 /DUNICODE /D_UNICODE /DWIN32_LEAN_AND_MEAN /DNOMINMAX /LD /Fo"..\..\bin\host.obj" host.cpp bcrypt.lib user32.lib /link /DEF:exports.def /DYNAMICBASE /NXCOMPAT /OUT:"..\..\runtime\ChaoGarage.dll" /IMPLIB:"..\..\bin\host.lib"
if errorlevel 1 goto failed
cl /nologo /std:c++17 /EHsc /O2 /MT /GS /W4 /DUNICODE /D_UNICODE /DWIN32_LEAN_AND_MEAN /DNOMINMAX compose.cpp bcrypt.lib /Fo"..\..\bin\compose.obj" /Fe"..\..\bin\compose.exe"
if errorlevel 1 goto failed
popd
pushd bootstrap
cl /nologo /c /std:c++17 /EHsc /O2 /MT /GS /W4 /Fo"..\..\bin\\" bootstrap_policy.cpp proxy.cpp
if errorlevel 1 goto failed
link /NOLOGO /IGNORE:4222 /DLL /MACHINE:X86 /DYNAMICBASE /NXCOMPAT /DEF:exports.def /OUT:"..\..\runtime\dinput8.dll" /IMPLIB:"..\..\bin\bootstrap.lib" "..\..\bin\bootstrap_policy.obj" "..\..\bin\proxy.obj" kernel32.lib bcrypt.lib
if errorlevel 1 goto failed
popd
popd
exit /b 0
:failed
popd
popd
exit /b 1
