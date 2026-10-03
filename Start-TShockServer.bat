@echo off
rem ============================================================
rem  TShock 6.2.1 Server  /  Terraria 1.4.5.8
rem  Portable .NET 9 runtime (no system installation required)
rem  NOTE: keep this file ASCII-only. Non-ASCII + chcp breaks
rem        batch parsing under the SYSTEM scheduled task.
rem ============================================================
setlocal

set "ROOT=%~dp0"
set "DOTNET_ROOT=%ROOT%dotnet"
set "PATH=%DOTNET_ROOT%;%PATH%"
set "SERVERDIR=%ROOT%TShock-Server"
set "WORLD=%SERVERDIR%\Worlds\TShockWorld.wld"

if not exist "%DOTNET_ROOT%\dotnet.exe" (
    echo [ERROR] .NET runtime not found at: %DOTNET_ROOT%\dotnet.exe
    echo         Please re-extract dotnet-runtime-9.0.20-win-x64.zip into that folder.
    pause
    exit /b 1
)

if not exist "%WORLD%" (
    echo [ERROR] World file not found: %WORLD%
    pause
    exit /b 1
)

cd /d "%SERVERDIR%"

echo ============================================================
echo   TShock 6.2.1.0   (Terraria 1.4.5.8 / protocol 326)
echo ------------------------------------------------------------
echo   Runtime : %DOTNET_ROOT%
echo   World   : %WORLD%
echo   Port    : 7777      (tshock\config.json)
echo   Slots   : 16        (tshock\config.json)
echo   Mode    : Master difficulty / random evil
echo ------------------------------------------------------------
echo   Console: type "exit" to shut down safely, "save" to save now
echo ============================================================
echo.

"%SERVERDIR%\TShock.Server.exe" -world "%WORLD%"

echo.
echo [Server exited] Press any key to close...
pause >nul
endlocal
