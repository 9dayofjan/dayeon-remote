# Dayeon Remote Control System (다연코퍼레이션 원격 관제 시스템)

## Overview
6-Split high-performance multi-PC remote monitoring and hardware control software built for Dayeon Corporation.

## Architecture
- **Manager (관리자 EXE)**: `manager_app.cs` (C# .NET 4.0 Windows Forms)
  - Compiles to: `C:\Users\user\Desktop\다연코퍼레이션 관리자.exe`
  - Compile Command: `csc.exe /target:winexe /out:"다연코퍼레이션 관리자.exe" /r:System.Windows.Forms.dll,System.Drawing.dll,Microsoft.VisualBasic.dll,System.dll,System.Web.Extensions.dll,System.Core.dll /optimize+ /platform:anycpu manager_app.cs`
- **Hardware Input Engine**: `input_ctrl.cs` -> `core/input_ctrl.exe`
- **Capture Engine**: `fastcap.cs` -> `core/fastcap.exe` (0.2ms GDI hardware capture)
- **Agent**: `agent.js` -> `다연코퍼레이션.exe`
- **Server**: `server.js` (Hosted on Render: `https://dayeon-remote.onrender.com`)
- **Claude MCP Server**: `dayeon_remote_mcp.js` (Exposes `list_remote_pcs`, `capture_pc_screen`, `control_pc_input`, `send_notice_popup`, `reboot_remote_pc`)

## MCP Integration
Claude can directly call tools via `dayeon_remote_mcp.js`:
- `list_remote_pcs`: Lists online PCs and their status
- `capture_pc_screen`: Returns base64 screenshot of remote PC
- `control_pc_input`: Sends clicks, keypresses, hotkeys, text to remote PC
- `send_notice_popup`: Shows emergency notice popup on remote PC
- `reboot_remote_pc`: Reboots remote PC
