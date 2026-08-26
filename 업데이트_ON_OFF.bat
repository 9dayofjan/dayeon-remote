@echo off
chcp 65001 >nul
title [다연코퍼레이션] 시스템 업데이트 모드 제어
cls
echo.
echo =======================================================
echo   🏢 [다연코퍼레이션] 시스템 업데이트 모드 (ON/OFF)
echo =======================================================
echo.
powershell -NoProfile -Command "try { $r = Invoke-RestMethod -Uri 'http://127.0.0.1:8080/api/control?pc=self&type=blind_toggle' -TimeoutSec 3; if ($r.isBlindMode) { Write-Host '  [상태] 🔒 [시스템 업데이트 모드 ON] 가동 완료!' -ForegroundColor Yellow; Write-Host '  👉 대표님 웹 관제 화면에 [시스템 업데이트 및 유지보수 진행 중] 화면이 표시됩니다.' -ForegroundColor Green; Write-Host '  👉 실제 PC 모니터는 가리지 않고 정상적으로 사용하실 수 있습니다.' -ForegroundColor Cyan; Write-Host '  👉 원격 마우스/키보드 조작도 100% 차단됩니다.' -ForegroundColor Yellow; Write-Host '  👉 내가 이 bat 파일을 다시 실행할 때까지 업데이트 상태가 유지됩니다.' -ForegroundColor White; } else { Write-Host '  [상태] 🔓 [시스템 업데이트 모드 OFF] 해제 완료!' -ForegroundColor Cyan; Write-Host '  👉 대표님 웹 관제 화면이 정상 실시간 화면으로 복귀했습니다.' -ForegroundColor White; } } catch { Write-Host '  ❌ 서버 통신 실패 (관리자 서버가 켜져 있는지 확인하세요)' -ForegroundColor Red; }"
echo.
echo =======================================================
echo.
timeout /t 3 >nul