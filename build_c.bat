@echo off
chcp 65001 > NUL
echo ==================================================
echo   C언어 원격 서버 컴파일 시작 (MSVC cl.exe)
echo ==================================================

set "MSVC_PATH=C:\Program Files\Microsoft Visual Studio\18\Community\VC\Tools\MSVC\14.51.36231\bin\Hostx64\x64"
set "WIN_SDK_INCLUDE=C:\Program Files (x86)\Windows Kits\10\Include"
set "WIN_SDK_LIB=C:\Program Files (x86)\Windows Kits\10\Lib"

if exist "%MSVC_PATH%\cl.exe" (
    set "PATH=%MSVC_PATH%;%PATH%"
)

where cl.exe >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo [경고] Visual Studio cl.exe가 PATH에 설정되어 있지 않습니다.
    echo Developer Command Prompt 환경에서 실행하거나 vcvars64.bat을 먼저 호출하세요.
    pause
    exit /b 1
)

cl.exe /O2 /W3 remote_server.c ws2_32.lib gdi32.lib user32.lib /Fe:remote_server.exe

if %ERRORLEVEL% EQU 0 (
    echo.
    echo ==================================================
    echo   [성공] remote_server.exe 컴파일 성공!
    echo   실행: remote_server.exe
    echo ==================================================
) else (
    echo [오류] 컴파일에 실패했습니다.
)
