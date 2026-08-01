@echo off
setlocal

dotnet publish "%~dp0XunxianDpkViewer.csproj" -c Release -r win-x64 --self-contained true -p:Platform=x64 -o "%~dp0bin\single-file"
if errorlevel 1 exit /b %errorlevel%

copy /Y "%~dp0bin\single-file\XunxianDpkViewer.exe" "%~dp0XunxianDpkViewer.exe" >nul
echo Created: %~dp0XunxianDpkViewer.exe
