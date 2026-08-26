using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

class FastCap {
    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod", SetLastError = true)]
    static extern uint timeBeginPeriod(uint uMilliseconds);

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SetThreadDesktop(IntPtr hDesktop);

    [DllImport("user32.dll")]
    static extern bool SetProcessDPIAware();

    [DllImport("user32.dll")]
    static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);

    [DllImport("gdi32.dll")]
    static extern bool StretchBlt(IntPtr hdcDest, int nXOriginDest, int nYOriginDest, int nWidthDest, int nHeightDest, IntPtr hdcSrc, int nXOriginSrc, int nYOriginSrc, int nWidthSrc, int nHeightSrc, int dwRop);

    const int COLORONCOLOR = 3;
    const int HALFTONE = 4;

    [DllImport("gdi32.dll")]
    static extern int SetStretchBltMode(IntPtr hdc, int nStretchMode);

    [DllImport("gdi32.dll")]
    static extern bool SetBrushOrgEx(IntPtr hdc, int nXOrg, int nYOrg, IntPtr lppt);

    const uint GENERIC_ALL = 0x10000000;
    const int SRCOPY = 0x00CC0020;
    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

    [DllImport("shcore.dll", SetLastError = true)]
    static extern int SetProcessDpiAwareness(int awareness);

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
        var list = new System.Collections.Generic.List<RECT>();
        try {
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
        } catch { }

        if (list.Count == 0) {
            list.Add(new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 });
        }
        return list.OrderBy(m => m.Left).ThenBy(m => m.Top).ThenBy(m => m.Width).ToArray();
    }

    static void EnableTrueNativeDpi() {
        try {
            SetProcessDpiAwarenessContext((IntPtr)(-4)); // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
            return;
        } catch { }
        try {
            SetProcessDpiAwareness(2); // PROCESS_PER_MONITOR_DPI_AWARE
            return;
        } catch { }
        try {
            SetProcessDPIAware();
        } catch { }
    }

    static ImageCodecInfo GetJpegCodec() {
        return ImageCodecInfo.GetImageEncoders().FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);
    }

    public static void Main(string[] args) {
        try { timeBeginPeriod(1); } catch { }

        try {
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.AboveNormal;
            Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;
        } catch { }

        EnableTrueNativeDpi();
        try {
            IntPtr hDesk = OpenInputDesktop(0, false, GENERIC_ALL);
            if (hDesk != IntPtr.Zero) SetThreadDesktop(hDesk);
        } catch {
        }

        var codec = GetJpegCodec();
        var encoderParams = new EncoderParameters(1);
        long currentQuality = 88L;
        encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, currentQuality);

        if (args.Length > 0 && args[0] == "daemon") {
            int currentMon = 0;
            int targetFps = 60;
            long targetQuality = 88L;
            if (args.Length > 1) int.TryParse(args[1], out currentMon);

            RECT[] cachedScreens = GetPhysicalMonitors();

            Thread inThread = new Thread(() => {
                string line;
                while ((line = Console.ReadLine()) != null) {
                    line = line.Trim();
                    if (line.StartsWith("monitor ")) {
                        int m;
                        if (int.TryParse(line.Substring(8), out m)) {
                            currentMon = m;
                            try { cachedScreens = GetPhysicalMonitors(); } catch { }
                        }
                    } else if (line.StartsWith("fps ")) {
                        int f;
                        if (int.TryParse(line.Substring(4), out f) && f > 0 && f <= 60) {
                            targetFps = f;
                        }
                    } else if (line.StartsWith("quality ")) {
                        long q;
                        if (long.TryParse(line.Substring(8), out q) && q >= 10 && q <= 100) {
                            targetQuality = q;
                        }
                    } else if (line == "exit" || line == "quit") {
                        Environment.Exit(0);
                    }
                }
            });
            inThread.IsBackground = true;
            inThread.Start();


            Stream stdOut = Console.OpenStandardOutput();
            byte[] headerBuf = new byte[12];
            headerBuf[0] = 0x53; // 'S'
            headerBuf[1] = 0x43; // 'C'
            headerBuf[2] = 0x41; // 'A'
            headerBuf[3] = 0x50; // 'P'

            MemoryStream ms = new MemoryStream(512 * 1024);
            Bitmap bmp = null;
            Graphics g = null;
            int lastW = 0, lastH = 0;

            cachedScreens = GetPhysicalMonitors();
            int screenCheckCounter = 0;
            IntPtr hdcSrc = GetDC(IntPtr.Zero);
            Stopwatch sw = new Stopwatch();

            while (true) {
                try {
                    sw.Restart();
                    screenCheckCounter++;
                    if (screenCheckCounter > 300) {
                        screenCheckCounter = 0;
                        try {
                            RECT[] newScreens = GetPhysicalMonitors();
                            if (newScreens.Length != cachedScreens.Length) cachedScreens = newScreens;
                        } catch { }
                    }

                    int mIdx = (currentMon >= 0 && currentMon < cachedScreens.Length) ? currentMon : 0;
                    RECT bounds = cachedScreens[mIdx];

                    int targetW = bounds.Width;
                    int targetH = bounds.Height;
                    bool needScale = false;

                    // 🌟 2560x1440(QHD) 이하 1:1 완벽 무손실 원본 캡처 (선명도 100%), 4K 이상은 초고화질 QHD 스케일링
                    if (targetW > 2560 || targetH > 1440) {
                        needScale = true;
                        double scale = Math.Min(2560.0 / bounds.Width, 1440.0 / bounds.Height);
                        targetW = (int)(bounds.Width * scale);
                        targetH = (int)(bounds.Height * scale);
                    }

                    long effectiveQuality = targetQuality;
                    if (effectiveQuality != currentQuality) {
                        currentQuality = effectiveQuality;
                        encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, currentQuality);
                    }

                    if (hdcSrc == IntPtr.Zero || screenCheckCounter % 60 == 0) {
                        if (hdcSrc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, hdcSrc);
                        hdcSrc = GetDC(IntPtr.Zero);
                    }

                    if (bmp == null || lastW != targetW || lastH != targetH) {
                        if (g != null) g.Dispose();
                        if (bmp != null) bmp.Dispose();
                        bmp = new Bitmap(targetW, targetH, PixelFormat.Format24bppRgb);
                        g = Graphics.FromImage(bmp);
                        lastW = targetW;
                        lastH = targetH;
                    }

                    IntPtr hdcDest = IntPtr.Zero;
                    try {
                        hdcDest = g.GetHdc();
                        if (needScale) {
                            SetStretchBltMode(hdcDest, 4); // HALFTONE (부드럽고 선명한 고품질 스케일링)
                            SetBrushOrgEx(hdcDest, 0, 0, IntPtr.Zero);
                            StretchBlt(hdcDest, 0, 0, targetW, targetH, hdcSrc, bounds.Left, bounds.Top, bounds.Width, bounds.Height, SRCOPY);
                        } else {
                            BitBlt(hdcDest, 0, 0, targetW, targetH, hdcSrc, bounds.Left, bounds.Top, SRCOPY);
                        }
                    } finally {
                        if (hdcDest != IntPtr.Zero) {
                            g.ReleaseHdc(hdcDest);
                        }
                    }

                    ms.SetLength(0);
                    if (codec != null) bmp.Save(ms, codec, encoderParams);
                    else bmp.Save(ms, ImageFormat.Jpeg);

                    int length = (int)ms.Length;
                    headerBuf[4] = (byte)(mIdx & 0xFF);
                    headerBuf[5] = (byte)((mIdx >> 8) & 0xFF);
                    headerBuf[6] = (byte)((mIdx >> 16) & 0xFF);
                    headerBuf[7] = (byte)((mIdx >> 24) & 0xFF);
                    headerBuf[8] = (byte)(length & 0xFF);
                    headerBuf[9] = (byte)((length >> 8) & 0xFF);
                    headerBuf[10] = (byte)((length >> 16) & 0xFF);
                    headerBuf[11] = (byte)((length >> 24) & 0xFF);

                    stdOut.Write(headerBuf, 0, 12);
                    stdOut.Write(ms.GetBuffer(), 0, length);
                    stdOut.Flush();

                    long elapsed = sw.ElapsedMilliseconds;
                    int targetInterval = Math.Max(1, 1000 / targetFps);
                    int sleepMs = (int)(targetInterval - elapsed);
                    if (sleepMs > 0) Thread.Sleep(sleepMs);
                    else Thread.Sleep(1);
                } catch {
                    try { if (g != null) { g.Dispose(); g = null; } } catch { }
                    try { if (bmp != null) { bmp.Dispose(); bmp = null; } } catch { }
                    try { if (hdcSrc != IntPtr.Zero) { ReleaseDC(IntPtr.Zero, hdcSrc); hdcSrc = IntPtr.Zero; } } catch { }
                    Thread.Sleep(30);
                }
            }
        } else {
            int mIdx = 0;
            if (args.Length > 0) int.TryParse(args[0], out mIdx);
            RECT[] cachedScreens = GetPhysicalMonitors();
            if (mIdx < 0 || mIdx >= cachedScreens.Length) mIdx = 0;
            RECT bounds = cachedScreens[mIdx];

            IntPtr hdcSrc = GetDC(IntPtr.Zero);
            try {
                using (Bitmap bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb))
                using (Graphics g = Graphics.FromImage(bmp)) {
                    IntPtr hdcDest = g.GetHdc();
                    BitBlt(hdcDest, 0, 0, bounds.Width, bounds.Height, hdcSrc, bounds.Left, bounds.Top, SRCOPY);
                    g.ReleaseHdc(hdcDest);
                    using (MemoryStream ms = new MemoryStream()) {
                        if (codec != null) bmp.Save(ms, codec, encoderParams);
                        else bmp.Save(ms, ImageFormat.Jpeg);
                        Console.WriteLine(Convert.ToBase64String(ms.ToArray()));
                    }
                }
            } finally {
                if (hdcSrc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, hdcSrc);
            }
        }
    }
}
