/**
 * server.js (네이티브 MJPEG 라이브 비디오 스트리밍 & 실시간 원격 제어 관제 서버)
 */

const http = require('http');
const fs = require('fs');
const path = require('path');
const { exec, execFile } = require('child_process');

const PORT = 8080;
const PUBLIC_DIR = path.join(__dirname, 'public');

const pcSessions = {};
const pendingCommands = {};
const activeViewedMonitor = {}; // PC별 사용자가 보고 있는 활성 모니터
const streamClients = {}; // pcId -> Set of { res, monIdx }
const ONLINE_TIMEOUT_MS = 20000;

function executeControlNative(type, relX, relY, key, monitorIdx) {
    const inputCtrlPath = path.join(__dirname, 'input_ctrl.exe');
    if (fs.existsSync(inputCtrlPath)) {
        let args = [type];
        if (type === 'move' || type === 'click' || type === 'rightclick' ||
            type === 'mousedown' || type === 'mouseup' || type === 'mousemove' ||
            type === 'dblclick') {
            args.push(relX.toString(), relY.toString(), (monitorIdx || '0').toString());
        } else if (type === 'keydown') {
            args.push(key);
        }
        try {
            execFile(inputCtrlPath, args, () => {});
        } catch (e) {}
    }
}

const server = http.createServer((req, res) => {
    const urlObj = new URL(req.url, `http://${req.headers.host}`);
    const pathname = urlObj.pathname;

    // 1. 활성 PC 목록
    if (pathname === '/api/pcs') {
        const now = Date.now();
        const activePcs = Object.values(pcSessions).filter(pc => (now - pc.lastSeen) < ONLINE_TIMEOUT_MS);
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify(activePcs));
        return;
    }

    // 2. MJPEG 네이티브 라이브 비디오 스트림 (0.000% 플리커프리)
    if (pathname === '/api/stream') {
        const targetPc = urlObj.searchParams.get('pc') || (Object.values(pcSessions)[0] ? Object.values(pcSessions)[0].id : 'PC-Agent');
        const monIdx = urlObj.searchParams.get('monitor') || '0';

        activeViewedMonitor[targetPc] = monIdx;

        res.writeHead(200, {
            'Content-Type': 'multipart/x-mixed-replace; boundary=--frame',
            'Cache-Control': 'no-cache, no-store, must-revalidate',
            'Connection': 'close',
            'Pragma': 'no-cache'
        });

        if (!streamClients[targetPc]) {
            streamClients[targetPc] = new Set();
        }

        const client = { res, monIdx };
        streamClients[targetPc].add(client);

        if (pcSessions[targetPc] && pcSessions[targetPc].rawBuffers[monIdx]) {
            const buf = pcSessions[targetPc].rawBuffers[monIdx];
            try {
                res.write(`--frame\r\nContent-Type: image/jpeg\r\nContent-Length: ${buf.length}\r\n\r\n`);
                res.write(buf);
                res.write('\r\n');
            } catch(e) {}
        }

        req.on('close', () => {
            if (streamClients[targetPc]) {
                streamClients[targetPc].delete(client);
            }
        });
        return;
    }

    // 3. 에이전트 리포트 수신 & 실시간 MJPEG 브로드캐스트 & 제어 명령 전달
    if (pathname === '/api/agent/report' && req.method === 'POST') {
        let body = '';
        req.on('data', chunk => body += chunk);
        req.on('end', () => {
            try {
                const data = JSON.parse(body);
                const pcId = data.id || 'PC-Agent';
                const monIdx = (data.monitor !== undefined && data.monitor !== null) ? data.monitor.toString() : '0';
                
                if (!pcSessions[pcId]) {
                    pcSessions[pcId] = {
                        id: pcId,
                        name: data.name || pcId,
                        ip: req.socket.remoteAddress.replace('::ffff:', ''),
                        lastSeen: Date.now(),
                        rawBuffers: { '0': null, '1': null, '2': null },
                        lastGoodBuffer: null
                    };
                }

                pcSessions[pcId].lastSeen = Date.now();

                if (data.image && data.image.length > 100) {
                    const cleanB64 = data.image.replace(/^data:image\/\w+;base64,/, '');
                    const buf = Buffer.from(cleanB64, 'base64');
                    if (buf.length > 1000) {
                        pcSessions[pcId].rawBuffers[monIdx] = buf;
                        pcSessions[pcId].lastGoodBuffer = buf;

                        if (streamClients[pcId]) {
                            for (const client of streamClients[pcId]) {
                                if (client.monIdx === monIdx) {
                                    try {
                                        client.res.write(`--frame\r\nContent-Type: image/jpeg\r\nContent-Length: ${buf.length}\r\n\r\n`);
                                        client.res.write(buf);
                                        client.res.write('\r\n');
                                    } catch(e) {}
                                }
                            }
                        }
                    }
                }

                const cmdsToExecute = pendingCommands[pcId] || [];
                pendingCommands[pcId] = [];

                const targetMon = activeViewedMonitor[pcId] || '0';

                res.writeHead(200, { 'Content-Type': 'application/json' });
                res.end(JSON.stringify({ 
                    status: 'ok', 
                    commands: cmdsToExecute,
                    requestedMonitor: targetMon 
                }));
            } catch (e) {
                res.writeHead(400);
                res.end('Invalid JSON');
            }
        });
        return;
    }

    // 4. 원격 제어 명령 수신 (정밀 타겟 PC 라우팅)
    if (pathname === '/api/control') {
        const rawTargetPc = urlObj.searchParams.get('pc');
        const type = urlObj.searchParams.get('type');
        const relX = urlObj.searchParams.get('relX') || urlObj.searchParams.get('x') || '0';
        const relY = urlObj.searchParams.get('relY') || urlObj.searchParams.get('y') || '0';
        const key = urlObj.searchParams.get('key') || '';
        const monitorIdx = urlObj.searchParams.get('monitor') || '0';

        let targetPcId = null;
        if (rawTargetPc) {
            const cleanTarget = decodeURIComponent(rawTargetPc).trim().toLowerCase();
            for (const pcId of Object.keys(pcSessions)) {
                if (pcId.toLowerCase() === cleanTarget || (pcSessions[pcId].name && pcSessions[pcId].name.toLowerCase() === cleanTarget)) {
                    targetPcId = pcId;
                    break;
                }
            }
        }

        if (!targetPcId && Object.keys(pcSessions).length > 0) {
            targetPcId = Object.keys(pcSessions)[0];
        }

        if (targetPcId) {
            activeViewedMonitor[targetPcId] = monitorIdx;
            const cmd = { type, relX, relY, key, monitorIdx };
            if (!pendingCommands[targetPcId]) pendingCommands[targetPcId] = [];
            pendingCommands[targetPcId].push(cmd);
        } else {
            executeControlNative(type, relX, relY, key, monitorIdx);
        }

        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({ status: 'ok' }));
        return;
    }

    let filePath = path.join(PUBLIC_DIR, pathname === '/' ? 'index.html' : pathname);
    if (!fs.existsSync(filePath)) filePath = path.join(PUBLIC_DIR, 'index.html');

    const ext = path.extname(filePath);
    const mimeTypes = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.css': 'text/css; charset=utf-8' };

    fs.readFile(filePath, (err, data) => {
        if (err) { res.writeHead(404); res.end('Not Found'); return; }
        res.writeHead(200, { 'Content-Type': mimeTypes[ext] || 'text/plain' });
        res.end(data);
    });
});

server.listen(PORT, () => {
    console.log('==================================================');
    console.log('  다연코퍼레이션 서버 가동');
    console.log(`  접속 주소: http://localhost:${PORT}`);
    console.log('==================================================');
});
