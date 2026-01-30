@echo off
echo ========================================
echo  A copiar WhatsAppSimHubPlugin.dll...
echo ========================================

set SOURCE="%~dp0bin\Release\WhatsAppSimHubPlugin.dll"
set DEST="C:\Program Files (x86)\SimHub\WhatsAppSimHubPlugin.dll"

if not exist %SOURCE% (
    echo ERRO: DLL nao encontrada em %SOURCE%
    pause
    exit /b 1
)

copy /Y %SOURCE% %DEST%
if %ERRORLEVEL% EQU 0 (
    echo.
    echo DLL copiada com sucesso!
    echo Podes abrir o SimHub agora.
) else (
    echo.
    echo ERRO ao copiar DLL!
)

echo.
pause
