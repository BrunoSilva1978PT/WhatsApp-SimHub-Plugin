@echo off
setlocal enabledelayedexpansion

title WhatsApp Plugin - Build and Deploy

echo ========================================
echo Building WhatsApp SimHub Plugin...
echo ========================================
echo.

cd /d "%~dp0"

REM Force rebuild (not just build)
"C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\amd64\MSBuild.exe" WhatsAppSimHubPlugin.sln -t:Rebuild -p:Configuration=Release -v:minimal

if %ERRORLEVEL% neq 0 (
    echo.
    echo ========================================
    echo BUILD FAILED! Error: %ERRORLEVEL%
    echo ========================================
    echo.
    pause
    exit /b 1
)

echo.
echo ========================================
echo Build successful! Deploying...
echo ========================================
echo.

tasklist /FI "IMAGENAME eq SimHub.exe" 2>NUL | find /I "SimHub.exe" >NUL
if %ERRORLEVEL% equ 0 (
    echo SimHub is running. Closing it...
    taskkill /IM SimHub.exe /F
    timeout /t 2 /nobreak >NUL
    echo SimHub closed.
) else (
    echo SimHub is not running.
)

echo.
set "SIMHUB_PATH=C:\Program Files (x86)\SimHub"
echo Copying DLL...
echo   Source: %CD%\bin\Release\WhatsAppSimHubPlugin.dll
echo   Target: %SIMHUB_PATH%\
echo.

copy /Y "bin\Release\WhatsAppSimHubPlugin.dll" "%SIMHUB_PATH%\" 

set COPY_RESULT=%ERRORLEVEL%

echo.
if %COPY_RESULT% neq 0 (
    echo ========================================
    echo COPY FAILED! Error code: %COPY_RESULT%
    echo ========================================
    echo.
    echo Possible reasons:
    echo - Need Administrator privileges
    echo - SimHub folder protected
    echo.
    echo Try: Right-click this file and "Run as administrator"
    echo ========================================
) else (
    echo ========================================
    echo DEPLOYMENT SUCCESSFUL!
    echo ========================================
    echo.
    echo Plugin has been copied to SimHub.
    echo You can now start SimHub.
    echo ========================================
)

echo.
echo.
pause
