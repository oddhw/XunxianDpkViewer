@echo off
setlocal

set "DOTNET_CMD="
if defined DOTNET_EXE if exist "%DOTNET_EXE%" set "DOTNET_CMD=%DOTNET_EXE%"
if not defined DOTNET_CMD if defined DOTNET_ROOT if exist "%DOTNET_ROOT%\dotnet.exe" set "DOTNET_CMD=%DOTNET_ROOT%\dotnet.exe"
if not defined DOTNET_CMD if exist "%~dp0..\.dotnet-sdk\dotnet.exe" set "DOTNET_CMD=%~dp0..\.dotnet-sdk\dotnet.exe"
if not defined DOTNET_CMD if exist "%~d0\GIT\.dotnet-sdk\dotnet.exe" set "DOTNET_CMD=%~d0\GIT\.dotnet-sdk\dotnet.exe"
if not defined DOTNET_CMD for /f "delims=" %%D in ('where dotnet 2^>nul') do if not defined DOTNET_CMD set "DOTNET_CMD=%%D"

if not defined DOTNET_CMD (
  echo ERROR: A usable .NET SDK was not found.
  exit /b 1
)

set "SDK_LIST=%TEMP%\xunxian-dotnet-sdks-%RANDOM%.txt"
"%DOTNET_CMD%" --list-sdks > "%SDK_LIST%" 2>nul
findstr /r "." "%SDK_LIST%" >nul
if errorlevel 1 (
  del /q "%SDK_LIST%" >nul 2>&1
  echo ERROR: %DOTNET_CMD% is only a runtime host and has no SDK.
  exit /b 1
)
del /q "%SDK_LIST%" >nul 2>&1

set "PUBLISH_EXE=%~dp0bin\single-file\XunxianDpkViewer.exe"
del /q "%PUBLISH_EXE%" >nul 2>&1
"%DOTNET_CMD%" publish "%~dp0XunxianDpkViewer.csproj" -c Release -r win-x64 --self-contained true -p:Platform=x64 -o "%~dp0bin\single-file"
if errorlevel 1 exit /b 1
if not exist "%PUBLISH_EXE%" (
  echo ERROR: Publish completed without creating XunxianDpkViewer.exe.
  exit /b 1
)

copy /Y "%PUBLISH_EXE%" "%~dp0XunxianDpkViewer.exe" >nul
if errorlevel 1 exit /b 1
echo Created: %~dp0XunxianDpkViewer.exe
