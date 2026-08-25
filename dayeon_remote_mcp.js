const http = require('http');
const https = require('https');
const readline = require('readline');
const fs = require('fs');
const path = require('path');

let SERVER_URL = 'https://dayeon-remote.onrender.com';
const ipConfigFile = path.join(__dirname, 'server_ip.txt');
if (fs.existsSync(ipConfigFile)) {
    try {
        const saved = fs.readFileSync(ipConfigFile, 'utf8').trim();
        if (saved) SERVER_URL = saved;
    } catch(e) {}
}

function apiRequest(endpoint, method = 'GET', postData = null, headers = {}) {
    return new Promise((resolve, reject) => {
        const url = new URL(endpoint, SERVER_URL);
        const isHttps = url.protocol === 'https:';
        const netModule = isHttps ? https : http;

        const req = netModule.request({
            hostname: url.hostname,
            port: url.port || (isHttps ? 443 : 80),
            path: url.pathname + url.search,
            method: method,
            headers: {
                ...headers,
                'User-Agent': 'Dayeon-Claude-MCP/1.0'
            },
            timeout: 10000
        }, (res) => {
            const chunks = [];
            res.on('data', c => chunks.push(c));
            res.on('end', () => {
                const buffer = Buffer.concat(chunks);
                const contentType = res.headers['content-type'] || '';
                if (contentType.includes('application/json')) {
                    try {
                        resolve({ status: res.statusCode, data: JSON.parse(buffer.toString('utf8')) });
                    } catch(e) {
                        resolve({ status: res.statusCode, data: buffer.toString('utf8') });
                    }
                } else if (contentType.includes('image/')) {
                    resolve({ status: res.statusCode, buffer: buffer, isImage: true });
                } else {
                    resolve({ status: res.statusCode, data: buffer.toString('utf8') });
                }
            });
        });

        req.on('error', reject);
        req.on('timeout', () => { req.destroy(); reject(new Error('API Request Timeout')); });

        if (postData) {
            if (Buffer.isBuffer(postData)) req.write(postData);
            else if (typeof postData === 'object') req.write(JSON.stringify(postData));
            else req.write(postData.toString());
        }
        req.end();
    });
}

const TOOLS = [
    {
        name: 'list_remote_pcs',
        description: '현재 연결된 모든 원격 PC 목록 및 온라인 상태, 모니터 정보를 조회합니다.',
        inputSchema: { type: 'object', properties: {} }
    },
    {
        name: 'capture_pc_screen',
        description: '특정 원격 PC의 현재 화면을 스크린샷 캡처하여 확인합니다.',
        inputSchema: {
            type: 'object',
            properties: {
                pcId: { type: 'string', description: '조회할 원격 PC ID (예: DESKTOP-CB71HV6_7)' },
                monitor: { type: 'string', description: '모니터 번호 (기본값 0)' }
            },
            required: ['pcId']
        }
    },
    {
        name: 'control_pc_input',
        description: '특정 원격 PC에 마우스 클릭, 드래그, 키보드 타이핑, 단축키 명령을 전송합니다.',
        inputSchema: {
            type: 'object',
            properties: {
                pcId: { type: 'string', description: '제어할 원격 PC ID' },
                type: { type: 'string', enum: ['click', 'dblclick', 'rightclick', 'mousedown', 'mouseup', 'mousemove', 'keydown', 'hotkey', 'paste_text', 'wheel'], description: '조작 타입' },
                relX: { type: 'number', description: '모니터 상대 X좌표 (0.0~1.0)' },
                relY: { type: 'number', description: '모니터 상대 Y좌표 (0.0~1.0)' },
                monitor: { type: 'string', description: '모니터 번호 (기본값 0)' },
                key: { type: 'string', description: '키보드 키 또는 단축키 (예: Enter, ctrl+c, Hangul)' },
                text: { type: 'string', description: '입력할 텍스트' }
            },
            required: ['pcId', 'type']
        }
    },
    {
        name: 'send_notice_popup',
        description: '원격 PC 화면에 팝업 공지창을 띄웁니다.',
        inputSchema: {
            type: 'object',
            properties: {
                pcId: { type: 'string', description: '대상 PC ID (또는 all)' },
                message: { type: 'string', description: '공지할 메시지 내용' }
            },
            required: ['message']
        }
    },
    {
        name: 'reboot_remote_pc',
        description: '원격 PC를 재부팅합니다.',
        inputSchema: {
            type: 'object',
            properties: {
                pcId: { type: 'string', description: '재부팅할 원격 PC ID' }
            },
            required: ['pcId']
        }
    }
];

