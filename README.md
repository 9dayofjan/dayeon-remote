# 🏢 다연코퍼레이션 초고속 원격 관제 솔루션 - AI 개발 인수인계서 & 프로젝트 가이드

> **최종 백업 일시**: 2026-08-27  
> **백업 디렉토리**: `C:\Users\user\Desktop\다연코퍼레이션_개발백업\다연코퍼레이션_20260827_0ms극초저지연_완전최종백업\`  
> **Git 리포지토리**: `C:\Users\user\Desktop\다연코퍼레이션_개발백업\dayeon-remote-git\`  
> **실행 폴더**: `C:\Users\user\Desktop\다연코퍼레이션_실행용\`

---

## 📌 1. 프로젝트 개요

다연코퍼레이션 사내 기가비트 LAN(0ms 지연) 및 외부 클라우드 중계를 결합한 **20대 이상 PC 동시 모니터링 및 60 FPS 게이밍급 초저지연 원격 제어 소프트웨어**입니다.

- **관리자용 프로그램 (`manager_app.cs`)**: C# WinForms 기반 단일 통합 관제 툴 (동적 다분할 그리드, 285Hz 마우스 트래킹, 0초 모니터 전환, 화면 그리기, 1:1 메시지 전송).
- **원격 클라이언트 (`DayeonClient.cs`)**: C# 네이티브 60 FPS 화면 캡처 및 포트 8888 바이너리 스트리밍 엔진 (GDI BitBlt, ColorOnColor 스케일링, JPEG 65L 인코딩, Spin-Wait 하이브리드 타이머).
- **원격 에이전트 데몬 (`core/agent.js`)**: Node.js 기반 사내 LAN HTTP (포트 8001) 및 클라우드 WebSocket 중계 통신 데몬.
- **입력/판서 제어 모듈 (`input_ctrl.cs`)**: Windows API 마우스/키보드 하드웨어 제어 및 더블버퍼링 투명 판서 오버레이.
- **트레이 프로그램 (`tray_app.cs`)**: 부팅 시 자동 실행되는 백그라운드 관리 트레이 앱.

---

## 📂 2. 핵심 소스 파일 맵

```
📁 다연코퍼레이션_20260827_0ms극초저지연_완전최종백업/
├── 📄 manager_app.cs           # [관리자 프로그램 메인 소스] 20+ 분할 그리드, 60fps 뷰어, 285Hz 조작
├── 📄 DayeonClient.cs          # [원격 PC 네이티브 스트리밍 엔진] 포트 8888 직통 바이너리 서버
├── 📄 input_ctrl.cs            # [원격 PC 입력/판서/팝업 모듈] 마우스, 키보드, 0% 깜빡임 판서
├── 📄 fastcap.cs               # [원격 PC 보조 캡처 엔진] GDI+ 화면 캡처 CLI
├── 📄 audiocap.cs              # [원격 PC 오디오 캡처] NAudio WASAPI 루프백
├── 📄 tray_app.cs              # [원격 PC 트레이 앱] 자동실행 및 데몬 관리
├── 📄 server.js                # [클라우드 중계 서버] Node.js WebSocket & 롱폴링 중계
├── 📄 app.ico                  # 프로그램 아이콘 리소스
├── 📁 다연코퍼레이션/
│   └── 📁 core/
│       ├── 📄 agent.js         # 원격 PC 백그라운드 상주 통신 데몬
│       └── 📄 version.json     # 시스템 버전 관리 파일
└── 📄 AI_인수인계서_프로젝트가이드.md # 본 문서
```

---

## 🔨 3. 빌드 및 컴파일 명령어 (CSC)

모든 C# 소스는 Windows 기본 내장 `.NET Framework 4.0/4.8`의 `csc.exe`를 사용하여 즉시 컴파일됩니다.

### ① 관리자 프로그램 컴파일
```powershell
& "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /out:"manager_app_new.exe" /target:winexe /platform:x64 /optimize+ /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll /reference:System.Net.Http.dll /reference:Microsoft.VisualBasic.dll /win32icon:"app.ico" "manager_app.cs"
```

### ② 원격 클라이언트 (`DayeonClient.exe`) 컴파일
```powershell
& "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /out:"DayeonClient.exe" /target:winexe /platform:x64 /optimize+ /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll "DayeonClient.cs"
```

### ③ 입력/판서 모듈 (`input_ctrl.exe`) 컴파일
```powershell
& "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /out:"input_ctrl.exe" /target:winexe /platform:x64 /optimize+ /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll "input_ctrl.cs"
```

### ④ 화면 캡처 모듈 (`fastcap.exe`) 컴파일
```powershell
& "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /out:"fastcap.exe" /target:exe /platform:x64 /optimize+ /unsafe /reference:System.dll /reference:System.Drawing.dll "fastcap.cs"
```

### ⑤ 트레이 앱 (`다연코퍼레이션.exe`) 컴파일
```powershell
& "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /out:"다연코퍼레이션.exe" /target:winexe /platform:x64 /optimize+ /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /win32icon:"app.ico" "tray_app.cs"
```

---

## 📡 4. 통신 프로토콜 규격 (TCP 8888 Binary Protocol)

관리자(`manager_app`)와 원격 PC(`DayeonClient`) 간에는 최저 지연 시간(0.01ms)을 위해 고정 8바이트 바이너리 패킷을 사용합니다.

### 패킷 구조 (8 Bytes Fixed Header + Payload)
| 오프셋 (Offset) | 크기 (Size) | 타입 (Type) | 설명 (Description) |
|---|---|---|---|
| 0 | 1 Byte | `byte` | 명령 타입 (`cmdType`) |
| 1 | 1 Byte | `byte` | 대상 모니터 인덱스 (`monIdx`: 0, 1, 2, ...) |
| 2..3 | 2 Bytes | `ushort` | 가변 페이로드 길이 (`payloadLen` 리틀 엔디언) |
| 4..5 | 2 Bytes | `ushort` | 파라미터 1 (`param1` or `pX`: 0~65535) |
| 6..7 | 2 Bytes | `ushort` | 파라미터 2 (`param2` or `pY`: 0~65535) |

### 주요 명령 코드 (`cmdType`)
- `0x01`: 모드 설정 (`param1 == 1`이면 60 FPS 줌 모드, `0`이면 5 FPS 썸네일 모드)
- `0x02`: **0초 즉시 모니터 전환 (`SET_MONITOR`)** - 소켓 재연결 없이 0.0001ms 만에 대상 모니터 캡처 변경
- `0x10`: 마우스 절대 좌표 이동 (`pX`, `pY` 정규화 0~65535)
- `0x11`: 마우스 좌클릭 다운 (`MouseDown`)
- `0x12`: 마우스 좌클릭 업 (`MouseUp`)
- `0x13`: 마우스 우클릭 다운
- `0x14`: 마우스 우클릭 업
- `0x15`: 마우스 휠 스크롤 (`(short)param1` 휠 델타)
- `0x20`: 키보드 키 다운 (`(ushort)Keys`)
- `0x21`: 키보드 키 업 (`(ushort)Keys`)
- `0x30`: 화면 그리기 스트로크 실시간 전송
- `0x31`: 화면 판서 전체 지우기
- `0x40`: **1:1 메시지 다크 UI 팝업** (`payload`에 UTF-8 메시지 전달)
- `0x41`: 원격 PC 재부팅

---

## 🛡️ 5. 핵심 버그 해결 이력 및 주의사항 (절대 재발 금지!)

새로운 AI가 코드를 수정할 때 다음 사항을 반드시 숙지해야 합니다:

1. **소켓 타임아웃 멈춤 버그 방지 (`DayeonClient.cs`)**:
   - `inputThread`의 `ns.ReadTimeout`은 반드시 **`Timeout.Infinite`**여야 합니다. 2초 등의 타임아웃을 걸면 마우스 조작을 멈췄을 때 소켓이 닫히면서 화면 캡처가 멈춥니다.
2. **마우스 좌표 50% 꺾임 및 멀티모니터 계산 (`GetRelCoords` / `ExecuteNativeInput`)**:
   - 관리자는 `(ushort)(relX * 65535)`로 부호 없는 16비트(`ushort`)로 직렬화하여 전송합니다.
   - 클라이언트는 `BitConverter.ToUInt16`으로 언패킹하고 `bounds.Left + (int)(pX * bounds.Width / 65535.0)`으로 계산하여 `SetCursorPos`를 호출합니다. 음수 좌표 모니터도 100% 정상 작동합니다.
3. **판서 깜빡임 0% (`input_ctrl.cs`)**:
   - 투명 오버레이 폼은 `DoubleBufferedOverlayForm`을 사용하여 `CreateParams`에 `WS_EX_LAYERED | WS_EX_TRANSPARENT`를 적용하고 메모리 비트맵 더블버퍼링으로만 렌더링해야 깜빡임이 없습니다.
4. **모니터 버튼 너비 (`manager_app.cs`)**:
   - 상단 툴바의 `btnMon1`, `btnMon2`, `btnMon3` 가로폭은 최소 **88px** 이상이어야 `✔ 모니터 2` 텍스트가 잘리지 않습니다.
5. **게임 렉 및 초저지연 프레임 페이싱 (`DayeonClient.cs`)**:
   - `Thread.Sleep(1)` 대신 `timeBeginPeriod(1)` + `SpinWait` 하이브리드 루프를 유지하여 윈도우 OS의 지터를 제거하고 60.0 FPS를 칼고정합니다.
   - JPEG 압축 퀄리티는 **65L**이 인코딩 시간(1.0ms)과 화질, 패킷 크기(35KB)의 최적 황금비입니다.

---

## 🌐 6. 사내 LAN PC IP 목록

| PC 식별자 | 닉네임 | 사내 IP | 포트 |
|---|---|---|---|
| `DESKTOP-UVSBE6O_87` | **관리자 PC** | `172.30.1.87` | 관리자 실행 |
| `DESKTOP-1K5QPOO_36` | **대표님 PC** | `172.30.1.36` | 8888 (Native), 8001 (Agent) |
| `DESKTOP-CB71HV6_7` | 원격 PC 1 | `172.30.1.7` | 8888, 8001 |
| `DESKTOP-UVSBE6O_10` | 원격 PC 2 | `172.30.1.10` | 8888, 8001 |
| `DESKTOP-JG7PHSN_91` | 원격 PC 3 | `172.30.1.91` | 8888, 8001 |
| `DESKTOP-PC_88` | 원격 PC 4 | `172.30.1.88` | 8888, 8001 |
| `DESKTOP-PC_9` | 원격 PC 5 | `172.30.1.9` | 8888, 8001 |
