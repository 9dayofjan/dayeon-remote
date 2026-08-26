# 🎥 초저지연 CCTV 원격 제어 & 화면 녹화 프로토타입

피제어 PC와 조종 PC 모두 **렉 없이** 실시간으로 화면을 CCTV처럼 관제하고, 버튼 클릭 한 번으로 **화면 녹화** 및 **원격 조작**이 가능한 경량 원격 제어 프로토타입입니다.

---

## 🚀 빠른 실행 방법 (Node.js 웹 버전)

Node.js 기본 설치 환경(별도 npm 설치 필요 없음)에서 즉시 실행 가능합니다.

1. **서버 실행**:
   ```cmd
   cd C:\Users\user\.gemini\antigravity\scratch\simple_remote_control
   node server.js
   ```

2. **웹 브라우저 접속**:
   - 웹 브라우저(Chrome / Edge 등)를 열고 `http://localhost:8080` 접속

3. **주요 기능 사용**:
   - **CCTV 관제**: 접속 즉시 실시간 피제어 PC 화면이 CCTV 스트림처럼 보입니다.
   - **화면 녹화**: 우상단 `🔴 화면 녹화 시작` 버튼을 누르면 녹화가 시작되며, `⬛ 녹화 중지`를 누르면 녹화된 동영상 파일(`.webm`)이 즉시 컴퓨터에 다운로드됩니다.
     - *(클라이언트 브라우저 Canvas 스트림 캡처 방식으로 피제어 PC의 CPU/GPU 자원을 전혀 소비하지 않아 **피제어 PC 렉 0%**)*
   - **원격 조작**: 화면 영역을 마우스로 클릭하거나 키보드를 누르면 피제어 PC에서 마우스 이동/클릭 및 키보드 입력이 실행됩니다.

---

## ⚡ C언어 순수 Win32 API 서버 컴파일 & 실행 (MSVC)

C언어 기반의 초고속 원격 엔진 소스가 제공됩니다.

1. **컴파일 (Developer Command Prompt 또는 build_c.bat)**:
   ```cmd
   build_c.bat
   ```
   또는 Visual Studio 명령 프롬프트에서:
   ```cmd
   cl.exe /O2 /W3 remote_server.c ws2_32.lib gdi32.lib user32.lib /Fe:remote_server.exe
   ```

2. **C 원격 서버 실행**:
   ```cmd
   remote_server.exe
   ```
   - 9000 포트로 TCP 소켓 스트리밍 및 원격 `SendInput` 입력 입력을 대기합니다.

---

## 🛠️ 기술적 렉 제로 노하우 구조

```
[ 피제어 PC ]                                     [ 관제 클라이언트 (웹 브라우저) ]
┌──────────────────────────┐                    ┌──────────────────────────────┐
│  • Win32 GDI / PowerShell│ ─── 30fps 스트림 ──► │ • HTML5 Canvas 렌더링 (CCTV)  │
│  • Win32 SendInput API   │ ◄── 원격 입력 신호 ─── │ • MediaRecorder API (무렉 녹화)│
└──────────────────────────┘                    └──────────────────────────────┘
```
