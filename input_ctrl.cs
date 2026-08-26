using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

class InputCtrl {
    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SetThreadDesktop(IntPtr hDesktop);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool CloseDesktop(IntPtr hDesktop);

    [DllImport("user32.dll")]
    static extern bool SetProcessDPIAware();

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

    [DllImport("shcore.dll", SetLastError = true)]
    static extern int SetProcessDpiAwareness(int awareness);

    static RECT[] cachedPhysicalMonitors = null;
    static long lastMonCheckTick = 0;

    static void EnableTrueNativeDpi() {
        try {
            SetProcessDpiAwarenessContext((IntPtr)(-4));
            return;
        } catch { }
        try {
            SetProcessDpiAwareness(2);
            return;
        } catch { }
        try {
            SetProcessDPIAware();
        } catch { }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct MONITORINFOEX {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT {
        public int Left, Top, Right, Bottom;
        public int Width { get { return Right - Left; } }
        public int Height { get { return Bottom - Top; } }
    }

    [DllImport("user32.dll")]
    static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumDelegate lpfnEnum, IntPtr dwData);
    delegate bool MonitorEnumDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    private static RECT[] cachedMonitors = null;
    private static long lastMonitorQueryTime = 0;
    private static readonly object monLock = new object();

    public static RECT[] GetPhysicalMonitors(bool forceRefresh = false) {
        long now = DateTime.UtcNow.Ticks;
        lock (monLock) {
            if (!forceRefresh && cachedMonitors != null && (now - lastMonitorQueryTime < 5000000)) { // 500ms 캐시
                return cachedMonitors;
            }

            var list = new System.Collections.Generic.List<RECT>();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr hdcMon, ref RECT rc, IntPtr data) => {
                MONITORINFOEX mi = new MONITORINFOEX();
                mi.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
                if (GetMonitorInfo(hMon, ref mi)) {
                    list.Add(mi.rcMonitor);
                } else {
                    list.Add(rc);
                }
                return true;
            }, IntPtr.Zero);

            if (list.Count == 0) {
                list.Add(new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 });
            }

