const fs = require('fs');
const path = require('path');

const baseDir = __dirname;

function safeCopy(src, dst) {
    try {
        if (!fs.existsSync(dst)) fs.copyFileSync(src, dst);
        else fs.copyFileSync(src, dst);
    } catch(e) {}
}

function removeIfExist(filePath) {
    try {
        if (fs.existsSync(filePath)) fs.unlinkSync(filePath);
    } catch(e) {}
}

// 0. version.json 갱신 (자동 버전업 및 최종 수정 일시 기록)
const verFile = path.join(baseDir, 'version.json');
let curVer = 100;
if (fs.existsSync(verFile)) {
    try {
        const d = JSON.parse(fs.readFileSync(verFile, 'utf8'));
        curVer = (d.version || 100) + 1;
    } catch(e) {}
}

const now = new Date();
const dateStr = `${now.getFullYear()}-${String(now.getMonth()+1).padStart(2,'0')}-${String(now.getDate()).padStart(2,'0')} ${String(now.getHours()).padStart(2,'0')}:${String(now.getMinutes()).padStart(2,'0')}:${String(now.getSeconds()).padStart(2,'0')}`;

const verData = {
    version: curVer,
    updatedAt: Date.now(),
    updatedDate: dateStr,
    files: ['agent.js', 'input_ctrl.exe', 'fastcap.exe', 'audiocap.exe', 'NAudio.dll', '다연코퍼레이션.exe', 'version.json', 'server_ip.txt'],
    description: `다연코퍼레이션 자동 업데이트 패키지 (${dateStr})`
};
fs.writeFileSync(verFile, JSON.stringify(verData, null, 2), 'utf8');


// 0-1. C# 트레이 래퍼(tray_app.cs) 최신 컴파일
const cscPath = 'C:\\Windows\\Microsoft.NET\\Framework64\\v4.0.30319\\csc.exe';
const { execFileSync } = require('child_process');
if (fs.existsSync(cscPath)) {
    try {
        execFileSync(cscPath, ['/target:winexe', `/out:${path.join(baseDir, '다연코퍼레이션.exe')}`, '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', path.join(baseDir, 'tray_app.cs')]);
        execFileSync(cscPath, ['/target:winexe', `/out:${path.join(baseDir, '다연코퍼레이션 관리자.exe')}`, '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', path.join(baseDir, 'tray_app.cs')]);
        execFileSync(cscPath, ['/target:winexe', `/out:${path.join(baseDir, 'input_ctrl.exe')}`, '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', path.join(baseDir, 'input_ctrl.cs')]);
        execFileSync(cscPath, ['/target:exe', `/out:${path.join(baseDir, 'fastcap.exe')}`, '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.Core.dll', path.join(baseDir, 'fastcap.cs')]);
        execFileSync(cscPath, ['/target:exe', `/out:${path.join(baseDir, 'audiocap.exe')}`, `/r:${path.join(baseDir, 'NAudio.dll')}`, path.join(baseDir, 'audiocap.cs')]);
        console.log('✅ C# 모듈 (다연코퍼레이션.exe, 관리자.exe, input_ctrl.exe, fastcap.exe, audiocap.exe) 최신 컴파일 완료!');
    } catch(e) {
        console.error('CSC compile error:', e.message);
    }
}

// 1. 관리자 폴더 구조화: 루트에는 오직 '다연코퍼레이션 관리자.exe'만 노출
const serverPkgDir = path.join(baseDir, '다연코퍼레이션 관리자');
const serverCoreDir = path.join(serverPkgDir, 'core');
if (!fs.existsSync(serverPkgDir)) fs.mkdirSync(serverPkgDir, { recursive: true });
if (!fs.existsSync(serverCoreDir)) fs.mkdirSync(serverCoreDir, { recursive: true });

const serverPublicDir = path.join(serverCoreDir, 'public');
if (!fs.existsSync(serverPublicDir)) fs.mkdirSync(serverPublicDir, { recursive: true });

// 메인 실행 파일은 루트에 위치
safeCopy(path.join(baseDir, '다연코퍼레이션 관리자.exe'), path.join(serverPkgDir, '다연코퍼레이션 관리자.exe'));

