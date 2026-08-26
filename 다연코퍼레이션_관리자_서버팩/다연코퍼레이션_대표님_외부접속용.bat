@echo off
cd /d "%~dp0"
cls
start "다연코퍼레이션 관제서버" /min node.exe server.js
timeout /t 2 >nul
npx --yes localtunnel --port 8080 --subdomain dayeon-cctv-2026
pause
