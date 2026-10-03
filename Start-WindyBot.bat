@echo off
rem ============================================================
rem  CaiBotWindy - Terraria server management QQ bot
rem  Host  : Windy  (net10.0)   .\CaiBotWindy\deploy\Windy.exe
rem  Plugin: CaiBotWindy.dll   .\CaiBotWindy\deploy\Plugins\
rem  NOTE  : keep this file ASCII-only.  Non-ASCII + chcp breaks
rem          batch parsing under the SYSTEM scheduled task.
rem ============================================================
setlocal

rem Switch the console to UTF-8 so bot.log is encoded the same way as
rem TShock's own logs (tshock\logs\*.log).  Safe here because this file
rem contains no non-ASCII characters itself.
chcp 65001 >nul

set "ROOT=%~dp0"
set "DOTNET_ROOT=%ROOT%dotnet-sdk"
set "PATH=%DOTNET_ROOT%;%PATH%"
set "DEPLOY=%ROOT%CaiBotWindy\deploy"

if not exist "%DOTNET_ROOT%\dotnet.exe" (
    echo [ERROR] .NET 10 runtime not found at: %DOTNET_ROOT%\dotnet.exe
    exit /b 1
)

if not exist "%DEPLOY%\Windy.exe" (
    echo [ERROR] Windy host not found at: %DEPLOY%\Windy.exe
    exit /b 1
)

rem DOTNET_ROOT must point at the portable .NET 10 runtime (dotnet-sdk),
rem NOT at the machine-wide DOTNET_ROOT which serves TShock's .NET 9.
cd /d "%DEPLOY%"

if not exist "%DEPLOY%\Logs" mkdir "%DEPLOY%\Logs"

echo ============================================================
echo   CaiBotWindy  (Windy host / net10.0)
echo ------------------------------------------------------------
echo   Runtime : %DOTNET_ROOT%
echo   Deploy  : %DEPLOY%
echo   HTTP    : http://+:22338/    (bot API + WebSocket)
echo   Config  : %DEPLOY%\Config\
echo   Log     : %DEPLOY%\Logs\bot.log
echo ------------------------------------------------------------
echo   Press Ctrl+C to stop the bot.  (appending to bot.log)
echo ============================================================
echo.

"%DEPLOY%\Windy.exe" >> "%DEPLOY%\Logs\bot.log" 2>&1

echo.
echo [Bot exited]  See %DEPLOY%\Logs\bot.log
endlocal