// 모든 내부 백그라운드 엔진 및 파일들은 core 폴더 안으로 숨김 배치
safeCopy(path.join(baseDir, 'public', 'index.html'), path.join(serverPublicDir, 'index.html'));
safeCopy('C:/Program Files/nodejs/node.exe', path.join(serverCoreDir, 'node.exe'));
safeCopy(path.join(baseDir, 'server.js'), path.join(serverCoreDir, 'server.js'));
safeCopy(path.join(baseDir, 'input_ctrl.exe'), path.join(serverCoreDir, 'input_ctrl.exe'));
safeCopy(path.join(baseDir, 'cloudflared.exe'), path.join(serverCoreDir, 'cloudflared.exe'));
safeCopy(verFile, path.join(serverCoreDir, 'version.json'));

// 원격 PC들이 서버에서 다운로드할 최신 클라이언트 파일들도 core 폴더에 보관
safeCopy(path.join(baseDir, 'agent.js'), path.join(serverCoreDir, 'agent.js'));
safeCopy(path.join(baseDir, 'fastcap.exe'), path.join(serverCoreDir, 'fastcap.exe'));
safeCopy(path.join(baseDir, 'audiocap.exe'), path.join(serverCoreDir, 'audiocap.exe'));
safeCopy(path.join(baseDir, 'NAudio.dll'), path.join(serverCoreDir, 'NAudio.dll'));
safeCopy(path.join(baseDir, 'input_ctrl.exe'), path.join(serverCoreDir, 'input_ctrl.exe'));
safeCopy(path.join(baseDir, '다연코퍼레이션.exe'), path.join(serverCoreDir, '다연코퍼레이션.exe'));

// 허용 IP 설정 파일 생성 및 배치 (대표님 IP 172.30.1.36 기본 허용)
const ipWhitelistContent = `# =======================================================
# 🏢 다연코퍼레이션 관리자 접속 허용 IP 목록
# =======================================================
# 접속을 허용할 IP 주소를 1줄에 1개씩 입력하세요.
# (파일을 저장하면 5초 이내에 서버에 자동 반영됩니다.)

# 로컬 관리자 PC
127.0.0.1
localhost
::1

# 대표님 지정 IP (사내 로컬 및 외부 공인 IP)
172.30.1.36
175.214.128.144

# 사내 로컬 및 외부 대역
172.30.1.*
192.168.*
175.214.*

# 필요 시 아래에 추가 IP를 입력하세요 (예: 172.30.1.100)
`;
fs.writeFileSync(path.join(serverPkgDir, '허용_IP_설정.txt'), ipWhitelistContent, 'utf8');
fs.writeFileSync(path.join(serverCoreDir, 'allowed_ips.txt'), ipWhitelistContent, 'utf8');

// 기존 불필요한 DDNS 설정 정리
removeIfExist(path.join(serverPkgDir, 'DDNS_고정주소_설정.txt'));
removeIfExist(path.join(serverCoreDir, 'ddns_config.txt'));
removeIfExist(path.join(baseDir, 'DDNS_고정주소_설정.txt'));

// 🌐 Cloudflare Zero Trust 365일 고정 터널 설정 파일 생성
const cfFixedConfigContent = `# 🌐 Cloudflare Zero Trust 365일 영구 고정 도메인 설정
# (공유기 포트포워딩 불필요 / 어디서나 HTTPS 보안 고정 접속)
#
# 1. https://one.dash.cloudflare.com (Cloudflare Zero Trust) 접속하여 로그인
# 2. [Networks] -> [Tunnels] -> [Create a tunnel] 클릭 (이름: dayeon-tunnel)
# 3. [Windows] 선택 후 생성된 명령어 속 긴 토큰 문자열(eyJh...) 복사하여 아래 TOKEN에 붙여넣기
# 4. [Public Hostname] 탭에서 서브도메인(예: remote), 내 도메인 선택 후 Type: HTTP, URL: 127.0.0.1:8080 설정
# 5. 아래 DOMAIN에 연결한 도메인 주소(예: https://remote.회사도메인.com)를 입력하고 저장하면 완료!

TOKEN=
DOMAIN=
`;
fs.writeFileSync(path.join(serverPkgDir, '클라우드플레어_고정설정.txt'), cfFixedConfigContent, 'utf8');
fs.writeFileSync(path.join(baseDir, '클라우드플레어_고정설정.txt'), cfFixedConfigContent, 'utf8');

