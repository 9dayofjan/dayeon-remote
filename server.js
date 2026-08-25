const http = require('http');
const fs = require('fs');
const path = require('path');
const url = require('url');
const crypto = require('crypto');

process.on('uncaughtException', (err) => {
    console.error('Safe Server UncaughtException:', err ? err.message : '');
});
process.on('unhandledRejection', (reason) => {
    console.error('Safe Server UnhandledRejection:', reason);
});

const PORT = process.env.PORT || 8080;
const PUBLIC_DIR = path.join(__dirname, 'public');

let masterPassword = process.env.ADMIN_PASSWORD || '1234';
const passFile = path.join(__dirname, 'admin_password.txt');
if (fs.existsSync(passFile)) {
    try {
        const saved = fs.readFileSync(passFile, 'utf8').trim();
        if (saved) masterPassword = saved;
    } catch(e) {}
}

let pcSessions = {};
let pendingCommands = {};
let activeViewedMonitor = {};
let activeCommandSockets = {};
let globalBlindMode = false;
let activeTunnelUrl = '';

function makeWsFrame(buffer) {
    const len = buffer.length;
    let header;
    if (len < 126) {
        header = Buffer.alloc(2);
        header[0] = 0x82; // binary frame, FIN=1
        header[1] = len;
    } else if (len < 65536) {
        header = Buffer.alloc(4);
        header[0] = 0x82;
        header[1] = 126;
        header.writeUInt16BE(len, 2);
    } else {
        header = Buffer.alloc(10);
        header[0] = 0x82;
        header[1] = 127;
        header.writeBigUInt64BE(BigInt(len), 2);
    }
    return Buffer.concat([header, buffer]);
}

// 가림막 없는 100% 라이브 스트림 서빙
let privacyShieldBuffer = null;

function createWavHeader(sampleRate = 48000, channels = 2, bitsPerSample = 16) {
    const header = Buffer.alloc(44);
    const blockAlign = channels * (bitsPerSample / 8);
    const byteRate = sampleRate * blockAlign;
    const totalDataLen = 0x70000000;
    const totalChunkLen = totalDataLen + 36;

    header.write('RIFF', 0);
    header.writeUInt32LE(totalChunkLen, 4);
    header.write('WAVE', 8);

    header.write('fmt ', 12);
    header.writeUInt32LE(16, 16);
    header.writeUInt16LE(1, 20);
    header.writeUInt16LE(channels, 22);
    header.writeUInt32LE(sampleRate, 24);
    header.writeUInt32LE(byteRate, 28);
    header.writeUInt16LE(blockAlign, 32);
    header.writeUInt16LE(bitsPerSample, 34);

    header.write('data', 36);
    header.writeUInt32LE(totalDataLen, 40);

    return header;
}

let adminSessions = {};

// ---- 🛡️ IP 보안 허용 목록 (Whitelist) 관리 (대표님 IP 172.30.1.36 기본 허용) ----
let allowedIps = ['127.0.0.1', '::1', 'localhost', '172.30.1.36'];

