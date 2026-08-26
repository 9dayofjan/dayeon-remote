const fs = require('fs');
const path = require('path');

const pkgDir = path.join(__dirname, 'agent_package');
if (!fs.existsSync(pkgDir)) {
    fs.mkdirSync(pkgDir, { recursive: true });
}

fs.copyFileSync('C:/Program Files/nodejs/node.exe', path.join(pkgDir, 'node.exe'));
fs.copyFileSync(path.join(__dirname, 'agent.js'), path.join(pkgDir, 'agent.js'));
fs.copyFileSync(path.join(__dirname, 'fastcap.exe'), path.join(pkgDir, 'fastcap.exe'));
fs.copyFileSync(path.join(__dirname, 'input_ctrl.exe'), path.join(pkgDir, 'input_ctrl.exe'));

const batContent = `@echo off
chcp 65001 > nul
cls
cd /d "%~dp0"
node.exe agent.js
pause
`;

fs.writeFileSync(path.join(pkgDir, '다연코퍼레이션_에이전트_실행.bat'), batContent, 'utf8');

console.log('✅ agent_package 생성 완료!');
console.log('포함된 파일 목록:');
fs.readdirSync(pkgDir).forEach(f => {
    const stat = fs.statSync(path.join(pkgDir, f));
    console.log(` - ${f} (${(stat.size / 1024 / 1024).toFixed(2)} MB)`);
});
