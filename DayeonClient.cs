using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace DayeonRemoteClient {
    public class Program {
        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod", SetLastError = true)]
        static extern uint timeBeginPeriod(uint uMilliseconds);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SetProcessDPIAware();

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

        [DllImport("shcore.dll", SetLastError = true)]
        static extern int SetProcessDpiAwareness(int awareness);

        [DllImport("user32.dll")]
        static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);

        [DllImport("gdi32.dll")]
        static extern bool StretchBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, int nSrcWidth, int nSrcHeight, int dwRop);

        [DllImport("gdi32.dll")]
        static extern int SetStretchBltMode(IntPtr hdc, int iStretchMode);

        [DllImport("gdi32.dll")]
        static extern bool SetBrushOrgEx(IntPtr hdc, int nXOrg, int nYOrg, IntPtr lppt);

        const int COLORONCOLOR = 3;
        const int HALFTONE = 4;
        const int SRCCOPY = 0x00CC0020;

        // 마우스 커서 캡처 API
        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int x, y; }

        [StructLayout(LayoutKind.Sequential)]
        struct CURSORINFO {
            public int cbSize;
            public int flags;
            public IntPtr hCursor;
            public POINT ptScreenPos;
        }
        const int CURSOR_SHOWING = 0x00000001;

        [DllImport("user32.dll")]
        static extern bool GetCursorInfo(out CURSORINFO pci);

        [DllImport("user32.dll")]
        static extern bool DrawIconEx(IntPtr hdc, int xLeft, int yTop, IntPtr hIcon, int cxWidth, int cyWidth, int istepIfAniCur, IntPtr hbrFlickerFreeDraw, int diFlags);
        const int DI_NORMAL = 0x0003;

        [DllImport("user32.dll")]
        static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll")]
        static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, int dwExtraInfo);

        [DllImport("user32.dll")]
        static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

        [DllImport("user32.dll")]
        static extern int GetSystemMetrics(int nIndex);

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
        const uint KEYEVENTF_KEYUP = 0x0002;

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

        public static RECT[] GetPhysicalMonitors() {
            var list = new List<RECT>();
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
            return list.OrderBy(m => m.Left).ThenBy(m => m.Top).ThenBy(m => m.Width).ToArray();
        }

        static void EnableTrueNativeDpi() {
            try { SetProcessDpiAwarenessContext((IntPtr)(-4)); return; } catch { }
            try { SetProcessDpiAwareness(2); return; } catch { }
            try { SetProcessDPIAware(); } catch { }
        }

        static ImageCodecInfo GetJpegCodec() {
            foreach (var codec in ImageCodecInfo.GetImageEncoders()) {
                if (codec.MimeType == "image/jpeg") return codec;
            }
            return null;
        }

        public const int TCP_PORT = 8888;
        public const int UDP_DISCOVERY_PORT = 8889;

        static string pcName = Environment.MachineName;
        static RECT[] cachedMonitors = null;
        static long lastMonCheck = 0;

        static ImageCodecInfo jpegCodec = null;
        static EncoderParameters encoderParams = new EncoderParameters(1);

        [STAThread]
        public static void Main(string[] args) {
            bool createdNew;
            using (Mutex mutex = new Mutex(true, "DayeonRemoteClient_SingleInstanceMutex", out createdNew)) {
                if (!createdNew) return;

                try { timeBeginPeriod(1); } catch { }
                try {
                    Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
                    Thread.CurrentThread.Priority = ThreadPriority.Highest;
                } catch { }

                EnableTrueNativeDpi();
                jpegCodec = GetJpegCodec();
                encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 75L);
                cachedMonitors = GetPhysicalMonitors();

                Thread udpThread = new Thread(RunUdpDiscoveryServer) { IsBackground = true };
                udpThread.Start();

                Thread tcpThread = new Thread(RunTcpServer) { IsBackground = true, Priority = ThreadPriority.Highest };
                tcpThread.Start();

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run();
            }
        }

        static void RunUdpDiscoveryServer() {
            UdpClient udp = null;
            try {
                udp = new UdpClient(UDP_DISCOVERY_PORT);
                udp.EnableBroadcast = true;
                IPEndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);

                while (true) {
                    byte[] req = udp.Receive(ref remoteEp);
                    string msg = Encoding.UTF8.GetString(req);
                    if (msg.StartsWith("DAYEON_DISCOVER")) {
                        int monCount = (cachedMonitors != null) ? cachedMonitors.Length : 1;
                        string res = string.Format("DAYEON_OFFER|{0}|{1}|{2}", pcName, TCP_PORT, monCount);
                        byte[] resBytes = Encoding.UTF8.GetBytes(res);
                        udp.Send(resBytes, resBytes.Length, remoteEp);
                    }
                }
            } catch {
            } finally {
                if (udp != null) try { udp.Close(); } catch { }
            }
        }

        static void RunTcpServer() {
            TcpListener listener = null;
            try {
                listener = new TcpListener(IPAddress.Any, TCP_PORT);
                listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                listener.Server.NoDelay = true;
                listener.Start();

                while (true) {
                    TcpClient client = listener.AcceptTcpClient();
                    client.NoDelay = true;
                    client.ReceiveBufferSize = 1024 * 128;
                    client.SendBufferSize = 1024 * 1024 * 8;

                    Thread clientThread = new Thread(() => HandleClient(client));
                    clientThread.IsBackground = true;
                    clientThread.Priority = ThreadPriority.Highest;
                    clientThread.Start();
                }
            } catch {
            } finally {
                if (listener != null) try { listener.Stop(); } catch { }
            }
        }

        static void HandleClient(TcpClient client) {
            client.NoDelay = true;
            client.SendTimeout = 2000;
            client.ReceiveTimeout = Timeout.Infinite;
            NetworkStream ns = client.GetStream();
            ns.ReadTimeout = Timeout.Infinite;
            ns.WriteTimeout = 2000;
            int currentMonitor = 0;
            int targetFps = 60;
            long quality = 65L;
            bool isZoomMode = false;
            bool isAlive = true;

            // 1. 초기 모드 및 모니터 설정 패킷 동기 수신 (첫 프레임부터 정확한 모니터 캡처)
            byte[] initHdr = new byte[8];
            int rInit = ReadExact(ns, initHdr, 0, 8);
            if (rInit == 8) {
                byte cmdType = initHdr[0];
                byte monIdx = initHdr[1];
                ushort param1 = BitConverter.ToUInt16(initHdr, 4);
                if (cmdType == 0x01) {
                    isZoomMode = (param1 == 1);
                    targetFps = isZoomMode ? 60 : 5;
                    quality = isZoomMode ? 65L : 45L;
                    currentMonitor = monIdx;
                }
            }

            // 2. 입력 명령 수신 스레드
            Thread inputThread = new Thread(() => {
                byte[] inHeader = new byte[8];
                byte[] inPayload = new byte[1024 * 16];

                try {
                    while (isAlive && client.Connected) {
                        int read = ReadExact(ns, inHeader, 0, 8);
                        if (read < 8) break;

                        byte cmdType = inHeader[0];
                        byte monIdx = inHeader[1];
                        ushort payloadLen = (ushort)(inHeader[2] | (inHeader[3] << 8));
                        ushort param1 = BitConverter.ToUInt16(inHeader, 4);
                        ushort param2 = BitConverter.ToUInt16(inHeader, 6);

                        if (payloadLen > 0) {
                            ReadExact(ns, inPayload, 0, payloadLen);
                        }

                        if (cmdType == 0x01) { // SET_MODE
                            isZoomMode = (param1 == 1);
                            targetFps = isZoomMode ? 60 : 5;
                            quality = isZoomMode ? 65L : 45L;
                            currentMonitor = monIdx;
                        } else if (cmdType == 0x02) { // SET_MONITOR
                            currentMonitor = monIdx;
                        } else if (cmdType >= 0x10 && cmdType <= 0x50) {
                            ExecuteNativeInput(cmdType, monIdx, param1, param2, inPayload, payloadLen);
                        }
                    }
                } catch {
                } finally {
                    isAlive = false;
                    try { client.Close(); } catch { }
                }
            });
            inputThread.IsBackground = true;
            inputThread.Priority = ThreadPriority.Highest;
            inputThread.Start();

            // 3. 60 FPS 초고속 비디오 캡처 & 다이렉트 바이너리 스트리밍 루프
            byte[] outHeader = new byte[12];
            outHeader[0] = (byte)'D';
            outHeader[1] = (byte)'Y';
            outHeader[2] = (byte)'0';
            outHeader[3] = (byte)'1';

            MemoryStream ms = new MemoryStream(1024 * 512);
            Bitmap bmp = null;
            Graphics g = null;
            int lastW = 0, lastH = 0;
            byte lastCapturedMon = 255;
            IntPtr hdcSrc = GetDC(IntPtr.Zero);
            Stopwatch sw = Stopwatch.StartNew();

            try {
                while (isAlive && client.Connected) {
                    sw.Restart();

                    if (hdcSrc == IntPtr.Zero) {
                        hdcSrc = GetDC(IntPtr.Zero);
                    }

                    long nowTick = DateTime.UtcNow.Ticks;
                    if (nowTick - lastMonCheck > 20000000) {
                        lastMonCheck = nowTick;
                        cachedMonitors = GetPhysicalMonitors();
                    }

                    int mIdx = (currentMonitor >= 0 && currentMonitor < cachedMonitors.Length) ? currentMonitor : 0;
                    RECT bounds = cachedMonitors[mIdx];

                    int targetW = bounds.Width;
                    int targetH = bounds.Height;
                    bool needScale = false;

                    if (!isZoomMode) {
                        needScale = true;
                        targetW = 480;
                        targetH = (int)(480.0 * bounds.Height / Math.Max(1, bounds.Width));
                    } else if (targetW > 1920 || targetH > 1080) {
                        needScale = true;
                        double scale = Math.Min(1920.0 / bounds.Width, 1080.0 / bounds.Height);
                        targetW = (int)(bounds.Width * scale);
                        targetH = (int)(bounds.Height * scale);
                    }

                    if (bmp == null || lastW != targetW || lastH != targetH || lastCapturedMon != mIdx) {
                        lastCapturedMon = (byte)mIdx;
                        if (g != null) g.Dispose();
                        if (bmp != null) bmp.Dispose();
                        bmp = new Bitmap(targetW, targetH, PixelFormat.Format24bppRgb);
                        g = Graphics.FromImage(bmp);
                        lastW = targetW;
                        lastH = targetH;
                    }

                    IntPtr hdcDest = g.GetHdc();
                    if (needScale) {
                        SetStretchBltMode(hdcDest, COLORONCOLOR);
                        StretchBlt(hdcDest, 0, 0, targetW, targetH, hdcSrc, bounds.Left, bounds.Top, bounds.Width, bounds.Height, SRCCOPY);
                    } else {
                        BitBlt(hdcDest, 0, 0, targetW, targetH, hdcSrc, bounds.Left, bounds.Top, SRCCOPY);
                    }

                    g.ReleaseHdc(hdcDest);

                    encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
                    ms.SetLength(0);
                    bmp.Save(ms, jpegCodec, encoderParams);

                    int frameLen = (int)ms.Length;
                    outHeader[4] = (byte)mIdx;
                    outHeader[8] = (byte)(frameLen & 0xFF);
                    outHeader[9] = (byte)((frameLen >> 8) & 0xFF);
                    outHeader[10] = (byte)((frameLen >> 16) & 0xFF);
                    outHeader[11] = (byte)((frameLen >> 24) & 0xFF);

                    ns.Write(outHeader, 0, 12);
                    ns.Write(ms.GetBuffer(), 0, frameLen);

                    long elapsed = sw.ElapsedMilliseconds;
                    int targetInterval = 1000 / targetFps;
                    if (elapsed < targetInterval) {
                        int sleepMs = (int)(targetInterval - elapsed);
                        if (sleepMs > 2) Thread.Sleep(sleepMs - 1);
                        while (sw.ElapsedMilliseconds < targetInterval) { Thread.SpinWait(10); }
                    }
                }
            } catch {
            } finally {
                isAlive = false;
                if (hdcSrc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, hdcSrc);
                if (g != null) g.Dispose();
                if (bmp != null) bmp.Dispose();
                try { client.Close(); } catch { }
            }
        }



        static int ReadExact(NetworkStream ns, byte[] buf, int offset, int count) {
            int total = 0;
            while (total < count) {
                int r = ns.Read(buf, offset + total, count - total);
                if (r <= 0) return total;
                total += r;
            }
            return total;
        }

        static void ExecuteNativeInput(byte cmdType, byte monIdx, int pX, int pY, byte[] payload, int payloadLen) {
            try {
                var mons = cachedMonitors;
                int mIdx = (mons != null && monIdx >= 0 && monIdx < mons.Length) ? monIdx : 0;
                RECT bounds = (mons != null && mons.Length > 0) ? mons[mIdx] : new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };

                // 🌟 마우스 이동 및 클릭 명령(0x10~0x14)일 때만 물리 픽셀 좌표 직접 이동
                if (cmdType >= 0x10 && cmdType <= 0x14) {
                    uint uX = (uint)(pX & 0xFFFF);
                    uint uY = (uint)(pY & 0xFFFF);

                    int actualX = bounds.Left + (int)Math.Round((double)uX * Math.Max(1, bounds.Width - 1) / 65535.0);
                    int actualY = bounds.Top + (int)Math.Round((double)uY * Math.Max(1, bounds.Height - 1) / 65535.0);

                    // 화면 경계 안전 클램핑
                    actualX = Math.Max(bounds.Left, Math.Min(bounds.Right - 1, actualX));
                    actualY = Math.Max(bounds.Top, Math.Min(bounds.Bottom - 1, actualY));

                    SetCursorPos(actualX, actualY);
                }

                switch (cmdType) {
                    case 0x10: // MOUSE_MOVE
                        break;
                    case 0x11: // MOUSE_LEFT_DOWN
                        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                        break;
                    case 0x12: // MOUSE_LEFT_UP
                        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
                        break;
                    case 0x13: // MOUSE_RIGHT_DOWN
                        mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, 0);
                        break;
                    case 0x14: // MOUSE_RIGHT_UP
                        mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, 0);
                        break;
                    case 0x15: // MOUSE_WHEEL (현재 위치에서 휠만 회전)
                        mouse_event(MOUSEEVENTF_WHEEL, 0, 0, (uint)((short)pX), 0);
                        break;
                    case 0x20: // KEY_DOWN
                        keybd_event((byte)pX, 0, 0, 0);
                        break;
                    case 0x21: // KEY_UP
                        keybd_event((byte)pX, 0, KEYEVENTF_KEYUP, 0);
                        break;
                    case 0x30: // PASTE_TEXT
                        if (payloadLen > 0) {
                            string text = Encoding.UTF8.GetString(payload, 0, payloadLen);
                            Thread t = new Thread(() => {
                                try {
                                    Clipboard.SetText(text);
                                    keybd_event(0x11, 0, 0, 0);
                                    keybd_event(0x56, 0, 0, 0);
                                    keybd_event(0x56, 0, KEYEVENTF_KEYUP, 0);
                                    keybd_event(0x11, 0, KEYEVENTF_KEYUP, 0);
                                } catch { }
                            });
                            t.SetApartmentState(ApartmentState.STA);
                            t.Start();
                        }
                        break;
                    case 0x40: // POPUP_MSG (1:1 메시지 팝업)
                        if (payloadLen > 0) {
                            string msg = Encoding.UTF8.GetString(payload, 0, payloadLen);
                            CustomNoticeForm.ShowNotice(msg, "다연코퍼레이션 1:1 메시지");
                        }
                        break;
                    case 0x41: // REBOOT_PC (원격 PC 재부팅)
                        Thread rbThread = new Thread(() => {
                            try {
                                CustomNoticeForm.ShowNotice("1초 후 PC가 재부팅됩니다...", "시스템 안내");
                                Thread.Sleep(1000);
                                Process.Start(new ProcessStartInfo("shutdown", "/r /t 0 /f") { CreateNoWindow = true, UseShellExecute = false });
                            } catch { }
                        });
                        rbThread.IsBackground = true;
                        rbThread.Start();
                        break;
                    case 0x42: // KILL_HUNG_TASKS (프로그램 정리)
                        Thread khThread = new Thread(() => {
                            try {
                                Process.Start(new ProcessStartInfo("taskkill.exe", "/F /FI \"STATUS eq NOT RESPONDING\"") { CreateNoWindow = true, UseShellExecute = false });
                            } catch { }
                        });
                        khThread.IsBackground = true;
                        khThread.Start();
                        break;
                    case 0x43: // AUTO_UPDATE (원격 PC 단독 업데이트)
                        Thread upThread = new Thread(() => {
                            try {
                                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                                string inputCtrl = Path.Combine(baseDir, "input_ctrl.exe");
                                if (!File.Exists(inputCtrl)) inputCtrl = Path.Combine(baseDir, "core", "input_ctrl.exe");
                                if (File.Exists(inputCtrl)) {
                                    Process.Start(new ProcessStartInfo(inputCtrl, "update_widget") { CreateNoWindow = true, UseShellExecute = false });
                                }
                            } catch { }
                        });
                        upThread.IsBackground = true;
                        upThread.Start();
                        break;
                    case 0x44: // DRAW_CMD (실시간 판서/그리기)
                        if (payloadLen > 0) {
                            string drawText = Encoding.UTF8.GetString(payload, 0, payloadLen);
                            EnsureDrawOverlay();
                            if (drawOverlayProc != null && !drawOverlayProc.HasExited) {
                                try { drawOverlayProc.StandardInput.WriteLine(drawText); } catch { }
                            }
                        }
                        break;
                }
            } catch { }
        }

        static Process drawOverlayProc = null;
        static void EnsureDrawOverlay() {
            if (drawOverlayProc != null && !drawOverlayProc.HasExited) return;
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string exe = Path.Combine(baseDir, "input_ctrl.exe");
            if (!File.Exists(exe)) exe = Path.Combine(baseDir, "core", "input_ctrl.exe");
            if (File.Exists(exe)) {
                try {
                    drawOverlayProc = new Process {
                        StartInfo = new ProcessStartInfo {
                            FileName = exe,
                            Arguments = "draw_overlay",
                            UseShellExecute = false,
                            RedirectStandardInput = true,
                            CreateNoWindow = true
                        }
                    };
                    drawOverlayProc.Start();
                } catch { }
            }
        }
    }

    public class CustomNoticeForm : Form {
        public static void ShowNotice(string message, string title = "다연코퍼레이션 1:1 메시지") {
            Thread t = new Thread(() => {
                try {
                    System.Media.SystemSounds.Asterisk.Play();
                    using (var form = new CustomNoticeForm(message, title)) {
                        Application.Run(form);
                    }
                } catch {
                    try { MessageBox.Show(message, title); } catch { }
                }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
        }

        [DllImport("user32.dll")]
        static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        const int WM_NCLBUTTONDOWN = 0xA1;
        const int HT_CAPTION = 0x2;

        public CustomNoticeForm(string message, string title) {
            this.Size = new Size(440, 250);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.TopMost = true;
            this.BackColor = Color.FromArgb(8, 13, 28);
            this.ForeColor = Color.White;
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = true;

            // 🌟 은은한 시안색 테두리 (프레임 없는 창을 은은하게 감싸줌)
            this.Paint += (s, e) => {
                using (Pen p = new Pen(Color.FromArgb(56, 189, 248), 1.5f)) {
                    e.Graphics.DrawRectangle(p, 0, 0, this.Width - 1, this.Height - 1);
                }
            };

            // Header Panel (드래그로 창 이동 + 자체 닫기 버튼)
            Panel header = new Panel {
                Dock = DockStyle.Top,
                Height = 46,
                BackColor = Color.FromArgb(17, 24, 45),
                Cursor = Cursors.SizeAll
            };
            MouseEventHandler dragHandler = (s, e) => {
                if (e.Button == MouseButtons.Left) {
                    ReleaseCapture();
                    SendMessage(this.Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
                }
            };
            header.MouseDown += dragHandler;

            Label lblHeader = new Label {
                Text = "🏢  " + title,
                Font = new Font("Malgun Gothic", 11.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(125, 211, 252),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(16, 0, 0, 0),
                Cursor = Cursors.SizeAll
            };
            lblHeader.MouseDown += dragHandler;

            Button btnClose = new Button {
                Text = "✕",
                Dock = DockStyle.Right,
                Width = 46,
                Font = new Font("Malgun Gothic", 11f, FontStyle.Bold),
                BackColor = Color.FromArgb(17, 24, 45),
                ForeColor = Color.FromArgb(148, 163, 184),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.FlatAppearance.MouseOverBackColor = Color.FromArgb(220, 38, 38);
            btnClose.Click += (s, e) => this.Close();

            header.Controls.Add(lblHeader);
            header.Controls.Add(btnClose);

            // Body message box (은은한 카드 배경 위에 메시지)
            Panel bodyCard = new Panel {
                BackColor = Color.FromArgb(15, 23, 42),
                Location = new Point(20, 62),
                Size = new Size(this.Width - 40, 110)
            };
            TextBox txtMsg = new TextBox {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Text = message,
                Font = new Font("Malgun Gothic", 10.5f, FontStyle.Regular),
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.FromArgb(241, 245, 249),
                BorderStyle = BorderStyle.None,
                Dock = DockStyle.Fill,
                Padding = new Padding(14, 12, 14, 12)
            };
            bodyCard.Controls.Add(txtMsg);

            // Bottom OK button
            Button btnOk = new Button {
                Text = "확인",
                Font = new Font("Malgun Gothic", 10.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(2, 132, 199),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(120, 38),
                Cursor = Cursors.Hand
            };
            btnOk.Location = new Point((this.Width - btnOk.Width) / 2, 190);
            btnOk.FlatAppearance.BorderSize = 0;
            btnOk.FlatAppearance.MouseOverBackColor = Color.FromArgb(3, 105, 161);
            btnOk.Click += (s, e) => this.Close();

            this.Controls.Add(header);
            this.Controls.Add(bodyCard);
            this.Controls.Add(btnOk);
            this.AcceptButton = btnOk;
        }
    }
}