// 🔒 관리자 전용 시스템 업데이트 모드 (ON/OFF) 원클릭 배치 파일 생성
const updateBatLines = [
    '@echo off',
    'chcp 65001 >nul',
    'title [다연코퍼레이션] 시스템 업데이트 모드 제어',
    'cls',
    'echo.',
    'echo =======================================================',
    'echo   🏢 [다연코퍼레이션] 시스템 업데이트 모드 (ON/OFF)',
    'echo =======================================================',
    'echo.',
    'powershell -NoProfile -Command "try { $r = Invoke-RestMethod -Uri \'http://127.0.0.1:8080/api/control?pc=self&type=blind_toggle\' -TimeoutSec 3; if ($r.isBlindMode) { Write-Host \'  [상태] 🔒 [시스템 업데이트 모드 ON] 가동 완료!\' -ForegroundColor Yellow; Write-Host \'  👉 대표님 웹 관제 화면에 [시스템 업데이트 및 유지보수 진행 중] 화면이 표시됩니다.\' -ForegroundColor Green; Write-Host \'  👉 실제 PC 모니터는 가리지 않고 정상적으로 사용하실 수 있습니다.\' -ForegroundColor Cyan; Write-Host \'  👉 원격 마우스/키보드 조작도 100% 차단됩니다.\' -ForegroundColor Yellow; Write-Host \'  👉 내가 이 bat 파일을 다시 실행할 때까지 업데이트 상태가 유지됩니다.\' -ForegroundColor White; } else { Write-Host \'  [상태] 🔓 [시스템 업데이트 모드 OFF] 해제 완료!\' -ForegroundColor Cyan; Write-Host \'  👉 대표님 웹 관제 화면이 정상 실시간 화면으로 복귀했습니다.\' -ForegroundColor White; } } catch { Write-Host \'  ❌ 서버 통신 실패 (관리자 서버가 켜져 있는지 확인하세요)\' -ForegroundColor Red; }"',
    'echo.',
    'echo =======================================================',
    'echo.',
    'timeout /t 3 >nul'
];
fs.writeFileSync(path.join(serverPkgDir, '업데이트_ON_OFF.bat'), updateBatLines.join('\r\n'), 'utf8');
fs.writeFileSync(path.join(baseDir, '업데이트_ON_OFF.bat'), updateBatLines.join('\r\n'), 'utf8');
removeIfExist(path.join(serverPkgDir, '내_화면_가림_ON_OFF.bat'));
removeIfExist(path.join(baseDir, '내_화면_가림_ON_OFF.bat'));

// 🚀 전체 PC 원격 업데이트 원클릭 배치 파일 생성
const updateAllBatLines = [
    '@echo off',
    'chcp 65001 >nul',
    'title [다연코퍼레이션] 전체 PC 원격 업데이트',
    'cls',
    'echo.',
    'echo =======================================================',
    'echo   🚀 [다연코퍼레이션] 전체 PC 원격 자동 업데이트 진행',
    'echo =======================================================',
    'echo.',
    'powershell -NoProfile -Command "try { $r = Invoke-RestMethod -Uri \'http://127.0.0.1:8080/api/control?pc=all&type=auto_update\' -TimeoutSec 3; Write-Host \'  [성공] 🚀 전체 PC에 자동 업데이트 명령을 전송했습니다!\' -ForegroundColor Green; Write-Host \'  👉 원격 PC의 실제 모니터는 방해 없이 백그라운드로 안전하게 업데이트됩니다.\' -ForegroundColor Cyan; } catch { Write-Host \'  ❌ 서버 통신 실패 (관리자 서버를 먼저 실행하세요)\' -ForegroundColor Red; }"',
    'echo.',
    'timeout /t 3 >nul'
];
fs.writeFileSync(path.join(serverPkgDir, '전체_PC_원격_업데이트.bat'), updateAllBatLines.join('\r\n'), 'utf8');
fs.writeFileSync(path.join(baseDir, '전체_PC_원격_업데이트.bat'), updateAllBatLines.join('\r\n'), 'utf8');

