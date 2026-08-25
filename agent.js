const http = require('http');
const https = require('https');
const { execFile, spawn, execSync } = require('child_process');
const fs = require('fs');
const path = require('path');
const os = require('os');
const readline = require('readline');

process.on('uncaughtException', (err) => {
    console.error('Safe UncaughtException:', err ? err.message : '');
});
process.on('unhandledRejection', (reason) => {
    console.error('Safe UnhandledRejection:', reason);
});

// 🔒 단일 인스턴스 단독 실행 보장 (중복 실행 원천 차단)
const net = require('net');
const AGENT_LOCK_PORT = 49153;
const lockServer = net.createServer();
lockServer.once('error', (err) => {
    if (err.code === 'EADDRINUSE') {
        console.log('⚠️ [중복 실행 방지] 이미 다른 원격(agent) 프로세스가 실행 중이므로 즉시 종료합니다.');
        process.exit(0);
    }
});
lockServer.listen(AGENT_LOCK_PORT, '127.0.0.1');

// 잔존 캡처 프로세스 정리 및 8001번 사내 기가비트 LAN 포트 방화벽 개방
try { execSync('taskkill /F /IM fastcap.exe /IM audiocap.exe /IM input_ctrl.exe 2>nul'); } catch(e) {}
try { execSync('netsh advfirewall firewall add rule name="DayeonLAN" dir=in action=allow protocol=TCP localport=8001 2>nul'); } catch(e) {}

// 🌟 윈도우 부팅 시 다연코퍼레이션 100% 자동 실행 등록
function ensureAutoStart() {
    try {
        const exePath = path.join(__dirname, '..', '다연코퍼레이션.exe');
        if (fs.existsSync(exePath)) {
            try {
                execSync(`reg add "HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run" /v "DayeonCorp" /t REG_SZ /d "\"${exePath}\"" /f 2>nul`);
            } catch(e) {}
            try {
                const startupDir = path.join(process.env.APPDATA, 'Microsoft', 'Windows', 'Start Menu', 'Programs', 'Startup');
                if (fs.existsSync(startupDir)) {
                    const oldBat = path.join(startupDir, 'DayeonCorpAutoStart.bat');
                    if (fs.existsSync(oldBat)) {
                        try { fs.unlinkSync(oldBat); } catch(e) {}
                    }
                }
            } catch(e) {}
        }
    } catch(e) {}
}
ensureAutoStart();

function showNoticeToast(msg, duration = 3000) {
    if (!msg) return;
    const inputCtrlPath = path.join(__dirname, 'input_ctrl.exe');
    if (fs.existsSync(inputCtrlPath)) {
        try {
            spawn(inputCtrlPath, ['toast', duration.toString(), msg], { detached: true, stdio: 'ignore' }).unref();
        } catch(e) {}
    }
}

// 🌟 시작 시 업데이트 완료 토스트 자동 출력
const updateFlagFile = path.join(__dirname, 'update_temp_done.flag');
if (fs.existsSync(updateFlagFile)) {
    try {
        fs.unlinkSync(updateFlagFile);
        const verFile = path.join(__dirname, 'version.json');
        let localVer = { version: 0, updatedDate: '' };
        if (fs.existsSync(verFile)) {
            try { localVer = JSON.parse(fs.readFileSync(verFile, 'utf8')); } catch(e) {}
        }
        const verDate = localVer.updatedDate || `${new Date().toLocaleDateString()}`;
        showNoticeToast(`[다연코퍼레이션] 최신 버전(${verDate}) 업데이트 완료!`, 3500);
    } catch(e) {}
}

// PC 고유 식별자 생성 (호스트네임 + IP 끝자리로 복제 PC 중복 100% 방지)
const baseHostname = process.env.COMPUTERNAME || os.hostname() || 'PC';
const interfaces = os.networkInterfaces();
let localIpSuffix = '';
let myLanIp = '127.0.0.1';
for (const k in interfaces) {
    for (const iface of interfaces[k]) {
        if (iface.family === 'IPv4' && !iface.internal) {
            myLanIp = iface.address;
            const segs = iface.address.split('.');
            localIpSuffix = segs[segs.length - 1];
            break;
        }
    }
    if (localIpSuffix) break;
}
const pcId = localIpSuffix ? `${baseHostname}_${localIpSuffix}` : baseHostname;

let ipConfigFile = path.join(__dirname, 'server_ip.txt');
if (!fs.existsSync(ipConfigFile) && fs.existsSync(path.join(__dirname, '..', 'server_ip.txt'))) {
    ipConfigFile = path.join(__dirname, '..', 'server_ip.txt');
}
let savedIp = '172.30.1.90';
if (fs.existsSync(ipConfigFile)) {
    try {
        const content = fs.readFileSync(ipConfigFile, 'utf8').trim();
        if (content) savedIp = content;
    } catch(e) {}
} else {
    try { fs.writeFileSync(ipConfigFile, savedIp, 'utf8'); } catch(e) {}
}

let input = savedIp;
let isHttps = false;
let targetHost = '172.30.1.90';
let targetPort = 8080;

