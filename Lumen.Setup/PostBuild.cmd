@echo off
setlocal

set "MSI=%~1"
set "LOGO=%~2"
set "ICON=%~3"
set "BANNER=%~dp0Generated\LumenBanner.bmp"

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0GenerateInstallerBranding.ps1" -LogoPath "%LOGO%" -OutputPath "%BANNER%"
if errorlevel 1 exit /b %errorlevel%

cscript.exe //nologo "%~dp0FixMsiDirectories.js" "%MSI%" "%BANNER%" "%ICON%"
exit /b %errorlevel%
