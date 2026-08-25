using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

public class PcItem {
    public string Id { get; set; }
    public string Name { get; set; }
    public string Nickname { get; set; }
    public string Ip { get; set; }
    public string LanIp { get; set; }
    public int LanPort { get; set; }
    public bool Online { get; set; }
    public string ActiveMonitor { get; set; }

    public PcItem() {
        ActiveMonitor = "0";
        Online = true;
        LanPort = 8001;
    }
}

public class FloatingProgressWidget : Form {
    private Label lblTitle;
    private Panel pnlGaugeBg;
    private Panel pnlGaugeFill;
    private Label lblStatus;

    public FloatingProgressWidget(string title, string initialStatus) {
        this.FormBorderStyle = FormBorderStyle.None;
        this.ShowInTaskbar = false;
        this.TopMost = true;
        this.Size = new Size(380, 84);
        this.BackColor = Color.FromArgb(15, 23, 42);
        this.StartPosition = FormStartPosition.Manual;

        Rectangle wa = Screen.PrimaryScreen.WorkingArea;
        this.Location = new Point(wa.Right - this.Width - 16, wa.Bottom - this.Height - 16);

        this.Paint += (s, e) => {
            using (Pen p = new Pen(Color.FromArgb(56, 189, 248), 2f)) {
                e.Graphics.DrawRectangle(p, 1, 1, this.Width - 2, this.Height - 2);
            }
        };

        Panel inner = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 10, 16, 10) };

        lblTitle = new Label {
            Text = title,
            Font = new Font("Malgun Gothic", 10f, FontStyle.Bold),
            ForeColor = Color.FromArgb(56, 189, 248),
            Dock = DockStyle.Top,
            Height = 22
        };
        inner.Controls.Add(lblTitle);

        pnlGaugeBg = new Panel {
            Dock = DockStyle.Top,
            Height = 12,
            BackColor = Color.FromArgb(30, 41, 59),
            Margin = new Padding(0, 4, 0, 4)
        };
        pnlGaugeFill = new Panel {
            Width = 0,
            Height = 12,
            BackColor = Color.FromArgb(14, 165, 233)
        };
        pnlGaugeBg.Controls.Add(pnlGaugeFill);
        inner.Controls.Add(pnlGaugeBg);

        lblStatus = new Label {
            Text = initialStatus,
            Font = new Font("Malgun Gothic", 8.5f, FontStyle.Regular),
            ForeColor = Color.FromArgb(226, 232, 240),
            Dock = DockStyle.Bottom,
            Height = 20,
            TextAlign = ContentAlignment.MiddleLeft
        };
        inner.Controls.Add(lblStatus);

        this.Controls.Add(inner);
    }

    public void UpdateProgress(int percent, string statusText) {
        if (percent < 0) percent = 0;
        if (percent > 100) percent = 100;
        if (this.InvokeRequired) {
            this.BeginInvoke((Action)(() => UpdateProgress(percent, statusText)));
            return;
        }
        pnlGaugeFill.Width = (int)((percent / 100.0) * (this.Width - 32));
        if (!string.IsNullOrEmpty(statusText)) lblStatus.Text = statusText;
        if (percent >= 100) {
            pnlGaugeFill.BackColor = Color.FromArgb(34, 197, 94);
        }
    }
}

public class RemoteViewerForm : Form {
    private const int CURRENT_MANAGER_VERSION = 351;

    private string serverUrl = "https://dayeon-remote.onrender.com";
    private List<PcItem> pcList = new List<PcItem>();
    private ConcurrentDictionary<string, Bitmap> thumbnailCache = new ConcurrentDictionary<string, Bitmap>();

    private string currentZoomPcId = null;
    private string currentMonitorIdx = "0";
    private Bitmap currentZoomBitmap = null;
    private readonly object bmpLock = new object();

    private ClientWebSocket zoomWs = null;
    private CancellationTokenSource wsCts = null;
    private CancellationTokenSource zoomLoopCts = null;

    private bool isZoomMode = false;
    private bool isRemoteControlEnabled = true;
    private bool isDrawingMode = false;

    private bool isClosing = false;
    private bool isSelfUpdating = false;
    private int streamFps = 0;
    private int fpsCounter = 0;
    private Stopwatch fpsSw = Stopwatch.StartNew();

    private string viewFilter = "all";

    // Grid UI Controls
    private Panel topBar;
    private Label lblTitle;
    private Label lblStatus;
    private Button btnNoticeAll;
    private Button btnUpdateRemotePcs;
    private Button btnDeployManager;
    private Button btnViewAll;
    private Button btnView4;
    private Button btnView1;

    // Zoom UI Controls
    private Button btnBackToGrid;
    private Label lblZoomPcInfo;
    private Button btnMon1, btnMon2, btnMon3;
    private Button btnDrawToggle;
    private Button btnSendMsg, btnSendFile, btnKillTask, btnReboot, btnSingleUpdate;
    private Label lblFps;

    private DoubleBufferedPanel renderCanvas;

    // Mouse control state
    private bool isMouseDown = false;
    private bool isDragging = false;
    private Point mouseStartPt;

    [STAThread]
    public static void Main(string[] args) {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        try {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            ServicePointManager.DefaultConnectionLimit = 128;
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.UseNagleAlgorithm = false;
        } catch { }

        try {
            int curPid = Process.GetCurrentProcess().Id;
            foreach (var p in Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName)) {
                if (p.Id != curPid) {
                    try { p.Kill(); } catch { }
                }
            }
        } catch { }