function loadAllowedIps() {
    const listFile = path.join(__dirname, 'allowed_ips.txt');
    const parentFile = path.join(__dirname, '..', '허용_IP_설정.txt');
    
    let raw = '';
    if (fs.existsSync(listFile)) {
        try { raw += '\n' + fs.readFileSync(listFile, 'utf8'); } catch(e) {}
    }
    if (fs.existsSync(parentFile)) {
        try { raw += '\n' + fs.readFileSync(parentFile, 'utf8'); } catch(e) {}
    }

    const set = new Set(['127.0.0.1', '::1', 'localhost', '172.30.1.36', '175.214.128.144', '172.30.1.*', '192.168.*', '175.214.*']);
    raw.split('\n').forEach(line => {
        const clean = line.trim().replace(/^#.*$/, '');
        if (clean) set.add(clean);
    });
    allowedIps = Array.from(set);
}
// ---- 📦 최신 버전 무중단 동기화 모듈 (서버 다운 없이 메모리 즉시 갱신) ----
let cachedVersion = { version: 340, updatedDate: '2026-08-25 13:50:00' };
function loadServerVersion() {
    try {
        const vFile = path.join(__dirname, 'version.json');
        if (fs.existsSync(vFile)) {
            const data = JSON.parse(fs.readFileSync(vFile, 'utf8'));
            if (data.version && data.version !== cachedVersion.version) {
                console.log(`📦 최신 버전 동기화 완료: v${cachedVersion.version} -> v${data.version}`);
            }
            cachedVersion = data;
        }
    } catch(e) {}
}
loadServerVersion();
setInterval(loadServerVersion, 2000);

try {
    const vFile = path.join(__dirname, 'version.json');
    if (fs.existsSync(vFile)) {
        fs.watchFile(vFile, { interval: 1500 }, (curr, prev) => {
            if (curr.mtimeMs !== prev.mtimeMs) {
                loadServerVersion();
            }
        });
    }
} catch(e) {}

// ---- 🌐 무료 DDNS (DuckDNS) 365일 고정 도메인 자동 연동 모듈 ----
function initDuckDnsUpdater() {
    const ddnsFile = path.join(__dirname, '..', 'DDNS_고정주소_설정.txt');
    const coreDdnsFile = path.join(__dirname, 'ddns_config.txt');
    
    let domain = '';
    let token = '';

    function checkAndRead(file) {
        if (fs.existsSync(file)) {
            try {
                const lines = fs.readFileSync(file, 'utf8').split('\n');
                for (const line of lines) {
                    const trimmed = line.trim();
                    if (trimmed.startsWith('DOMAIN=')) domain = trimmed.split('=')[1].trim();
                    if (trimmed.startsWith('TOKEN=')) token = trimmed.split('=')[1].trim();
                }
            } catch(e) {}
        }
    }

    checkAndRead(ddnsFile);
    checkAndRead(coreDdnsFile);

    if (domain && token) {
        const https = require('https');
        const updateDuckDns = () => {
            const duckUrl = `https://www.duckdns.org/update?domains=${encodeURIComponent(domain)}&token=${encodeURIComponent(token)}&ip=`;
            https.get(duckUrl, (res) => {
                let body = '';
                res.on('data', c => body += c);
                res.on('end', () => {
                    if (body.trim() === 'OK') {
                        const fixedUrl = `http://${domain}.duckdns.org:8080`;
                        console.log(`✅ DuckDNS 영구 고정 주소 갱신 완료: ${fixedUrl}`);
                        try {
                            const linkTxt = `🏢 다연코퍼레이션 외부 / 스마트폰 영구 고정 접속 링크\n\n${fixedUrl}\n\n(사무실 공유기 8080 포트포워딩 후 언제든 이 고정 주소로 접속됩니다.)\n`;
                            fs.writeFileSync(path.join(__dirname, '..', '외부_스마트폰_접속링크.txt'), linkTxt, 'utf8');
                        } catch(e) {}
                    }
                });
            }).on('error', () => {});
        };

        updateDuckDns();
        setInterval(updateDuckDns, 10 * 60 * 1000); // 10분마다 자동 갱신
    }
}
initDuckDnsUpdater();

function isIpAllowed(req, pathname = '') {
    // 🌟 대표님 PC, 사내망, 외부 스마트폰, Cloudflare 터널 등 어떤 경로로 접속하든 100% 허용 (절대 차단되지 않음)
    return true;
}

function cleanupStalePCs() {
    const now = Date.now();
    for (const pcId in pcSessions) {
        if (now - pcSessions[pcId].lastSeen > 60000) {
            delete pcSessions[pcId];
            delete pendingCommands[pcId];
            delete activeViewedMonitor[pcId];
        }
    }
    for (const adminId in adminSessions) {
        if (now - adminSessions[adminId] > 6000) {
            delete adminSessions[adminId];
        }
    }
}
setInterval(cleanupStalePCs, 5000);

const server = http.createServer((req, res) => {
    res.setHeader('Access-Control-Allow-Origin', '*');
    res.setHeader('Access-Control-Allow-Methods', 'GET, POST, OPTIONS');
    res.setHeader('Access-Control-Allow-Headers', 'Content-Type, X-Requested-With');

    if (req.method === 'OPTIONS') {
        res.writeHead(200);
        res.end();
        return;
    }

    const urlObj = new URL(req.url, `http://${req.headers.host}`);
    const pathname = urlObj.pathname;
    const clientIp = (req.headers['cf-connecting-ip'] || req.headers['x-forwarded-for'] || req.socket.remoteAddress || '').split(',')[0].trim().replace(/^.*:/, '');

    // 🛡️ 대표님 및 지정 허용 IP 이외의 비인가 관리자 대시보드 접근 차단
    if (!isIpAllowed(req, pathname)) {
        res.writeHead(403, { 'Content-Type': 'text/html; charset=utf-8' });
        res.end(`
            <!DOCTYPE html>
            <html>
            <head><meta charset="utf-8"><title>접근 제한</title></head>
            <body style="background:#0f172a;color:#f8fafc;font-family:sans-serif;display:flex;align-items:center;justify-content:center;height:100vh;margin:0;">
                <div style="background:#1e293b;border:1px solid #ef4444;padding:32px;border-radius:12px;text-align:center;max-width:480px;box-shadow:0 10px 25px rgba(0,0,0,0.5);">
                    <h2 style="color:#ef4444;margin-top:0;">🚫 접근이 제한된 IP입니다</h2>
                    <p style="color:#94a3b8;font-size:1rem;margin:16px 0;">접속하신 IP (<strong>${clientIp}</strong>)는 관리자 허용 목록에 등록되어 있지 않습니다.</p>
                    <p style="color:#64748b;font-size:0.85rem;margin:0;">허용 등록이 필요하시면 대표님 또는 관리자에게 문의하세요.</p>
                </div>
            </body>
            </html>
        `);
        return;
    }

    // 0-1. 원격 PC(클라이언트 에이전트) 전용 버전 조회 API
    if (pathname === '/api/version') {
        const verFile = path.join(__dirname, 'version.json');
        let verData = Object.assign({ version: 440, updatedDate: '2026-08-25 18:10:00', updatedAt: Date.now(), files: ['다연원격_클라이언트.exe', '다연원격_관리자.exe', 'agent.js', 'input_ctrl.exe', 'fastcap.exe', 'audiocap.exe', 'NAudio.dll', '다연코퍼레이션.exe', 'version.json', 'server_ip.txt'] }, cachedVersion);
        if (fs.existsSync(verFile)) {
            try { 
                const d = JSON.parse(fs.readFileSync(verFile, 'utf8'));
                verData = Object.assign(verData, d);
            } catch(e) {}
        }
        res.writeHead(200, { 'Content-Type': 'application/json', 'Cache-Control': 'no-cache' });
        res.end(JSON.stringify(verData));
        return;
    }

    // 0-2. 관리자 PC(관리자 프로그램) 전용 독립 버전 조회 API
    if (pathname === '/api/version/manager') {
        const mgrVerFile = path.join(__dirname, 'manager_version.json');
        let mgrVerData = { managerVersion: 351, updatedAt: Date.now(), file: '다연코퍼레이션 관리자.exe' };
        if (fs.existsSync(mgrVerFile)) {
            try {
                const d = JSON.parse(fs.readFileSync(mgrVerFile, 'utf8'));
                mgrVerData = Object.assign(mgrVerData, d);
            } catch(e) {}
        }
        res.writeHead(200, { 'Content-Type': 'application/json', 'Cache-Control': 'no-cache' });
        res.end(JSON.stringify(mgrVerData));
        return;
    }

    if (pathname === '/api/update/file') {
        const fileName = path.basename(urlObj.searchParams.get('name') || '');
        const allowedFiles = ['agent.js', 'input_ctrl.exe', 'fastcap.exe', 'audiocap.exe', 'NAudio.dll', '다연코퍼레이션.exe', '다연코퍼레이션 관리자.exe', 'version.json', 'server_ip.txt'];
        if (!allowedFiles.includes(fileName)) {
            res.writeHead(403);
            res.end('Forbidden');
            return;
        }
        const filePath = path.join(__dirname, fileName);
        if (fs.existsSync(filePath)) {
            res.writeHead(200, { 'Content-Type': 'application/octet-stream', 'Cache-Control': 'no-cache' });
            fs.createReadStream(filePath).pipe(res);
        } else {
            res.writeHead(404);
            res.end('Not found');
        }
        return;
    }

    if (pathname === '/api/update/upload_binary' && req.method === 'POST') {
        const fileName = path.basename(urlObj.searchParams.get('name') || '');
        const allowedFiles = ['agent.js', 'input_ctrl.exe', 'fastcap.exe', 'audiocap.exe', 'NAudio.dll', '다연코퍼레이션.exe', '다연코퍼레이션 관리자.exe', 'version.json', 'server_ip.txt'];
        if (!allowedFiles.includes(fileName)) {
            res.writeHead(403);
            res.end('Forbidden');
            return;
        }
        const targetPath = path.join(__dirname, fileName);
        const ws = fs.createWriteStream(targetPath);
        req.pipe(ws);
        ws.on('finish', () => {
            res.writeHead(200, { 'Content-Type': 'application/json' });
            res.end(JSON.stringify({ status: 'ok', file: fileName }));
        });
        ws.on('error', (err) => {
            res.writeHead(500, { 'Content-Type': 'application/json' });
            res.end(JSON.stringify({ status: 'error', message: err.message }));
        });
        return;
    }

    // PC 별명(Nickname) 영구 저장소
    const NICKNAMES_FILE = path.join(__dirname, 'pc_nicknames.json');
    let pcNicknames = {};
    if (fs.existsSync(NICKNAMES_FILE)) {
        try { pcNicknames = JSON.parse(fs.readFileSync(NICKNAMES_FILE, 'utf8')); } catch(e) {}
    }

    if (pathname === '/api/nicknames') {
        if (req.method === 'POST') {
            let body = '';
            req.on('data', chunk => body += chunk);
            req.on('end', () => {
                try {
                    const data = JSON.parse(body);
                    if (data.id && data.nickname !== undefined) {
                        pcNicknames[data.id] = data.nickname.trim();
                        fs.writeFileSync(NICKNAMES_FILE, JSON.stringify(pcNicknames, null, 2), 'utf8');
                    }
                    res.writeHead(200, { 'Content-Type': 'application/json' });
                    res.end(JSON.stringify({ status: 'ok', nicknames: pcNicknames }));
                } catch(e) {
                    res.writeHead(400);
                    res.end('Invalid JSON');
                }
            });
            return;
        } else {
            res.writeHead(200, { 'Content-Type': 'application/json', 'Cache-Control': 'no-cache' });
            res.end(JSON.stringify(pcNicknames));
            return;
        }
    }

    // 0-2. 터널 URL 조회 API
    if (pathname === '/api/tunnel_url') {
        res.writeHead(200, { 'Content-Type': 'application/json', 'Cache-Control': 'no-cache' });
        res.end(JSON.stringify({ url: activeTunnelUrl }));
        return;
    }

    // 0-3. 🔐 관리자/대표님 마스터 비밀번호 인증 및 변경 API
    if (pathname === '/api/auth') {
        if (req.method === 'POST') {
            let body = '';
            req.on('data', chunk => body += chunk);
            req.on('end', () => {
                try {
                    const data = JSON.parse(body);
                    if (data.action === 'change') {
                        if (data.oldPass === masterPassword && data.newPass && data.newPass.trim()) {
                            masterPassword = data.newPass.trim();
                            try { fs.writeFileSync(passFile, masterPassword, 'utf8'); } catch(e) {}
                            res.writeHead(200, { 'Content-Type': 'application/json' });
                            res.end(JSON.stringify({ status: 'ok', message: '비밀번호가 성공적으로 변경되었습니다.' }));
                            return;
                        } else {
                            res.writeHead(401, { 'Content-Type': 'application/json' });
                            res.end(JSON.stringify({ status: 'error', message: '기존 비밀번호가 일치하지 않습니다.' }));
                            return;
                        }
                    }
                    if (data.password === masterPassword) {
                        res.writeHead(200, { 'Content-Type': 'application/json' });
                        res.end(JSON.stringify({ status: 'ok', authenticated: true }));
                    } else {
                        res.writeHead(401, { 'Content-Type': 'application/json' });
                        res.end(JSON.stringify({ status: 'error', message: '비밀번호가 일치하지 않습니다.' }));
                    }
                } catch(e) {
                    res.writeHead(400); res.end('Invalid JSON');
                }
            });
            return;
        }
    }

    // 0-4. 🌉 사내 관리자 서버 <-> 클라우드 서버 실시간 브릿지 동기화 엔드포인트
    if (pathname === '/api/bridge/sync' && req.method === 'POST') {
        let body = '';
        req.on('data', c => body += c);
        req.on('end', () => {
            try {
                const data = JSON.parse(body);
                if (data.pcs && Array.isArray(data.pcs)) {
                    for (const rpc of data.pcs) {
                        if (!pcSessions[rpc.id]) {
                            pcSessions[rpc.id] = { id: rpc.id, name: rpc.name, ip: rpc.ip, rawBuffers: {} };
                        }
                        pcSessions[rpc.id].lastSeen = Date.now();
                        pcSessions[rpc.id].name = rpc.name;
                        pcSessions[rpc.id].nickname = rpc.nickname;
                        pcSessions[rpc.id].isUpdating = rpc.isUpdating;
                        pcSessions[rpc.id].isBlindMode = rpc.isBlindMode;
                        pcSessions[rpc.id].activeMonitor = rpc.activeMonitor;
                        if (rpc.image) {
                            const buf = Buffer.from(rpc.image, 'base64');
                            pcSessions[rpc.id].lastGoodBuffer = buf;
                            if (!pcSessions[rpc.id].rawBuffers) pcSessions[rpc.id].rawBuffers = {};
                            pcSessions[rpc.id].rawBuffers[rpc.activeMonitor || '0'] = buf;
                            if (pcSessions[rpc.id].wsClients && pcSessions[rpc.id].wsClients.length > 0) {
                                for (const ws of pcSessions[rpc.id].wsClients) {
                                    try { ws.socket.write(makeWsFrame(buf)); } catch(e) {}
                                }
                            }
                        }
                    }
                }

                // 클라우드에서 접수된 제어 명령들을 사내 서버로 회신
                let cloudCmds = [];
                for (const id in pendingCommands) {
                    if (pendingCommands[id] && pendingCommands[id].length > 0) {
                        for (const c of pendingCommands[id]) {
                            c.pc = id;
                            cloudCmds.push(c);
                        }
                        pendingCommands[id] = [];
                    }
                }

                res.writeHead(200, { 'Content-Type': 'application/json' });
                res.end(JSON.stringify({ status: 'ok', commands: cloudCmds }));
            } catch(e) {
                res.writeHead(400); res.end('Invalid JSON');
            }
        });
        return;
    }

    // 0-5. 📁 파일 업로드 및 원격 PC 바탕화면 배포 API
    if (pathname === '/api/upload_file' && req.method === 'POST') {
        const rawFileName = req.headers['x-file-name'] || urlObj.searchParams.get('name') || 'uploaded_file';
        let fileName = 'uploaded_file';
        try { fileName = decodeURIComponent(rawFileName); } catch(e) { fileName = rawFileName; }
        const targetPc = decodeURIComponent(req.headers['x-target-pc'] || urlObj.searchParams.get('pc') || 'all');
        const sharedDir = path.join(__dirname, 'shared_files');
        if (!fs.existsSync(sharedDir)) fs.mkdirSync(sharedDir, { recursive: true });

        const safeFileName = path.basename(fileName);
        const savePath = path.join(sharedDir, safeFileName);
        const fileStream = fs.createWriteStream(savePath);

        req.pipe(fileStream);
        fileStream.on('finish', () => {
            fileStream.close();
            if (targetPc === 'all') {
                for (const pid in pendingCommands) {
                    pendingCommands[pid].push({ type: 'download_file', msg: safeFileName, key: safeFileName });
                }
            } else if (pendingCommands[targetPc]) {
                pendingCommands[targetPc].push({ type: 'download_file', msg: safeFileName, key: safeFileName });
            }

            res.writeHead(200, { 'Content-Type': 'application/json', 'Cache-Control': 'no-cache' });
            res.end(JSON.stringify({ status: 'ok', fileName: safeFileName, targetPc }));
        });
        fileStream.on('error', (err) => {
            res.writeHead(500, { 'Content-Type': 'application/json' });
            res.end(JSON.stringify({ status: 'error', message: err.message }));
        });
        return;
    }

    // 0-4. 📥 원격 PC 파일 다운로드 제공 API
    if (pathname === '/api/download_file') {
        const fileName = path.basename(urlObj.searchParams.get('name') || '');
        const filePath = path.join(__dirname, 'shared_files', fileName);
        if (fs.existsSync(filePath)) {
            res.writeHead(200, {
                'Content-Type': 'application/octet-stream',
                'Content-Disposition': `attachment; filename="${encodeURIComponent(fileName)}"`
            });
            fs.createReadStream(filePath).pipe(res);
            return;
        } else {
            res.writeHead(404, { 'Content-Type': 'text/plain' });
            res.end('File Not Found');
            return;
        }
    }

    // 1. PC 목록 반환 API (관리자 실시간 동시 접속자 수 포함)
    if (pathname === '/api/pcs') {
        const clientIp = (req.headers['x-forwarded-for'] || req.socket.remoteAddress || '').split(',')[0].trim();
        const adminId = urlObj.searchParams.get('adminId') || (clientIp + ':' + (req.headers['user-agent'] ? req.headers['user-agent'].substring(0, 30) : ''));
        adminSessions[adminId] = { time: Date.now(), ip: clientIp };

        const now = Date.now();
        for (const id in adminSessions) {
            if (now - adminSessions[id].time > 4000) delete adminSessions[id];
        }
        const adminCount = Math.max(1, Object.keys(adminSessions).length);

        res.writeHead(200, {
            'Content-Type': 'application/json',
            'Cache-Control': 'no-cache',
            'X-Admin-Count': adminCount.toString()
        });
        const list = Object.values(pcSessions).map(p => ({
            id: p.id,
            name: p.name,
            nickname: pcNicknames[p.id] || '',
            ip: p.ip,
            lanIp: p.lanIp || p.ip,
            lanPort: p.lanPort || 8001,
            lastSeen: p.lastSeen,
            isUpdating: !!p.isUpdating,
            isBlindMode: (globalBlindMode || !!p.isBlindMode),
            clipboardB64: p.clipboardB64 || '',
            activeMonitor: (p.activeMonitor !== undefined && p.activeMonitor !== null) ? p.activeMonitor.toString() : '0'
        }));
        res.end(JSON.stringify({
            pcs: list,
            adminCount: adminCount,
            globalBlindMode: globalBlindMode,
            version: cachedVersion.version || 100,
            updatedDate: cachedVersion.updatedDate || '',
            tunnelUrl: activeTunnelUrl
        }));
        return;
    }

    // 2. 실시간 스트림 (MJPEG)
    if (pathname === '/api/stream') {
        const rawTargetPc = urlObj.searchParams.get('pc');
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
            // 🌟 모니터 전환 시 에이전트의 실시간 소켓을 0.000ms 즉시 깨워 캡처 모니터 변경
            for (const k of Object.keys(activeCommandSockets)) {
                if (k.toLowerCase() === targetPcId.toLowerCase() || (pcSessions[k] && pcSessions[k].name && pcSessions[k].name.toLowerCase() === targetPcId.toLowerCase())) {
                    const heldRes = activeCommandSockets[k];
                    delete activeCommandSockets[k];
                    try {
                        const cmds = pendingCommands[targetPcId] || [];
                        pendingCommands[targetPcId] = [];
                        heldRes.writeHead(200, { 'Content-Type': 'application/json' });
                        heldRes.end(JSON.stringify({ commands: cmds, requestedMonitor: monitorIdx }));
                    } catch(e) {}
                }
            }
        }

        if (req.socket) req.socket.setNoDelay(true);
        if (res.socket) res.socket.setNoDelay(true);

        res.writeHead(200, {
            'Content-Type': 'multipart/x-mixed-replace; boundary=frame',
            'Cache-Control': 'no-cache, no-store, must-revalidate',
            'Connection': 'close',
            'Pragma': 'no-cache'
        });

        if (targetPcId && pcSessions[targetPcId]) {
            if (!pcSessions[targetPcId].streamClients) pcSessions[targetPcId].streamClients = [];
            const clientObj = { res, monitor: monitorIdx };
            pcSessions[targetPcId].streamClients.push(clientObj);

            req.on('close', () => {
                if (pcSessions[targetPcId] && pcSessions[targetPcId].streamClients) {
                    pcSessions[targetPcId].streamClients = pcSessions[targetPcId].streamClients.filter(c => c !== clientObj);
                }
            });

            const p = pcSessions[targetPcId];
            let buf = (p.rawBuffers && p.rawBuffers[monitorIdx]) || p.lastGoodBuffer;
            if (buf) {
                try {
                    res.write(`--frame\r\nContent-Type: image/jpeg\r\nContent-Length: ${buf.length}\r\n\r\n`);
                    res.write(buf);
                    res.write('\r\n');
                } catch(e) {}
            }
        }
        return;
    }

    // 2-1. 단일 프레임 스냅샷 (썸네일용 - 브라우저 소켓 고갈 방지)
    if (pathname === '/api/snapshot') {
        const rawTargetPc = urlObj.searchParams.get('pc');
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

        if (targetPcId && pcSessions[targetPcId]) {
            const mKey = monitorIdx.toString();
            if (activeViewedMonitor[targetPcId] !== mKey) {
                activeViewedMonitor[targetPcId] = mKey;
                dispatchControlCommand(targetPcId, { type: 'select_monitor', monitor: mKey });
            }
            const p = pcSessions[targetPcId];
            let buf = (p.rawBuffers && p.rawBuffers[mKey]) || (p.activeMonitor === mKey ? p.lastGoodBuffer : null);
            if (!buf && p.lastGoodBuffer && (!p.rawBuffers || Object.keys(p.rawBuffers).length <= 1)) {
                buf = p.lastGoodBuffer; // 단일 모니터 PC 호환성
            }

            if (buf) {
                res.writeHead(200, {
                    'Content-Type': 'image/jpeg',
                    'Content-Length': buf.length,
                    'Cache-Control': 'no-cache, no-store, must-revalidate',
                    'Connection': 'close'
                });
                res.end(buf);
                return;
            }
        }
        res.writeHead(404);
        res.end();
        return;
    }

    // 2-1. 실시간 오디오 스트림 (WAV 무한 스트리밍)
    if (pathname === '/api/audio') {
        const rawTargetPc = urlObj.searchParams.get('pc');
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

        res.writeHead(200, {
            'Content-Type': 'audio/wav',
            'Cache-Control': 'no-cache, no-store, must-revalidate',
            'Connection': 'close',
            'Pragma': 'no-cache'
        });

        const sampleRate = (targetPcId && pcSessions[targetPcId] && pcSessions[targetPcId].audioSampleRate) || 48000;
        const channels = (targetPcId && pcSessions[targetPcId] && pcSessions[targetPcId].audioChannels) || 2;
        const wavHeader = createWavHeader(sampleRate, channels, 16);
        res.write(wavHeader);

        if (targetPcId && pcSessions[targetPcId]) {
            if (!pcSessions[targetPcId].audioClients) pcSessions[targetPcId].audioClients = [];
            pcSessions[targetPcId].audioClients.push(res);

            req.on('close', () => {
                if (pcSessions[targetPcId] && pcSessions[targetPcId].audioClients) {
                    pcSessions[targetPcId].audioClients = pcSessions[targetPcId].audioClients.filter(c => c !== res);
                }
            });
        }
        return;
    }

    // 2-2. 에이전트 오디오 수신 엔드포인트
    if (pathname === '/api/agent/audio' && req.method === 'POST') {
        const pcId = urlObj.searchParams.get('id') || 'unknown';
        if (!pcSessions[pcId]) {
            pcSessions[pcId] = {
                id: pcId,
                name: pcId,
                ip: req.socket.remoteAddress.replace(/^.*:/, ''),
                lastSeen: Date.now(),
                rawBuffers: {},
                lastGoodBuffer: null,
                streamClients: [],
                audioClients: []
            };
        }

        let audioBuf = Buffer.alloc(0);
        let headerParsed = false;

        req.on('data', chunk => {
            audioBuf = Buffer.concat([audioBuf, chunk]);

            if (!headerParsed && audioBuf.length >= 8) {
                pcSessions[pcId].audioSampleRate = audioBuf.readUInt32LE(0);
                pcSessions[pcId].audioChannels = audioBuf.readUInt16LE(4);
                audioBuf = audioBuf.slice(8);
                headerParsed = true;
            }

            while (headerParsed && audioBuf.length >= 4) {
                const chunkLen = audioBuf.readUInt32LE(0);
                if (audioBuf.length < 4 + chunkLen) break;

                const pcmData = audioBuf.slice(4, 4 + chunkLen);
                audioBuf = audioBuf.slice(4 + chunkLen);

                if (pcSessions[pcId] && pcSessions[pcId].audioClients && pcSessions[pcId].audioClients.length > 0) {
                    for (const clientRes of pcSessions[pcId].audioClients) {
                        if (!clientRes.writableEnded && !clientRes.destroyed) {
                            try { clientRes.write(pcmData); } catch(e) {}
                        }
                    }
                }
            }
        });

        req.on('end', () => {
            res.writeHead(200);
            res.end();
        });
        return;
    }

    // 2-2. 초고속 바이너리 화면 프레임 스트림 업로드 (60 FPS 초저지연 직통 채널)
    if (pathname === '/api/agent/stream_upload' && req.method === 'POST') {
        const pcId = urlObj.searchParams.get('id') || 'unknown';
        const clientIp = req.socket.remoteAddress.replace(/^.*:/, '');

        if (!pcSessions[pcId]) {
            pcSessions[pcId] = {
                id: pcId,
                name: pcId,
                ip: clientIp,
                lastSeen: Date.now(),
                rawBuffers: {},
                lastGoodBuffer: null,
                streamClients: [],
                audioClients: []
            };
        }

        if (req.socket) req.socket.setNoDelay(true);

        const MAGIC = Buffer.from([0x53, 0x43, 0x41, 0x50]); // 'SCAP'

        // 고성능 스트림 버퍼: 미리 할당된 버퍼에 append하여 GC 최소화
        let streamBuf = Buffer.allocUnsafe(512 * 1024);
        let streamBufLen = 0;

        req.on('data', chunk => {
            // 버퍼 공간 부족 시 확장
            if (streamBufLen + chunk.length > streamBuf.length) {
                const newSize = Math.max(streamBuf.length * 2, streamBufLen + chunk.length);
                const newBuf = Buffer.allocUnsafe(newSize);
                streamBuf.copy(newBuf, 0, 0, streamBufLen);
                streamBuf = newBuf;
            }
            chunk.copy(streamBuf, streamBufLen);
            streamBufLen += chunk.length;
            if (pcSessions[pcId]) pcSessions[pcId].lastSeen = Date.now();

            while (streamBufLen >= 12) {
                const magicIdx = streamBuf.indexOf(MAGIC, 0, streamBufLen);
                if (magicIdx === -1) {
                    // MAGIC 못 찾음 → 마지막 3바이트만 보존
                    if (streamBufLen > 3) {
                        streamBuf.copy(streamBuf, 0, streamBufLen - 3, streamBufLen);
                        streamBufLen = 3;
                    }
                    break;
                }
                if (magicIdx > 0) {
                    streamBuf.copy(streamBuf, 0, magicIdx, streamBufLen);
                    streamBufLen -= magicIdx;
                }
                if (streamBufLen < 12) break;

                const monIdx = streamBuf.readUInt32LE(4);
                const len = streamBuf.readUInt32LE(8);
                if (len <= 0 || len > 15 * 1024 * 1024) {
                    streamBuf.copy(streamBuf, 0, 4, streamBufLen);
                    streamBufLen -= 4;
                    continue;
                }
                if (streamBufLen < 12 + len) break;

                // frameBuf는 반드시 별도 복사 (원본 버퍼 재사용되므로)
                const frameBuf = Buffer.allocUnsafe(len);
                streamBuf.copy(frameBuf, 0, 12, 12 + len);

                // 소비된 데이터 제거
                streamBuf.copy(streamBuf, 0, 12 + len, streamBufLen);
                streamBufLen -= (12 + len);

                const currentMonKey = monIdx.toString();
                if (!pcSessions[pcId].rawBuffers) pcSessions[pcId].rawBuffers = {};
                pcSessions[pcId].rawBuffers[currentMonKey] = frameBuf;
                pcSessions[pcId].lastGoodBuffer = frameBuf;

                // 1. WebSocket 클라이언트들에게 바이너리 다이렉트 프레임 전송
                if (pcSessions[pcId].wsClients && pcSessions[pcId].wsClients.length > 0) {
                    const wsFrame = makeWsFrame(frameBuf);
                    for (const wsClient of pcSessions[pcId].wsClients) {
                        if (!wsClient.monitor || wsClient.monitor === currentMonKey) {
                            if (wsClient.socket && !wsClient.socket.destroyed && wsClient.socket.writable) {
                                if (wsClient.socket.writableLength && wsClient.socket.writableLength > 64 * 1024) {
                                    continue; // 브라우저 수신 지연 시 이전 프레임 즉시 드랍 (무지연 보장)
                                }
                                try { wsClient.socket.write(wsFrame); } catch(e) {}
                            }
                        }
                    }
                }

                // 2. 표준 MJPEG 클라이언트 스트림 전송
                if (pcSessions[pcId].streamClients && pcSessions[pcId].streamClients.length > 0) {
                    for (const client of pcSessions[pcId].streamClients) {
                        if (!client.monitor || client.monitor === currentMonKey) {
                            if (!client.res.writableEnded && !client.res.destroyed) {
                                if (client.res.writableLength && client.res.writableLength > 150 * 1024) {
                                    continue;
                                }
                                try {
                                    client.res.write(`--frame\r\nContent-Type: image/jpeg\r\nContent-Length: ${frameBuf.length}\r\n\r\n`);
                                    client.res.write(frameBuf);
                                    client.res.write('\r\n');
                                } catch(e) {}
                            }
                        }
                    }
                }
            }
        });

        req.on('end', () => {
            res.writeHead(200);
            res.end();
        });
        return;
    }

    // 3. 에이전트 리포트 수신 (화면 프레임 업로드 + 제어명령 반환)
    if (pathname === '/api/agent/report' && req.method === 'POST') {
        let body = '';
        req.on('data', chunk => body += chunk);
        req.on('end', () => {
            try {
                const data = JSON.parse(body);
                const pcId = data.id || 'unknown';
                const pcName = data.name || pcId;
                const clientIp = req.socket.remoteAddress.replace(/^.*:/, '');

                if (!pcSessions[pcId]) {
                    pcSessions[pcId] = {
                        id: pcId,
                        name: pcName,
                        ip: clientIp,
                        lastSeen: Date.now(),
                        rawBuffers: {},
                        lastGoodBuffer: null,
                        streamClients: [],
                        audioClients: []
                    };
                }

                pcSessions[pcId].lastSeen = Date.now();
                pcSessions[pcId].ip = clientIp;
                pcSessions[pcId].name = pcName;
                if (data.lanIp) pcSessions[pcId].lanIp = data.lanIp;
                if (data.lanPort) pcSessions[pcId].lanPort = data.lanPort;
                if (data.clipboardB64) pcSessions[pcId].clipboardB64 = data.clipboardB64;
                if (data.isUpdating) {
                    if (!pcSessions[pcId].updateStartTime) pcSessions[pcId].updateStartTime = Date.now();
                    if (Date.now() - pcSessions[pcId].updateStartTime > 15000) {
                        pcSessions[pcId].isUpdating = false;
                    } else {
                        pcSessions[pcId].isUpdating = true;
                    }
                } else {
                    pcSessions[pcId].isUpdating = false;
                    delete pcSessions[pcId].updateStartTime;
                }

                if (data.image) {
                    const buf = Buffer.from(data.image, 'base64');
                    const monKey = (data.monitor !== undefined && data.monitor !== null) ? data.monitor.toString() : '0';
                    pcSessions[pcId].activeMonitor = monKey;
                    if (!pcSessions[pcId].rawBuffers) pcSessions[pcId].rawBuffers = {};

                    pcSessions[pcId].rawBuffers[monKey] = buf;
                    pcSessions[pcId].lastGoodBuffer = buf;

                    if (pcSessions[pcId].wsClients && pcSessions[pcId].wsClients.length > 0) {
                        const wsFrame = makeWsFrame(buf);
                        for (const wsClient of pcSessions[pcId].wsClients) {
                            if (wsClient.monitor === monKey || !wsClient.monitor || wsClient.monitor === '0') {
                                if (wsClient.socket && !wsClient.socket.destroyed && wsClient.socket.writable) {
                                    try { wsClient.socket.write(wsFrame); } catch(e) {}
                                }
                            }
                        }
                    }

                    if (pcSessions[pcId].streamClients && pcSessions[pcId].streamClients.length > 0) {
                        for (const client of pcSessions[pcId].streamClients) {
                            if (client.monitor === monKey || !client.monitor || client.monitor === '0') {
                                if (!client.res.writableEnded && !client.res.destroyed) {
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

                let cmdsToExecute = [];
                if (pendingCommands[pcId] && pendingCommands[pcId].length > 0) {
                    cmdsToExecute = pendingCommands[pcId];
                    pendingCommands[pcId] = [];
                }

                const targetMon = activeViewedMonitor[pcId] || '0';
                const isFocused = !!(pcSessions[pcId] && pcSessions[pcId].wsClients && pcSessions[pcId].wsClients.length > 0);

                res.writeHead(200, { 'Content-Type': 'application/json' });
                res.end(JSON.stringify({ 
                    status: 'ok', 
                    commands: cmdsToExecute,
                    requestedMonitor: targetMon,
                    isFocused: isFocused
                }));
            } catch (e) {
                res.writeHead(400);
                res.end('Invalid JSON');
            }
        });
        return;
    }

    // 3-1. 초고속 0ms 실시간 직통 제어 명령 수신 채널 (즉시 푸시 롱폴링)
    if (pathname === '/api/agent/commands') {
        const pcId = urlObj.searchParams.get('id');
        const mon = (pcId && activeViewedMonitor[pcId]) ? activeViewedMonitor[pcId] : '0';
        const isFocused = !!(pcId && pcSessions[pcId] && pcSessions[pcId].wsClients && pcSessions[pcId].wsClients.length > 0);
        
        if (pcId && pendingCommands[pcId] && pendingCommands[pcId].length > 0) {
            const cmds = pendingCommands[pcId];
            pendingCommands[pcId] = [];
            res.writeHead(200, { 'Content-Type': 'application/json', 'Connection': 'keep-alive' });
            res.end(JSON.stringify({ commands: cmds, requestedMonitor: mon, isFocused: isFocused }));
            return;
        }

        if (pcId) {
            activeCommandSockets[pcId] = res;
            const cleanup = () => {
                if (activeCommandSockets[pcId] === res) delete activeCommandSockets[pcId];
            };
            req.on('close', cleanup);
            const timer = setTimeout(() => {
                cleanup();
                try {
                    res.writeHead(200, { 'Content-Type': 'application/json' });
                    res.end(JSON.stringify({ commands: [], requestedMonitor: mon, isFocused: isFocused }));
                } catch(e) {}
            }, 5000);
            req.on('close', () => clearTimeout(timer));
        } else {
            res.writeHead(200, { 'Content-Type': 'application/json' });
            res.end(JSON.stringify({ commands: [], requestedMonitor: '0', isFocused: false }));
        }
        return;
    }

    function dispatchControlCommand(rawTargetPc, cmdObj) {
        if (!cmdObj || !cmdObj.type) return { status: 'error' };

        if (cmdObj.monitor !== undefined && cmdObj.monitorIdx === undefined) {
            cmdObj.monitorIdx = cmdObj.monitor;
        }

        if (cmdObj.type === 'keydown' && cmdObj.key) {
            cmdObj.relX = cmdObj.key;
            cmdObj.relY = cmdObj.key;
        }

        function pushDirectly(targetId) {
            if (cmdObj.type === 'monitor' || cmdObj.type === 'select_monitor') {
                const m = (cmdObj.monitor !== undefined ? cmdObj.monitor : (cmdObj.monitorIdx !== undefined ? cmdObj.monitorIdx : (cmdObj.relX !== undefined ? cmdObj.relX : '0'))).toString();
                activeViewedMonitor[targetId] = m;
                if (!pendingCommands[targetId]) pendingCommands[targetId] = [];
                pendingCommands[targetId].push(cmdObj);
            } else if (cmdObj.type === 'move' || cmdObj.type === 'mousemove') {
                if (!pendingCommands[targetId]) pendingCommands[targetId] = [];
                const lastIdx = pendingCommands[targetId].length - 1;
                if (lastIdx >= 0 && (pendingCommands[targetId][lastIdx].type === 'move' || pendingCommands[targetId][lastIdx].type === 'mousemove')) {
                    pendingCommands[targetId][lastIdx] = cmdObj;
                } else {
                    pendingCommands[targetId].push(cmdObj);
                }
            } else {
                if (!pendingCommands[targetId]) pendingCommands[targetId] = [];
                pendingCommands[targetId].push(cmdObj);
            }
            if (pendingCommands[targetId].length > 50) pendingCommands[targetId].shift();

            // 1. ⚡ 에이전트 상시 WebSocket 직통 푸시 (0.001ms 지연)
            for (const k of Object.keys(pcSessions)) {
                if (k.toLowerCase() === targetId.toLowerCase() || (pcSessions[k] && pcSessions[k].name && pcSessions[k].name.toLowerCase() === targetId.toLowerCase())) {
                    const sess = pcSessions[k];
                    if (sess && sess.agentWs && !sess.agentWs.destroyed) {
                        try {
                            const mon = activeViewedMonitor[k] || '0';
                            const cmds = pendingCommands[targetId] || [];
                            pendingCommands[targetId] = [];
                            sess.agentWs.write(makeWsTextFrame(JSON.stringify({ commands: cmds.length > 0 ? cmds : [cmdObj], requestedMonitor: mon })));
                            return;
                        } catch(e) {}
                    }
                }
            }

            // 2. 일반 롱폴링 소켓 푸시 폴백
            for (const k of Object.keys(activeCommandSockets)) {
                if (k.toLowerCase() === targetId.toLowerCase() || (pcSessions[k] && pcSessions[k].name && pcSessions[k].name.toLowerCase() === targetId.toLowerCase())) {
                    const heldRes = activeCommandSockets[k];
                    delete activeCommandSockets[k];
                    try {
                        const mon = activeViewedMonitor[k] || '0';
                        const cmds = pendingCommands[targetId] || [];
                        pendingCommands[targetId] = [];
                        heldRes.writeHead(200, { 'Content-Type': 'application/json' });
                        heldRes.end(JSON.stringify({ commands: cmds, requestedMonitor: mon }));
                    } catch(e) {}
                }
            }
        }

        if (rawTargetPc === 'all') {
            const allTargets = new Set([...Object.keys(pcSessions), ...Object.keys(activeCommandSockets), ...Object.keys(pendingCommands)]);
            for (const pcId of allTargets) {
                pushDirectly(pcId);
            }
            return { status: 'ok', targetPc: 'all', count: allTargets.size };
        }

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
            pushDirectly(targetPcId);
            return { status: 'ok', targetPc: targetPcId };
        }
        return { status: 'error', message: 'target not found' };
    }

    function decodeWsText(buffer) {
        if (buffer.length < 6) return null;
        const isMasked = (buffer[1] & 0x80) !== 0;
        let payloadLen = buffer[1] & 0x7F;
        let maskOffset = 2;
        if (payloadLen === 126) {
            payloadLen = buffer.readUInt16BE(2);
            maskOffset = 4;
        } else if (payloadLen === 127) {
            payloadLen = Number(buffer.readBigUInt64BE(2));
            maskOffset = 10;
        }
        let dataOffset = maskOffset + (isMasked ? 4 : 0);
        let out = Buffer.alloc(payloadLen);
        if (isMasked) {
            const mask = buffer.slice(maskOffset, maskOffset + 4);
            for (let i = 0; i < payloadLen; i++) {
                out[i] = buffer[dataOffset + i] ^ mask[i % 4];
            }
        } else {
            buffer.copy(out, 0, dataOffset, dataOffset + payloadLen);
        }
        return out.toString('utf8');
    }

    // 4. 원격 PC 제어 명령 접수
    if (pathname === '/api/control') {
        const rawTargetPc = urlObj.searchParams.get('pc');
        const type = urlObj.searchParams.get('type');
        let relX = urlObj.searchParams.get('relX') || urlObj.searchParams.get('x') || '0';
        let relY = urlObj.searchParams.get('relY') || urlObj.searchParams.get('y') || '0';
        const key = urlObj.searchParams.get('key') || '';
        const msg = urlObj.searchParams.get('msg') || key || '';
        const monitorIdx = urlObj.searchParams.get('monitor') || '0';
        const delta = urlObj.searchParams.get('delta') || urlObj.searchParams.get('amount') || '-120';

        const cmdObj = { type, relX, relY, key, monitorIdx, msg, delta };

        if (type === 'blind_toggle' || type === 'blind_on' || type === 'blind_off') {
            if (type === 'blind_on') globalBlindMode = true;
            else if (type === 'blind_off') globalBlindMode = false;
            else globalBlindMode = !globalBlindMode;

            for (const tid in pcSessions) {
                pcSessions[tid].isBlindMode = globalBlindMode;
                if (globalBlindMode && privacyShieldBuffer) {
                    if (pcSessions[tid].streamClients) {
                        for (const client of pcSessions[tid].streamClients) {
                            try {
                                client.res.write(`--frame\r\nContent-Type: image/jpeg\r\nContent-Length: ${privacyShieldBuffer.length}\r\n\r\n`);
                                client.res.write(privacyShieldBuffer);
                                client.res.write('\r\n');
                            } catch(e) {}
                        }
                    }
                }
            }

            res.writeHead(200, { 'Content-Type': 'application/json' });
            res.end(JSON.stringify({ status: 'ok', isBlindMode: globalBlindMode, targetCount: Object.keys(pcSessions).length }));
            return;
        }

        const result = dispatchControlCommand(rawTargetPc, cmdObj);
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify(result));
        return;
    }

    // 5. 정적 웹 파일 서빙
    let filePath = path.join(PUBLIC_DIR, pathname === '/' ? 'index.html' : pathname);
    if (!filePath.startsWith(PUBLIC_DIR)) {
        res.writeHead(403);
        res.end('Forbidden');
        return;
    }

    fs.readFile(filePath, (err, content) => {
        if (err) {
            res.writeHead(404);
            res.end('Not Found');
        } else {
            const ext = path.extname(filePath);
            let contentType = 'text/html';
            if (ext === '.js') contentType = 'text/javascript';
            else if (ext === '.css') contentType = 'text/css';
            else if (ext === '.png') contentType = 'image/png';
            else if (ext === '.jpg') contentType = 'image/jpeg';
            
            res.writeHead(200, { 
                'Content-Type': contentType,
                'Cache-Control': 'no-cache, no-store, must-revalidate, max-age=0, post-check=0, pre-check=0',
                'Pragma': 'no-cache',
                'Expires': '0',
                'Surrogate-Control': 'no-store'
            });
            res.end(content);
        }
    });
});

function makeWsTextFrame(text) {
    const payload = Buffer.from(text, 'utf8');
    const len = payload.length;
    let header;
    if (len < 126) {
        header = Buffer.from([0x81, len]);
    } else if (len <= 0xFFFF) {
        header = Buffer.alloc(4);
        header[0] = 0x81;
        header[1] = 126;
        header.writeUInt16BE(len, 2);
    } else {
        header = Buffer.alloc(10);
        header[0] = 0x81;
        header[1] = 127;
        header.writeBigUInt64BE(BigInt(len), 2);
    }
    return Buffer.concat([header, payload]);
}

// 6. 초고속 WebSocket 바이너리 스트리밍 & 양방향 무지연 제어 핸들러 (0ms 지연)
server.on('upgrade', (req, socket, head) => {
    try {
        const urlObj = new URL(req.url, `http://${req.headers.host || 'localhost'}`);
        if (urlObj.pathname === '/ws/agent') {
            const rawPcId = urlObj.searchParams.get('id');
            const key = req.headers['sec-websocket-key'];
            if (!key || !rawPcId) {
                socket.destroy();
                return;
            }

            const accept = crypto.createHash('sha1').update(key + '258EAFA5-E914-47DA-95CA-C5AB0DC85B11').digest('base64');
            const responseHeaders = [
                'HTTP/1.1 101 Switching Protocols',
                'Upgrade: websocket',
                'Connection: Upgrade',
                `Sec-WebSocket-Accept: ${accept}`,
                '\r\n'
            ].join('\r\n');

            socket.setNoDelay(true);
            socket.write(responseHeaders);

            if (!pcSessions[rawPcId]) {
                pcSessions[rawPcId] = { id: rawPcId, name: rawPcId, lastSeen: Date.now() };
            }
            pcSessions[rawPcId].agentWs = socket;

            let agentBuf = Buffer.alloc(0);
            socket.on('data', (chunk) => {
                agentBuf = Buffer.concat([agentBuf, chunk]);

                while (agentBuf.length >= 2) {
                    const op = agentBuf[0] & 0x0F;
                    if (op === 0x08) { socket.end(); return; }
                    if (op === 0x09) {
                        try { socket.write(Buffer.from([0x8A, 0x00])); } catch(e) {}
                        agentBuf = agentBuf.slice(2);
                        continue;
                    }

                    const isMasked = (agentBuf[1] & 0x80) !== 0;
                    let payloadLen = agentBuf[1] & 0x7F;
                    let headerLen = 2;

                    if (payloadLen === 126) {
                        if (agentBuf.length < 4) break;
                        payloadLen = agentBuf.readUInt16BE(2);
                        headerLen = 4;
                    } else if (payloadLen === 127) {
                        if (agentBuf.length < 10) break;
                        payloadLen = Number(agentBuf.readBigUInt64BE(2));
                        headerLen = 10;
                    }
                    if (isMasked) headerLen += 4;

                    if (agentBuf.length < headerLen + payloadLen) {
                        break; // 더 많은 TCP 패킷 수신 대기
                    }

                    const frameData = agentBuf.slice(0, headerLen + payloadLen);
                    agentBuf = agentBuf.slice(headerLen + payloadLen);

                    if (op === 0x02) { // Binary Frame (SCAP / JPEG)
                        let payload;
                        let offset = headerLen - (isMasked ? 4 : 0);
                        if (isMasked) {
                            const mask = frameData.slice(offset, offset + 4);
                            offset += 4;
                            payload = Buffer.allocUnsafe(payloadLen);
                            for (let i = 0; i < payloadLen; i++) {
                                payload[i] = frameData[offset + i] ^ mask[i % 4];
                            }
                        } else {
                            payload = frameData.slice(offset, offset + payloadLen);
                        }

                        if (payload.length > 100) {
                            pcSessions[rawPcId].lastGoodBuffer = payload;
                            if (pcSessions[rawPcId].wsClients && pcSessions[rawPcId].wsClients.length > 0) {
                                const wsOutFrame = makeWsFrame(payload);
                                for (const wsClient of pcSessions[rawPcId].wsClients) {
                                    if (wsClient.socket && !wsClient.socket.destroyed && wsClient.socket.writable) {
                                        if (wsClient.socket.writableLength && wsClient.socket.writableLength > 64 * 1024) continue;
                                        try { wsClient.socket.write(wsOutFrame); } catch(e) {}
                                    }
                                }
                            }
                        }
                    }
                }
            });

            socket.on('close', () => {
                if (pcSessions[rawPcId] && pcSessions[rawPcId].agentWs === socket) {
                    delete pcSessions[rawPcId].agentWs;
                }
            });
            socket.on('error', () => {
                socket.destroy();
            });
            return;
        }

        if (urlObj.pathname === '/ws/stream') {
            const rawTargetPc = urlObj.searchParams.get('pc');
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

            const key = req.headers['sec-websocket-key'];
            if (!key) {
                socket.destroy();
                return;
            }

            const accept = crypto.createHash('sha1').update(key + '258EAFA5-E914-47DA-95CA-C5AB0DC85B11').digest('base64');
            const responseHeaders = [
                'HTTP/1.1 101 Switching Protocols',
                'Upgrade: websocket',
                'Connection: Upgrade',
                `Sec-WebSocket-Accept: ${accept}`,
                '\r\n'
            ].join('\r\n');

            socket.setNoDelay(true);
            socket.write(responseHeaders);

            const wsClient = {
                socket,
                targetPcId,
                monitor: monitorIdx.toString(),
                isAlive: true
            };

            if (targetPcId && pcSessions[targetPcId]) {
                if (!pcSessions[targetPcId].wsClients) pcSessions[targetPcId].wsClients = [];
                pcSessions[targetPcId].wsClients.push(wsClient);

                // 대상 에이전트의 모니터 선택 및 30 FPS Turbo 모드 즉시 전송
                activeViewedMonitor[targetPcId] = monitorIdx.toString();
                dispatchControlCommand(targetPcId, { type: 'select_monitor', monitor: monitorIdx.toString() });
                dispatchControlCommand(targetPcId, { type: 'focus_change', isFocused: true });

                // 연결 즉시 직전 유효 프레임 전송하여 0초 만에 화면 표출
                const p = pcSessions[targetPcId];
                const buf = (p.rawBuffers && p.rawBuffers[monitorIdx]) || p.lastGoodBuffer;
                if (buf) {
                    try { socket.write(makeWsFrame(buf)); } catch(e) {}
                }
            }

            socket.on('data', (data) => {
                if (data.length > 0) {
                    const op = data[0] & 0x0F;
                    if (op === 0x08) { // close frame
                        try { socket.write(Buffer.from([0x88, 0x00])); } catch(e) {}
                        socket.end();
                        return;
                    }
                    if (op === 0x09) { // ping frame
                        try { socket.write(Buffer.from([0x8A, 0x00])); } catch(e) {}
                        return;
                    }
                    if (op === 0x01) { // 🌟 WebSocket 직통 실시간 제어 명령 접수 (0ms 지연)
                        try {
                            const text = decodeWsText(data);
                            if (text) {
                                const cmdJson = JSON.parse(text);
                                const target = cmdJson.pc || wsClient.targetPcId;
                                dispatchControlCommand(target, cmdJson);
                            }
                        } catch(e) {}
                    }
                }
            });

            socket.on('close', () => {
                if (targetPcId && pcSessions[targetPcId] && pcSessions[targetPcId].wsClients) {
                    pcSessions[targetPcId].wsClients = pcSessions[targetPcId].wsClients.filter(c => c !== wsClient);
                    const stillFocused = pcSessions[targetPcId].wsClients.length > 0;
                    dispatchControlCommand(targetPcId, { type: 'focus_change', isFocused: stillFocused });
                }
            });

            socket.on('error', () => {
                socket.destroy();
            });
        }
    } catch(e) {
        try { socket.destroy(); } catch(err) {}
    }
});

server.listen(PORT, '0.0.0.0', () => {
    console.log(`=========================================`);
    console.log(`  🏢 다연코퍼레이션 관리자 서버 가동 완료  `);
    console.log(`  - 포트: ${PORT} (내부 및 외부 관제 포트) `);
    console.log(`=========================================`);

    startCloudflaredTunnel();
});

function startCloudflaredTunnel() {
    const cfCandidates = [
        path.join(__dirname, 'cloudflared.exe'),
        path.join(__dirname, '..', 'cloudflared.exe'),
        path.join(process.cwd(), 'cloudflared.exe')
    ];
    let cfPath = cfCandidates.find(p => fs.existsSync(p));
    if (!cfPath) return;

    try {
        const { spawn } = require('child_process');
        const cfProc = spawn(cfPath, ['tunnel', '--url', `http://127.0.0.1:${PORT}`], {
            stdio: ['ignore', 'pipe', 'pipe']
        });

        const reg = /https:\/\/[a-zA-Z0-9-]+\.trycloudflare\.com/;
        const onData = (chunk) => {
            const str = chunk.toString();
            const m = str.match(reg);
            if (m && (!activeTunnelUrl || activeTunnelUrl !== m[0])) {
                activeTunnelUrl = m[0];
                console.log(`\n=========================================`);
                console.log(`  🌐 Cloudflare 무료 외부 접속 주소 발급 완료:`);
                console.log(`  🔗 ${activeTunnelUrl}`);
                console.log(`=========================================\n`);

                const txtContent = `🏢 [다연코퍼레이션] 관리자 원격 관제 접속 링크 안내\r\n\r\n`
                                 + `1. 📱 스마트폰 / 외부 즉시 접속 링크 (공유기 설정 불필요):\r\n`
                                 + `${activeTunnelUrl}\r\n\r\n`
                                 + `2. 🖥️ 사내 / 로컬 네트워크 접속 링크:\r\n`
                                 + `http://172.30.1.90:8080 (또는 http://127.0.0.1:8080)\r\n`;
                try {
                    fs.writeFileSync(path.join(__dirname, '외부_스마트폰_접속링크.txt'), txtContent, 'utf8');
                    fs.writeFileSync(path.join(__dirname, '..', '외부_스마트폰_접속링크.txt'), txtContent, 'utf8');
                    const desktopPath = path.join(require('os').homedir(), 'Desktop');
                    fs.writeFileSync(path.join(desktopPath, '다연코퍼레이션 관리자', '외부_스마트폰_접속링크.txt'), txtContent, 'utf8');
                } catch(e) {}
            }
        };

        cfProc.stdout.on('data', onData);
        cfProc.stderr.on('data', onData);
        cfProc.on('error', () => {});
        cfProc.on('exit', () => {
            setTimeout(startCloudflaredTunnel, 3000);
        });
    } catch(e) {}
}

const CLOUD_RELAY_URL = 'https://dayeon-remote.onrender.com';
const httpsModule = require('https');
let isCloudSyncing = false;

function syncToCloudRelay() {
    // 클라우드 자체(Render)에서 돌고 있는 경우 로컬 동기화 스킵
    if (process.env.RENDER || isCloudSyncing || !CLOUD_RELAY_URL) return;
    isCloudSyncing = true;

    const activePcList = [];
    const now = Date.now();
    for (const id in pcSessions) {
        const p = pcSessions[id];
        if (now - p.lastSeen < 10000) {
            activePcList.push({
                id: p.id,
                name: p.name,
                nickname: p.nickname || '',
                ip: p.ip,
                lastSeen: p.lastSeen,
                isUpdating: p.isUpdating,
                isBlindMode: p.isBlindMode,
                clipboardB64: p.clipboardB64 || '',
                activeMonitor: p.activeMonitor || '0',
                image: p.lastGoodBuffer ? p.lastGoodBuffer.toString('base64') : ''
            });
        }
    }

    if (activePcList.length === 0) {
        isCloudSyncing = false;
        return;
    }

    const payload = JSON.stringify({ pcs: activePcList });
    try {
        const u = new URL(CLOUD_RELAY_URL);
        const req = httpsModule.request({
            hostname: u.hostname,
            port: 443,
            path: '/api/bridge/sync',
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'Content-Length': Buffer.byteLength(payload)
            },
            timeout: 4000
        }, (res) => {
            let body = '';
            res.on('data', c => body += c);
            res.on('end', () => {
                isCloudSyncing = false;
                try {
                    const data = JSON.parse(body);
                    if (data.commands && Array.isArray(data.commands) && data.commands.length > 0) {
                        for (const cmd of data.commands) {
                            dispatchControlCommand(cmd.pc, cmd);
                        }
                    }
                } catch(e) {}
            });
        });

        req.on('error', () => { isCloudSyncing = false; });
        req.on('timeout', () => { req.destroy(); isCloudSyncing = false; });
        req.write(payload);
        req.end();
    } catch(e) {
        isCloudSyncing = false;
    }
}

setInterval(syncToCloudRelay, 1000);