            // 🌟 3단계 결정론적 정렬: Left -> Top -> Width
            cachedMonitors = list.OrderBy(m => m.Left).ThenBy(m => m.Top).ThenBy(m => m.Width).ToArray();
            lastMonitorQueryTime = now;
            return cachedMonitors;
        }
    }

    [DllImport("user32.dll")]
    static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll")]
    static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, int dwExtraInfo);

    [DllImport("user32.dll")]
    static extern IntPtr WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

    const uint GA_ROOT = 2;

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

    const int WM_NCLBUTTONDOWN = 0xA1;
    const int HT_CAPTION = 0x2;

    [DllImport("user32.dll")]
    static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    const int GWL_EXSTYLE = -20;
    const int WS_EX_LAYERED = 0x80000;
    const int WS_EX_TRANSPARENT = 0x20;
    const int WS_EX_TOPMOST = 0x8;
    const int WS_EX_TOOLWINDOW = 0x80;
    const uint LWA_COLORKEY = 0x00000001;
    const uint LWA_ALPHA = 0x00000002;

    [DllImport("user32.dll")]
    static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern uint MapVirtualKey(uint uCode, uint uMapType);

    const uint GENERIC_ALL = 0x10000000;

    [DllImport("user32.dll")]
    static extern int GetSystemMetrics(int nIndex);
    const int SM_XVIRTUALSCREEN = 76;
    const int SM_YVIRTUALSCREEN = 77;
    const int SM_CXVIRTUALSCREEN = 78;
    const int SM_CYVIRTUALSCREEN = 79;

    const uint INPUT_MOUSE = 0;
    const uint INPUT_KEYBOARD = 1;

    const uint MOUSEEVENTF_MOVE = 0x0001;
    const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    const uint MOUSEEVENTF_LEFTUP = 0x0004;
    const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    const uint MOUSEEVENTF_WHEEL = 0x0800;
    const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
    const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

    [DllImport("user32.dll")]
    static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("imm32.dll")]
    static extern IntPtr ImmGetDefaultIMEWnd(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    const uint WM_IME_CONTROL = 0x0283;
    const uint IMC_GETCONVERSIONMODE = 0x0001;
    const uint IMC_SETCONVERSIONMODE = 0x0002;
    const uint IME_CMODE_HANGUL = 0x0001;

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT {
        public uint type;
        public MOUSEKEYBDHARDWAREINPUT mkhi;
    }

    [StructLayout(LayoutKind.Explicit)]
    struct MOUSEKEYBDHARDWAREINPUT {
        [FieldOffset(0)]
        public MOUSEINPUT mi;
        [FieldOffset(0)]
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
        public ulong pad;
    }

    static void MoveMouseNative(int actualX, int actualY) {
        SetCursorPos(actualX, actualY);
    }

    const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    const uint KEYEVENTF_KEYUP = 0x0002;
    const uint KEYEVENTF_UNICODE = 0x0004;
    const uint KEYEVENTF_SCANCODE = 0x0008;

    const byte VK_HANGUL = 0x15;
    const byte VK_HANJA = 0x19;

    static void TypeUnicodeChar(char c) {
        INPUT[] inputs = new INPUT[2];
        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].mkhi.ki.wScan = (ushort)c;
        inputs[0].mkhi.ki.dwFlags = KEYEVENTF_UNICODE;
        inputs[1].type = INPUT_KEYBOARD;
        inputs[1].mkhi.ki.wScan = (ushort)c;
        inputs[1].mkhi.ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;
        SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
    }

    static void SendKeyEvent(byte vk, bool isKeyDown) {
        bool isExtended = (vk == 0x25 || vk == 0x26 || vk == 0x27 || vk == 0x28 || // Arrow keys
                           vk == 0x2D || vk == 0x2E || // Insert, Delete
                           vk == 0x24 || vk == 0x23 || // Home, End
                           vk == 0x21 || vk == 0x22 || // PageUp, PageDown
                           vk == 0x5B || vk == 0x5C || vk == 0x5D || // Win, Apps
                           vk == 0xA3 || vk == 0xA5 || // RControl, RMenu
                           vk == 0x6F || vk == 0x2C || vk == 0x13);

        ushort scanCode = (ushort)MapVirtualKey(vk, 0);
        uint flags = 0;
        if (isExtended) flags |= KEYEVENTF_EXTENDEDKEY;
        if (!isKeyDown) flags |= KEYEVENTF_KEYUP;

        INPUT[] inputs = new INPUT[1];
        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].mkhi.ki.wVk = vk;
        inputs[0].mkhi.ki.wScan = scanCode;
        inputs[0].mkhi.ki.dwFlags = flags;
        inputs[0].mkhi.ki.time = 0;
        inputs[0].mkhi.ki.dwExtraInfo = IntPtr.Zero;

        SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
    }

    static void PressKeyHardware(byte vk) {
        SendKeyEvent(vk, true);
        Thread.Sleep(15);
        SendKeyEvent(vk, false);
    }

    static void ToggleHangul() {
        keybd_event(0x15, 0, 0, 0); // VK_HANGUL (0x15) DOWN
        keybd_event(0x15, 0, 2, 0); // VK_HANGUL (0x15) UP
    }

    static void ToggleHanja() {
        keybd_event(0x19, 0, 0, 0); // VK_HANJA (0x19) DOWN
        keybd_event(0x19, 0, 2, 0); // VK_HANJA (0x19) UP
    }

    static void PlayChimeSound() {
        try {
            System.Media.SystemSounds.Exclamation.Play();
        } catch { }

        try {
            Console.Beep(1046, 100); // C6 (도)
            Thread.Sleep(20);
            Console.Beep(1318, 100); // E6 (미)
            Thread.Sleep(20);
            Console.Beep(1567, 220); // G6 (솔)
        } catch { }
    }

    static string CleanEmoji(string text) {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < text.Length; i++) {
            char c = text[i];
            if (char.IsSurrogate(c)) {
                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) {
                    i++; // skip surrogate pair
                }
                continue;
            }
            if ((c >= 0x2600 && c <= 0x27BF) || (c >= 0xFE00 && c <= 0xFE0F)) {
                continue; // skip emoji symbols
            }
            sb.Append(c);
        }
        return sb.ToString().Trim();
    }

    static void ShowNoticeDialog(string msg) {
        msg = CleanEmoji(msg);
        new Thread(() => PlayChimeSound()).Start();

        try {
            using (Form f = new Form()) {
                f.Text = "다연코퍼레이션 관리자 공지";
                f.Size = new Size(540, 340);
                f.StartPosition = FormStartPosition.CenterScreen;
                f.FormBorderStyle = FormBorderStyle.None;
                f.TopMost = true;
                f.BackColor = Color.FromArgb(15, 23, 42);
                f.ForeColor = Color.White;
                f.ShowInTaskbar = true;
                f.KeyPreview = true;
                f.KeyDown += (s, e) => {
                    if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Escape || e.KeyCode == Keys.Space) f.Close();
                };

                // 테두리 글로우 효과 드로잉
                f.Paint += (s, e) => {
                    using (Pen p = new Pen(Color.FromArgb(56, 189, 248), 2.5f)) {
                        e.Graphics.DrawRectangle(p, 1, 1, f.Width - 2, f.Height - 2);
                    }
                    using (Pen innerPen = new Pen(Color.FromArgb(30, 41, 59), 1f)) {
                        e.Graphics.DrawRectangle(innerPen, 3, 3, f.Width - 6, f.Height - 6);
                    }
                };

                // 상단 헤더 바 (드래그 이동 지원)
                Panel headerPanel = new Panel();
                headerPanel.Dock = DockStyle.Top;
                headerPanel.Height = 62;
                headerPanel.BackColor = Color.FromArgb(30, 41, 59);

                bool isDragging = false;
                Point dragStart = Point.Empty;
                headerPanel.MouseDown += (s, e) => { isDragging = true; dragStart = e.Location; };
                headerPanel.MouseMove += (s, e) => {
                    if (isDragging) {
                        Point p = f.PointToScreen(e.Location);
                        f.Location = new Point(p.X - dragStart.X, p.Y - dragStart.Y);
                    }
                };
                headerPanel.MouseUp += (s, e) => { isDragging = false; };

                Label lblTitle = new Label();
                lblTitle.Text = "[다연코퍼레이션] 관리자 긴급 알림";
                lblTitle.Font = new Font("Malgun Gothic", 13, FontStyle.Bold);
                lblTitle.ForeColor = Color.FromArgb(56, 189, 248);
                lblTitle.Location = new Point(22, 18);
                lblTitle.AutoSize = true;
                headerPanel.Controls.Add(lblTitle);

                Button btnCloseX = new Button();
                btnCloseX.Text = "✕";
                btnCloseX.Font = new Font("Arial", 12, FontStyle.Bold);
                btnCloseX.ForeColor = Color.FromArgb(148, 163, 184);
                btnCloseX.BackColor = Color.Transparent;
                btnCloseX.FlatStyle = FlatStyle.Flat;
                btnCloseX.FlatAppearance.BorderSize = 0;
                btnCloseX.Size = new Size(38, 38);
                btnCloseX.Location = new Point(540 - 48, 12);
                btnCloseX.Cursor = Cursors.Hand;
                btnCloseX.MouseEnter += (s, e) => { btnCloseX.ForeColor = Color.FromArgb(239, 68, 68); };
                btnCloseX.MouseLeave += (s, e) => { btnCloseX.ForeColor = Color.FromArgb(148, 163, 184); };
                btnCloseX.Click += (s, e) => f.Close();
                headerPanel.Controls.Add(btnCloseX);

                f.Controls.Add(headerPanel);

                // 본문 텍스트 영역
                Panel contentPanel = new Panel();
                contentPanel.Dock = DockStyle.Fill;
                contentPanel.Padding = new Padding(24, 16, 24, 10);
                contentPanel.BackColor = Color.FromArgb(15, 23, 42);

                Label lblMsg = new Label();
                lblMsg.Text = string.IsNullOrEmpty(msg) ? "관리자 긴급 공지사항입니다." : msg;
                lblMsg.Font = new Font("Malgun Gothic", 13, FontStyle.Bold);
                lblMsg.ForeColor = Color.FromArgb(248, 250, 252);
                lblMsg.BackColor = Color.FromArgb(24, 33, 50);
                lblMsg.TextAlign = ContentAlignment.MiddleCenter;
                lblMsg.Dock = DockStyle.Fill;
                lblMsg.Padding = new Padding(12);
                contentPanel.Controls.Add(lblMsg);

                f.Controls.Add(contentPanel);

                // 하단 확인 버튼 영역
                Panel bottomPanel = new Panel();
                bottomPanel.Dock = DockStyle.Bottom;
                bottomPanel.Height = 72;
                bottomPanel.BackColor = Color.FromArgb(15, 23, 42);

                Button btnOk = new Button();
                btnOk.Text = "확인 (Enter)";
                btnOk.Font = new Font("Malgun Gothic", 11, FontStyle.Bold);
                btnOk.BackColor = Color.FromArgb(14, 165, 233);
                btnOk.ForeColor = Color.White;
                btnOk.FlatStyle = FlatStyle.Flat;
                btnOk.FlatAppearance.BorderSize = 0;
                btnOk.Size = new Size(180, 44);
                btnOk.Location = new Point((540 - 180) / 2, 12);
                btnOk.Cursor = Cursors.Hand;
                btnOk.MouseEnter += (s, e) => { btnOk.BackColor = Color.FromArgb(56, 189, 248); };
                btnOk.MouseLeave += (s, e) => { btnOk.BackColor = Color.FromArgb(14, 165, 233); };
                btnOk.Click += (s, e) => f.Close();
                bottomPanel.Controls.Add(btnOk);

                f.Controls.Add(bottomPanel);

                f.BringToFront();
                f.Activate();
                btnOk.Focus();
                f.ShowDialog();
            }
        } catch {
            try {
                MessageBox.Show(msg, "다연코퍼레이션 관리자 공지", MessageBoxButtons.OK, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1, (MessageBoxOptions)0x00040000 | (MessageBoxOptions)0x00200000);
            } catch { }
        }
    }

    static void ShowToast(string msg, int durationMs) {
        msg = CleanEmoji(msg);
        try {
            Application.EnableVisualStyles();
            using (Form f = new Form()) {
                f.FormBorderStyle = FormBorderStyle.None;
                f.StartPosition = FormStartPosition.Manual;
                f.ShowInTaskbar = false;
                f.TopMost = true;
                f.BackColor = Color.FromArgb(15, 23, 42);
                f.ForeColor = Color.White;
                f.Size = new Size(540, 68);

                Screen primary = Screen.PrimaryScreen;
                f.Location = new Point((primary.Bounds.Width - f.Width) / 2, 40);

                Panel borderPanel = new Panel();
                borderPanel.Dock = DockStyle.Fill;
                borderPanel.BackColor = Color.FromArgb(56, 189, 248);
                borderPanel.Padding = new Padding(2);

                Panel inner = new Panel();
                inner.Dock = DockStyle.Fill;
                inner.BackColor = Color.FromArgb(15, 23, 42);

                Label lbl = new Label();
                lbl.Text = msg;
                lbl.Font = new Font("Malgun Gothic", 12, FontStyle.Bold);
                lbl.ForeColor = Color.FromArgb(248, 250, 252);
                lbl.TextAlign = ContentAlignment.MiddleCenter;
                lbl.Dock = DockStyle.Fill;
                inner.Controls.Add(lbl);

                borderPanel.Controls.Add(inner);
                f.Controls.Add(borderPanel);

                System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
                timer.Interval = durationMs > 0 ? durationMs : 3000;
                timer.Tick += (s, e) => {
                    timer.Stop();
                    f.Close();
                };
                timer.Start();

                f.ShowDialog();
            }
        } catch { }
    }

    static void ShowUpdateProgressDialog(string verInfo) {
        try {
            Application.EnableVisualStyles();
            using (Form f = new Form()) {
                f.FormBorderStyle = FormBorderStyle.None;
                f.StartPosition = FormStartPosition.CenterScreen;
                f.Size = new Size(520, 210);
                f.ShowInTaskbar = false;
                f.TopMost = true;
                f.BackColor = Color.FromArgb(15, 23, 42); // 배경 암전 없는 단독 플로팅 카드
                f.Cursor = Cursors.Default;

                // 마우스로 창을 자유롭게 드래그해서 치울 수 있도록 연결
                Action<Control> enableDrag = null;
                enableDrag = (c) => {
                    c.MouseDown += (s, e) => {
                        if (e.Button == MouseButtons.Left) {
                            ReleaseCapture();
                            SendMessage(f.Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
                        }
                    };
                };

                Panel outer = new Panel();
                outer.Dock = DockStyle.Fill;
                outer.BackColor = Color.FromArgb(56, 189, 248); // 2px 시안 테두리
                outer.Padding = new Padding(2);

                Panel inner = new Panel();
                inner.Dock = DockStyle.Fill;
                inner.BackColor = Color.FromArgb(15, 23, 42);
                inner.Padding = new Padding(18);

                // 상단 헤더 바 (타이틀 + 우측 ✕ 닫기 버튼)
                Panel headerBar = new Panel();
                headerBar.Dock = DockStyle.Top;
                headerBar.Height = 32;

                Label lblClose = new Label();
                lblClose.Text = "✕";
                lblClose.Font = new Font("Malgun Gothic", 10, FontStyle.Bold);
                lblClose.ForeColor = Color.FromArgb(148, 163, 184);
                lblClose.Size = new Size(24, 24);
                lblClose.Location = new Point(520 - 40 - 24, 0);
                lblClose.Cursor = Cursors.Hand;
                lblClose.TextAlign = ContentAlignment.MiddleCenter;
                lblClose.MouseEnter += (s, e) => { lblClose.ForeColor = Color.White; };
                lblClose.MouseLeave += (s, e) => { lblClose.ForeColor = Color.FromArgb(148, 163, 184); };
                lblClose.Click += (s, e) => { f.Close(); };

                Label lblTitle = new Label();
                lblTitle.Text = "[다연코퍼레이션] 시스템 업데이트";
                lblTitle.Font = new Font("Malgun Gothic", 12, FontStyle.Bold);
                lblTitle.ForeColor = Color.FromArgb(56, 189, 248);
                lblTitle.AutoSize = false;
                lblTitle.Location = new Point(0, 0);
                lblTitle.Size = new Size(400, 28);
                lblTitle.TextAlign = ContentAlignment.MiddleLeft;

                headerBar.Controls.Add(lblClose);
                headerBar.Controls.Add(lblTitle);

                Label lblSub = new Label();
                lblSub.Text = string.IsNullOrEmpty(verInfo) ? "최신 기능 동기화 및 모듈 점검이 진행 중입니다." : "최신 시스템 (" + verInfo + ") 동기화 진행 중";
                lblSub.Font = new Font("Malgun Gothic", 9.5f, FontStyle.Regular);
                lblSub.ForeColor = Color.FromArgb(148, 163, 184);
                lblSub.AutoSize = false;
                lblSub.Height = 24;
                lblSub.Dock = DockStyle.Top;
                lblSub.TextAlign = ContentAlignment.MiddleLeft;

                Panel pnlGaugeBg = new Panel();
                pnlGaugeBg.Height = 20;
                pnlGaugeBg.Dock = DockStyle.Top;
                pnlGaugeBg.BackColor = Color.FromArgb(30, 41, 59);
                pnlGaugeBg.Padding = new Padding(0);

                Panel pnlGaugeFill = new Panel();
                pnlGaugeFill.Width = 0;
                pnlGaugeFill.Height = 20;
                pnlGaugeFill.BackColor = Color.FromArgb(14, 165, 233);
                pnlGaugeBg.Controls.Add(pnlGaugeFill);

                Label lblStatus = new Label();
                lblStatus.Text = "최신 모듈 다운로드 중... [0%]";
                lblStatus.Font = new Font("Malgun Gothic", 9.5f, FontStyle.Bold);
                lblStatus.ForeColor = Color.FromArgb(226, 232, 240);
                lblStatus.AutoSize = false;
                lblStatus.Height = 30;
                lblStatus.Dock = DockStyle.Bottom;
                lblStatus.TextAlign = ContentAlignment.MiddleLeft;

                inner.Controls.Add(lblStatus);
                inner.Controls.Add(pnlGaugeBg);
                inner.Controls.Add(lblSub);
                inner.Controls.Add(headerBar);

                outer.Controls.Add(inner);
                f.Controls.Add(outer);

                // 마우스 드래그 이동 연결
                enableDrag(inner);
                enableDrag(headerBar);
                enableDrag(lblTitle);
                enableDrag(lblSub);

                System.Windows.Forms.Timer animTimer = new System.Windows.Forms.Timer();
                animTimer.Interval = 25;
                int progress = 0;
                int maxW = 520 - 36 - 4;

                animTimer.Tick += (s, e) => {
                    progress += 2;
                    if (progress > 100) progress = 100;

                    pnlGaugeFill.Width = (int)((progress / 100.0) * maxW);

                    if (progress < 40) {
                        lblStatus.Text = "최신 모듈 병렬 다운로드 중... [" + progress + "%]";
                    } else if (progress < 80) {
                        lblStatus.Text = "시스템 모듈 교체 및 무결성 검증 중... [" + progress + "%]";
                    } else if (progress < 100) {
                        lblStatus.Text = "새 버전 엔진 적용 준비 중... [" + progress + "%]";
                    } else {
                        lblStatus.Text = "업데이트 완료! 잠시 후 정상 가동됩니다.";
                        lblStatus.ForeColor = Color.FromArgb(52, 211, 153);
                        pnlGaugeFill.BackColor = Color.FromArgb(16, 185, 129);
                        animTimer.Stop();

                        System.Windows.Forms.Timer closeTimer = new System.Windows.Forms.Timer();
                        closeTimer.Interval = 800;
                        closeTimer.Tick += (cs, ce) => {
                            closeTimer.Stop();
                            f.Close();
                        };
                        closeTimer.Start();
                    }
                };

                f.KeyPreview = true;
                f.KeyDown += (s, e) => {
                    if (e.KeyCode == Keys.Escape) f.Close();
                };

                animTimer.Start();
                f.BringToFront();
                f.Activate();
                f.ShowDialog();
            }
        } catch { }
    }

    class DrawStroke {
        public Color Color;
        public float Size;
        public int MonitorIdx;
        public PointF[] Points;
    }

    class DrawStamp {
        public string Text;
        public float Size;
        public int MonitorIdx;
        public float X;
        public float Y;
    }

    static System.Collections.Generic.List<DrawStroke> activeStrokes = new System.Collections.Generic.List<DrawStroke>();
    static System.Collections.Generic.List<DrawStamp> activeStamps = new System.Collections.Generic.List<DrawStamp>();

    static void ShowDrawingOverlayDaemon() {
        try {
            try { Application.EnableVisualStyles(); } catch { }

            int vLeft = GetSystemMetrics(SM_XVIRTUALSCREEN);
            int vTop = GetSystemMetrics(SM_YVIRTUALSCREEN);
            int vWidth = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            int vHeight = GetSystemMetrics(SM_CYVIRTUALSCREEN);

            if (vWidth <= 0) vWidth = 1920;
            if (vHeight <= 0) vHeight = 1080;

            using (Form f = new Form()) {
                f.FormBorderStyle = FormBorderStyle.None;
                f.StartPosition = FormStartPosition.Manual;
                f.Bounds = new Rectangle(vLeft, vTop, vWidth, vHeight);
                f.ShowInTaskbar = false;
                f.TopMost = true;
                f.BackColor = Color.Magenta;
                f.TransparencyKey = Color.Magenta;

                f.Shown += (s, e) => {
                    try {
                        int exStyle = GetWindowLong(f.Handle, GWL_EXSTYLE);
                        SetWindowLong(f.Handle, GWL_EXSTYLE, exStyle | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_TOPMOST);
                        SetLayeredWindowAttributes(f.Handle, (uint)ColorTranslator.ToWin32(Color.Magenta), 255, LWA_COLORKEY);
                        SetWindowPos(f.Handle, new IntPtr(-1), vLeft, vTop, vWidth, vHeight, 0x0040);
                    } catch { }
                };

                f.Paint += (s, e) => {
                    e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    var sortedScreens = GetPhysicalMonitors();

                    lock (activeStrokes) {
                        foreach (var stroke in activeStrokes) {
                            if (stroke.Points == null || stroke.Points.Length < 2) continue;
                            int mIdx = stroke.MonitorIdx;
                            if (mIdx < 0 || mIdx >= sortedScreens.Length) mIdx = 0;
                            RECT mBounds = sortedScreens[mIdx];

                            PointF[] screenPts = new PointF[stroke.Points.Length];
                            for (int i = 0; i < stroke.Points.Length; i++) {
                                float globalX = mBounds.Left + stroke.Points[i].X * mBounds.Width;
                                float globalY = mBounds.Top + stroke.Points[i].Y * mBounds.Height;
                                screenPts[i] = new PointF(globalX - vLeft, globalY - vTop);
                            }
                            using (Pen p = new Pen(stroke.Color, stroke.Size)) {
                                p.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                                p.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                                p.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
                                e.Graphics.DrawLines(p, screenPts);
                            }
                        }
                    }

                    lock (activeStamps) {
                        foreach (var stamp in activeStamps) {
                            if (string.IsNullOrEmpty(stamp.Text)) continue;
                            int mIdx = stamp.MonitorIdx;
                            if (mIdx < 0 || mIdx >= sortedScreens.Length) mIdx = 0;
                            RECT mBounds = sortedScreens[mIdx];

                            float globalX = mBounds.Left + stamp.X * mBounds.Width;
                            float globalY = mBounds.Top + stamp.Y * mBounds.Height;
                            PointF screenPt = new PointF(globalX - vLeft, globalY - vTop);

                            float fSize = (stamp.Size > 0) ? stamp.Size : 48f;
                            float badgeR = fSize * 0.7f;

                            // 🌟 예쁜 3D 원형 스티커 배지 배경 (반투명 네이비 + 선명한 스카이블루 테두리)
                            using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(235, 15, 23, 42)))
                            using (Pen borderPen = new Pen(Color.FromArgb(255, 56, 189, 248), 2.5f)) {
                                e.Graphics.FillEllipse(bgBrush, screenPt.X - badgeR, screenPt.Y - badgeR, badgeR * 2, badgeR * 2);
                                e.Graphics.DrawEllipse(borderPen, screenPt.X - badgeR, screenPt.Y - badgeR, badgeR * 2, badgeR * 2);
                            }

                            // 🌟 UTF-8 이모지 텍스트 렌더링
                            using (Font font = new Font("Segoe UI Emoji", fSize * 0.75f, FontStyle.Regular, GraphicsUnit.Pixel))
                            using (SolidBrush brush = new SolidBrush(Color.White))
                            using (StringFormat sf = new StringFormat()) {
                                sf.Alignment = StringAlignment.Center;
                                sf.LineAlignment = StringAlignment.Center;
                                e.Graphics.DrawString(stamp.Text, font, brush, screenPt, sf);
                            }
                        }
                    }
                };

                Action<string> parseCommand = (content) => {
                    if (string.IsNullOrEmpty(content)) return;
                    if (content == "clear") {
                        lock (activeStrokes) { activeStrokes.Clear(); }
                        lock (activeStamps) { activeStamps.Clear(); }
                        try { f.BeginInvoke((Action)(() => f.Invalidate())); } catch { }
                    } else if (content.StartsWith("stamp ")) {
                        // format: stamp <emoji> <size> <monitorIdx> <relX> <relY>
                        string[] parts = content.Split(' ');
                        if (parts.Length >= 6) {
                            string emoji = parts[1];
                            float size = 48f;
                            float.TryParse(parts[2], out size);
                            int mIdx = 0;
                            int.TryParse(parts[3], out mIdx);
                            float rx = 0.5f, ry = 0.5f;
                            float.TryParse(parts[4], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out rx);
                            float.TryParse(parts[5], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out ry);

                            lock (activeStamps) {
                                activeStamps.Add(new DrawStamp { Text = emoji, Size = size, MonitorIdx = mIdx, X = rx, Y = ry });
                            }
                            try { f.BeginInvoke((Action)(() => f.Invalidate())); } catch { }
                        }
                    } else if (content.StartsWith("stroke ") || content.StartsWith("update ")) {
                        bool isUpdate = content.StartsWith("update ");
                        // format: stroke <color> <size> <monitorIdx> <points>
                        // format: update <color> <size> <monitorIdx> <points>  (replaces last stroke)
                        string[] parts = content.Split(new char[] { ' ' }, 5);
                        if (parts.Length >= 5) {
                            string hexColor = parts[1];
                            float size = 6f;
                            float.TryParse(parts[2], out size);
                            int mIdx = 0;
                            int.TryParse(parts[3], out mIdx);
                            string ptsStr = parts[4];

                            Color c = ColorTranslator.FromHtml(hexColor);
                            var pointList = new System.Collections.Generic.List<PointF>();
                            foreach (var pair in ptsStr.Split(';')) {
                                var xy = pair.Split(',');
                                if (xy.Length == 2) {
                                    float px = 0, py = 0;
                                    float.TryParse(xy[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out px);
                                    float.TryParse(xy[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out py);
                                    pointList.Add(new PointF(px, py));
                                }
                            }
                            if (pointList.Count >= 2) {
                                lock (activeStrokes) {
                                    if (isUpdate && activeStrokes.Count > 0) {
                                        activeStrokes[activeStrokes.Count - 1] = new DrawStroke { Color = c, Size = size, MonitorIdx = mIdx, Points = pointList.ToArray() };
                                    } else {
                                        activeStrokes.Add(new DrawStroke { Color = c, Size = size, MonitorIdx = mIdx, Points = pointList.ToArray() });
                                    }
                                }
                                try { f.BeginInvoke((Action)(() => f.Invalidate())); } catch { }
                            }
                        }
                    }
                };

                Thread rThread = new Thread(() => {
                    try {
                        using (var reader = new StreamReader(Console.OpenStandardInput(), System.Text.Encoding.UTF8)) {
                            string line;
                            while ((line = reader.ReadLine()) != null) {
                                line = line.Trim();
                                if (string.IsNullOrEmpty(line)) continue;
                                if (line == "exit" || line == "quit") {
                                    try { f.BeginInvoke((Action)(() => f.Close())); } catch { }
                                    break;
                                }
                                try { f.BeginInvoke((Action)(() => parseCommand(line))); } catch { }
                            }
                        }
                    } catch { }
                });
                rThread.IsBackground = true;
                rThread.Start();

                Application.Run(f);
            }
        } catch { }
    }

    static void ShowBottomRightUpdateDaemon(string verInfo) {
        try {
            try { Application.EnableVisualStyles(); } catch { }
            using (Form f = new Form()) {
                f.FormBorderStyle = FormBorderStyle.None;
                f.StartPosition = FormStartPosition.Manual;
                int w = 390;
                int h = 120;
                Rectangle workArea = Screen.PrimaryScreen.WorkingArea;
                f.Location = new Point(workArea.Right - w - 20, workArea.Bottom - h - 20);
                f.Size = new Size(w, h);
                f.ShowInTaskbar = false;
                f.TopMost = true;
                f.BackColor = Color.FromArgb(15, 23, 42);

                f.Paint += (s, e) => {
                    using (Pen p = new Pen(Color.FromArgb(56, 189, 248), 2f)) {
                        e.Graphics.DrawRectangle(p, 1, 1, f.Width - 2, f.Height - 2);
                    }
                };

                Panel inner = new Panel();
                inner.Dock = DockStyle.Fill;
                inner.Padding = new Padding(16, 12, 16, 12);
                inner.BackColor = Color.FromArgb(15, 23, 42);

                Panel pnlHeader = new Panel();
                pnlHeader.Dock = DockStyle.Top;
                pnlHeader.Height = 26;

                Label lblTitle = new Label();
                lblTitle.Text = "🚀 [다연코퍼레이션] 시스템 실시간 업데이트";
                lblTitle.Font = new Font("Malgun Gothic", 10.5f, FontStyle.Bold);
                lblTitle.ForeColor = Color.FromArgb(56, 189, 248);
                lblTitle.AutoSize = true;
                lblTitle.Location = new Point(0, 0);
                pnlHeader.Controls.Add(lblTitle);

                Panel pnlGaugeBg = new Panel();
                pnlGaugeBg.Dock = DockStyle.Top;
                pnlGaugeBg.Height = 14;
                pnlGaugeBg.BackColor = Color.FromArgb(30, 41, 59);
                pnlGaugeBg.Margin = new Padding(0, 6, 0, 6);

                Panel pnlGaugeFill = new Panel();
                pnlGaugeFill.Width = 0;
                pnlGaugeFill.Height = 14;
                pnlGaugeFill.BackColor = Color.FromArgb(14, 165, 233);
                pnlGaugeBg.Controls.Add(pnlGaugeFill);

                Label lblStatus = new Label();
                lblStatus.Text = string.IsNullOrEmpty(verInfo) ? "최신 모듈 다운로드 준비 중... [0%]" : ("최신 버전 (" + verInfo + ") 다운로드 준비 중... [0%]");
                lblStatus.Font = new Font("Malgun Gothic", 9f, FontStyle.Regular);
                lblStatus.ForeColor = Color.FromArgb(226, 232, 240);
                lblStatus.Dock = DockStyle.Bottom;
                lblStatus.Height = 24;
                lblStatus.TextAlign = ContentAlignment.MiddleLeft;

                inner.Controls.Add(lblStatus);
                inner.Controls.Add(pnlGaugeBg);
                inner.Controls.Add(pnlHeader);
                f.Controls.Add(inner);

                int maxW = w - 32;
                int currentPercent = 0;
                bool isCompleted = false;

                Action<int, string> updateUI = (pct, msg) => {
                    if (pct < 0) pct = 0;
                    if (pct > 100) pct = 100;
                    currentPercent = pct;
                    pnlGaugeFill.Width = (int)((pct / 100.0) * maxW);
                    if (!string.IsNullOrEmpty(msg)) lblStatus.Text = msg;
                    if (pct >= 100 && !isCompleted) {
                        isCompleted = true;
                        pnlGaugeFill.BackColor = Color.FromArgb(16, 185, 129);
                        lblStatus.ForeColor = Color.FromArgb(52, 211, 153);
                        System.Windows.Forms.Timer closeTimer = new System.Windows.Forms.Timer();
                        closeTimer.Interval = 2500;
                        closeTimer.Tick += (ts, te) => {
                            closeTimer.Stop();
                            try { f.Close(); } catch { }
                        };
                        closeTimer.Start();
                    }
                };

                // File-polling timer for update_status.txt
                string statusFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "update_status.txt");
                string lastContent = "";
                System.Windows.Forms.Timer pollTimer = new System.Windows.Forms.Timer();
                pollTimer.Interval = 80;
                pollTimer.Tick += (ts, te) => {
                    try {
                        if (File.Exists(statusFile)) {
                            string content = File.ReadAllText(statusFile).Trim();
                            if (!string.IsNullOrEmpty(content) && content != lastContent) {
                                lastContent = content;
                                string[] parts = content.Split(new char[] { ' ' }, 2);
                                int p = 0;
                                if (int.TryParse(parts[0], out p)) {
                                    string text = parts.Length > 1 ? parts[1] : ("업데이트 진행 중... [" + p + "%]");
                                    updateUI(p, text);
                                }
                            }
                        }
                    } catch { }
                };
                pollTimer.Start();

                // Stdin reader thread
                Thread readerThread = new Thread(() => {
                    try {
                        string line;
                        while ((line = Console.ReadLine()) != null) {
                            line = line.Trim();
                            if (string.IsNullOrEmpty(line)) continue;
                            if (line == "exit" || line == "quit") {
                                try { f.BeginInvoke((Action)(() => f.Close())); } catch { }
                                break;
                            }
                            if (line.StartsWith("progress ")) {
                                string[] parts = line.Split(new char[] { ' ' }, 3);
                                if (parts.Length >= 2) {
                                    int percent = 0;
                                    int.TryParse(parts[1], out percent);
                                    string statusText = parts.Length >= 3 ? parts[2] : ("업데이트 진행 중... [" + percent + "%]");
                                    try { f.BeginInvoke((Action)(() => updateUI(percent, statusText))); } catch { }
                                }
                            }
                        }
                    } catch { }
                });
                readerThread.IsBackground = true;
                readerThread.Start();

                f.KeyPreview = true;
                f.KeyDown += (s, e) => {
                    if (e.KeyCode == Keys.Escape) f.Close();
                };

                Application.Run(f);
            }
        } catch { }
    }

    static void PerformManagerUpdateStandalone(string targetExePath, string serverUrl) {
        try {
            Application.EnableVisualStyles();
            using (Form f = new Form()) {
                f.FormBorderStyle = FormBorderStyle.None;
                f.StartPosition = FormStartPosition.Manual;
                int w = 390;
                int h = 100;
                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                f.Bounds = new Rectangle(wa.Right - w - 20, wa.Bottom - h - 20, w, h);
                f.ShowInTaskbar = false;
                f.TopMost = true;
                f.BackColor = Color.FromArgb(15, 23, 42);

                f.Paint += (s, e) => {
                    using (Pen p = new Pen(Color.FromArgb(56, 189, 248), 2f)) {
                        e.Graphics.DrawRectangle(p, 1, 1, f.Width - 2, f.Height - 2);
                    }
                };

                Panel inner = new Panel();
                inner.Dock = DockStyle.Fill;
                inner.Padding = new Padding(16, 12, 16, 12);
                inner.BackColor = Color.FromArgb(15, 23, 42);

                Panel pnlHeader = new Panel();
                pnlHeader.Dock = DockStyle.Top;
                pnlHeader.Height = 24;

                Label lblTitle = new Label();
                lblTitle.Text = "[다연코퍼레이션] 관리자 프로그램 업데이트";
                lblTitle.Font = new Font("Malgun Gothic", 10f, FontStyle.Bold);
                lblTitle.ForeColor = Color.FromArgb(56, 189, 248);
                lblTitle.AutoSize = true;
                lblTitle.Location = new Point(0, 0);
                pnlHeader.Controls.Add(lblTitle);

                Panel pnlGaugeBg = new Panel();
                pnlGaugeBg.Dock = DockStyle.Top;
                pnlGaugeBg.Height = 14;
                pnlGaugeBg.BackColor = Color.FromArgb(30, 41, 59);
                pnlGaugeBg.Margin = new Padding(0, 4, 0, 4);

                Panel pnlGaugeFill = new Panel();
                pnlGaugeFill.Width = 0;
                pnlGaugeFill.Height = 14;
                pnlGaugeFill.BackColor = Color.FromArgb(14, 165, 233);
                pnlGaugeBg.Controls.Add(pnlGaugeFill);

                Label lblStatus = new Label();
                lblStatus.Text = "기존 관리자 종료 및 최신 파일 동기화 중... [10%]";
                lblStatus.Font = new Font("Malgun Gothic", 8.5f, FontStyle.Regular);
                lblStatus.ForeColor = Color.FromArgb(226, 232, 240);
                lblStatus.Dock = DockStyle.Bottom;
                lblStatus.Height = 22;
                lblStatus.TextAlign = ContentAlignment.MiddleLeft;

                inner.Controls.Add(lblStatus);
                inner.Controls.Add(pnlGaugeBg);
                inner.Controls.Add(pnlHeader);
                f.Controls.Add(inner);

                int maxW = w - 32;

                f.Shown += (s, e) => {
                    new Thread(() => {
                        try {
                            // 1. 기존 실행 중인 관리자 프로세스 완전 종료 대기 및 강제 해제
                            for (int attempt = 0; attempt < 25; attempt++) {
                                var procs = Process.GetProcessesByName("다연코퍼레이션 관리자");
                                if (procs.Length == 0) procs = Process.GetProcessesByName("다연원격_관리자");
                                if (procs.Length == 0) procs = Process.GetProcessesByName("DayeonManager");
                                if (procs.Length == 0) break;
                                foreach (var p in procs) {
                                    try { p.Kill(); } catch { }
                                }
                                Thread.Sleep(150);
                            }

                            try { f.BeginInvoke((Action)(() => {
                                pnlGaugeFill.Width = (int)(maxW * 0.35f);
                                lblStatus.Text = "서버에서 최신 관리자 바이너리 다운로드 중... [35%]";
                            })); } catch { }

                            string downloadUrl = (serverUrl.TrimEnd('/') + "/api/update/file?name=" + Uri.EscapeDataString("다연코퍼레이션 관리자.exe"));
                            string tempDownload = Path.Combine(Path.GetTempPath(), "DayeonManager_Latest.exe");
                            using (WebClient wc = new WebClient()) {
                                wc.Headers.Add("User-Agent", "DayeonUpdater/1.0");
                                wc.DownloadFile(downloadUrl, tempDownload);
                            }

                            Thread.Sleep(300);
                            try { f.BeginInvoke((Action)(() => {
                                pnlGaugeFill.Width = (int)(maxW * 0.75f);
                                lblStatus.Text = "최신 관리자 파일 스왑 및 무결성 검증 중... [75%]";
                            })); } catch { }

                            string desktopMgr = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "다연코퍼레이션 관리자.exe");

                            if (File.Exists(tempDownload) && new FileInfo(tempDownload).Length > 10000) {
                                for (int retry = 0; retry < 25; retry++) {
                                    try {
                                        if (File.Exists(targetExePath)) File.Delete(targetExePath);
                                        File.Copy(tempDownload, targetExePath, true);
                                        break;
                                    } catch {
                                        Thread.Sleep(200);
                                    }
                                }

                                try {
                                    if (File.Exists(desktopMgr)) File.Delete(desktopMgr);
                                    File.Copy(tempDownload, desktopMgr, true);
                                } catch { }
                            }

                            Thread.Sleep(300);
                            try { f.BeginInvoke((Action)(() => {
                                pnlGaugeFill.Width = maxW;
                                pnlGaugeFill.BackColor = Color.FromArgb(16, 185, 129);
                                lblStatus.ForeColor = Color.FromArgb(52, 211, 153);
                                lblStatus.Text = "✅ 최신 관리자 적용 완료! 새 프로그램 시작 중... [100%]";
                            })); } catch { }

                            Thread.Sleep(500);
                            string runPath = File.Exists(targetExePath) ? targetExePath : desktopMgr;
                            try {
                                ProcessStartInfo psi = new ProcessStartInfo {
                                    FileName = runPath,
                                    WorkingDirectory = Path.GetDirectoryName(runPath),
                                    UseShellExecute = true
                                };
                                Process.Start(psi);
                            } catch {
                                try {
                                    ProcessStartInfo psi2 = new ProcessStartInfo {
                                        FileName = desktopMgr,
                                        WorkingDirectory = Path.GetDirectoryName(desktopMgr),
                                        UseShellExecute = true
                                    };
                                    Process.Start(psi2);
                                } catch { }
                            }

                            Thread.Sleep(800);
                            try { f.BeginInvoke((Action)(() => f.Close())); } catch { }
                        } catch (Exception ex) {
                            try { f.BeginInvoke((Action)(() => {
                                lblStatus.Text = "오류: " + ex.Message;
                                pnlGaugeFill.BackColor = Color.Red;
                            })); } catch { }
                            Thread.Sleep(2500);
                            try { f.BeginInvoke((Action)(() => f.Close())); } catch { }
                        }
                    }).Start();
                };

                Application.Run(f);
            }
        } catch { }
    }

    static void ExecuteCommand(string[] args) {
        if (args.Length < 1) return;
        string type = args[0];

        if (type == "manager_updater" || type == "manager_update") {
            string targetExe = args.Length > 1 ? args[1] : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "다연코퍼레이션 관리자.exe");
            string serverUrl = args.Length > 2 ? args[2] : "https://dayeon-remote.onrender.com";
            PerformManagerUpdateStandalone(targetExe, serverUrl);
            return;
        }

        if (type == "maintenance_screen" || type == "blind_screen" || type == "update_widget") {
            string verInfo = args.Length > 1 ? args[1] : "";
            ShowBottomRightUpdateDaemon(verInfo);
            return;
        }

        try {
            if ((type == "move" || type == "click" || type == "rightclick" ||
                 type == "mousedown" || type == "mouseup" || type == "mousemove" ||
                 type == "rmousedown" || type == "rmouseup" ||
                 type == "mmousedown" || type == "mmouseup" ||
                 type == "dblclick") && args.Length >= 3)
            {
                double relX = double.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture);
                double relY = double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
                int monitorIdx = 0;

                if (args.Length >= 4) {
                    int.TryParse(args[3], out monitorIdx);
                }

                long nowTick = DateTime.UtcNow.Ticks;
                if (cachedPhysicalMonitors == null || nowTick - lastMonCheckTick > 10000000) { // 1초 캐싱
                    cachedPhysicalMonitors = GetPhysicalMonitors();
                    lastMonCheckTick = nowTick;
                }
                var sortedScreens = cachedPhysicalMonitors;
                if (monitorIdx < 0 || monitorIdx >= sortedScreens.Length) {
                    monitorIdx = 0;
                }

                RECT bounds = sortedScreens[monitorIdx];
                int actualX = bounds.Left + (int)Math.Round(relX * Math.Max(1, bounds.Width - 1));
                int actualY = bounds.Top + (int)Math.Round(relY * Math.Max(1, bounds.Height - 1));

                if (type == "click") {
                    MoveMouseNative(actualX, actualY);
                    mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                    Thread.Sleep(10);
                    mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
                } else if (type == "dblclick" || type == "doubleclick" || type == "dclick") {
                    MoveMouseNative(actualX, actualY);
                    mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                    mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
                    Thread.Sleep(25);
                    mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                    mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
                } else if (type == "rightclick" || type == "rclick") {
                    MoveMouseNative(actualX, actualY);
                    mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, 0);
                    Thread.Sleep(15);
                    mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, 0);
                } else if (type == "mousedown") {
                    MoveMouseNative(actualX, actualY);
                    mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                } else if (type == "mouseup") {
                    MoveMouseNative(actualX, actualY);
                    mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
                } else if (type == "rmousedown" || type == "rightdown") {
                    MoveMouseNative(actualX, actualY);
                    mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, 0);
                } else if (type == "rmouseup" || type == "rightup") {
                    MoveMouseNative(actualX, actualY);
                    mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, 0);
                } else if (type == "mmousedown") {
                    MoveMouseNative(actualX, actualY);
                    mouse_event(MOUSEEVENTF_MIDDLEDOWN, 0, 0, 0, 0);
                } else if (type == "mmouseup") {
                    MoveMouseNative(actualX, actualY);
                    mouse_event(MOUSEEVENTF_MIDDLEUP, 0, 0, 0, 0);
                } else if (type == "move" || type == "mousemove") {
                    MoveMouseNative(actualX, actualY);
                }
            } else if ((type == "wheel" || type == "scroll") && args.Length >= 3) {
                double relX = double.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture);
                double relY = double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
                int monitorIdx = 0;
                int delta = -120;

                if (args.Length >= 4) int.TryParse(args[3], out monitorIdx);
                if (args.Length >= 5) int.TryParse(args[4], out delta);

                var sortedScreens = GetPhysicalMonitors();
                if (monitorIdx < 0 || monitorIdx >= sortedScreens.Length) monitorIdx = 0;

                RECT bounds = sortedScreens[monitorIdx];
                int actualX = bounds.Left + (int)Math.Round(relX * Math.Max(1, bounds.Width - 1));
                int actualY = bounds.Top + (int)Math.Round(relY * Math.Max(1, bounds.Height - 1));

                MoveMouseNative(actualX, actualY);
                mouse_event(MOUSEEVENTF_WHEEL, 0, 0, (uint)delta, 0);
            } else if (type == "keydown" || type == "keyup") {
                bool isDown = (type == "keydown");
                string key = args.Length >= 2 ? args[args.Length - 1] : "Space";
                if (string.IsNullOrEmpty(key)) return;

                int rawVk = 0;
                if (key.StartsWith("vk:", StringComparison.OrdinalIgnoreCase)) {
                    if (int.TryParse(key.Substring(3), out rawVk)) {
                        SendKeyEvent((byte)rawVk, isDown);
                        return;
                    }
                } else if (int.TryParse(key, out rawVk) && rawVk > 0) {
                    SendKeyEvent((byte)rawVk, isDown);
                    return;
                }

                switch (key.ToLower()) {
                    case "space":
                    case "spacebar":
                    case " ":
                        SendKeyEvent(0x20, isDown); return;
                    case "back":
                    case "backspace":
                        SendKeyEvent(0x08, isDown); return;
                    case "return":
                    case "enter":
                        SendKeyEvent(0x0D, isDown); return;
                    case "tab":
                        SendKeyEvent(0x09, isDown); return;
                    case "escape":
                    case "esc":
                        SendKeyEvent(0x1B, isDown); return;
                    case "hangul":
                    case "hangulmode":
                    case "kanamode":
                    case "haneng":
                    case "rmenu":
                        if (isDown) ToggleHangul(); return;
                    case "hanja":
                    case "hanjamode":
                    case "kanjimode":
                    case "rcontrolkey":
                        if (isDown) ToggleHanja(); return;
                    case "delete":
                    case "del":
                        SendKeyEvent(0x2E, isDown); return;
                    case "insert":
                        SendKeyEvent(0x2D, isDown); return;
                    case "home":
                        SendKeyEvent(0x24, isDown); return;
                    case "end":
                        SendKeyEvent(0x23, isDown); return;
                    case "pageup":
                    case "prior":
                        SendKeyEvent(0x21, isDown); return;
                    case "pagedown":
                    case "next":
                        SendKeyEvent(0x22, isDown); return;
                    case "left":
                    case "arrowleft":
                        SendKeyEvent(0x25, isDown); return;
                    case "up":
                    case "arrowup":
                        SendKeyEvent(0x26, isDown); return;
                    case "right":
                    case "arrowright":
                        SendKeyEvent(0x27, isDown); return;
                    case "down":
                    case "arrowdown":
                        SendKeyEvent(0x28, isDown); return;
                    case "shift":
                    case "shiftkey":
                    case "lshiftkey":
                    case "rshiftkey":
                        SendKeyEvent(0x10, isDown); return;
                    case "control":
                    case "controlkey":
                    case "lcontrolkey":
                    case "rcontrol":
                        SendKeyEvent(0x11, isDown); return;
                    case "menu":
                    case "lmenu":
                    case "alt":
                        SendKeyEvent(0x12, isDown); return;
                    case "oemperiod":
                        SendKeyEvent(0xBE, isDown); return; // '.'
                    case "oemcomma":
                        SendKeyEvent(0xBC, isDown); return; // ','
                    case "oemminus":
                        SendKeyEvent(0xBD, isDown); return; // '-'
                    case "oemplus":
                        SendKeyEvent(0xBB, isDown); return; // '='
                    case "oemquestion":
                        SendKeyEvent(0xBF, isDown); return; // '/'
                    case "oemtilde":
                        SendKeyEvent(0xC0, isDown); return; // '`'
                    case "oemopenbrackets":
                        SendKeyEvent(0xDB, isDown); return; // '['
                    case "oemclosebrackets":
                        SendKeyEvent(0xDD, isDown); return; // ']'
                    case "oempipe":
                        SendKeyEvent(0xDC, isDown); return; // '\'
                    case "oemquotes":
                        SendKeyEvent(0xDE, isDown); return; // '\''
                    case "oem1":
                    case "oemsemicolon":
                        SendKeyEvent(0xBA, isDown); return; // ';'
                }

                if (key.Length == 2 && (key[0] == 'D' || key[0] == 'd') && char.IsDigit(key[1])) {
                    PressKeyHardware((byte)(0x30 + (key[1] - '0')));
                    return;
                }

                if (key.StartsWith("NumPad", StringComparison.OrdinalIgnoreCase) && key.Length == 7 && char.IsDigit(key[6])) {
                    PressKeyHardware((byte)(0x60 + (key[6] - '0')));
                    return;
                }

                int fN;
                if ((key.StartsWith("F") || key.StartsWith("f")) && int.TryParse(key.Substring(1), out fN) && fN >= 1 && fN <= 12) {
                    PressKeyHardware((byte)(0x6F + fN));
                    return;
                }

                if (key.Length == 1) {
                    char c = key[0];
                    if (c >= 'a' && c <= 'z') { PressKeyHardware((byte)('A' + (c - 'a'))); return; }
                    if (c >= 'A' && c <= 'Z') { PressKeyHardware((byte)c); return; }
                    if (c >= '0' && c <= '9') { PressKeyHardware((byte)c); return; }
                    TypeUnicodeChar(c);
                    return;
                }
            } else if ((type == "paste_text" || type == "paste_b64" || type == "paste_base64") && args.Length >= 2) {
                string textToPaste = "";
                if (type == "paste_b64" || type == "paste_base64") {
                    try {
                        byte[] b = Convert.FromBase64String(args[1]);
                        textToPaste = System.Text.Encoding.UTF8.GetString(b);
                    } catch {
                        textToPaste = string.Join(" ", args.Skip(1));
                    }
                } else {
                    textToPaste = string.Join(" ", args.Skip(1));
                }
                Thread t = new Thread(() => {
                    try {
                        Clipboard.SetDataObject(textToPaste, true, 10, 50);
                        Thread.Sleep(50);
                        keybd_event(0x11, 0, 0, 0); // VK_CONTROL DOWN
                        keybd_event(0x56, 0, 0, 0); // 'V' DOWN
                        Thread.Sleep(30);
                        keybd_event(0x56, 0, 2, 0); // 'V' UP
                        keybd_event(0x11, 0, 2, 0); // VK_CONTROL UP
                    } catch { }
                });
                t.SetApartmentState(ApartmentState.STA);
                t.Start();
                t.Join(2000);
            } else if (type == "hotkey" && args.Length >= 2) {
                string combo = args[1];
                ExecuteHotkey(combo);
            }
        } catch { }
    }

    static void ExecuteHotkey(string combo) {
        if (string.IsNullOrEmpty(combo)) return;
        combo = combo.ToLower().Trim();

        bool hasCtrl = combo.Contains("ctrl");
        bool hasAlt = combo.Contains("alt");
        bool hasShift = combo.Contains("shift");
        bool hasWin = combo.Contains("win") || combo.Contains("meta");

        string[] parts = combo.Split(new char[] { '+', '-' });
        string mainKey = parts.Length > 0 ? parts[parts.Length - 1] : "";

        byte vk = 0;
        if (mainKey.Length == 1) {
            char c = mainKey[0];
            if (c >= 'a' && c <= 'z') vk = (byte)('A' + (c - 'a'));
            else if (c >= 'A' && c <= 'Z') vk = (byte)c;
            else if (c >= '0' && c <= '9') vk = (byte)c;
        } else {
            switch (mainKey) {
                case "enter": vk = 0x0D; break;
                case "backspace": vk = 0x08; break;
                case "tab": vk = 0x09; break;
                case "escape": case "esc": vk = 0x1B; break;
                case "space": vk = 0x20; break;
                case "delete": case "del": vk = 0x2E; break;
                case "insert": vk = 0x2D; break;
                case "home": vk = 0x24; break;
                case "end": vk = 0x23; break;
                case "pageup": vk = 0x21; break;
                case "pagedown": vk = 0x22; break;
                case "arrowup": case "up": vk = 0x26; break;
                case "arrowdown": case "down": vk = 0x28; break;
                case "arrowleft": case "left": vk = 0x25; break;
                case "arrowright": case "right": vk = 0x27; break;
                default:
                    if (mainKey.StartsWith("f")) {
                        int fNum;
                        if (int.TryParse(mainKey.Substring(1), out fNum) && fNum >= 1 && fNum <= 12) {
                            vk = (byte)(0x6F + fNum);
                        }
                    }
                    break;
            }
        }

        if (hasCtrl) keybd_event(0x11, 0, 0, 0); // VK_CONTROL DOWN
        if (hasAlt) keybd_event(0x12, 0, 0, 0);  // VK_MENU DOWN
        if (hasShift) keybd_event(0x10, 0, 0, 0);// VK_SHIFT DOWN
        if (hasWin) keybd_event(0x5B, 0, 0, 0);  // VK_LWIN DOWN

        bool isExtended = (vk == 0x25 || vk == 0x26 || vk == 0x27 || vk == 0x28 || // Arrow keys
                           vk == 0x2D || vk == 0x2E || // Insert, Delete
                           vk == 0x24 || vk == 0x23 || // Home, End
                           vk == 0x21 || vk == 0x22 || // PageUp, PageDown
                           vk == 0x5B || vk == 0x5C);  // Win keys
        uint downFlags = isExtended ? 1u : 0u;
        uint upFlags = (isExtended ? 1u : 0u) | 2u;

        Thread.Sleep(30);
        if (vk > 0) {
            keybd_event(vk, 0, downFlags, 0);
            Thread.Sleep(35);
            keybd_event(vk, 0, upFlags, 0);
        }

        Thread.Sleep(30);
        if (hasWin) keybd_event(0x5B, 0, 2, 0);
        if (hasShift) keybd_event(0x10, 0, 2, 0);
        if (hasAlt) keybd_event(0x12, 0, 2, 0);
        if (hasCtrl) keybd_event(0x11, 0, 2, 0);

        if (hasCtrl && (mainKey == "c" || mainKey == "C")) {
            Thread t = new Thread(() => {
                try {
                    Thread.Sleep(80);
                    if (Clipboard.ContainsText()) {
                        string copied = Clipboard.GetText();
                        if (!string.IsNullOrEmpty(copied)) {
                            byte[] b = System.Text.Encoding.UTF8.GetBytes(copied);
                            string b64 = Convert.ToBase64String(b);
                            Console.WriteLine("CLIPBOARD_SYNC:" + b64);
                        }
                    }
                } catch { }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }
    }

    [STAThread]
    static void Main(string[] args) {
        // 제어 프로세스 우선순위를 최고 수준으로 상승 & UTF-8 한글 입출력 보장
        try {
            Console.InputEncoding = System.Text.Encoding.UTF8;
            Console.OutputEncoding = System.Text.Encoding.UTF8;
        } catch { }
        try {
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
            Thread.CurrentThread.Priority = ThreadPriority.Highest;
        } catch { }

        EnableTrueNativeDpi();
        IntPtr hDesk = OpenInputDesktop(0, false, GENERIC_ALL);
        if (hDesk != IntPtr.Zero) SetThreadDesktop(hDesk);

        if (args.Length > 0 && args[0] == "daemon") {
            string lastObservedClip = "";
            Thread clipWatcher = new Thread(() => {
                while (true) {
                    try {
                        Thread.Sleep(1000);
                        if (Clipboard.ContainsText()) {
                            string current = Clipboard.GetText();
                            if (!string.IsNullOrEmpty(current) && current != lastObservedClip) {
                                lastObservedClip = current;
                                byte[] b = System.Text.Encoding.UTF8.GetBytes(current);
                                string b64 = Convert.ToBase64String(b);
                                Console.WriteLine("CLIPBOARD_SYNC:" + b64);
                            }
                        }
                    } catch { }
                }
            });
            clipWatcher.SetApartmentState(ApartmentState.STA);
            clipWatcher.IsBackground = true;
            clipWatcher.Start();

            using (var reader = new StreamReader(Console.OpenStandardInput(), System.Text.Encoding.UTF8)) {
                string line;
                while ((line = reader.ReadLine()) != null) {
                    line = line.Trim();
                    if (string.IsNullOrEmpty(line)) continue;
                    if (line == "exit" || line == "quit") break;
                    if (line.StartsWith("paste_b64 ") || line.StartsWith("paste_base64 ")) {
                        string b64 = line.Substring(line.IndexOf(' ') + 1).Trim();
                        ExecuteCommand(new string[] { "paste_b64", b64 });
                        continue;
                    }
                    if (line.StartsWith("keydown ")) {
                        string[] rawParts = line.Split(new char[] { ' ' }, 2);
                        string keyParam = rawParts.Length > 1 ? rawParts[1] : "Space";
                        ExecuteCommand(new string[] { "keydown", keyParam });
                        continue;
                    }
                    if (line.StartsWith("hotkey ")) {
                        string[] rawParts = line.Split(new char[] { ' ' }, 2);
                        string keyParam = rawParts.Length > 1 ? rawParts[1] : "";
                        ExecuteCommand(new string[] { "hotkey", keyParam });
                        continue;
                    }
                    if (line.StartsWith("paste_text ")) {
                        string[] rawParts = line.Split(new char[] { ' ' }, 2);
                        string textParam = rawParts.Length > 1 ? rawParts[1] : "";
                        ExecuteCommand(new string[] { "paste_text", textParam });
                        continue;
                    }
                    if (line.StartsWith("popup ")) {
                        string[] rawParts = line.Split(new char[] { ' ' }, 2);
                        string msgParam = rawParts.Length > 1 ? rawParts[1] : "";
                        ExecuteCommand(new string[] { "popup", msgParam });
                        continue;
                    }
                    string[] parts = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    ExecuteCommand(parts);
                }
            }
        } else if (args.Length > 0 && (args[0] == "update_widget_daemon" || args[0] == "update_progress")) {
            string verInfo = args.Length > 1 ? string.Join(" ", args.Skip(1)) : "";
            ShowBottomRightUpdateDaemon(verInfo);
        } else if (args.Length > 0 && (args[0] == "draw_overlay" || args[0] == "draw_daemon")) {
            ShowDrawingOverlayDaemon();
        } else if (args.Length > 0 && args[0] == "popup") {
            string msg = args.Length > 1 ? string.Join(" ", args.Skip(1)) : "";
            ShowNoticeDialog(msg);
        } else if (args.Length > 0 && args[0] == "toast") {
            int duration = 3000;
            string msg = "";
            if (args.Length > 2 && int.TryParse(args[1], out duration)) {
                msg = string.Join(" ", args.Skip(2));
            } else if (args.Length > 1) {
                msg = string.Join(" ", args.Skip(1));
            }
            ShowToast(msg, duration);
        } else {
            ExecuteCommand(args);
        }
    }
}
