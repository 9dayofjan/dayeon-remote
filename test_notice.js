const fs = require('fs');
const os = require('os');
const path = require('path');
const { execFile, spawn } = require('child_process');

function showNotice(msg) {
    const cleanMsg = msg.replace(/"/g, '""');
    const vbs = `MsgBox "${cleanMsg}", 4096 + 64, "🏢 다연코퍼레이션 관리자 공지"`;
    const tmp = path.join(os.tmpdir(), `dayeon_msg_${Date.now()}.vbs`);
    fs.writeFileSync(tmp, '\ufeff' + vbs, 'utf16le');
    
    execFile('wscript.exe', [tmp], (err) => {
        try { fs.unlinkSync(tmp); } catch(e) {}
    });

    const inputCtrlPath = path.join(__dirname, 'input_ctrl.exe');
    if (fs.existsSync(inputCtrlPath)) {
        try { spawn(inputCtrlPath, ['popup', msg], { detached: true, stdio: 'ignore' }).unref(); } catch(e) {}
    }
}

showNotice('안녕하세요! 다연코퍼레이션 관리자 공지입니다.');
console.log('Notice sent successfully!');
