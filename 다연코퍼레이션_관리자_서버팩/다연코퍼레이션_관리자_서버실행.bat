@echo off
cd /d "%~dp0"
cls
start http://localhost:8080
node.exe server.js
pause
