@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

title WhatsApp Plugin - Build and Deploy

echo ========================================
echo Building WhatsApp SimHub Plugin...
echo ========================================
echo.

REM Detectar PC e definir path do projeto
REM Por defeito usa drive E:\
set "PROJECT_PATH=E:\Programaçao\GitHub\WhatsApp-SimHub-Plugin"

REM Se for PC-TIAGO, sobrescreve para drive D:\
if "%COMPUTERNAME%"=="PC-TIAGO" (
    set "PROJECT_PATH=D:\Programaçao\GitHub\WhatsApp-SimHub-Plugin"
    echo PC detectado: PC-TIAGO
) else (
    echo A usar path por defeito
)
echo Path: !PROJECT_PATH!
echo.

cd /d "!PROJECT_PATH!"

REM Detectar caminho do MSBuild automaticamente
set "MSBUILD_PATH="

REM Tentar Visual Studio 2022 Professional
if exist "C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\amd64\MSBuild.exe" (
    set "MSBUILD_PATH=C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\amd64\MSBuild.exe"
    echo MSBuild encontrado: VS 2022 Professional
)

REM Tentar Visual Studio 2022 Community
if not defined MSBUILD_PATH (
    if exist "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\amd64\MSBuild.exe" (
        set "MSBUILD_PATH=C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\amd64\MSBuild.exe"
        echo MSBuild encontrado: VS 2022 Community
    )
)

REM Tentar Visual Studio 2019 Professional
if not defined MSBUILD_PATH (
    if exist "C:\Program Files (x86)\Microsoft Visual Studio\2019\Professional\MSBuild\Current\Bin\amd64\MSBuild.exe" (
        set "MSBUILD_PATH=C:\Program Files (x86)\Microsoft Visual Studio\2019\Professional\MSBuild\Current\Bin\amd64\MSBuild.exe"
        echo MSBuild encontrado: VS 2019 Professional
    )
)

REM Tentar Visual Studio 2019 Community
if not defined MSBUILD_PATH (
    if exist "C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\amd64\MSBuild.exe" (
        set "MSBUILD_PATH=C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\amd64\MSBuild.exe"
        echo MSBuild encontrado: VS 2019 Community
    )
)

REM Verificar se encontrou MSBuild
if not defined MSBUILD_PATH (
    echo.
    echo ========================================
    echo ERRO: MSBuild nao encontrado!
    echo ========================================
    echo.
    echo Instala o Visual Studio 2019 ou 2022
    echo ========================================
    pause
    exit /b 1
)

echo.
REM Force rebuild (not just build)
"!MSBUILD_PATH!" WhatsAppSimHubPlugin.sln -t:Rebuild -p:Configuration=Release -v:minimal

if !ERRORLEVEL! neq 0 (
    echo.
    echo ========================================
    echo BUILD FAILED! Error: !ERRORLEVEL!
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

tasklist /FI "IMAGENAME eq SimHubWPF.exe" 2>NUL | find /I "SimHubWPF.exe" >NUL
if !ERRORLEVEL! equ 0 (
    echo SimHub is running. Closing it...
    taskkill /IM SimHubWPF.exe /F
    timeout /t 2 /nobreak >NUL
    echo SimHub closed.
) else (
    echo SimHub is not running.
)

echo.
set "SIMHUB_PATH=C:\Program Files (x86)\SimHub"
echo Copying DLL...
echo   Source: !CD!\bin\Release\WhatsAppSimHubPlugin.dll
echo   Target: !SIMHUB_PATH!\
echo.

copy /Y "bin\Release\WhatsAppSimHubPlugin.dll" "!SIMHUB_PATH!\"

set COPY_RESULT=!ERRORLEVEL!

echo.
if !COPY_RESULT! neq 0 (
    echo ========================================
    echo COPY FAILED! Error code: !COPY_RESULT!
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
