@echo off
setlocal
for /f "usebackq tokens=*" %%i in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "TEXTURE_VS=%%i"
if not defined TEXTURE_VS exit /b 1
call "%TEXTURE_VS%\VC\Auxiliary\Build\vcvars32.bat" >nul
if errorlevel 1 exit /b %errorlevel%
pushd "%~dp0"
if not exist "..\runtime" mkdir "..\runtime"
if not exist "..\bin" mkdir "..\bin"
cl /nologo /O2 /MT /LD /W4 /Brepro /Fo"..\bin\TextureRuntime.obj" TextureRuntime.cpp /link /DEF:TextureRuntime.def /OUT:"..\runtime\TextureRuntime.dll" /IMPLIB:"..\bin\TextureRuntime.lib" /Brepro
if errorlevel 1 goto failed
cl /nologo /O2 /MT /W4 /Fo"..\bin\NativeRuntimeTest.obj" "..\tests\NativeRuntimeTest.cpp" /Fe"..\bin\NativeRuntimeTest.exe"
if errorlevel 1 goto failed
"..\bin\NativeRuntimeTest.exe" "..\runtime\TextureRuntime.dll"
set result=%errorlevel%
popd
exit /b %result%
:failed
popd
exit /b 1
