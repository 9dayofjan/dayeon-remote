@echo off
cd /d "%~dp0"
cls
start /b "" node.exe server.js
timeout /t 2 >nul
npx --yes localtunnel --port 8080
pause
