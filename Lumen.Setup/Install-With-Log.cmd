@echo off
setlocal
set "MSI=%~dp0Release\Lumen-Setup-1.0.0-RC1.msi"
set "LOG=%TEMP%\Lumen-RC1-install.log"

if not exist "%MSI%" (
    echo Installer not found:
    echo   %MSI%
    echo.
    echo Build Lumen.Setup in Release first.
    pause
    exit /b 1
)

echo Installing Lumen 1.0.0 RC1...
echo Detailed MSI log: %LOG%
echo.
msiexec.exe /i "%MSI%" /l*v "%LOG%"

echo.
echo Windows Installer exit code: %ERRORLEVEL%
echo Log: %LOG%
pause
