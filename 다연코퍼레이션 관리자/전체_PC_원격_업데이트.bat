@echo off
chcp 65001 >nul
title [다연코퍼레이션] 전체 PC 원격 업데이트
cls
echo.
echo =======================================================
echo   🚀 [다연코퍼레이션] 전체 PC 원격 자동 업데이트 진행
echo =======================================================
echo.
powershell -NoProfile -Command "try { $r = Invoke-RestMethod -Uri 'http://127.0.0.1:8080/api/control?pc=all&type=auto_update' -TimeoutSec 3; Write-Host '  [성공] 🚀 전체 PC에 자동 업데이트 명령을 전송했습니다!' -ForegroundColor Green; Write-Host '  👉 원격 PC의 실제 모니터는 방해 없이 백그라운드로 안전하게 업데이트됩니다.' -ForegroundColor Cyan; } catch { Write-Host '  ❌ 서버 통신 실패 (관리자 서버를 먼저 실행하세요)' -ForegroundColor Red; }"
echo.
timeout /t 3 >nul