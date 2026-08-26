using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

class TrayApp : Form {
    private NotifyIcon trayIcon;
    private ContextMenuStrip trayMenu;
    private Process childProcess;
    private Process tunnelProcess;
    private string mode = "agent"; // "agent" or "server"
    private string appTitle = "다연코퍼레이션";
    private string externalUrl = "";
    private bool isExiting = false;

    [STAThread]
    static void Main(string[] args) {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        string m = "agent";
        string exeName = Path.GetFileNameWithoutExtension(Application.ExecutablePath).ToLower();
        if (exeName.Contains("관리자") || (args.Length > 0 && args[0].ToLower() == "server")) {
            m = "server";
        }

        // 중복 실행 방지: 이전 동일 프로세스가 있다면 이전 것을 종료하고 현재 인스턴스가 안정적으로 상주
        try {
            int currentPid = Process.GetCurrentProcess().Id;
            string currentProcName = Process.GetCurrentProcess().ProcessName;
            foreach (var p in Process.GetProcessesByName(currentProcName)) {
                if (p.Id != currentPid) {
                    try { p.Kill(); } catch { }
                }
            }
            // 잔존 백그라운드 캡처 프로세스 정리
            foreach (var name in new string[] { "fastcap", "audiocap", "input_ctrl" }) {
                foreach (var p in Process.GetProcessesByName(name)) {
                    try { if (!p.HasExited) p.Kill(); } catch { }
                }
            }
        } catch { }

        Application.Run(new TrayApp(m));
    }

    public static void OpenBrowserApp(string url) {
        string userDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DayeonManagerApp");
        string appArgs = string.Format("--app=\"{0}\" --user-data-dir=\"{1}\" --window-size=1600,920 --enable-gpu-rasterization --enable-zero-copy --disable-pinch --disable-features=Translate,OptimizationHints", url, userDataDir);

        // 1. Edge (Windows 10/11 기본 탑재 초고속 Chromium 엔진)
        string edgePath = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe";
        if (!File.Exists(edgePath)) edgePath = @"C:\Program Files\Microsoft\Edge\Application\msedge.exe";
        if (File.Exists(edgePath)) {
            try {
                Process.Start(edgePath, appArgs);
                return;
            } catch { }
        }

        // 2. Google Chrome
        string chromePath = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
        if (!File.Exists(chromePath)) chromePath = @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe";
        if (!File.Exists(chromePath)) {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            chromePath = Path.Combine(localAppData, @"Google\Chrome\Application\chrome.exe");
        }
        if (File.Exists(chromePath)) {
            try {
                Process.Start(chromePath, appArgs);
                return;
            } catch { }
        }

        // 3. Fallback
        try {
            Process.Start(url);
        } catch { }
    }

    public TrayApp(string runMode) {
        this.mode = runMode;
        this.ShowInTaskbar = false;
        this.WindowState = FormWindowState.Minimized;
        this.FormBorderStyle = FormBorderStyle.None;
        this.Size = new Size(0, 0);

        if (mode == "server") {
            appTitle = "다연코퍼레이션 관리자";
            new Thread(() => {
                Thread.Sleep(800);
                OpenBrowserApp("https://dayeon-remote.onrender.com/");
            }).Start();
        } else {
            appTitle = "다연코퍼레이션";
        }

        InitTray();
        EnsureAutoStart();
        StartBackendProcess();

        if (mode == "server") {
            StartTunnelProcess();
        }
    }

    private void EnsureAutoStart() {
        try {
            string exePath = Application.ExecutablePath;
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return;

            // 1. 레지스트리 HKCU Run 등록 (가장 안정적인 윈도우 표준 방식)
            try {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true)) {
                    if (key != null) {
                        key.SetValue("DayeonCorp", "\"" + exePath + "\"");
                    }
                }
            } catch { }

