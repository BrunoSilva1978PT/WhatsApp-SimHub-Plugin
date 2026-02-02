@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

echo ========================================
echo  A copiar WhatsAppSimHubPlugin.dll...
echo ========================================

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

set "SOURCE=!PROJECT_PATH!\bin\Release\WhatsAppSimHubPlugin.dll"
set "DEST=C:\Program Files (x86)\SimHub\WhatsAppSimHubPlugin.dll"

if not exist "!SOURCE!" (
    echo ERRO: DLL nao encontrada em !SOURCE!
    pause
    exit /b 1
)

copy /Y "!SOURCE!" "!DEST!"
if !ERRORLEVEL! EQU 0 (
    echo.
    echo DLL copiada com sucesso!
    echo Podes abrir o SimHub agora.
) else (
    echo.
    echo ERRO ao copiar DLL!
)

echo.
pause
