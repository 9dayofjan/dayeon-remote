@echo off
chcp 65001 > nul
cls
cd /d "%~dp0"
node agent.js
pause
