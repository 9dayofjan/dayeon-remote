@echo off
cd /d "%~dp0"
title CCTV Remote Control Server Launcher

echo ==================================================
echo   CCTV Remote Control 1-Click Server Launcher
echo ==================================================
echo.

:: Check Node.js installation
where node >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Node.js is not installed.
    echo Please download and install Node.js from https://nodejs.org
    echo.
    pause
    exit /b 1
)

echo [1/3] Opening Firewall Port 8080...
netsh advfirewall firewall add rule name=RemoteControl8080 dir=in action=allow protocol=TCP localport=8080 >nul 2>&1

echo [2/3] Detecting local IP address...
set IP=127.0.0.1
for /f "tokens=2 delims=:" %%a in ('ipconfig ^| findstr /c:"IPv4"') do (
    set IP=%%a
    goto :break_ip
)
:break_ip
set IP=%IP: =%

echo [3/3] Server ready!
echo.
echo ==================================================================
echo   Access this URL from controller PC or smartphone:
echo.
echo   👉 http://%IP%:8080
echo ==================================================================
echo.
echo Server running... (Closing this window stops the server)
echo.

node "%~dp0server.js"
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [ERROR] Server process stopped.
)

echo.
echo Server stopped. Press any key to exit.
pause
