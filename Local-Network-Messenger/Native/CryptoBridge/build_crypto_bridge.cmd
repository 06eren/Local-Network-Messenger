@echo off
setlocal
set VS_DEV_CMD=C:\Program Files\Microsoft Visual Studio\18\Professional\Common7\Tools\VsDevCmd.bat
if not exist "%VS_DEV_CMD%" (
  echo VsDevCmd.bat bulunamadi: %VS_DEV_CMD%
  exit /b 1
)
call "%VS_DEV_CMD%" -arch=amd64 > nul
cl /nologo /EHsc /std:c++17 "%~dp0crypto_bridge.cpp" /Fe:"%~dp0..\..\Tools\crypto_bridge.exe" /link bcrypt.lib crypt32.lib
cl /nologo /EHsc /std:c++17 /LD "%~dp0crypto_bridge_dll.cpp" /Fe:"%~dp0..\..\Tools\crypto_bridge.dll" /link bcrypt.lib crypt32.lib ole32.lib
endlocal