if (input.startsWith('https://')) {
    isHttps = true;
    try {
        const u = new URL(input);
        targetHost = u.hostname;
        targetPort = u.port ? parseInt(u.port) : 443;
    } catch(e) { targetHost = input.replace(/^https?:\/\//i, ''); }
} else if (input.startsWith('http://')) {
    try {
        const u = new URL(input);
        targetHost = u.hostname;
        targetPort = u.port ? parseInt(u.port) : 8080;
    } catch(e) { targetHost = input.replace(/^https?:\/\//i, ''); }
} else {
    const parts = input.split(':');
    targetHost = parts[0].trim();
    if (parts.length > 1) {
        targetPort = parseInt(parts[1].trim()) || 8080;
    }
}

const netModule = isHttps ? https : http;
const httpAgent = new netModule.Agent({ keepAlive: true, maxSockets: 20 });

console.log('==================================================');
console.log(`  🏢 다연코퍼레이션 클라이언트 [PC: ${pcId}]`);
console.log(`  서버 대상: ${isHttps ? 'https' : 'http'}://${targetHost}:${targetPort}`);
console.log('==================================================\n');

    let inputCtrlProcess = null;
    let latestRemoteClipboardB64 = '';

    function ensureInputCtrlDaemon() {
        if (!inputCtrlProcess || inputCtrlProcess.killed) {
            const inputCtrlPath = path.join(__dirname, 'input_ctrl.exe');
            if (fs.existsSync(inputCtrlPath)) {
                try {
                    inputCtrlProcess = spawn(inputCtrlPath, ['daemon'], { stdio: ['pipe', 'pipe', 'ignore'] });

                    inputCtrlProcess.stdout.on('data', chunk => {
                        const str = chunk.toString('utf8');
                        const lines = str.split('\n');
                        for (let l of lines) {
                            l = l.trim();
                            if (l.startsWith('CLIPBOARD_SYNC:')) {
                                const b64 = l.substring(15).trim();
                                if (b64 && b64 !== latestRemoteClipboardB64) {
                                    latestRemoteClipboardB64 = b64;
                                }
                            }
                        }
                    });

                    inputCtrlProcess.on('error', () => { inputCtrlProcess = null; });
                    inputCtrlProcess.on('exit', () => { inputCtrlProcess = null; });
                } catch(e) {
                    inputCtrlProcess = null;
                }
            }
        }
    }

    let fastcapDaemon = null;
    let latestCapturedFrame = null;
    let fastcapMonitor = '0';
    let fastcapRawBuf = Buffer.alloc(0);

    let streamUploadReq = null;
    let streamReconnectTimer = null;

    function scheduleStreamReconnect() {
        if (streamReconnectTimer) return;
        streamReconnectTimer = setTimeout(() => {
            streamReconnectTimer = null;
            connectStreamUpload();
        }, 500);
    }

    function connectStreamUpload() {
        if (streamUploadReq && !streamUploadReq.destroyed && !streamUploadReq.writableEnded) return;
        try {
            if (streamUploadReq) {
                try { streamUploadReq.destroy(); } catch(e) {}
                streamUploadReq = null;
            }

            const req = netModule.request({
                hostname: targetHost,
                port: targetPort,
                path: `/api/agent/stream_upload?id=${encodeURIComponent(pcId)}`,
                method: 'POST',
                headers: {
                    'Content-Type': 'application/octet-stream',
                    'Transfer-Encoding': 'chunked',
                    'Connection': 'keep-alive'
                }
            });

            req.on('socket', (socket) => {
                socket.setNoDelay(true);
                socket.on('error', () => {});
            });

            req.on('error', () => {
                if (streamUploadReq === req) streamUploadReq = null;
                scheduleStreamReconnect();
            });

            req.on('close', () => {
                if (streamUploadReq === req) streamUploadReq = null;
                scheduleStreamReconnect();
            });

            streamUploadReq = req;
        } catch(e) {
            streamUploadReq = null;
            scheduleStreamReconnect();
        }
    }
    connectStreamUpload();

    let isCurrentZoomFocused = false;

    function applyStreamFocusState(focused) {
        if (isCurrentZoomFocused === focused) return;
        isCurrentZoomFocused = focused;
        if (fastcapDaemon && fastcapDaemon.stdin && !fastcapDaemon.stdin.destroyed) {
            try {
                if (isCurrentZoomFocused) {
                    fastcapDaemon.stdin.write('fps 60\nquality 90\n');
                } else {
                    fastcapDaemon.stdin.write('fps 3\nquality 60\n');
                }
            } catch(e) {}
        }
    }

    function ensureFastcapDaemon() {
        if (!fastcapDaemon || fastcapDaemon.killed) {
            const fastcapPath = path.join(__dirname, 'fastcap.exe');
            if (fs.existsSync(fastcapPath)) {
                try {
                    fastcapDaemon = spawn(fastcapPath, ['daemon', targetMonitor || '0'], { stdio: ['pipe', 'pipe', 'ignore'] });
                    fastcapMonitor = targetMonitor || '0';

                    // 초기 상태 적용 (포커스 여부에 따른 지능형 대역폭 제어: 줌 시 60 FPS, 대기 시 3 FPS)
                    if (isCurrentZoomFocused) {
                        try { fastcapDaemon.stdin.write('fps 60\nquality 90\n'); } catch(e) {}
                    } else {
                        try { fastcapDaemon.stdin.write('fps 3\nquality 60\n'); } catch(e) {}
                    }

                    fastcapDaemon.stdout.on('data', (chunk) => {
                        if (streamUploadReq && !streamUploadReq.destroyed && !streamUploadReq.writableEnded) {
                            try {
                                streamUploadReq.write(chunk);
                            } catch(e) {
                                streamUploadReq = null;
                                scheduleStreamReconnect();
                            }
                        }

                        // ⚡ 최신 캡처 프레임을 버퍼에 실시간 파싱하여 LAN 직통 서버에 즉시 공급
                        fastcapRawBuf = Buffer.concat([fastcapRawBuf, chunk]);
                        while (fastcapRawBuf.length >= 12) {
                            if (fastcapRawBuf[0] === 0x53 && fastcapRawBuf[1] === 0x43 && fastcapRawBuf[2] === 0x41 && fastcapRawBuf[3] === 0x50) {
                                const frameLen = fastcapRawBuf.readUInt32LE(8);
                                if (fastcapRawBuf.length >= 12 + frameLen) {
                                    latestCapturedFrame = fastcapRawBuf.slice(12, 12 + frameLen);
                                    fastcapRawBuf = fastcapRawBuf.slice(12 + frameLen);
                                } else {
                                    break;
                                }
                            } else {
                                fastcapRawBuf = fastcapRawBuf.slice(1);
                            }
                        }
                    });

                    fastcapDaemon.on('error', () => { fastcapDaemon = null; });
                    fastcapDaemon.on('exit', () => { fastcapDaemon = null; });
                } catch(e) {
                    fastcapDaemon = null;
                }
            }
        }
    }

    // ⚡ 사내 초고속 직통 LAN 서버 (0.1ms 무지연 캡처 및 즉각 제어)
    try {
        const lanServer = http.createServer((lReq, lRes) => {
            const lUrl = new URL(lReq.url, 'http://127.0.0.1:8001');
            lRes.setHeader('Access-Control-Allow-Origin', '*');
            lRes.setHeader('Cache-Control', 'no-cache, no-store, must-revalidate');

            if (lUrl.pathname === '/api/snapshot') {
                const mon = lUrl.searchParams.get('monitor') || '0';
                if (latestCapturedFrame && latestCapturedFrame.length > 100) {
                    lRes.writeHead(200, { 'Content-Type': 'image/jpeg', 'Content-Length': latestCapturedFrame.length });
                    lRes.end(latestCapturedFrame);
                } else {
                    captureScreen(mon, (imgBuf) => {
                        if (imgBuf) {
                            const buf = Buffer.isBuffer(imgBuf) ? imgBuf : Buffer.from(imgBuf, 'base64');
                            lRes.writeHead(200, { 'Content-Type': 'image/jpeg', 'Content-Length': buf.length });
                            lRes.end(buf);
                        } else {
                            lRes.writeHead(503);
                            lRes.end('No frame');
                        }
                    });
                }
                return;
            }

            if (lUrl.pathname === '/api/control') {
                const type = lUrl.searchParams.get('type');
                const relX = lUrl.searchParams.get('relX') || '0';
                const relY = lUrl.searchParams.get('relY') || '0';
                const key = lUrl.searchParams.get('key') || '';
                const monitorIdx = lUrl.searchParams.get('monitor') || '0';
                const msg = lUrl.searchParams.get('msg') || key || '';
                const delta = lUrl.searchParams.get('delta') || '-120';
                
                executeControlNative(type, relX, relY, key, monitorIdx, msg, delta);
                lRes.writeHead(200, { 'Content-Type': 'application/json' });
                lRes.end(JSON.stringify({ status: 'ok' }));
                return;
            }

            lRes.writeHead(404);
            lRes.end();
        });

        lanServer.on('error', () => {});
        lanServer.listen(8001, '0.0.0.0', () => {});
    } catch(e) {}

    ensureFastcapDaemon();

    function captureScreen(monitorIdx, callback) {
        ensureFastcapDaemon();
        if (fastcapDaemon && fastcapDaemon.stdin && !fastcapDaemon.stdin.destroyed) {
            if (fastcapMonitor !== monitorIdx.toString()) {
                fastcapMonitor = monitorIdx.toString();
                try { fastcapDaemon.stdin.write(`monitor ${fastcapMonitor}\n`); } catch(e) {}
            }
            if (latestCapturedFrame) {
                callback(latestCapturedFrame);
                return;
            }
        }

        const mKey = (monitorIdx !== undefined && monitorIdx !== null) ? monitorIdx.toString() : '0';
        const fastcapPath = path.join(__dirname, 'fastcap.exe');
        if (fs.existsSync(fastcapPath)) {
            try {
                execFile(fastcapPath, [mKey], { maxBuffer: 1024 * 1024 * 30 }, (err, stdout) => {
                    if (!err && stdout && stdout.length > 100) {
                        callback(stdout.trim());
                        return;
                    }
                    callback(null);
                });
            } catch (e) {
                callback(null);
            }
        } else {
            callback(null);
        }
    }

    function showNoticePopup(msg) {
        if (!msg) return;

        const inputCtrlPath = path.join(__dirname, 'input_ctrl.exe');
        if (fs.existsSync(inputCtrlPath)) {
            try {
                spawn(inputCtrlPath, ['popup', msg], { detached: true, stdio: 'ignore' }).unref();
                return;
            } catch(e) {}
        }

        // input_ctrl.exe가 없을 때만 안전용 Fallback
        const cleanMsg = msg.replace(/"/g, '""');
        const vbs = `MsgBox "${cleanMsg}", 4096 + 64, "🏢 다연코퍼레이션 관리자 공지"`;
        const tmp = path.join(os.tmpdir(), `dayeon_msg_${Date.now()}.vbs`);
        try {
            fs.writeFileSync(tmp, '\ufeff' + vbs, 'utf16le');
            execFile('wscript.exe', [tmp], () => {
                try { fs.unlinkSync(tmp); } catch(e) {}
            });
        } catch(e) {}
    }

    let isUpdating = false;

    function checkAndApplyUpdate(force = false) {
        if (isUpdating) return;
        const req = netModule.request({
            hostname: targetHost,
            port: targetPort,
            path: '/api/version',
            method: 'GET',
            agent: httpAgent,
            timeout: 3000
        }, (res) => {
            let body = '';
            res.on('data', chunk => body += chunk);
            res.on('end', () => {
                try {
                    const serverVer = JSON.parse(body);
                    const localVerFile = path.join(__dirname, 'version.json');
                    let localVer = { version: 0, updatedAt: 0 };
                    if (fs.existsSync(localVerFile)) {
                        try { localVer = JSON.parse(fs.readFileSync(localVerFile, 'utf8')); } catch(e) {}
                    }

                    if (force || (serverVer.version && serverVer.version > (localVer.version || 0))) {
                        console.log(`[🚀 자동 업데이트 실행] v${localVer.version} -> v${serverVer.version} (force: ${force})`);
                        performUpdate(serverVer);
                    }
                } catch(e) {}
            });
        });
        req.on('error', () => {});
        req.end();
    }

    function downloadFileWithProgress(fileName, destDir, onProgress) {
        return new Promise((resolve, reject) => {
            const tempPath = path.join(destDir, fileName);
            const fileStream = fs.createWriteStream(tempPath);
            const req = netModule.request({
                hostname: targetHost,
                port: targetPort,
                path: `/api/update/file?name=${encodeURIComponent(fileName)}`,
                method: 'GET',
                agent: httpAgent,
                timeout: 15000
            }, (res) => {
                if (res.statusCode !== 200) {
                    fileStream.close();
                    try { fs.unlinkSync(tempPath); } catch(e) {}
                    return reject(new Error('Download failed: ' + res.statusCode));
                }
                const totalBytes = parseInt(res.headers['content-length'] || '0', 10);
                let curBytes = 0;
                res.on('data', (chunk) => {
                    curBytes += chunk.length;
                    if (onProgress) onProgress(chunk.length, curBytes, totalBytes);
                });
                res.pipe(fileStream);
                fileStream.on('finish', () => {
                    fileStream.close(() => resolve(tempPath));
                });
            });
            req.on('error', (err) => {
                fileStream.close();
                try { fs.unlinkSync(tempPath); } catch(e) {}
                reject(err);
            });
            req.end();
        });
    }

    async function performUpdate(serverVer) {
        if (isUpdating) return;
        isUpdating = true;

        let updateWidgetProc = null;
        const inputCtrlPath = path.join(__dirname, 'input_ctrl.exe');
        const verStr = serverVer.version ? `v${serverVer.version}` : '';
        if (fs.existsSync(inputCtrlPath)) {
            try {
                updateWidgetProc = spawn(inputCtrlPath, ['update_widget_daemon', verStr], {
                    stdio: ['pipe', 'ignore', 'ignore']
                });
            } catch(e) {}
        }

        function setWidgetProgress(percent, msg) {
            try {
                fs.writeFileSync(path.join(__dirname, 'update_status.txt'), `${percent} ${msg}`, 'utf8');
            } catch(e) {}
            if (updateWidgetProc && updateWidgetProc.stdin && !updateWidgetProc.stdin.destroyed) {
                try {
                    updateWidgetProc.stdin.write(`progress ${percent} ${msg}\n`);
                } catch(e) {}
            }
        }

        try {
            setWidgetProgress(5, `최신 서버 연결 및 다운로드 준비... [5%]`);
            console.log('🚀 서버에서 최신 파일 초고속 병렬 다운로드 시작...');
            const updateTempDir = path.join(__dirname, 'update_temp');
            if (!fs.existsSync(updateTempDir)) fs.mkdirSync(updateTempDir, { recursive: true });

            const files = serverVer.files || ['agent.js', 'input_ctrl.exe', 'fastcap.exe', 'audiocap.exe', '다연코퍼레이션.exe', 'version.json'];
            let completedCount = 0;
            const totalFiles = files.length;

            await Promise.all(files.map(async (f) => {
                await downloadFileWithProgress(f, updateTempDir, (chunkBytes, curBytes, totalBytes) => {
                    // 실시간 청크 다운로드
                });
                completedCount++;
                const pct = Math.round(10 + (completedCount / totalFiles) * 60); // 10% ~ 70%
                setWidgetProgress(pct, `최신 모듈 다운로드 중... (${completedCount}/${totalFiles}) [${pct}%]`);
            }));

            setWidgetProgress(75, `무결성 검증 및 버전 동기화... [75%]`);
            // 🌟 1. version.json을 현재 디렉토리에 즉시 영구 기록하여 무한 업데이트 루프 원천 차단
            try {
                fs.writeFileSync(path.join(__dirname, 'version.json'), JSON.stringify(serverVer, null, 2), 'utf8');
                fs.writeFileSync(path.join(updateTempDir, 'version.json'), JSON.stringify(serverVer, null, 2), 'utf8');
            } catch(e) {}

            await new Promise(r => setTimeout(r, 200));
            setWidgetProgress(85, `시스템 모듈 교체 및 안전 스왑 진행 중... [85%]`);

            // 3. updater.bat 생성 (실행 중인 파일 잠금 충돌 완벽 방지 및 트레이 앱 자동 재기동)
            const updaterBat = path.join(__dirname, 'updater.bat');
            const batContent = [
                '@echo off',
                'chcp 65001 >nul',
                'cd /d "%~dp0"',
                'timeout /t 1 /nobreak >nul',
                'taskkill /F /IM fastcap.exe /IM audiocap.exe /IM input_ctrl.exe >nul 2>&1',
                'timeout /t 1 /nobreak >nul',
                'if exist "update_temp\\다연코퍼레이션.exe" (',
                '    if exist "..\\다연코퍼레이션.exe.old" del /F /Q "..\\다연코퍼레이션.exe.old" >nul 2>&1',
                '    ren "..\\다연코퍼레이션.exe" "다연코퍼레이션.exe.old" >nul 2>&1',
                '    copy /Y "update_temp\\다연코퍼레이션.exe" "..\\다연코퍼레이션.exe" >nul 2>&1',
                ')',
                'if exist "update_temp\\다연코퍼레이션 관리자.exe" (',
                '    if exist "..\\다연코퍼레이션 관리자.exe.old" del /F /Q "..\\다연코퍼레이션 관리자.exe.old" >nul 2>&1',
                '    ren "..\\다연코퍼레이션 관리자.exe" "다연코퍼레이션 관리자.exe.old" >nul 2>&1',
                '    copy /Y "update_temp\\다연코퍼레이션 관리자.exe" "..\\다연코퍼레이션 관리자.exe" >nul 2>&1',
                ')',
                'xcopy /Y /Q /E "update_temp\\*" ".\\" >nul 2>&1',
                'rmdir /S /Q "update_temp" >nul 2>&1',
                'timeout /t 1 /nobreak >nul',
                'del "%~f0" >nul 2>&1'
            ].join('\r\n');
            fs.writeFileSync(updaterBat, batContent, 'utf8');

            setWidgetProgress(95, `새 버전 엔진 재시작 준비 완료! [95%]`);
            await new Promise(r => setTimeout(r, 200));

            setWidgetProgress(100, `✅ 업데이트 완료! 정상 가동됩니다. [100%]`);
            await new Promise(r => setTimeout(r, 400));

            try { if (fastcapDaemon) fastcapDaemon.kill(); } catch(e) {}

            // updater.bat를 완전히 분리된 백그라운드 프로세스로 실행
            execFile('cmd.exe', ['/c', 'updater.bat'], {
                detached: true,
                stdio: 'ignore',
                cwd: __dirname
            }).unref();

            setTimeout(() => {
                process.exit(0);
            }, 300);
        } catch(e) {
            isUpdating = false;
        }
    }

    let drawingOverlayProc = null;
    function ensureDrawingOverlay() {
        if (drawingOverlayProc && !drawingOverlayProc.killed) return;
        const inputCtrlPath = path.join(__dirname, 'input_ctrl.exe');
        if (fs.existsSync(inputCtrlPath)) {
            try {
                drawingOverlayProc = spawn(inputCtrlPath, ['draw_overlay'], {
                    stdio: ['pipe', 'ignore', 'ignore']
                });
                drawingOverlayProc.on('error', () => { drawingOverlayProc = null; });
                drawingOverlayProc.on('exit', () => { drawingOverlayProc = null; });
            } catch(e) {
                drawingOverlayProc = null;
            }
        }
    }

    function executeControlNative(type, relX, relY, key, monitorIdx, msg, delta) {
        if (type === 'exit' || type === 'kill_agent') {
            try { if (inputCtrlProcess) inputCtrlProcess.kill(); } catch(e) {}
            try { if (fastcapDaemon) fastcapDaemon.kill(); } catch(e) {}
            setTimeout(() => { process.exit(0); }, 200);
            return;
        }

        if (type === 'select_monitor' || type === 'monitor') {
            const targetM = (monitorIdx !== undefined && monitorIdx !== null ? monitorIdx : (relX !== undefined ? relX : (key || msg || '0'))).toString();
            targetMonitor = targetM;
            if (fastcapDaemon && fastcapDaemon.stdin && !fastcapDaemon.stdin.destroyed) {
                fastcapMonitor = targetM;
                try { fastcapDaemon.stdin.write(`monitor ${fastcapMonitor}\n`); } catch(e) {}
            }
            return;
        }

        if (type === 'close_update_widget') {
            try { execSync('taskkill /F /FI "WINDOWTITLE eq *시스템 실시간 업데이트*" >nul 2>&1'); } catch(e) {}
            return;
        }

        if (type === 'focus_change') {
            applyStreamFocusState(!!(key === 'true' || key === true || msg === 'true' || delta === 1 || relX === 1 || relX === 'true'));
            return;
        }

        if (type === 'change_server_url' && (msg || key || relX)) {
            const newUrl = (msg || key || relX).toString().trim();
            if (newUrl) {
                try { fs.writeFileSync(ipConfigFile, newUrl, 'utf8'); } catch(e) {}
                try { if (inputCtrlProcess) inputCtrlProcess.kill(); } catch(e) {}
                try { if (fastcapDaemon) fastcapDaemon.kill(); } catch(e) {}
                setTimeout(() => { process.exit(0); }, 300);
            }
            return;
        }

        if (type === 'draw_stamp') {
            ensureDrawingOverlay();
            const emoji = key || '🐱';
            const stampSize = relX || '56';
            const mIdx = (monitorIdx !== undefined && monitorIdx !== null) ? monitorIdx.toString() : '0';
            const coords = (typeof msg === 'string' && msg.includes(',')) ? msg.split(',') : ['0.5', '0.5'];
            const rx = coords[0] || '0.5';
            const ry = coords[1] || '0.5';
            const line = `stamp ${emoji} ${stampSize} ${mIdx} ${rx} ${ry}`;
            if (drawingOverlayProc && drawingOverlayProc.stdin && !drawingOverlayProc.stdin.destroyed) {
                try { drawingOverlayProc.stdin.write(line + '\n'); } catch(e) {}
            }
            return;
        }

        if (type === 'draw_stroke' || type === 'draw_update') {
            ensureDrawingOverlay();
            const hexColor = key || '#ef4444';
            const strokeSize = relX || '6';
            const mIdx = (monitorIdx !== undefined && monitorIdx !== null) ? monitorIdx.toString() : '0';
            const pts = msg || '';
            const cmd = (type === 'draw_update') ? 'update' : 'stroke';
            const line = `${cmd} ${hexColor} ${strokeSize} ${mIdx} ${pts}`;
            if (drawingOverlayProc && drawingOverlayProc.stdin && !drawingOverlayProc.stdin.destroyed) {
                try { drawingOverlayProc.stdin.write(line + '\n'); } catch(e) {}
            }
            return;
        }

        if (type === 'draw_clear') {
            if (drawingOverlayProc && drawingOverlayProc.stdin && !drawingOverlayProc.stdin.destroyed) {
                try { drawingOverlayProc.stdin.write('clear\n'); } catch(e) {}
            }
            return;
        }

        if (type === 'auto_update' || type === 'update') {
            checkAndApplyUpdate(true);
            return;
        }

        if (type === 'reboot') {
            showNoticeToast('[다연코퍼레이션] 1초 후 PC가 재부팅됩니다...', 2500);
            setTimeout(() => {
                try { execSync('shutdown /r /t 0 /f'); } catch(e) {}
            }, 1000);
            return;
        }

        if (type === 'shutdown') {
            showNoticeToast('[다연코퍼레이션] 1초 후 PC 전원이 종료됩니다...', 2500);
            setTimeout(() => {
                try { execSync('shutdown /s /t 0 /f'); } catch(e) {}
            }, 1000);
            return;
        }

        if (type === 'kill_hung_tasks') {
            try {
                execSync('taskkill /F /FI "STATUS eq NOT RESPONDING" >nul 2>&1');
                showNoticeToast('[다연코퍼레이션] 멈춘 프로그램 강제 종료 정리 완료!', 3000);
            } catch(e) {}
            return;
        }

        if (type === 'download_file') {
            const fileName = (msg || key || '').toString();
            if (!fileName) return;
            const destPath = path.join(os.homedir(), 'Desktop', path.basename(fileName));
            const req = netModule.request({
                hostname: targetHost,
                port: targetPort,
                path: `/api/download_file?name=${encodeURIComponent(fileName)}`,
                method: 'GET',
                agent: httpAgent
            }, (res) => {
                if (res.statusCode === 200) {
                    const stream = fs.createWriteStream(destPath);
                    res.pipe(stream);
                    stream.on('finish', () => {
                        stream.close();
                        showNoticeToast(`📥 파일 수신 완료 (바탕화면): ${fileName}`, 4000);
                    });
                }
            });
            req.on('error', () => {});
            req.end();
            return;
        }

        if (type === 'popup') {
            showNoticePopup(msg || key || '');
            return;
        }

        ensureInputCtrlDaemon();
        if (inputCtrlProcess && inputCtrlProcess.stdin && !inputCtrlProcess.stdin.destroyed) {
            try {
                if (type === 'wheel' || type === 'scroll') {
                    inputCtrlProcess.stdin.write(`wheel ${relX || 0} ${relY || 0} ${monitorIdx || 0} ${delta || '-120'}\n`);
                } else if (type === 'paste_text') {
                    const rawText = (msg || key || '').toString();
                    const b64 = Buffer.from(rawText, 'utf8').toString('base64');
                    inputCtrlProcess.stdin.write(`paste_b64 ${b64}\n`);
                } else if (type === 'hotkey') {
                    inputCtrlProcess.stdin.write(`hotkey ${(key || msg || '').toString()}\n`);
                } else if (type === 'popup') {
                    inputCtrlProcess.stdin.write(`popup ${(msg || key || '관리자 공지사항이 도착했습니다.').toString()}\n`);
                } else if (type === 'keydown') {
                    inputCtrlProcess.stdin.write(`keydown ${key || 'Space'}\n`);
                } else {
                    inputCtrlProcess.stdin.write(`${type} ${relX || 0} ${relY || 0} ${monitorIdx || 0}\n`);
                }
                return;
            } catch (e) {
                inputCtrlProcess = null;
            }
        }

        const inputCtrlPath = path.join(__dirname, 'input_ctrl.exe');
        if (fs.existsSync(inputCtrlPath)) {
            let args = [type];
            if (type === 'move' || type === 'click' || type === 'rightclick' ||
                type === 'mousedown' || type === 'mouseup' || type === 'mousemove' ||
                type === 'dblclick') {
                args.push((relX || 0).toString(), (relY || 0).toString(), (monitorIdx || '0').toString());
            } else if (type === 'wheel' || type === 'scroll') {
                args.push((relX || 0).toString(), (relY || 0).toString(), (monitorIdx || '0').toString(), (delta || '-120').toString());
            } else if (type === 'keydown' || type === 'hotkey') {
                args.push(key || '');
            } else if (type === 'paste_text') {
                const b64 = Buffer.from((msg || key || '').toString(), 'utf8').toString('base64');
                args = ['paste_b64', b64];
            } else if (type === 'popup') {
                args.push(msg || key || '');
            }
            try {
                execFile(inputCtrlPath, args, () => {});
            } catch (e) {}
        }
    }

    function processCommands(commands) {
        if (!commands || !Array.isArray(commands)) return;
        for (const cmd of commands) {
            if (cmd && cmd.type) {
                executeControlNative(cmd.type, cmd.relX, cmd.relY, cmd.key, cmd.monitorIdx || cmd.monitor || '0', cmd.msg, cmd.delta);
            }
        }
    }

    let isReporting = false;
    let isPollingCmds = false;
    let targetMonitor = '0';
    let lastLogTime = 0;

    // 1. 상태 보고 및 하트비트 루프
    function sendReportLoop() {
        if (isReporting) return;
        isReporting = true;

        ensureFastcapDaemon();
        const currentMon = targetMonitor;
        if (fastcapDaemon && fastcapDaemon.stdin && !fastcapDaemon.stdin.destroyed) {
            if (fastcapMonitor !== currentMon) {
                fastcapMonitor = currentMon;
                try { fastcapDaemon.stdin.write(`monitor ${fastcapMonitor}\n`); } catch(e) {}
            }
        }

        const payload = JSON.stringify({
            id: pcId,
            name: pcId,
            lanIp: myLanIp,
            lanPort: 8001,
            monitor: currentMon,
            isUpdating: isUpdating,
            clipboardB64: latestRemoteClipboardB64
        });

        const req = netModule.request({
            hostname: targetHost,
            port: targetPort,
            path: '/api/agent/report',
            method: 'POST',
            agent: httpAgent,
            headers: {
                'Content-Type': 'application/json',
                'Content-Length': Buffer.byteLength(payload)
            },
            timeout: 3000
        }, (res) => {
            let body = '';
            res.on('data', chunk => body += chunk);
            res.on('end', () => {
                isReporting = false;
                const now = Date.now();
                if (now - lastLogTime > 10000) {
                    lastLogTime = now;
                    console.log(`[${new Date().toLocaleTimeString()}] 🟢 관리자 서버와 정상 통신 중 (PC: ${pcId})`);
                }
                try {
                    const data = JSON.parse(body);
                    if (data.isFocused !== undefined) {
                        applyStreamFocusState(!!data.isFocused);
                    }
                    if (data.requestedMonitor !== undefined && data.requestedMonitor !== null) {
                        targetMonitor = data.requestedMonitor.toString();
                    }
                    if (data.commands && Array.isArray(data.commands) && data.commands.length > 0) {
                        processCommands(data.commands);
                    }
                } catch(e) {}
            });
        });

        req.on('error', (err) => {
            isReporting = false;
            const now = Date.now();
            if (now - lastLogTime > 10000) {
                lastLogTime = now;
                console.error(`[${new Date().toLocaleTimeString()}] 🔴 서버 연결 대기 중... (${err.message})`);
            }
        });

        req.on('timeout', () => { req.destroy(); isReporting = false; });

        req.write(payload);
        req.end();
    }

    // 2. 초고속 0ms 실시간 직통 제어 명령 수신 루프 (상시 유지 푸시 채널)
    function fastControlLoop() {
        if (isPollingCmds) return;
        isPollingCmds = true;

        const req = netModule.request({
            hostname: targetHost,
            port: targetPort,
            path: `/api/agent/commands?id=${encodeURIComponent(pcId)}`,
            method: 'GET',
            agent: httpAgent,
            timeout: 30000
        }, (res) => {
            if (res.socket) res.socket.setNoDelay(true);
            let body = '';
            res.on('data', chunk => {
                body += chunk;
                try {
                    const data = JSON.parse(body);
                    body = '';
                    if (data.isFocused !== undefined) {
                        applyStreamFocusState(!!data.isFocused);
                    }
                    if (data.requestedMonitor !== undefined && data.requestedMonitor !== null) {
                        const newMon = data.requestedMonitor.toString();
                        if (targetMonitor !== newMon) {
                            targetMonitor = newMon;
                            if (fastcapDaemon && fastcapDaemon.stdin && !fastcapDaemon.stdin.destroyed) {
                                fastcapMonitor = targetMonitor;
                                try { fastcapDaemon.stdin.write(`monitor ${fastcapMonitor}\n`); } catch(e) {}
                            }
                        }
                    }
                    if (data.commands && Array.isArray(data.commands) && data.commands.length > 0) {
                        processCommands(data.commands);
                    }
                } catch(e) {}
            });
            res.on('end', () => {
                isPollingCmds = false;
                setImmediate(fastControlLoop);
            });
        });

        req.on('error', () => {
            isPollingCmds = false;
            setTimeout(fastControlLoop, 100);
        });
        req.on('timeout', () => {
            req.destroy();
            isPollingCmds = false;
            setImmediate(fastControlLoop);
        });
        req.end();
    }

    // 3. 실시간 오디오 루프백 캡처 및 전송 루프
    let audioProc = null;
    let audioReq = null;

    function startAudioStream() {
        const audioCapPath = path.join(__dirname, 'audiocap.exe');
        if (!fs.existsSync(audioCapPath)) return;

        if (audioProc && !audioProc.killed) {
            try { audioProc.kill(); } catch(e) {}
        }

        try {
            audioProc = spawn(audioCapPath, [], { stdio: ['ignore', 'pipe', 'ignore'] });
        } catch(e) {
            setTimeout(startAudioStream, 3000);
            return;
        }

        function connectAudioUpload() {
            if (audioReq && !audioReq.destroyed && !audioReq.writableEnded) return;
            try {
                if (audioReq) {
                    try { audioReq.destroy(); } catch(e) {}
                    audioReq = null;
                }
                audioReq = netModule.request({
                    hostname: targetHost,
                    port: targetPort,
                    path: `/api/agent/audio?id=${encodeURIComponent(pcId)}`,
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/octet-stream',
                        'Transfer-Encoding': 'chunked',
                        'Connection': 'keep-alive'
                    }
                });
                audioReq.on('socket', (sock) => {
                    sock.setNoDelay(true);
                    sock.on('error', () => {});
                });
                audioReq.on('error', () => {
                    audioReq = null;
                    setTimeout(connectAudioUpload, 2000);
                });
                audioReq.on('close', () => {
                    audioReq = null;
                    setTimeout(connectAudioUpload, 2000);
                });
            } catch(e) {
                audioReq = null;
                setTimeout(connectAudioUpload, 2000);
            }
        }
        connectAudioUpload();

        audioProc.stdout.on('data', (chunk) => {
            if (audioReq && !audioReq.destroyed && !audioReq.writableEnded) {
                try {
                    audioReq.write(chunk);
                } catch(e) {
                    audioReq = null;
                    connectAudioUpload();
                }
            } else {
                connectAudioUpload();
            }
        });

        audioProc.on('error', () => {
            audioProc = null;
            setTimeout(startAudioStream, 3000);
        });

        audioProc.on('exit', () => {
            audioProc = null;
            setTimeout(startAudioStream, 3000);
        });
    }

    startAudioStream();

    setInterval(sendReportLoop, 1000);
    fastControlLoop();

    setTimeout(checkAndApplyUpdate, 2000);
    setInterval(checkAndApplyUpdate, 60000);