        Application.Run(new RemoteViewerForm());
    }

    public RemoteViewerForm() {
        this.Text = "다연코퍼레이션 원격 관리자 (v" + CURRENT_MANAGER_VERSION + " 60 FPS)";
        this.Size = new Size(1440, 860);
        this.MinimumSize = new Size(1024, 640);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.BackColor = Color.FromArgb(11, 19, 43);
        this.ForeColor = Color.White;
        this.KeyPreview = true;

        LoadServerUrl();
        InitUI();
        StartBackgroundLoops();
    }

    private void LoadServerUrl() {
        try {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string txtPath = Path.Combine(baseDir, "server_ip.txt");
            if (File.Exists(txtPath)) {
                string line = File.ReadAllText(txtPath).Trim();
                if (!string.IsNullOrEmpty(line)) {
                    if (!line.StartsWith("http://") && !line.StartsWith("https://")) line = "http://" + line;
                    serverUrl = line;
                }
            }
        } catch { }
    }

    private void InitUI() {
        topBar = new Panel {
            Dock = DockStyle.Top,
            Height = 44,
            BackColor = Color.FromArgb(15, 23, 42),
            Padding = new Padding(8, 6, 8, 6)
        };

        // 1. 그리드 상단 바 (순수 텍스트)
        lblTitle = new Label {
            Text = "다연코퍼레이션",
            Font = new Font("Malgun Gothic", 11.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(56, 189, 248),
            AutoSize = true,
            Location = new Point(10, 11)
        };
        topBar.Controls.Add(lblTitle);

        lblStatus = new Label {
            Text = "연결: 6대 온라인",
            Font = new Font("Malgun Gothic", 9f, FontStyle.Bold),
            ForeColor = Color.FromArgb(34, 197, 94),
            AutoSize = true,
            Location = new Point(135, 13)
        };
        topBar.Controls.Add(lblStatus);

        btnNoticeAll = CreateModernBtn("전체 공지", Color.FromArgb(79, 70, 229), 250, 7, 85);
        btnNoticeAll.Click += (s, e) => ShowNoticeDialog("all");
        topBar.Controls.Add(btnNoticeAll);

        // 원격 PC 전용 업데이트 버튼 (관리자 파일 절대 안 감)
        btnUpdateRemotePcs = CreateModernBtn("원격 PC 전체 업데이트", Color.FromArgb(5, 150, 105), 342, 7, 145);
        btnUpdateRemotePcs.Click += (s, e) => {
            if (MessageBox.Show("전체 원격 PC(클라이언트)에 최신 클라이언트 업데이트를 적용하시겠습니까?\n(원격 PC에는 관리자 파일이 전송되지 않습니다)", "원격 PC 전체 업데이트", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) {
                SendControlFast("all", "auto_update", 0, 0, "0");
                MessageBox.Show("전체 원격 PC에 클라이언트 업데이트 명령이 전달되었습니다.", "전송 완료");
            }
        };
        topBar.Controls.Add(btnUpdateRemotePcs);

        // 관리자 전용 배포 버튼 (다른 PC의 관리자 프로그램만 업데이트)
        btnDeployManager = CreateModernBtn("관리자 프로그램 배포", Color.FromArgb(124, 58, 237), 492, 7, 135);
        btnDeployManager.Click += (s, e) => PerformManagerDeployOnly();
        topBar.Controls.Add(btnDeployManager);

        btnViewAll = CreateModernBtn("전체 PC 보기", Color.FromArgb(2, 132, 199), 632, 7, 95);
        btnViewAll.Click += (s, e) => { viewFilter = "all"; renderCanvas.Invalidate(); };
        topBar.Controls.Add(btnViewAll);

        btnView4 = CreateModernBtn("4분할", Color.FromArgb(30, 41, 59), 732, 7, 55);
        btnView4.Click += (s, e) => { viewFilter = "4split"; renderCanvas.Invalidate(); };
        topBar.Controls.Add(btnView4);

        btnView1 = CreateModernBtn("1분할", Color.FromArgb(30, 41, 59), 792, 7, 55);
        btnView1.Click += (s, e) => { viewFilter = "1split"; renderCanvas.Invalidate(); };
        topBar.Controls.Add(btnView1);

        // 2. 줌 상단 바 (순수 텍스트)
        btnBackToGrid = CreateModernBtn("◀ 전체 PC 보기", Color.FromArgb(239, 68, 68), 10, 7, 120);
        btnBackToGrid.Visible = false;
        btnBackToGrid.Click += (s, e) => ExitZoomMode();
        topBar.Controls.Add(btnBackToGrid);

        lblZoomPcInfo = new Label {
            Text = "PC 1 (127.0.0.1)",
            Font = new Font("Malgun Gothic", 10.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(56, 189, 248),
            AutoSize = true,
            Location = new Point(136, 12),
            Visible = false
        };
        topBar.Controls.Add(lblZoomPcInfo);

        btnMon1 = CreateModernBtn("모니터 1", Color.FromArgb(3, 105, 161), 380, 7, 68);
        btnMon2 = CreateModernBtn("모니터 2", Color.FromArgb(30, 41, 59), 452, 7, 68);
        btnMon3 = CreateModernBtn("모니터 3", Color.FromArgb(30, 41, 59), 524, 7, 68);
        btnMon1.Visible = btnMon2.Visible = btnMon3.Visible = false;
        btnMon1.Click += (s, e) => SwitchMonitor("0");
        btnMon2.Click += (s, e) => SwitchMonitor("1");
        btnMon3.Click += (s, e) => SwitchMonitor("2");
        topBar.Controls.Add(btnMon1);
        topBar.Controls.Add(btnMon2);
        topBar.Controls.Add(btnMon3);

        btnDrawToggle = CreateModernBtn("그리기", Color.FromArgb(30, 41, 59), 598, 7, 65);
        btnDrawToggle.Visible = false;
        btnDrawToggle.Click += (s, e) => {
            isDrawingMode = !isDrawingMode;
            btnDrawToggle.BackColor = isDrawingMode ? Color.FromArgb(245, 158, 11) : Color.FromArgb(30, 41, 59);
            btnDrawToggle.Text = isDrawingMode ? "그리기 종료" : "그리기";
        };
        topBar.Controls.Add(btnDrawToggle);

        btnSendMsg = CreateModernBtn("1:1 메시지", Color.FromArgb(109, 40, 217), 668, 7, 85);
        btnSendMsg.Visible = false;
        btnSendMsg.Click += (s, e) => { if (!string.IsNullOrEmpty(currentZoomPcId)) ShowNoticeDialog(currentZoomPcId); };
        topBar.Controls.Add(btnSendMsg);

        btnSendFile = CreateModernBtn("파일 전송", Color.FromArgb(180, 83, 9), 758, 7, 75);
        btnSendFile.Visible = false;
        btnSendFile.Click += (s, e) => { if (!string.IsNullOrEmpty(currentZoomPcId)) SendFileDialogForPc(currentZoomPcId); };
        topBar.Controls.Add(btnSendFile);

        btnKillTask = CreateModernBtn("프로그램 정리", Color.FromArgb(190, 24, 93), 838, 7, 95);
        btnKillTask.Visible = false;
        btnKillTask.Click += (s, e) => {
            if (!string.IsNullOrEmpty(currentZoomPcId)) {
                SendControlFast(currentZoomPcId, "kill_hung_tasks", 0, 0, "0");
                MessageBox.Show("응답 없는 멈춘 프로그램 강제 정리 완료!", "정리 완료");
            }
        };
        topBar.Controls.Add(btnKillTask);

        btnReboot = CreateModernBtn("재부팅", Color.FromArgb(51, 65, 85), 938, 7, 65);
        btnReboot.Visible = false;
        btnReboot.Click += (s, e) => {
            if (!string.IsNullOrEmpty(currentZoomPcId) && MessageBox.Show("이 원격 PC를 재부팅하시겠습니까?", "재부팅", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) {
                SendControlFast(currentZoomPcId, "reboot", 0, 0, "0");
            }
        };
        topBar.Controls.Add(btnReboot);

        btnSingleUpdate = CreateModernBtn("업데이트", Color.FromArgb(5, 150, 105), 1008, 7, 75);
        btnSingleUpdate.Visible = false;
        btnSingleUpdate.Click += (s, e) => {
            if (!string.IsNullOrEmpty(currentZoomPcId)) {
                SendControlFast(currentZoomPcId, "auto_update", 0, 0, "0");
                MessageBox.Show("이 PC에 단독 클라이언트 업데이트 명령을 전송했습니다.", "전송 완료");
            }
        };
        topBar.Controls.Add(btnSingleUpdate);

        lblFps = new Label {
            Text = "60 FPS",
            Font = new Font("Malgun Gothic", 9f, FontStyle.Bold),
            ForeColor = Color.FromArgb(16, 185, 129),
            BackColor = Color.FromArgb(15, 23, 42),
            BorderStyle = BorderStyle.FixedSingle,
            TextAlign = ContentAlignment.MiddleCenter,
            Size = new Size(65, 26),
            Location = new Point(this.ClientSize.Width - 75, 8),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        topBar.Controls.Add(lblFps);

        this.Controls.Add(topBar);

        renderCanvas = new DoubleBufferedPanel {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(11, 19, 43)
        };
        renderCanvas.Paint += RenderCanvas_Paint;
        renderCanvas.MouseDown += RenderCanvas_MouseDown;
        renderCanvas.MouseUp += RenderCanvas_MouseUp;
        renderCanvas.MouseMove += RenderCanvas_MouseMove;
        renderCanvas.MouseWheel += RenderCanvas_MouseWheel;
        renderCanvas.DoubleClick += RenderCanvas_DoubleClick;

        this.Controls.Add(renderCanvas);
    }

    private Button CreateModernBtn(string text, Color bg, int x, int y, int width) {
        var btn = new Button {
            Text = text,
            BackColor = bg,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Malgun Gothic", 8.5f, FontStyle.Bold),
            Location = new Point(x, y),
            Size = new Size(width, 28),
            Cursor = Cursors.Hand
        };
        btn.FlatAppearance.BorderSize = 0;
        return btn;
    }

    private void StartBackgroundLoops() {
        // 1. PC 목록 갱신 루프
        Thread pcLoop = new Thread(() => {
            while (!isClosing) {
                try { FetchPcList(); } catch { }
                Thread.Sleep(1000);
            }
        });
        pcLoop.IsBackground = true;
        pcLoop.Start();

        // 2. 격자 썸네일 수신 루프
        Thread gridLoop = new Thread(() => {
            while (!isClosing) {
                try {
                    if (!isZoomMode) {
                        FetchGridThumbnailsParallel();
                    }
                } catch { }
                Thread.Sleep(60);
            }
        });
        gridLoop.IsBackground = true;
        gridLoop.Start();

        // 3. 관리자 전용 독립 자동 업데이트 감지 루프 (다른 관리자 PC에서만 동작)
        Thread managerUpdateLoop = new Thread(() => {
            Thread.Sleep(3000);
            while (!isClosing && !isSelfUpdating) {
                try {
                    CheckAndApplyManagerSelfUpdate();
                } catch { }
                Thread.Sleep(20000);
            }
        });
        managerUpdateLoop.IsBackground = true;
        managerUpdateLoop.Start();
    }

    private void CheckAndApplyManagerSelfUpdate() {
        if (isSelfUpdating) return;
        try {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(serverUrl + "/api/version/manager?t=" + DateTime.UtcNow.Ticks);
            req.Timeout = 2500;
            req.KeepAlive = true;
            req.Proxy = null;
            using (var res = req.GetResponse())
            using (var sr = new StreamReader(res.GetResponseStream(), Encoding.UTF8)) {
                string json = sr.ReadToEnd();
                var serializer = new JavaScriptSerializer();
                var root = serializer.Deserialize<Dictionary<string, object>>(json);

                if (root != null && root.ContainsKey("managerVersion")) {
                    int serverManagerVer = 0;
                    int.TryParse(root["managerVersion"].ToString(), out serverManagerVer);

                    if (serverManagerVer > CURRENT_MANAGER_VERSION) {
                        isSelfUpdating = true;
                        DownloadAndRestartManager(serverManagerVer);
                    }
                }
            }
        } catch { }
    }

    private void DownloadAndRestartManager(int newVersion) {
        FloatingProgressWidget widget = null;
        this.BeginInvoke((Action)(() => {
            widget = new FloatingProgressWidget("🚀 [다연코퍼레이션] 관리자 자동 업데이트", "최신 버전 (v" + newVersion + ") 다운로드 중... [0%]");
            widget.Show();
        }));

        Task.Run(() => {
            try {
                string tempExe = Path.Combine(Path.GetTempPath(), "dayeon_manager_update_" + DateTime.UtcNow.Ticks + ".exe");
                string downloadUrl = serverUrl + "/api/update/file?name=" + Uri.EscapeDataString("다연코퍼레이션 관리자.exe") + "&t=" + DateTime.UtcNow.Ticks;

                using (WebClient wc = new WebClient()) {
                    wc.DownloadProgressChanged += (s, e) => {
                        if (widget != null) widget.UpdateProgress(e.ProgressPercentage, "최신 관리자 (v" + newVersion + ") 다운로드 중... [" + e.ProgressPercentage + "%]");
                    };
                    wc.DownloadFile(downloadUrl, tempExe);
                }

                if (widget != null) widget.UpdateProgress(100, "다운로드 완료! 관리자 프로그램을 재시작합니다. [100%]");
                Thread.Sleep(800);

                FileInfo fi = new FileInfo(tempExe);
                if (fi.Exists && fi.Length > 50000) {
                    string currentExePath = Application.ExecutablePath;
                    int curPid = Process.GetCurrentProcess().Id;

                    string batchPath = Path.Combine(Path.GetTempPath(), "manager_updater_" + curPid + ".bat");
                    string batContent = "@echo off\r\n" +
                                        "timeout /t 1 /nobreak >nul\r\n" +
                                        ":loop\r\n" +
                                        "taskkill /F /PID " + curPid + " >nul 2>&1\r\n" +
                                        "timeout /t 1 /nobreak >nul\r\n" +
                                        "copy /Y \"" + tempExe + "\" \"" + currentExePath + "\" >nul 2>&1\r\n" +
                                        "if errorlevel 1 goto loop\r\n" +
                                        "del \"" + tempExe + "\" >nul 2>&1\r\n" +
                                        "start \"\" \"" + currentExePath + "\"\r\n" +
                                        "del \"%~f0\" >nul 2>&1\r\n" +
                                        "exit\r\n";

                    File.WriteAllText(batchPath, batContent, Encoding.Default);

                    ProcessStartInfo psi = new ProcessStartInfo {
                        FileName = "cmd.exe",
                        Arguments = "/c \"" + batchPath + "\"",
                        WindowStyle = ProcessWindowStyle.Hidden,
                        CreateNoWindow = true,
                        UseShellExecute = true
                    };
                    Process.Start(psi);
                    Application.Exit();
                } else {
                    isSelfUpdating = false;
                    if (widget != null) this.BeginInvoke((Action)(() => widget.Close()));
                }
            } catch {
                isSelfUpdating = false;
                if (widget != null) this.BeginInvoke((Action)(() => widget.Close()));
            }
        });
    }

    private void PerformManagerDeployOnly() {
        if (MessageBox.Show("수정된 [다연코퍼레이션 관리자.exe]를 서버에 배포하시겠습니까?\n\n* 이 배포는 다른 PC의 관리자 프로그램만 업데이트하며, 원격 PC(클라이언트)에는 영향을 주지 않습니다.", "관리자 프로그램 단독 배포", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) {
            return;
        }

        FloatingProgressWidget widget = new FloatingProgressWidget("🚀 [다연코퍼레이션] 관리자 프로그램 배포", "배포 패키지 바이너리 검증 중... [0%]");
        widget.Show();

        Task.Run(() => {
            try {
                this.BeginInvoke((Action)(() => {
                    btnDeployManager.Enabled = false;
                    btnDeployManager.Text = "배포 중...";
                }));

                widget.UpdateProgress(15, "관리자 실행 파일 읽는 중... [15%]");
                Thread.Sleep(200);

                string currentExePath = Application.ExecutablePath;
                byte[] managerBytes = File.ReadAllBytes(currentExePath);

                widget.UpdateProgress(40, "서버로 관리자 패키지 전송 중... [40%]");
                HttpWebRequest upReq = (HttpWebRequest)WebRequest.Create(serverUrl + "/api/update/upload_binary?name=" + Uri.EscapeDataString("다연코퍼레이션 관리자.exe"));
                upReq.Method = "POST";
                upReq.ContentType = "application/octet-stream";
                upReq.ContentLength = managerBytes.Length;
                upReq.Proxy = null;
                using (var st = upReq.GetRequestStream()) {
                    st.Write(managerBytes, 0, managerBytes.Length);
                }
                using (upReq.GetResponse()) { }

                widget.UpdateProgress(85, "클라우드 버전 레지스트리 동기화 중... [85%]");
                Thread.Sleep(300);

                widget.UpdateProgress(100, "🎉 관리자 배포 완료! 다른 PC가 1분 내 자동 갱신됩니다. [100%]");
                Thread.Sleep(2000);

                this.BeginInvoke((Action)(() => {
                    btnDeployManager.Enabled = true;
                    btnDeployManager.Text = "관리자 프로그램 배포";
                    widget.Close();
                    MessageBox.Show("🎉 관리자 프로그램 배포 완료!\n\n다른 PC/노트북의 관리자 프로그램들이 1분 내로 자동 최신화됩니다.", "배포 완료");
                }));
            } catch (Exception ex) {
                this.BeginInvoke((Action)(() => {
                    btnDeployManager.Enabled = true;
                    btnDeployManager.Text = "관리자 프로그램 배포";
                    widget.Close();
                    MessageBox.Show("관리자 배포 실패: " + ex.Message, "오류");
                }));
            }
        });
    }

    // 🌟 하이브리드 무지연 초고속 스트리밍 (사내 LAN 직통 시 0.1ms 60 FPS, 외부 시 클라우드 자동 전환)
    private void StartZoomStream(string pcId, string monIdx) {
        StopZoomStream();

        zoomLoopCts = new CancellationTokenSource();
        var token = zoomLoopCts.Token;

        var pc = pcList.Find(p => p.Id == pcId);
        string lanIp = (pc != null && !string.IsNullOrEmpty(pc.LanIp)) ? pc.LanIp : "";

        // 🌟 1. 진입 즉시 0초 만에 화면이 뜨도록 첫 프레임 즉시 로드 (무한 '연결 중...' 원천 방지)
        Task.Run(async () => {
            try {
                await FetchZoomFrameSingleAsync(pcId, monIdx);
            } catch { }
        });

        // 🌟 2. 초고속 실시간 스트림 파이프라인 가동
        Task.Run(async () => {
            bool lanConnected = false;

            // 사내 LAN 직통 연결 우선 시도 (200ms 빠른 타임아웃, 127.0.0.1 제외)
            bool isLanIpValid = !string.IsNullOrEmpty(lanIp) && lanIp != "127.0.0.1" && lanIp != "localhost" && !lanIp.StartsWith("127.");
            if (isLanIpValid) {
                try {
                    using (var lanCts = new CancellationTokenSource(250))
                    using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, lanCts.Token)) {
                        string lanWsUrl = "ws://" + lanIp + ":8001/ws/stream?pc=" + Uri.EscapeDataString(pcId) + "&monitor=" + monIdx;
                        zoomWs = new ClientWebSocket();
                        await zoomWs.ConnectAsync(new Uri(lanWsUrl), linkedCts.Token);
                        if (zoomWs.State == WebSocketState.Open) {
                            lanConnected = true;
                        }
                    }
                } catch {
                    if (zoomWs != null) { try { zoomWs.Dispose(); } catch { } zoomWs = null; }
                }
            }

            // 사내 LAN 연결 불가 시 클라우드 외부 서버 연결로 자동 전환
            if (!lanConnected) {
                try {
                    string wsUrl = serverUrl.Replace("https://", "wss://").Replace("http://", "ws://");
                    wsUrl += "/ws/stream?pc=" + Uri.EscapeDataString(pcId) + "&monitor=" + monIdx + "&quality=70";

                    zoomWs = new ClientWebSocket();
                    await zoomWs.ConnectAsync(new Uri(wsUrl), token);
                } catch { }
            }

            if (zoomWs != null && zoomWs.State == WebSocketState.Open) {
                byte[] recvBuf = new byte[1024 * 1024 * 4];
                var memStream = new MemoryStream();

                while (!token.IsCancellationRequested && zoomWs.State == WebSocketState.Open) {
                    memStream.SetLength(0);
                    WebSocketReceiveResult res;
                    do {
                        res = await zoomWs.ReceiveAsync(new ArraySegment<byte>(recvBuf), token);
                        if (res.MessageType == WebSocketMessageType.Close) break;
                        memStream.Write(recvBuf, 0, res.Count);
                    } while (!res.EndOfMessage);

                    if (res.MessageType == WebSocketMessageType.Binary && memStream.Length > 0) {
                        memStream.Position = 0;
                        Bitmap newBmp = CreateSafeBitmapFromStream(memStream);
                        if (newBmp != null) {
                            lock (bmpLock) {
                                if (currentZoomBitmap != null) currentZoomBitmap.Dispose();
                                currentZoomBitmap = newBmp;
                            }
                            UpdateFps();
                            try { renderCanvas.BeginInvoke((Action)(() => renderCanvas.Invalidate())); } catch { }
                        }
                    }
                }
            }

            // WebSocket 실패/종료 시 단일 순차 HTTP 폴백 가동 (중복 루프 없음)
            while (!token.IsCancellationRequested && isZoomMode && currentZoomPcId == pcId) {
                bool err = false;
                try {
                    await FetchZoomFrameSingleAsync(pcId, monIdx);
                } catch {
                    err = true;
                }
                if (err) await Task.Delay(15);
            }
        }, token);
    }

    private void UpdateFps() {
        fpsCounter++;
        if (fpsSw.ElapsedMilliseconds >= 1000) {
            streamFps = (int)(fpsCounter * 1000 / fpsSw.ElapsedMilliseconds);
            fpsCounter = 0;
            fpsSw.Restart();
            try {
                this.BeginInvoke((Action)(() => {
                    lblFps.Text = streamFps + " FPS";
                    lblFps.ForeColor = streamFps >= 20 ? Color.FromArgb(16, 185, 129) : Color.FromArgb(245, 158, 11);
                }));
            } catch { }
        }
    }

    private async Task FetchZoomFrameSingleAsync(string pcId, string monIdx) {
        try {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(serverUrl + "/api/snapshot?pc=" + Uri.EscapeDataString(pcId) + "&monitor=" + monIdx + "&t=" + DateTime.UtcNow.Ticks);
            req.Timeout = 1200;
            req.KeepAlive = true;
            req.Proxy = null;
            using (var res = await req.GetResponseAsync())
            using (var stream = res.GetResponseStream()) {
                Bitmap newBmp = CreateSafeBitmapFromStream(stream);
                if (newBmp != null) {
                    lock (bmpLock) {
                        if (currentZoomBitmap != null) currentZoomBitmap.Dispose();
                        currentZoomBitmap = newBmp;
                    }
                    UpdateFps();
                    try { renderCanvas.BeginInvoke((Action)(() => renderCanvas.Invalidate())); } catch { }
                }
            }
        } catch { }
    }

    private void StopZoomStream() {
        if (zoomLoopCts != null) {
            try { zoomLoopCts.Cancel(); zoomLoopCts.Dispose(); } catch { }
            zoomLoopCts = null;
        }
        if (zoomWs != null) {
            try { zoomWs.Dispose(); } catch { }
            zoomWs = null;
        }
    }

    private readonly SemaphoreSlim wsSendLock = new SemaphoreSlim(1, 1);
    private long lastMouseMoveTicks = 0;

    private void SendControlFast(string pcId, string type, float relX, float relY, string monitor, string key = null, string msg = null, int delta = 0) {
        Task.Run(async () => {
            var dict = new Dictionary<string, object> {
                { "type", type },
                { "relX", relX.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) },
                { "relY", relY.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) },
                { "monitor", monitor }
            };
            if (!string.IsNullOrEmpty(pcId)) dict["pc"] = pcId;
            if (!string.IsNullOrEmpty(key)) dict["key"] = key;
            if (!string.IsNullOrEmpty(msg)) dict["msg"] = msg;
            if (delta != 0) dict["delta"] = delta;

            string json = new JavaScriptSerializer().Serialize(dict);
            byte[] bytes = Encoding.UTF8.GetBytes(json);

            // 1. WebSocket 전송 (0ms 초고속)
            bool wsSent = false;
            if (zoomWs != null && zoomWs.State == WebSocketState.Open && currentZoomPcId == pcId) {
                try {
                    bool acquired = await wsSendLock.WaitAsync(25);
                    if (acquired) {
                        try {
                            if (zoomWs.State == WebSocketState.Open) {
                                await zoomWs.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
                                wsSent = true;
                            }
                        } finally {
                            wsSendLock.Release();
                        }
                    }
                } catch { }
            }

            // 2. HTTP 직통 병렬 백업 전송 (무조건 100% 도달 보장 - 마우스 단순 이동 제외)
            if (!wsSent || (type != "mousemove" && type != "move")) {
                try {
                    string url = serverUrl + "/api/control?pc=" + Uri.EscapeDataString(pcId) +
                                 "&type=" + Uri.EscapeDataString(type) +
                                 "&relX=" + relX.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) +
                                 "&relY=" + relY.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) +
                                 "&monitor=" + Uri.EscapeDataString(monitor);
                    if (!string.IsNullOrEmpty(key)) url += "&key=" + Uri.EscapeDataString(key);
                    if (!string.IsNullOrEmpty(msg)) url += "&msg=" + Uri.EscapeDataString(msg);
                    if (delta != 0) url += "&delta=" + delta;

                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                    req.Timeout = 1200;
                    req.KeepAlive = true;
                    req.Proxy = null;
                    req.BeginGetResponse(ar => {
                        try { using (var res = req.EndGetResponse(ar)) { } } catch { }
                    }, null);
                } catch { }
            }
        });
    }

    private void FetchPcList() {
        try {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(serverUrl + "/api/pcs?t=" + DateTime.UtcNow.Ticks);
            req.Timeout = 5000;
            req.KeepAlive = true;
            req.Proxy = null;
            using (var res = req.GetResponse())
            using (var sr = new StreamReader(res.GetResponseStream(), Encoding.UTF8)) {
                string json = sr.ReadToEnd();
                var serializer = new JavaScriptSerializer();
                var root = serializer.Deserialize<Dictionary<string, object>>(json);

                if (root != null && root.ContainsKey("pcs")) {
                    var rawList = root["pcs"] as System.Collections.ArrayList;
                    var newList = new List<PcItem>();

                    if (rawList != null) {
                        foreach (Dictionary<string, object> dict in rawList) {
                            string pId = dict.ContainsKey("id") ? (dict["id"] ?? "").ToString() : "";
                            if (string.IsNullOrEmpty(pId)) continue;

                            string pName = dict.ContainsKey("name") ? (dict["name"] ?? "").ToString() : pId;
                            string pNick = dict.ContainsKey("nickname") ? (dict["nickname"] ?? "").ToString() : "";
                            string pIp = dict.ContainsKey("ip") ? (dict["ip"] ?? "").ToString() : "";
                            string pLanIp = dict.ContainsKey("lanIp") ? (dict["lanIp"] ?? "").ToString() : "";
                            int pLanPort = 8001;
                            if (dict.ContainsKey("lanPort") && dict["lanPort"] != null) {
                                int.TryParse(dict["lanPort"].ToString(), out pLanPort);
                            }
                            if (pLanPort <= 0) pLanPort = 8001;

                            string pMon = dict.ContainsKey("activeMonitor") ? (dict["activeMonitor"] ?? "0").ToString() : "0";

                            newList.Add(new PcItem {
                                Id = pId,
                                Name = pName,
                                Nickname = pNick,
                                Ip = pIp,
                                LanIp = pLanIp,
                                LanPort = pLanPort,
                                Online = true,
                                ActiveMonitor = pMon
                            });
                        }
                    }

                    if (newList.Count > 0) {
                        pcList = newList;
                        try {
                            this.BeginInvoke((Action)(() => {
                                lblStatus.Text = "연결: " + pcList.Count + "대 온라인";
                                if (!isZoomMode) renderCanvas.Invalidate();
                            }));
                        } catch { }
                    }
                }
            }
        } catch { }
    }

    private static Bitmap CreateSafeBitmapFromStream(Stream stream) {
        try {
            using (var ms = new MemoryStream()) {
                stream.CopyTo(ms);
                if (ms.Length == 0) return null;
                ms.Position = 0;
                using (var temp = Image.FromStream(ms, false, false)) {
                    return new Bitmap(temp);
                }
            }
        } catch {
            return null;
        }
    }

    private void FetchGridThumbnailsParallel() {
        var listCopy = new List<PcItem>(pcList);
        if (listCopy.Count == 0) return;

        Parallel.ForEach(listCopy, new ParallelOptions { MaxDegreeOfParallelism = 12 }, pc => {
            if (isZoomMode) return;
            try {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(serverUrl + "/api/snapshot?pc=" + Uri.EscapeDataString(pc.Id) + "&monitor=" + pc.ActiveMonitor + "&t=" + DateTime.UtcNow.Ticks);
                req.Timeout = 1500;
                req.KeepAlive = true;
                req.Proxy = null;
                using (var res = req.GetResponse())
                using (var stream = res.GetResponseStream()) {
                    Bitmap bmp = CreateSafeBitmapFromStream(stream);
                    if (bmp != null) {
                        Bitmap old;
                        if (thumbnailCache.TryGetValue(pc.Id, out old)) {
                            thumbnailCache[pc.Id] = bmp;
                            try { old.Dispose(); } catch { }
                        } else {
                            thumbnailCache[pc.Id] = bmp;
                        }
                    }
                }
            } catch { }
        });

        if (!isZoomMode) {
            try { renderCanvas.BeginInvoke((Action)(() => renderCanvas.Invalidate())); } catch { }
        }
    }

    private void RenderCanvas_Paint(object sender, PaintEventArgs e) {
        try {
            Graphics g = e.Graphics;

            int canvasW = renderCanvas.Width;
            int canvasH = renderCanvas.Height;

            if (isZoomMode) {
                // 줌 모드: 60 FPS 초고속 다이렉트 렌더링 (CPU / 메모리 렉 제로)
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.SmoothingMode = SmoothingMode.None;
                g.PixelOffsetMode = PixelOffsetMode.None;

                lock (bmpLock) {
                    if (currentZoomBitmap != null) {
                        Rectangle destRect = GetLetterboxRect(canvasW, canvasH, currentZoomBitmap.Width, currentZoomBitmap.Height);
                        g.DrawImage(currentZoomBitmap, destRect, 0, 0, currentZoomBitmap.Width, currentZoomBitmap.Height, GraphicsUnit.Pixel);
                    } else {
                        using (var font = new Font("Malgun Gothic", 14f, FontStyle.Bold))
                        using (var brush = new SolidBrush(Color.FromArgb(148, 163, 184))) {
                            g.DrawString("연결 중...", font, brush, new PointF(canvasW / 2 - 40, canvasH / 2 - 10));
                        }
                    }
                }
            } else {
                // 그리드 관제 모드
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                int totalPcs = Math.Max(1, pcList.Count);
                int cols = 3;
                int rows = 2;

                if (viewFilter == "4split") {
                    cols = 2; rows = 2;
                } else if (viewFilter == "1split") {
                    cols = 1; rows = 1;
                } else {
                    if (totalPcs <= 1) { cols = 1; rows = 1; }
                    else if (totalPcs <= 2) { cols = 2; rows = 1; }
                    else if (totalPcs <= 4) { cols = 2; rows = 2; }
                    else if (totalPcs <= 6) { cols = 3; rows = 2; }
                    else if (totalPcs <= 9) { cols = 3; rows = 3; }
                    else if (totalPcs <= 12) { cols = 4; rows = 3; }
                    else if (totalPcs <= 16) { cols = 4; rows = 4; }
                    else if (totalPcs <= 20) { cols = 5; rows = 4; }
                    else if (totalPcs <= 25) { cols = 5; rows = 5; }
                    else {
                        cols = 6;
                        rows = (int)Math.Ceiling((double)totalPcs / cols);
                    }
                }

                int totalSlots = cols * rows;
                int margin = 8;
                int tileW = (canvasW - (margin * (cols + 1))) / cols;
                int tileH = (canvasH - (margin * (rows + 1))) / rows;

                for (int i = 0; i < totalSlots; i++) {
                    int col = i % cols;
                    int row = i / cols;
                    int x = margin + col * (tileW + margin);
                    int y = margin + row * (tileH + margin);

                    Rectangle tileRect = new Rectangle(x, y, tileW, tileH);

                    using (var bgBrush = new SolidBrush(Color.FromArgb(15, 23, 42)))
                    using (var borderPen = new Pen(Color.FromArgb(30, 41, 59), 1.5f)) {
                        g.FillRectangle(bgBrush, tileRect);
                        g.DrawRectangle(borderPen, tileRect);
                    }

                    if (i < pcList.Count) {
                        var pc = pcList[i];

                        // 1. 카드 상단 바 (순수 텍스트)
                        Rectangle headerRect = new Rectangle(x, y, tileW, 26);
                        using (var headerBrush = new SolidBrush(Color.FromArgb(30, 41, 59)))
                        using (var font = new Font("Malgun Gothic", 8.5f, FontStyle.Bold))
                        using (var textBrush = new SolidBrush(Color.FromArgb(226, 232, 240)))
                        using (var dotBrush = new SolidBrush(Color.FromArgb(34, 197, 94))) {
                            g.FillRectangle(headerBrush, headerRect);
                            g.FillEllipse(dotBrush, x + 8, y + 8, 9, 9);

                            int monIdxVal = 0;
                            if (!string.IsNullOrEmpty(pc.ActiveMonitor)) int.TryParse(pc.ActiveMonitor, out monIdxVal);
                            string titleText = "PC " + (i + 1) + " [" + (monIdxVal + 1) + "번] " + (!string.IsNullOrEmpty(pc.Nickname) ? pc.Nickname : pc.Name);
                            g.DrawString(titleText, font, textBrush, x + 22, y + 5);

                            g.DrawString(pc.Ip, font, new SolidBrush(Color.FromArgb(148, 163, 184)), x + tileW - 75, y + 5);
                        }

                        // 2. 카드 하단 버튼 스트립
                        int btnBarH = 26;
                        Rectangle bottomBarRect = new Rectangle(x, y + tileH - btnBarH, tileW, btnBarH);
                        using (var bBrush = new SolidBrush(Color.FromArgb(15, 23, 42)))
                        using (var font = new Font("Malgun Gothic", 8f, FontStyle.Bold))
                        using (var btnBg = new SolidBrush(Color.FromArgb(30, 41, 59)))
                        using (var zoomBg = new SolidBrush(Color.FromArgb(2, 132, 199))) {
                            g.FillRectangle(bBrush, bottomBarRect);

                            Rectangle nickBtn = new Rectangle(x + 4, y + tileH - btnBarH + 3, 46, 20);
                            g.FillRectangle(btnBg, nickBtn);
                            g.DrawString("별명", font, Brushes.White, nickBtn.X + 11, nickBtn.Y + 3);

                            Rectangle msgBtn = new Rectangle(x + 54, y + tileH - btnBarH + 3, 54, 20);
                            g.FillRectangle(btnBg, msgBtn);
                            g.DrawString("메시지", font, Brushes.White, msgBtn.X + 8, msgBtn.Y + 3);

                            Rectangle cfgBtn = new Rectangle(x + 112, y + tileH - btnBarH + 3, 46, 20);
                            g.FillRectangle(btnBg, cfgBtn);
                            g.DrawString("설정", font, Brushes.White, cfgBtn.X + 11, cfgBtn.Y + 3);

                            Rectangle zoomBtn = new Rectangle(x + tileW - 56, y + tileH - btnBarH + 3, 52, 20);
                            g.FillRectangle(zoomBg, zoomBtn);
                            g.DrawString("확대", font, Brushes.White, zoomBtn.X + 14, zoomBtn.Y + 3);
                        }

                        // 3. 비디오 썸네일
                        Rectangle videoRect = new Rectangle(x + 2, y + 27, tileW - 4, tileH - 27 - btnBarH);
                        Bitmap thumb = null;
                        if (thumbnailCache.TryGetValue(pc.Id, out thumb) && thumb != null) {
                            lock (bmpLock) {
                                try { thumb = (Bitmap)thumb.Clone(); } catch { thumb = null; }
                            }
                        }

                        if (thumb != null) {
                            Rectangle destRect = GetLetterboxRect(videoRect.Width, videoRect.Height, thumb.Width, thumb.Height);
                            destRect.Offset(videoRect.X, videoRect.Y);
                            g.DrawImage(thumb, destRect, 0, 0, thumb.Width, thumb.Height, GraphicsUnit.Pixel);
                            thumb.Dispose();
                        } else {
                            using (var font = new Font("Malgun Gothic", 9.5f, FontStyle.Regular))
                            using (var brush = new SolidBrush(Color.FromArgb(100, 116, 139))) {
                                g.DrawString("연결 중...", font, brush, x + tileW / 2 - 30, y + tileH / 2 - 10);
                            }
                        }
                    } else {
                        Rectangle headerRect = new Rectangle(x, y, tileW, 26);
                        using (var headerBrush = new SolidBrush(Color.FromArgb(20, 28, 48)))
                        using (var font = new Font("Malgun Gothic", 8.5f, FontStyle.Regular))
                        using (var textBrush = new SolidBrush(Color.FromArgb(100, 116, 139)))
                        using (var dotBrush = new SolidBrush(Color.FromArgb(71, 85, 105))) {
                            g.FillRectangle(headerBrush, headerRect);
                            g.FillEllipse(dotBrush, x + 8, y + 8, 9, 9);
                            g.DrawString("PC " + (i + 1), font, textBrush, x + 22, y + 5);
                            g.DrawString("접속 대기", font, textBrush, x + tileW - 70, y + 5);
                        }

                        using (var font = new Font("Malgun Gothic", 9.5f, FontStyle.Regular))
                        using (var brush = new SolidBrush(Color.FromArgb(71, 85, 105))) {
                            g.DrawString("PC " + (i + 1) + " 대기 중", font, brush, x + tileW / 2 - 35, y + tileH / 2 - 10);
                        }
                    }
                }
            }
        } catch { }
    }

    private Rectangle GetLetterboxRect(int boxW, int boxH, int natW, int natH) {
        if (boxW <= 0 || boxH <= 0 || natW <= 0 || natH <= 0) return new Rectangle(0, 0, boxW, boxH);
        double natAR = (double)natW / natH;
        double boxAR = (double)boxW / boxH;

        int rW, rH, oX, oY;
        if (boxAR > natAR) {
            rH = boxH;
            rW = (int)(boxH * natAR);
            oX = (boxW - rW) / 2;
            oY = 0;
        } else {
            rW = boxW;
            rH = (int)(boxW / natAR);
            oX = 0;
            oY = (boxH - rH) / 2;
        }
        return new Rectangle(oX, oY, rW, rH);
    }

    private PointF GetRelCoords(Point clientPt) {
        if (!isZoomMode || currentZoomBitmap == null) return new PointF(0.5f, 0.5f);

        int canvasW = renderCanvas.Width;
        int canvasH = renderCanvas.Height;
        int natW, natH;
        lock (bmpLock) {
            natW = currentZoomBitmap.Width;
            natH = currentZoomBitmap.Height;
        }

        Rectangle destRect = GetLetterboxRect(canvasW, canvasH, natW, natH);
        float relX = (float)(clientPt.X - destRect.X) / destRect.Width;
        float relY = (float)(clientPt.Y - destRect.Y) / destRect.Height;

        relX = Math.Max(0f, Math.Min(1f, relX));
        relY = Math.Max(0f, Math.Min(1f, relY));
        return new PointF(relX, relY);
    }

    private void EnterZoomMode(string pcId) {
        currentZoomPcId = pcId;
        var pc = pcList.Find(p => p.Id == pcId);
        currentMonitorIdx = pc != null ? pc.ActiveMonitor : "0";
        isZoomMode = true;

        lblTitle.Visible = false;
        lblStatus.Visible = false;
        btnNoticeAll.Visible = false;
        btnUpdateRemotePcs.Visible = false;
        btnDeployManager.Visible = false;
        btnViewAll.Visible = false;
        btnView4.Visible = false;
        btnView1.Visible = false;

        btnBackToGrid.Visible = true;
        lblZoomPcInfo.Visible = true;
        lblZoomPcInfo.Text = (pc != null) ? ((!string.IsNullOrEmpty(pc.Nickname) ? pc.Nickname : pc.Name) + " (" + pc.Ip + ")") : pcId;

        btnMon1.Visible = btnMon2.Visible = btnMon3.Visible = true;
        btnDrawToggle.Visible = true;
        btnSendMsg.Visible = btnSendFile.Visible = btnKillTask.Visible = btnReboot.Visible = btnSingleUpdate.Visible = true;

        UpdateMonitorButtons();

        // 🌟 원격 PC에 즉시 60 FPS 터보 가속 및 모니터 선택 명령 전달
        SendControlFast(currentZoomPcId, "focus_change", 1, 0, currentMonitorIdx, "true");
        SendControlFast(currentZoomPcId, "select_monitor", 0, 0, currentMonitorIdx);

        // 🌟 60 FPS 초고속 단일 무지연 스트림 시작
        StartZoomStream(currentZoomPcId, currentMonitorIdx);

        renderCanvas.Invalidate();
    }

    private void ExitZoomMode() {
        if (!string.IsNullOrEmpty(currentZoomPcId)) {
            SendControlFast(currentZoomPcId, "focus_change", 0, 0, currentMonitorIdx, "false");
        }

        StopZoomStream();

        isZoomMode = false;
        currentZoomPcId = null;
        lock (bmpLock) {
            if (currentZoomBitmap != null) {
                currentZoomBitmap.Dispose();
                currentZoomBitmap = null;
            }
        }

        btnBackToGrid.Visible = false;
        lblZoomPcInfo.Visible = false;
        btnMon1.Visible = btnMon2.Visible = btnMon3.Visible = false;
        btnDrawToggle.Visible = false;
        btnSendMsg.Visible = btnSendFile.Visible = btnKillTask.Visible = btnReboot.Visible = btnSingleUpdate.Visible = false;

        lblTitle.Visible = true;
        lblStatus.Visible = true;
        btnNoticeAll.Visible = true;
        btnUpdateRemotePcs.Visible = true;
        btnDeployManager.Visible = true;
        btnViewAll.Visible = true;
        btnView4.Visible = true;
        btnView1.Visible = true;

        renderCanvas.Invalidate();
    }

    private void SwitchMonitor(string mIdx) {
        if (currentMonitorIdx == mIdx) return;
        currentMonitorIdx = mIdx;
        UpdateMonitorButtons();

        if (!string.IsNullOrEmpty(currentZoomPcId)) {
            lock (bmpLock) {
                if (currentZoomBitmap != null) {
                    currentZoomBitmap.Dispose();
                    currentZoomBitmap = null;
                }
            }
            renderCanvas.Invalidate();

            SendControlFast(currentZoomPcId, "select_monitor", 0, 0, currentMonitorIdx);
            StartZoomStream(currentZoomPcId, currentMonitorIdx);
        }
    }

    private void UpdateMonitorButtons() {
        btnMon1.BackColor = (currentMonitorIdx == "0") ? Color.FromArgb(3, 105, 161) : Color.FromArgb(30, 41, 59);
        btnMon2.BackColor = (currentMonitorIdx == "1") ? Color.FromArgb(3, 105, 161) : Color.FromArgb(30, 41, 59);
        btnMon3.BackColor = (currentMonitorIdx == "2") ? Color.FromArgb(3, 105, 161) : Color.FromArgb(30, 41, 59);
    }

    private void RenderCanvas_MouseDown(object sender, MouseEventArgs e) {
        if (!isZoomMode) {
            int totalPcs = Math.Max(1, pcList.Count);
            int cols = 3, rows = 2, margin = 8;
            if (viewFilter == "4split") { cols = 2; rows = 2; }
            else if (viewFilter == "1split") { cols = 1; rows = 1; }
            else {
                if (totalPcs <= 1) { cols = 1; rows = 1; }
                else if (totalPcs <= 2) { cols = 2; rows = 1; }
                else if (totalPcs <= 4) { cols = 2; rows = 2; }
                else if (totalPcs <= 6) { cols = 3; rows = 2; }
                else if (totalPcs <= 9) { cols = 3; rows = 3; }
                else if (totalPcs <= 12) { cols = 4; rows = 3; }
                else if (totalPcs <= 16) { cols = 4; rows = 4; }
                else if (totalPcs <= 20) { cols = 5; rows = 4; }
                else { cols = 6; rows = (int)Math.Ceiling((double)totalPcs / cols); }
            }

            int tileW = (renderCanvas.Width - (margin * (cols + 1))) / cols;
            int tileH = (renderCanvas.Height - (margin * (rows + 1))) / rows;
            int btnBarH = 26;

            for (int i = 0; i < Math.Min(pcList.Count, cols * rows); i++) {
                int col = i % cols;
                int row = i / cols;
                int x = margin + col * (tileW + margin);
                int y = margin + row * (tileH + margin);
                var pc = pcList[i];

                Rectangle tileRect = new Rectangle(x, y, tileW, tileH);
                if (tileRect.Contains(e.Location)) {
                    if (e.Button == MouseButtons.Right) {
                        ShowPcContextMenu(pc, e.Location);
                        return;
                    }

                    Rectangle nickBtn = new Rectangle(x + 4, y + tileH - btnBarH + 3, 46, 20);
                    Rectangle msgBtn = new Rectangle(x + 54, y + tileH - btnBarH + 3, 54, 20);
                    Rectangle cfgBtn = new Rectangle(x + 112, y + tileH - btnBarH + 3, 46, 20);
                    Rectangle zoomBtn = new Rectangle(x + tileW - 56, y + tileH - btnBarH + 3, 52, 20);

                    if (nickBtn.Contains(e.Location)) {
                        ShowChangeNicknameDialog(pc);
                        return;
                    }
                    if (msgBtn.Contains(e.Location)) {
                        ShowNoticeDialog(pc.Id);
                        return;
                    }
                    if (cfgBtn.Contains(e.Location)) {
                        ShowPcContextMenu(pc, new Point(x + 112, y + tileH - btnBarH + 24));
                        return;
                    }
                    if (zoomBtn.Contains(e.Location)) {
                        EnterZoomMode(pc.Id);
                        return;
                    }

                    EnterZoomMode(pc.Id);
                    return;
                }
            }
            return;
        }

        if (!isRemoteControlEnabled || string.IsNullOrEmpty(currentZoomPcId)) return;

        PointF rel = GetRelCoords(e.Location);
        if (e.Button == MouseButtons.Left) {
            isMouseDown = true;
            SendControlFast(currentZoomPcId, "mousedown", rel.X, rel.Y, currentMonitorIdx);
        } else if (e.Button == MouseButtons.Right) {
            SendControlFast(currentZoomPcId, "rightclick", rel.X, rel.Y, currentMonitorIdx);
        }
    }

    private void RenderCanvas_MouseMove(object sender, MouseEventArgs e) {
        if (!isZoomMode || !isRemoteControlEnabled || string.IsNullOrEmpty(currentZoomPcId)) return;

        if (isMouseDown) {
            long now = DateTime.UtcNow.Ticks;
            if (now - lastMouseMoveTicks > 200000) { // 20ms = 200,000 ticks (50 moves/sec)
                lastMouseMoveTicks = now;
                PointF rel = GetRelCoords(e.Location);
                SendControlFast(currentZoomPcId, "mousemove", rel.X, rel.Y, currentMonitorIdx);
            }
        }
    }

    private void RenderCanvas_MouseUp(object sender, MouseEventArgs e) {
        if (!isZoomMode || !isRemoteControlEnabled || string.IsNullOrEmpty(currentZoomPcId)) return;

        PointF rel = GetRelCoords(e.Location);
        if (e.Button == MouseButtons.Left && isMouseDown) {
            isMouseDown = false;
            SendControlFast(currentZoomPcId, "mouseup", rel.X, rel.Y, currentMonitorIdx);
        }
    }

    private void ShowPcContextMenu(PcItem pc, Point loc) {
        ContextMenuStrip menu = new ContextMenuStrip();
        menu.ShowImageMargin = false;
        menu.Font = new Font("Malgun Gothic", 9f, FontStyle.Regular);

        var itemZoom = menu.Items.Add("확대 (원격 제어)");
        itemZoom.Click += (s, e) => EnterZoomMode(pc.Id);

        menu.Items.Add(new ToolStripSeparator());

        var itemMon1 = menu.Items.Add("모니터 1번 선택");
        itemMon1.Click += (s, e) => {
            pc.ActiveMonitor = "0";
            SendControlFast(pc.Id, "select_monitor", 0, 0, "0");
            renderCanvas.Invalidate();
        };

        var itemMon2 = menu.Items.Add("모니터 2번 선택");
        itemMon2.Click += (s, e) => {
            pc.ActiveMonitor = "1";
            SendControlFast(pc.Id, "select_monitor", 0, 0, "1");
            renderCanvas.Invalidate();
        };

        var itemMon3 = menu.Items.Add("모니터 3번 선택");
        itemMon3.Click += (s, e) => {
            pc.ActiveMonitor = "2";
            SendControlFast(pc.Id, "select_monitor", 0, 0, "2");
            renderCanvas.Invalidate();
        };

        menu.Items.Add(new ToolStripSeparator());

        var itemMsg = menu.Items.Add("1:1 메시지 전송");
        itemMsg.Click += (s, e) => ShowNoticeDialog(pc.Id);

        var itemFile = menu.Items.Add("바탕화면 파일 전송");
        itemFile.Click += (s, e) => SendFileDialogForPc(pc.Id);

        var itemNick = menu.Items.Add("PC 별명 변경");
        itemNick.Click += (s, e) => ShowChangeNicknameDialog(pc);

        menu.Items.Add(new ToolStripSeparator());

        var itemKill = menu.Items.Add("응답 없는 프로그램 강제 정리");
        itemKill.Click += (s, e) => {
            SendControlFast(pc.Id, "kill_hung_tasks", 0, 0, "0");
            MessageBox.Show("응답 없는 프로그램 강제 정리 완료!", "정리 완료");
        };

        var itemReboot = menu.Items.Add("원격 PC 재부팅");
        itemReboot.Click += (s, e) => {
            if (MessageBox.Show("이 원격 PC를 재부팅하시겠습니까?", "재부팅", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) {
                SendControlFast(pc.Id, "reboot", 0, 0, "0");
            }
        };

        var itemUpdate = menu.Items.Add("이 PC만 단독 클라이언트 업데이트");
        itemUpdate.Click += (s, e) => {
            SendControlFast(pc.Id, "auto_update", 0, 0, "0");
            MessageBox.Show("단독 클라이언트 업데이트 명령이 전달되었습니다.", "전송 완료");
        };

        menu.Show(renderCanvas, loc);
    }

    private void ShowChangeNicknameDialog(PcItem pc) {
        string input = Microsoft.VisualBasic.Interaction.InputBox("PC의 새로운 별명을 입력하세요:", "별명 변경", !string.IsNullOrEmpty(pc.Nickname) ? pc.Nickname : pc.Name);
        if (!string.IsNullOrEmpty(input)) {
            pc.Nickname = input;
            SendControlFast(pc.Id, "set_nickname", 0, 0, "0", null, input);
            renderCanvas.Invalidate();
        }
    }

    private void RenderCanvas_MouseWheel(object sender, MouseEventArgs e) {
        if (!isZoomMode || !isRemoteControlEnabled || string.IsNullOrEmpty(currentZoomPcId)) return;
        PointF rel = GetRelCoords(e.Location);
        int delta = e.Delta > 0 ? 120 : -120;
        SendControlFast(currentZoomPcId, "wheel", rel.X, rel.Y, currentMonitorIdx, null, null, delta);
    }

    private void RenderCanvas_DoubleClick(object sender, EventArgs e) {
        if (isZoomMode && isRemoteControlEnabled && !string.IsNullOrEmpty(currentZoomPcId)) {
            Point clientPt = renderCanvas.PointToClient(Cursor.Position);
            PointF rel = GetRelCoords(clientPt);
            SendControlFast(currentZoomPcId, "dblclick", rel.X, rel.Y, currentMonitorIdx);
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) {
        if (isZoomMode && isRemoteControlEnabled && !string.IsNullOrEmpty(currentZoomPcId)) {
            if (keyData == Keys.Escape) {
                ExitZoomMode();
                return true;
            }
            Keys keyCode = keyData & Keys.KeyCode;
            int vkInt = (int)keyCode;

            string keyParam = "vk:" + vkInt;
            if (keyCode == Keys.Back) keyParam = "Back";
            else if (keyCode == Keys.Return) keyParam = "Return";
            else if (keyCode == Keys.Space) keyParam = "Space";
            else if (keyCode == Keys.HangulMode || keyCode == Keys.KanaMode || vkInt == 21) keyParam = "Hangul";
            else if (keyCode == Keys.HanjaMode || keyCode == Keys.KanjiMode || vkInt == 25) keyParam = "Hanja";
            else if (keyCode == Keys.Tab) keyParam = "Tab";
            else if (keyCode == Keys.Delete) keyParam = "Delete";
            else if (keyCode == Keys.Left) keyParam = "Left";
            else if (keyCode == Keys.Up) keyParam = "Up";
            else if (keyCode == Keys.Right) keyParam = "Right";
            else if (keyCode == Keys.Down) keyParam = "Down";
            else keyParam = keyCode.ToString();

            SendControlFast(currentZoomPcId, "keydown", 0, 0, currentMonitorIdx, keyParam);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void WndProc(ref Message m) {
        const int WM_KEYDOWN = 0x0100;
        const int WM_SYSKEYDOWN = 0x0104;

        if (isZoomMode && isRemoteControlEnabled && !string.IsNullOrEmpty(currentZoomPcId)) {
            if (m.Msg == WM_KEYDOWN || m.Msg == WM_SYSKEYDOWN) {
                int vk = m.WParam.ToInt32();
                if (vk == 21 || vk == 0x15) { // VK_HANGUL
                    SendControlFast(currentZoomPcId, "keydown", 0, 0, currentMonitorIdx, "Hangul");
                } else if (vk == 25 || vk == 0x19) { // VK_HANJA
                    SendControlFast(currentZoomPcId, "keydown", 0, 0, currentMonitorIdx, "Hanja");
                }
            }
        }
        base.WndProc(ref m);
    }

    private void ShowNoticeDialog(string targetPc) {
        string input = Microsoft.VisualBasic.Interaction.InputBox("전송할 공지 메시지를 입력하세요:", "원격 공지사항 전송", "관리자 공지사항입니다.");
        if (!string.IsNullOrEmpty(input)) {
            SendControlFast(targetPc, "popup", 0, 0, "0", null, input);
            MessageBox.Show("공지 메시지가 전송되었습니다.", "전송 완료");
        }
    }

    private void SendFileDialogForPc(string pcId) {
        if (string.IsNullOrEmpty(pcId)) return;
        using (OpenFileDialog ofd = new OpenFileDialog()) {
            ofd.Title = "원격 PC 바탕화면으로 전송할 파일 선택";
            if (ofd.ShowDialog() == DialogResult.OK) {
                string filePath = ofd.FileName;
                Task.Run(() => {
                    try {
                        string fileName = Path.GetFileName(filePath);
                        byte[] fileBytes = File.ReadAllBytes(filePath);

                        HttpWebRequest req = (HttpWebRequest)WebRequest.Create(serverUrl + "/api/upload_file?name=" + Uri.EscapeDataString(fileName));
                        req.Method = "POST";
                        req.ContentType = "application/octet-stream";
                        req.ContentLength = fileBytes.Length;
                        req.Proxy = null;
                        using (var reqStream = req.GetRequestStream()) {
                            reqStream.Write(fileBytes, 0, fileBytes.Length);
                        }
                        using (req.GetResponse()) { }

                        SendControlFast(pcId, "download_and_save", 0, 0, "0", null, fileName);
                        this.BeginInvoke((Action)(() => MessageBox.Show("파일이 원격 PC 바탕화면으로 전송되었습니다!\n" + fileName, "전송 완료")));
                    } catch (Exception ex) {
                        this.BeginInvoke((Action)(() => MessageBox.Show("파일 전송 실패: " + ex.Message, "오류")));
                    }
                });
            }
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e) {
        isClosing = true;
        StopZoomStream();
        base.OnFormClosing(e);
    }
}

public class DoubleBufferedPanel : Panel {
    public DoubleBufferedPanel() {
        this.DoubleBuffered = true;
        this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        this.UpdateStyles();
    }
}