            // 2. 시작프로그램 폴더에 혹시 남아있을 수 있는 구형 .bat 파일들 완전 삭제 (검은창 및 인코딩 오류 차단)
            try {
                string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                if (Directory.Exists(startupFolder)) {
                    string batPath = Path.Combine(startupFolder, "DayeonCorpAutoStart.bat");
                    if (File.Exists(batPath)) {
                        try { File.Delete(batPath); } catch { }
                    }
                }
            } catch { }
        } catch { }
    }

    private Icon CreateCustomIcon(bool isServer) {
        using (Bitmap bmp = new Bitmap(32, 32)) {
            using (Graphics g = Graphics.FromImage(bmp)) {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Color bg = isServer ? Color.FromArgb(14, 165, 233) : Color.FromArgb(34, 197, 94);

                using (SolidBrush b = new SolidBrush(bg)) {
                    g.FillEllipse(b, 2, 2, 28, 28);
                }
                using (Pen p = new Pen(Color.White, 2f)) {
                    g.DrawEllipse(p, 2, 2, 28, 28);
                }
                using (Font f = new Font("Malgun Gothic", 10, FontStyle.Bold)) {
                    using (SolidBrush textBrush = new SolidBrush(Color.White)) {
                        string letter = isServer ? "관" : "다";
                        StringFormat sf = new StringFormat();
                        sf.Alignment = StringAlignment.Center;
                        sf.LineAlignment = StringAlignment.Center;
                        g.DrawString(letter, f, textBrush, new RectangleF(0, 1, 32, 32), sf);
                    }
                }
            }
            return Icon.FromHandle(bmp.GetHicon());
        }
    }

    private string GetActiveExternalUrl() {
        try {
            // 1. 실시간 서버 API를 통해 현재 활성화된 최신 터널 URL 즉시 조회
            try {
                using (var wc = new System.Net.WebClient()) {
                    wc.Encoding = System.Text.Encoding.UTF8;
                    string json = wc.DownloadString("http://127.0.0.1:8080/api/tunnel_url");
                    Match m = Regex.Match(json, @"https://[a-zA-Z0-9-]+\.trycloudflare\.com");
                    if (m.Success) {
                        externalUrl = m.Value;
                        return externalUrl;
                    }
                }
            } catch { }

            // 2. 실시간 텍스트 파일 조회
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string txtFile = Path.Combine(baseDir, "외부_스마트폰_접속링크.txt");
            if (File.Exists(txtFile)) {
                string content = File.ReadAllText(txtFile);
                Match m = Regex.Match(content, @"https://[a-zA-Z0-9-]+\.trycloudflare\.com");
                if (m.Success) {
                    externalUrl = m.Value;
                    return externalUrl;
                }
            }

            // 3. 고정 설정 파일 조회
            string tokenFile = Path.Combine(baseDir, "클라우드플레어_고정설정.txt");
            if (File.Exists(tokenFile)) {
                string fixedDomain = "";
                foreach (var l in File.ReadAllLines(tokenFile)) {
                    var tr = l.Trim();
                    if (tr.StartsWith("DOMAIN=")) fixedDomain = tr.Substring(7).Trim();
                }
                if (!string.IsNullOrEmpty(fixedDomain)) {
                    if (!fixedDomain.StartsWith("http")) fixedDomain = "https://" + fixedDomain;
                    return fixedDomain;
                }
            }

            if (!string.IsNullOrEmpty(externalUrl)) return externalUrl;
        } catch { }
        return "";
    }

    private void InitTray() {
        trayMenu = new ContextMenuStrip();
        
        var headerItem = new ToolStripMenuItem("🏢 " + appTitle);
        headerItem.Font = new Font(headerItem.Font, FontStyle.Bold);
        headerItem.Enabled = false;
        trayMenu.Items.Add(headerItem);

        trayMenu.Items.Add(new ToolStripSeparator());

        if (mode == "server") {
            var openLocalItem = new ToolStripMenuItem("🌐 화면열기", null, (s, e) => {
                OpenBrowserApp("https://dayeon-remote.onrender.com/");
            });
            openLocalItem.Font = new Font(openLocalItem.Font, FontStyle.Bold);
            trayMenu.Items.Add(openLocalItem);

            var copyLinkItem = new ToolStripMenuItem("📋 링크복사", null, (s, e) => {
                try {
                    string permanentUrl = "https://dayeon-remote.onrender.com/";
                    Clipboard.SetText(permanentUrl);
                    trayIcon.ShowBalloonTip(2500, "링크복사 완료!", "접속 링크가 복사되었습니다:\n" + permanentUrl, ToolTipIcon.Info);
                } catch { }
            });
            copyLinkItem.Font = new Font(copyLinkItem.Font, FontStyle.Bold);
            copyLinkItem.ForeColor = Color.FromArgb(168, 85, 247);
            trayMenu.Items.Add(copyLinkItem);

            trayMenu.Items.Add(new ToolStripSeparator());
        } else {
            var editIpItem = new ToolStripMenuItem("⚙️ 서버 설정", null, (s, e) => {
                try {
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    string ipFile = Path.Combine(baseDir, "server_ip.txt");
                    if (!File.Exists(ipFile)) File.WriteAllText(ipFile, "172.30.1.90");
                    Process.Start("notepad.exe", ipFile);
                } catch { }
            });
            trayMenu.Items.Add(editIpItem);
            trayMenu.Items.Add(new ToolStripSeparator());
        }

        var exitItem = new ToolStripMenuItem("🛑 종료", null, (s, e) => {
            ExitApplication();
        });
        exitItem.ForeColor = Color.Red;
        trayMenu.Items.Add(exitItem);

        trayIcon = new NotifyIcon();
        trayIcon.Text = appTitle;
        trayIcon.Icon = CreateCustomIcon(mode == "server");
        trayIcon.ContextMenuStrip = trayMenu;
        trayIcon.Visible = true;

        trayIcon.DoubleClick += (s, e) => {
            if (mode == "server") {
                try { Process.Start("http://127.0.0.1:8080"); } catch { }
            }
        };
    }

    private void StartBackendProcess() {
        try {
            KillProcessByName("fastcap");
            KillProcessByName("input_ctrl");
            KillProcessByName("audiocap");

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string coreDir = Path.Combine(baseDir, "core");
            if (!Directory.Exists(coreDir)) coreDir = baseDir;

            string nativeClient = Path.Combine(coreDir, "다연원격_클라이언트.exe");
            if (!File.Exists(nativeClient)) nativeClient = Path.Combine(baseDir, "다연원격_클라이언트.exe");
            if (File.Exists(nativeClient)) {
                try {
                    ProcessStartInfo npsi = new ProcessStartInfo();
                    npsi.FileName = nativeClient;
                    npsi.WorkingDirectory = Path.GetDirectoryName(nativeClient);
                    npsi.CreateNoWindow = true;
                    npsi.UseShellExecute = false;
                    npsi.WindowStyle = ProcessWindowStyle.Hidden;
                    Process.Start(npsi);
                } catch { }
            }

            string nodeExe = Path.Combine(coreDir, "node.exe");
            if (!File.Exists(nodeExe)) nodeExe = Path.Combine(baseDir, "node.exe");
            if (!File.Exists(nodeExe)) nodeExe = "node.exe";

            string scriptName = (mode == "server") ? "server.js" : "agent.js";
            string scriptPath = Path.Combine(coreDir, scriptName);
            if (!File.Exists(scriptPath)) scriptPath = Path.Combine(baseDir, scriptName);

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = nodeExe;
            psi.Arguments = "\"" + scriptPath + "\"";
            psi.WorkingDirectory = coreDir;
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            psi.WindowStyle = ProcessWindowStyle.Hidden;

            childProcess = Process.Start(psi);
            childProcess.EnableRaisingEvents = true;
            childProcess.Exited += (s, e) => {
                if (!isExiting) {
                    Thread.Sleep(1000);
                    StartBackendProcess();
                }
            };
        } catch (Exception ex) {
            MessageBox.Show("프로그램 시작 중 오류가 발생했습니다: " + ex.Message, appTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void StartTunnelProcess() {
        new Thread(() => {
            try {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string coreDir = Path.Combine(baseDir, "core");
                if (!Directory.Exists(coreDir)) coreDir = baseDir;

                string cloudflaredExe = Path.Combine(coreDir, "cloudflared.exe");
                if (!File.Exists(cloudflaredExe)) cloudflaredExe = Path.Combine(baseDir, "cloudflared.exe");
                if (!File.Exists(cloudflaredExe)) return;

                Thread.Sleep(1000);

                string tokenFile = Path.Combine(baseDir, "클라우드플레어_고정설정.txt");
                string fixedToken = "";
                string fixedDomain = "";
                if (File.Exists(tokenFile)) {
                    foreach (var l in File.ReadAllLines(tokenFile)) {
                        var tr = l.Trim();
                        if (tr.StartsWith("TOKEN=")) fixedToken = tr.Substring(6).Trim();
                        if (tr.StartsWith("DOMAIN=")) fixedDomain = tr.Substring(7).Trim();
                    }
                }

                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = cloudflaredExe;
                if (!string.IsNullOrEmpty(fixedToken)) {
                    psi.Arguments = "tunnel run --token " + fixedToken;
                    if (!string.IsNullOrEmpty(fixedDomain)) {
                        if (!fixedDomain.StartsWith("http")) fixedDomain = "https://" + fixedDomain;
                        externalUrl = fixedDomain;
                    }
                } else {
                    psi.Arguments = "tunnel --url http://127.0.0.1:8080";
                }
                psi.WorkingDirectory = coreDir;
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.WindowStyle = ProcessWindowStyle.Hidden;

                tunnelProcess = Process.Start(psi);

                if (!string.IsNullOrEmpty(fixedToken)) return;

                Regex reg = new Regex(@"https://[a-zA-Z0-9-]+\.trycloudflare\.com");
                Action<string> onLine = (line) => {
                    if (string.IsNullOrEmpty(line)) return;
                    Match m = reg.Match(line);
                    if (m.Success && string.IsNullOrEmpty(externalUrl)) {
                        externalUrl = m.Value;
                        try {
                            string linkTxt = "🏢 [다연코퍼레이션] 관리자 원격 접속 링크 안내\r\n\r\n"
                                           + "1. 📱 스마트폰 / 외부 즉시 접속 링크 (공유기 설정 불필요):\r\n"
                                           + externalUrl + "\r\n\r\n"
                                           + "2. 🖥️ 사내 / 로컬 네트워크 접속 링크:\r\n"
                                           + "http://172.30.1.90:8080 (또는 http://127.0.0.1:8080)\r\n";
                            File.WriteAllText(Path.Combine(baseDir, "외부_스마트폰_접속링크.txt"), linkTxt);
                        } catch { }

                        try {
                            this.BeginInvoke((Action)(() => {
                                try {
                                    Clipboard.SetText(externalUrl);
                                    trayIcon.ShowBalloonTip(4000, "🌐 무료 외부 접속 링크 준비 완료!", externalUrl + "\n(스마트폰이나 카톡에 붙여넣기 하시면 즉시 접속됩니다)", ToolTipIcon.Info);
                                } catch { }
                            }));
                        } catch { }
                    }
                };

                tunnelProcess.OutputDataReceived += (s, e) => onLine(e.Data);
                tunnelProcess.ErrorDataReceived += (s, e) => onLine(e.Data);
                tunnelProcess.BeginOutputReadLine();
                tunnelProcess.BeginErrorReadLine();
            } catch { }
        }).Start();
    }

    private void ExitApplication() {
        isExiting = true;
        try {
            trayIcon.Visible = false;
            trayIcon.Dispose();
        } catch { }

        try {
            if (childProcess != null && !childProcess.HasExited) childProcess.Kill();
        } catch { }

        try {
            if (tunnelProcess != null && !tunnelProcess.HasExited) tunnelProcess.Kill();
        } catch { }

        // 관련 백그라운드 프로세스 정리
        try {
            KillProcessByName("cloudflared");
            KillProcessByName("fastcap");
            KillProcessByName("input_ctrl");
            KillProcessByName("audiocap");
        } catch { }

        Application.Exit();
        Environment.Exit(0);
    }

    private void KillProcessByName(string name) {
        try {
            foreach (var p in Process.GetProcessesByName(name)) {
                try { if (!p.HasExited) p.Kill(); } catch { }
            }
        } catch { }
    }
}