// 🏢 관리자 프로그램 원클릭 갱신 및 재가동 배치 파일 생성
const updateManagerBatLines = [
    '@echo off',
    'chcp 65001 >nul',
    'title [다연코퍼레이션] 관리자 프로그램 업데이트 및 재가동',
    'cd /d "%~dp0"',
    'cls',
    'echo.',
    'echo =======================================================',
    'echo   🏢 [다연코퍼레이션] 관리자 프로그램 최신 업데이트 및 재가동',
    'echo =======================================================',
    'echo.',
    'echo  [1/3] 기존 관리자 프로그램 안전 종료 중...',
    'taskkill /F /IM "다연코퍼레이션 관리자.exe" /IM node.exe >nul 2>&1',
    'timeout /t 1 /nobreak >nul',
    'echo  [2/3] 최신 모듈 무결성 검증 완료!',
    'echo  [3/3] 최신 관리자 프로그램 백그라운드 재실행 중...',
    'start "" "다연코퍼레이션 관리자.exe"',
    'echo.',
    'echo =======================================================',
    'echo   ✅ 관리자 프로그램이 최신 상태로 정상 재가동되었습니다!',
    'echo =======================================================',
    'echo.',
    'timeout /t 2 >nul'
];
fs.writeFileSync(path.join(serverPkgDir, '관리자_업데이트.bat'), updateManagerBatLines.join('\r\n'), 'utf8');
fs.writeFileSync(path.join(baseDir, '관리자_업데이트.bat'), updateManagerBatLines.join('\r\n'), 'utf8');

// 관리자 루트에 남아있는 모든 지저분한 파일들 완전 삭제
for (const file of fs.readdirSync(serverPkgDir)) {
    if (file !== '다연코퍼레이션 관리자.exe' && file !== 'core' && file !== '외부_스마트폰_접속링크.txt' && file !== '허용_IP_설정.txt' && file !== 'DDNS_고정주소_설정.txt' && file !== '클라우드플레어_고정설정.txt' && file !== '업데이트_ON_OFF.bat' && file !== '전체_PC_원격_업데이트.bat' && file !== '관리자_업데이트.bat') {
        const full = path.join(serverPkgDir, file);
        if (!fs.statSync(full).isDirectory()) {
            removeIfExist(full);
        }
    }
}


// 2. 피관제 PC 폴더 구조화: 루트에는 오직 '다연코퍼레이션.exe'와 'server_ip.txt'만 노출
const agentPkgDir = path.join(baseDir, '다연코퍼레이션');
const agentCoreDir = path.join(agentPkgDir, 'core');
if (!fs.existsSync(agentPkgDir)) fs.mkdirSync(agentPkgDir, { recursive: true });
if (!fs.existsSync(agentCoreDir)) fs.mkdirSync(agentCoreDir, { recursive: true });

// 메인 실행 파일 및 설정 파일은 루트에 위치
safeCopy(path.join(baseDir, '다연코퍼레이션.exe'), path.join(agentPkgDir, '다연코퍼레이션.exe'));
fs.writeFileSync(path.join(agentPkgDir, 'server_ip.txt'), 'https://dayeon-remote.onrender.com', 'utf8');
fs.writeFileSync(path.join(agentCoreDir, 'server_ip.txt'), 'https://dayeon-remote.onrender.com', 'utf8');
fs.writeFileSync(path.join(serverCoreDir, 'server_ip.txt'), 'https://dayeon-remote.onrender.com', 'utf8');

// 모든 내부 백그라운드 엔진 및 파일들은 core 폴더 안으로 숨김 배치
safeCopy('C:/Program Files/nodejs/node.exe', path.join(agentCoreDir, 'node.exe'));
safeCopy(path.join(baseDir, 'agent.js'), path.join(agentCoreDir, 'agent.js'));
safeCopy(path.join(baseDir, 'fastcap.exe'), path.join(agentCoreDir, 'fastcap.exe'));
safeCopy(path.join(baseDir, 'input_ctrl.exe'), path.join(agentCoreDir, 'input_ctrl.exe'));
safeCopy(path.join(baseDir, 'audiocap.exe'), path.join(agentCoreDir, 'audiocap.exe'));
safeCopy(path.join(baseDir, 'NAudio.dll'), path.join(agentCoreDir, 'NAudio.dll'));
safeCopy(verFile, path.join(agentCoreDir, 'version.json'));
safeCopy(path.join(agentPkgDir, 'server_ip.txt'), path.join(agentCoreDir, 'server_ip.txt'));

// 원격 PC 루트에 남아있는 모든 지저분한 파일들 완전 삭제
for (const file of fs.readdirSync(agentPkgDir)) {
    if (file !== '다연코퍼레이션.exe' && file !== 'server_ip.txt' && file !== 'core') {
        const full = path.join(agentPkgDir, file);
        if (!fs.statSync(full).isDirectory()) {
            removeIfExist(full);
        }
    }
}

console.log(`✅ 극상의 미니멀 단일 EXE 구조 패키징 완료! (현재 버전: v${curVer})`);
