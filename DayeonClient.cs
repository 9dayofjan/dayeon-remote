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

        const int COLORONCOLOR = 3;
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

                NotifyIcon tray = new NotifyIcon();
                tray.Icon = SystemIcons.Shield;
                tray.Text = "다연코퍼레이션";
                tray.Visible = true;

                ContextMenuStrip menu = new ContextMenuStrip();
                var itemInfo = menu.Items.Add("다연코퍼레이션");
                itemInfo.Enabled = false;
                menu.Items.Add(new ToolStripSeparator());
                var itemExit = menu.Items.Add("종료(&X)");
                itemExit.Click += (s, e) => {
                    tray.Visible = false;
                    Application.Exit();
                };
                tray.ContextMenuStrip = menu;

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
            NetworkStream ns = client.GetStream();
            int currentMonitor = 0;
            int targetFps = 60;
            long quality = 75L;
            bool isZoomMode = false;
            bool isAlive = true;

            // 1. 입력 명령 수신 스레드
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
                            quality = isZoomMode ? 75L : 55L;
                            currentMonitor = monIdx;
                        } else if (cmdType == 0x02) { // SET_MONITOR
                            currentMonitor = monIdx;
                        } else if (cmdType >= 0x10 && cmdType <= 0x30) {
                            ExecuteNativeInput(cmdType, monIdx, param1, param2, inPayload, payloadLen);
                        }
                    }
                } catch {
                } finally {
                    isAlive = false;
                }
            });
            inputThread.IsBackground = true;
            inputThread.Priority = ThreadPriority.Highest;
            inputThread.Start();

            // 2. 화면 캡처 + 실제 마우스 커서 렌더링 파이프라인
            Bitmap bmp = null;
            Graphics g = null;
            int lastW = 0, lastH = 0;
            MemoryStream ms = new MemoryStream(1024 * 1024 * 2);
            byte[] outHeader = new byte[12];
            outHeader[0] = (byte)'D';
            outHeader[1] = (byte)'Y';
            outHeader[2] = (byte)'0';
            outHeader[3] = (byte)'1';

            IntPtr hdcSrc = GetDC(IntPtr.Zero);
            Stopwatch sw = new Stopwatch();

            try {
                while (isAlive && client.Connected) {
                    sw.Restart();

                    long nowTick = DateTime.UtcNow.Ticks;
                    if (nowTick - lastMonCheck > 20000000) {
                        lastMonCheck = nowTick;
                        cachedMonitors = GetPhysicalMonitors();
                        if (hdcSrc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, hdcSrc);
                        hdcSrc = GetDC(IntPtr.Zero);
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

                    if (bmp == null || lastW != targetW || lastH != targetH) {
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
                    int sleepMs = (int)(targetInterval - elapsed);
                    if (sleepMs > 2) Thread.Sleep(sleepMs - 1);
                    while (sw.ElapsedMilliseconds < targetInterval) {
                        Thread.SpinWait(100);
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
                int mIdx = (monIdx >= 0 && monIdx < cachedMonitors.Length) ? monIdx : 0;
                RECT bounds = cachedMonitors[mIdx];

                int actualX = bounds.Left + (int)Math.Round((double)(pX & 0xFFFF) * Math.Max(1, bounds.Width - 1) / 65535.0);
                int actualY = bounds.Top + (int)Math.Round((double)(pY & 0xFFFF) * Math.Max(1, bounds.Height - 1) / 65535.0);

                // ⚡ 100% 확실한 픽셀 좌표 직접 이동 (SetCursorPos)
                SetCursorPos(actualX, actualY);

                switch (cmdType) {
                    case 0x10: // MOUSE_MOVE
                        // SetCursorPos로 이동 완료
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
                    case 0x15: // MOUSE_WHEEL
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
                }
            } catch { }
        }
    }
}