async function handleToolCall(name, args) {
    switch (name) {
        case 'list_remote_pcs': {
            const res = await apiRequest('/api/pcs');
            return { content: [{ type: 'text', text: JSON.stringify(res.data, null, 2) }] };
        }
        case 'capture_pc_screen': {
            const pc = args.pcId;
            const mon = args.monitor || '0';
            const res = await apiRequest(`/api/snapshot?pc=${encodeURIComponent(pc)}&monitor=${encodeURIComponent(mon)}&t=${Date.now()}`);
            if (res.isImage && res.buffer) {
                return {
                    content: [{ type: 'image', data: res.buffer.toString('base64'), mimeType: 'image/jpeg' }]
                };
            } else {
                return { content: [{ type: 'text', text: '화면 캡처 실패 또는 PC 오프라인' }] };
            }
        }
        case 'control_pc_input': {
            let url = `/api/control?pc=${encodeURIComponent(args.pcId)}&type=${encodeURIComponent(args.type)}`;
            if (args.relX !== undefined) url += `&relX=${args.relX}`;
            if (args.relY !== undefined) url += `&relY=${args.relY}`;
            if (args.monitor !== undefined) url += `&monitor=${encodeURIComponent(args.monitor)}`;
            if (args.key) url += `&key=${encodeURIComponent(args.key)}`;
            if (args.text) url += `&msg=${encodeURIComponent(args.text)}`;
            const res = await apiRequest(url);
            return { content: [{ type: 'text', text: JSON.stringify(res.data) }] };
        }
        case 'send_notice_popup': {
            const pc = args.pcId || 'all';
            const url = `/api/control?pc=${encodeURIComponent(pc)}&type=popup&msg=${encodeURIComponent(args.message)}`;
            const res = await apiRequest(url);
            return { content: [{ type: 'text', text: JSON.stringify(res.data) }] };
        }
        case 'reboot_remote_pc': {
            const url = `/api/control?pc=${encodeURIComponent(args.pcId)}&type=reboot`;
            const res = await apiRequest(url);
            return { content: [{ type: 'text', text: JSON.stringify(res.data) }] };
        }
        default:
            throw new Error(`알 수 없는 도구: ${name}`);
    }
}

const rl = readline.createInterface({ input: process.stdin, output: process.stdout, terminal: false });

rl.on('line', async (line) => {
    line = line.trim();
    if (!line) return;
    try {
        const req = JSON.parse(line);
        const id = req.id;
        if (req.method === 'tools/list') {
            const resp = { jsonrpc: '2.0', id, result: { tools: TOOLS } };
            process.stdout.write(JSON.stringify(resp) + '\n');
        } else if (req.method === 'tools/call') {
            try {
                const result = await handleToolCall(req.params.name, req.params.arguments || {});
                const resp = { jsonrpc: '2.0', id, result };
                process.stdout.write(JSON.stringify(resp) + '\n');
            } catch(e) {
                const resp = { jsonrpc: '2.0', id, error: { code: -32000, message: e.message } };
                process.stdout.write(JSON.stringify(resp) + '\n');
            }
        } else if (req.method === 'initialize') {
            const resp = {
                jsonrpc: '2.0',
                id,
                result: {
                    protocolVersion: '2024-11-05',
                    capabilities: { tools: {} },
                    serverInfo: { name: 'dayeon-remote-mcp', version: '1.0.0' }
                }
            };
            process.stdout.write(JSON.stringify(resp) + '\n');
        } else {
            const resp = { jsonrpc: '2.0', id, result: {} };
            process.stdout.write(JSON.stringify(resp) + '\n');
        }
    } catch(e) {}
});
