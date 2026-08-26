using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
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
    public bool IsLanAlive { get; set; }
}

public class RemoteViewerForm : Form {
    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod", SetLastError = true)]
    static extern uint timeBeginPeriod(uint uMilliseconds);

    public const int CURRENT_MANAGER_VERSION = 500;

    private string serverUrl = "https://dayeon-remote.onrender.com";
    private List<PcItem> pcList = new List<PcItem>();
    private ConcurrentDictionary<string, Bitmap> thumbnailCache = new ConcurrentDictionary<string, Bitmap>();

    private string currentZoomPcId = null;
    private string currentMonitorIdx = "0";
    private Bitmap currentZoomBitmap = null;
    private readonly object bmpLock = new object();

    private CancellationTokenSource zoomLoopCts = null;
    private NetworkStream activeZoomTcpStream = null;
    private readonly object zoomTcpLock = new object();

    private bool isZoomMode = false;
    private bool isRemoteControlEnabled = false; // 🌟 기본값: 화면 보기 모드 (클릭하여 제어 활성화)
    private bool isDrawingMode = false; // 🌟 판서 / 그리기 모드
    private readonly List<List<Point>> drawingStrokes = new List<List<Point>>();
    private List<Point> currentStroke = null;
    private readonly object drawLock = new object();

    private bool isClosing = false;
    private int streamFps = 0;
    private int fpsCounter = 0;
    private Stopwatch fpsSw = Stopwatch.StartNew();
    private int adminCount = 1;

    private string viewFilter = "all";

    // 🌟 우측 하단 관리자 업데이트 & 배포 실시간 진행률 그래프 HUD 필드
    private bool isDeployingManager = false;
    private float deployProgress = 0f;
    private string deployStatusText = "";
    private readonly List<int> deploySpeedHistory = new List<int>();
    private bool isRenderPending = false;

    // UI Controls
    private Panel topBar;
    private Label lblTitle;
    private Label lblStatus;
    private Label lblAdminCount;
    private Button btnNoticeAll;
    private Button btnUpdateRemotePcs;
    private Button btnDeployManager;
    private Button btnViewAll;
    private Button btnView4;
    private Button btnView1;

    // Zoom Controls
    private Button btnBackToGrid;
    private Label lblZoomPcInfo;
    private Button btnControlToggle;
    private Button btnMon1, btnMon2, btnMon3;
    private Button btnDrawToggle;
    private Button btnDrawClear;
    private Button btnSendMsg;
    private Button btnSendFile;
    private Button btnKillTask;
    private Button btnReboot;
    private Button btnSingleUpdate;
    private Label lblFps;

    private DoubleBufferedPanel renderCanvas;
    private System.Windows.Forms.Timer uiTimer;

    private bool isMouseDown = false;
    private long lastMouseMoveTicks = 0;

    [STAThread]
    public static void Main() {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new RemoteViewerForm());
    }

    public RemoteViewerForm() {
        try { timeBeginPeriod(1); } catch { }
        try {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)768 | (SecurityProtocolType)192;
            ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
            ServicePointManager.DefaultConnectionLimit = 100;
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.UseNagleAlgorithm = false;
        } catch { }

        this.Text = "다연코퍼레이션";
        this.Icon = CreateAppIcon();
        this.Size = new Size(1460, 880);
        this.MinimumSize = new Size(1024, 640);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.BackColor = Color.FromArgb(11, 19, 43);
        this.ForeColor = Color.White;
        this.KeyPreview = true;
        this.AllowDrop = true;

        LoadServerUrl();
        InitDefaultPcList();
        InitUI();
        StartBackgroundLoops();
    }

    private static Icon CreateAppIcon() {
        try {
            using (Bitmap bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bmp)) {
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                using (GraphicsPath path = new GraphicsPath()) {
                    path.AddArc(1, 1, 6, 6, 180, 90);
                    path.AddArc(25, 1, 6, 6, 270, 90);
                    path.AddArc(25, 25, 6, 6, 0, 90);
                    path.AddArc(1, 25, 6, 6, 90, 90);
                    path.CloseFigure();
                    using (var brush = new LinearGradientBrush(new Point(0, 0), new Point(32, 32), Color.FromArgb(15, 23, 42), Color.FromArgb(30, 41, 59))) {
                        g.FillPath(brush, path);
                    }
                    using (var pen = new Pen(Color.FromArgb(56, 189, 248), 1.5f)) {
                        g.DrawPath(pen, path);
                    }
                }
                using (var scrBrush = new SolidBrush(Color.FromArgb(11, 19, 43)))
                using (var scrPen = new Pen(Color.FromArgb(14, 165, 233), 1f)) {
                    g.FillRectangle(scrBrush, 5, 5, 22, 14);
                    g.DrawRectangle(scrPen, 5, 5, 22, 14);
                }
                PointF[] bolt = new PointF[] {
                    new PointF(17, 6), new PointF(12, 13), new PointF(16, 13),
                    new PointF(14, 18), new PointF(20, 11), new PointF(16, 11)
                };
                using (var boltBrush = new SolidBrush(Color.FromArgb(250, 204, 21))) {
                    g.FillPolygon(boltBrush, bolt);
                }
                using (var stBrush = new SolidBrush(Color.FromArgb(71, 85, 105))) {
                    g.FillRectangle(stBrush, 14, 19, 4, 3);
                    g.FillRectangle(stBrush, 10, 22, 12, 2);
                }
                IntPtr hIcon = bmp.GetHicon();
                return Icon.FromHandle(hIcon);
            }
        } catch {
            return SystemIcons.Application;
        }
    }

    private static string GetConfigDir() {
        try {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DayeonCorporation");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return dir;
        } catch {
            return AppDomain.CurrentDomain.BaseDirectory;
        }
    }

    private void LoadServerUrl() {
        try {
            string txtPath = Path.Combine(GetConfigDir(), "server_ip.txt");
            if (!File.Exists(txtPath)) txtPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "server_ip.txt");
            if (File.Exists(txtPath)) {
                string line = File.ReadAllText(txtPath).Trim();
                if (!string.IsNullOrEmpty(line)) {
                    if (!line.StartsWith("http://") && !line.StartsWith("https://")) line = "http://" + line;
                    serverUrl = line;
                }
            }
        } catch { }
    }

    private static Dictionary<string, string> persistentNicknames = new Dictionary<string, string>();

    private void LoadPersistentNicknames() {
        try {
            string file = Path.Combine(GetConfigDir(), "pc_nicknames.json");
            if (File.Exists(file)) {
                string json = File.ReadAllText(file, Encoding.UTF8);
                var serializer = new JavaScriptSerializer();
                var dict = serializer.Deserialize<Dictionary<string, string>>(json);
                if (dict != null) {
                    persistentNicknames = dict;
                }
            }
        } catch { }
    }

    private void SavePersistentNicknames() {
        try {
            string file = Path.Combine(GetConfigDir(), "pc_nicknames.json");
            var serializer = new JavaScriptSerializer();
            File.WriteAllText(file, serializer.Serialize(persistentNicknames), Encoding.UTF8);
        } catch { }
    }

    private void InitDefaultPcList() {
        LoadPersistentNicknames();

        string cachePath = Path.Combine(GetConfigDir(), "pc_cache.json");
        if (File.Exists(cachePath)) {
            try {
                string json = File.ReadAllText(cachePath, Encoding.UTF8);
                var serializer = new JavaScriptSerializer();
                var list = serializer.Deserialize<List<PcItem>>(json);
                if (list != null && list.Count > 0) {
                    foreach (var pc in list) {
                        if (persistentNicknames.ContainsKey(pc.Id) && !string.IsNullOrEmpty(persistentNicknames[pc.Id])) {
                            pc.Nickname = persistentNicknames[pc.Id];
                        }
                    }
                    pcList = list;
                    return;
                }
            } catch { }
        }

        // 기본 사내 5대 PC 사전 등록 (클라우드 지연 없이 즉각 로딩)
        pcList = new List<PcItem> {
            new PcItem { Id = "DESKTOP-UVSBE6O_87", Name = "DESKTOP-UVSBE6O_87", LanIp = "172.30.1.87", LanPort = 8001, Online = true, ActiveMonitor = "0" },
            new PcItem { Id = "DESKTOP-1K5QPOO_36", Name = "DESKTOP-1K5QPOO_36", LanIp = "172.30.1.36", LanPort = 8001, Online = true, ActiveMonitor = "0" },
            new PcItem { Id = "DESKTOP-JG7PHSN_91", Name = "DESKTOP-JG7PHSN_91", LanIp = "172.30.1.91", LanPort = 8001, Online = true, ActiveMonitor = "0" },
            new PcItem { Id = "DESKTOP-CB71HV6_7", Name = "DESKTOP-CB71HV6_7", LanIp = "172.30.1.7", LanPort = 8001, Online = true, ActiveMonitor = "0" },
            new PcItem { Id = "DESKTOP-UVSBE6O_10", Name = "DESKTOP-UVSBE6O_10", LanIp = "172.30.1.10", LanPort = 8001, Online = true, ActiveMonitor = "0" }
        };

        foreach (var pc in pcList) {
            if (persistentNicknames.ContainsKey(pc.Id) && !string.IsNullOrEmpty(persistentNicknames[pc.Id])) {
                pc.Nickname = persistentNicknames[pc.Id];
            }
        }
    }

    private void InitUI() {
        topBar = new Panel {
            Dock = DockStyle.Top,
            Height = 46,
            BackColor = Color.FromArgb(15, 23, 42),
            Padding = new Padding(8, 6, 8, 6)
        };
        topBar.Resize += (s, e) => RepositionToolbarButtons();

        // 1. 좌측: 브랜드 및 상태 정보
        lblTitle = new Label {
            Text = "다연코퍼레이션",
            Font = new Font("Malgun Gothic", 11f, FontStyle.Bold),
            ForeColor = Color.FromArgb(56, 189, 248),
            AutoSize = true,
            Location = new Point(10, 13)
        };
        topBar.Controls.Add(lblTitle);

        lblStatus = new Label {
            Text = "연결: 5대 온라인",
            Font = new Font("Malgun Gothic", 9f, FontStyle.Bold),
            ForeColor = Color.FromArgb(34, 197, 94),
            AutoSize = true,
            Location = new Point(125, 15)
        };
        topBar.Controls.Add(lblStatus);

        lblAdminCount = new Label {
            Text = "관리자: 1명 접속 중",
            Font = new Font("Malgun Gothic", 9f, FontStyle.Bold),
            ForeColor = Color.FromArgb(245, 158, 11),
            AutoSize = true,
            Location = new Point(245, 15)
        };
        topBar.Controls.Add(lblAdminCount);

        // 2. 중앙 컨트롤 버튼들 (가운데 정렬)
        btnNoticeAll = CreateModernBtn("전체 공지", Color.FromArgb(79, 70, 229), 0, 7, 85);
        btnNoticeAll.Click += (s, e) => ShowNoticeDialog("all");
        topBar.Controls.Add(btnNoticeAll);

        btnUpdateRemotePcs = CreateModernBtn("원격 PC 전체 업데이트", Color.FromArgb(5, 150, 105), 0, 7, 145);
        btnUpdateRemotePcs.Click += (s, e) => {
            if (MessageBox.Show("전체 원격 PC 5대에 최신 업데이트를 일괄 적용하시겠습니까?\n\n(원격 PC들이 백그라운드에서 최신 파일을 받아 0.5초 만에 자동 재시작됩니다)", "원격 PC 전체 업데이트", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) {
                SendControlFast("all", "auto_update", 0, 0, "0");
                MessageBox.Show("원격 PC 5대에 최신 업데이트 신호가 정상 전송되었습니다.\n각 PC가 자동으로 최신 모듈을 교체하고 재기동됩니다.", "전송 완료");
            }
        };
        topBar.Controls.Add(btnUpdateRemotePcs);

        btnDeployManager = CreateModernBtn("관리자 프로그램 배포", Color.FromArgb(124, 58, 237), 0, 7, 135);
        btnDeployManager.Click += (s, e) => PerformManagerDeployOnly();
        topBar.Controls.Add(btnDeployManager);

        // 3. 우측 고정 버튼들 (전체 PC 보기 / 4분할 / 1분할)
        btnViewAll = CreateModernBtn("전체 PC 보기", Color.FromArgb(2, 132, 199), 0, 7, 95);
        btnViewAll.Click += (s, e) => {
            viewFilter = "all";
            btnViewAll.BackColor = Color.FromArgb(2, 132, 199);
            btnView4.BackColor = Color.FromArgb(30, 41, 59);
            btnView1.BackColor = Color.FromArgb(30, 41, 59);
            renderCanvas.Invalidate();
        };
        topBar.Controls.Add(btnViewAll);

        btnView4 = CreateModernBtn("4분할", Color.FromArgb(30, 41, 59), 0, 7, 55);
        btnView4.Click += (s, e) => {
            viewFilter = "4split";
            btnView4.BackColor = Color.FromArgb(2, 132, 199);
            btnViewAll.BackColor = Color.FromArgb(30, 41, 59);
            btnView1.BackColor = Color.FromArgb(30, 41, 59);
            renderCanvas.Invalidate();
        };
        topBar.Controls.Add(btnView4);

        btnView1 = CreateModernBtn("1분할", Color.FromArgb(30, 41, 59), 0, 7, 55);
        btnView1.Click += (s, e) => {
            viewFilter = "1split";
            btnView1.BackColor = Color.FromArgb(2, 132, 199);
            btnViewAll.BackColor = Color.FromArgb(30, 41, 59);
            btnView4.BackColor = Color.FromArgb(30, 41, 59);
            renderCanvas.Invalidate();
        };
        topBar.Controls.Add(btnView1);

        // 4. 줌(Zoom) 모드 툴바 요소들
        btnBackToGrid = CreateModernBtn("전체 PC 보기 (Esc)", Color.FromArgb(239, 68, 68), 10, 7, 135);
        btnBackToGrid.Visible = false;
        btnBackToGrid.Click += (s, e) => ExitZoomMode();
        topBar.Controls.Add(btnBackToGrid);

        lblZoomPcInfo = new Label {
            Text = "PC 1 (172.30.1.10)",
            Font = new Font("Malgun Gothic", 10f, FontStyle.Bold),
            ForeColor = Color.FromArgb(56, 189, 248),
            AutoSize = true,
            Location = new Point(155, 13),
            Visible = false
        };
        topBar.Controls.Add(lblZoomPcInfo);

        // 🌟 원격 제어 토글 버튼 (기본: 화면보기)
        btnControlToggle = CreateModernBtn("화면보기", Color.FromArgb(51, 65, 85), 0, 7, 75);
        btnControlToggle.Visible = false;
        btnControlToggle.Click += (s, e) => ToggleRemoteControlMode();
        topBar.Controls.Add(btnControlToggle);

        btnMon1 = CreateModernBtn("모니터 1", Color.FromArgb(3, 105, 161), 0, 7, 65);
        btnMon2 = CreateModernBtn("모니터 2", Color.FromArgb(30, 41, 59), 0, 7, 65);
        btnMon3 = CreateModernBtn("모니터 3", Color.FromArgb(30, 41, 59), 0, 7, 65);
        btnMon1.Visible = btnMon2.Visible = btnMon3.Visible = false;
        btnMon1.Click += (s, e) => SwitchMonitor("0");
        btnMon2.Click += (s, e) => SwitchMonitor("1");
        btnMon3.Click += (s, e) => SwitchMonitor("2");
        topBar.Controls.Add(btnMon1);
        topBar.Controls.Add(btnMon2);
        topBar.Controls.Add(btnMon3);

        // 🌟 판서 / 그리기 모드 버튼
        btnDrawToggle = CreateModernBtn("그리기 (판서)", Color.FromArgb(30, 41, 59), 0, 7, 95);
        btnDrawToggle.Visible = false;
        btnDrawToggle.Click += (s, e) => {
            isDrawingMode = !isDrawingMode;
            btnDrawToggle.BackColor = isDrawingMode ? Color.FromArgb(245, 158, 11) : Color.FromArgb(30, 41, 59);
            btnDrawToggle.Text = isDrawingMode ? "그리기 종료" : "그리기 (판서)";
            btnDrawClear.Visible = isDrawingMode;
            RepositionToolbarButtons();
            renderCanvas.Invalidate();
        };
        topBar.Controls.Add(btnDrawToggle);

        btnDrawClear = CreateModernBtn("지우기", Color.FromArgb(220, 38, 38), 0, 7, 65);
        btnDrawClear.Visible = false;
        btnDrawClear.Click += (s, e) => {
            lock (drawLock) { drawingStrokes.Clear(); currentStroke = null; }
            if (!string.IsNullOrEmpty(currentZoomPcId)) {
                SendControlFast(currentZoomPcId, "draw_clear", 0, 0, currentMonitorIdx);
            }
            renderCanvas.Invalidate();
        };
        topBar.Controls.Add(btnDrawClear);

        btnSendMsg = CreateModernBtn("1:1 메시지", Color.FromArgb(109, 40, 217), 0, 7, 85);
        btnSendMsg.Visible = false;
        btnSendMsg.Click += (s, e) => { if (!string.IsNullOrEmpty(currentZoomPcId)) ShowNoticeDialog(currentZoomPcId); };
        topBar.Controls.Add(btnSendMsg);

        btnSendFile = CreateModernBtn("파일 전송", Color.FromArgb(180, 83, 9), 0, 7, 75);
        btnSendFile.Visible = false;
        btnSendFile.Click += (s, e) => { if (!string.IsNullOrEmpty(currentZoomPcId)) SendFileDialogForPc(currentZoomPcId); };
        topBar.Controls.Add(btnSendFile);

        btnKillTask = CreateModernBtn("프로그램 정리", Color.FromArgb(190, 24, 93), 0, 7, 95);
        btnKillTask.Visible = false;
        btnKillTask.Click += (s, e) => {
            if (!string.IsNullOrEmpty(currentZoomPcId)) {
                SendControlFast(currentZoomPcId, "kill_hung_tasks", 0, 0, "0");
                MessageBox.Show("응답 없는 멈춘 프로그램 강제 정리 완료!", "정리 완료");
            }
        };
        topBar.Controls.Add(btnKillTask);

        btnReboot = CreateModernBtn("재부팅", Color.FromArgb(51, 65, 85), 0, 7, 65);
        btnReboot.Visible = false;
        btnReboot.Click += (s, e) => {
            if (!string.IsNullOrEmpty(currentZoomPcId) && MessageBox.Show("이 원격 PC를 재부팅하시겠습니까?", "재부팅", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) {
                SendControlFast(currentZoomPcId, "reboot", 0, 0, "0");
            }
        };
        topBar.Controls.Add(btnReboot);

        btnSingleUpdate = CreateModernBtn("업데이트", Color.FromArgb(5, 150, 105), 0, 7, 75);
        btnSingleUpdate.Visible = false;
        btnSingleUpdate.Click += (s, e) => {
            if (!string.IsNullOrEmpty(currentZoomPcId)) {
                SendControlFast(currentZoomPcId, "auto_update", 0, 0, "0");
                MessageBox.Show("이 PC에 단독 업데이트 명령을 전송했습니다.", "전송 완료");
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
            Location = new Point(this.ClientSize.Width - 75, 9),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        topBar.Controls.Add(lblFps);

        renderCanvas = new DoubleBufferedPanel {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(11, 19, 43),
            AllowDrop = true
        };
        renderCanvas.Paint += RenderCanvas_Paint;
        renderCanvas.MouseDown += RenderCanvas_MouseDown;
        renderCanvas.MouseUp += RenderCanvas_MouseUp;
        renderCanvas.MouseMove += RenderCanvas_MouseMove;
        renderCanvas.MouseWheel += RenderCanvas_MouseWheel;
        renderCanvas.DoubleClick += RenderCanvas_DoubleClick;

        // 🌟 파일 드래그 앤 드롭 이벤트 등록 (바탕화면에서 바로 끌어다 놓기)
        renderCanvas.DragEnter += RenderCanvas_DragEnter;
        renderCanvas.DragDrop += RenderCanvas_DragDrop;

        this.Controls.Add(topBar);
        this.Controls.Add(renderCanvas);
        renderCanvas.BringToFront();

        this.Resize += (s, e) => {
            RepositionToolbarButtons();
        };

        RepositionToolbarButtons();
    }

    private void RepositionToolbarButtons() {
        int w = topBar.Width;
        if (w <= 0) return;

        // 🌟 1. 우측 3개 버튼 고정 배치 (전체 PC 보기 / 4분할 / 1분할)
        int rightMargin = 85;
        btnView1.Location = new Point(w - rightMargin - 60, 8);
        btnView4.Location = new Point(w - rightMargin - 120, 8);
        btnViewAll.Location = new Point(w - rightMargin - 222, 8);

        // 🌟 2. 그리드 모드 중앙 버튼 배치 (가운데 정렬)
        int centerTotalW = 85 + 6 + 145 + 6 + 135;
        int startCenterX = Math.Max(420, (w - centerTotalW) / 2);
        btnNoticeAll.Location = new Point(startCenterX, 8);
        btnUpdateRemotePcs.Location = new Point(startCenterX + 91, 8);
        btnDeployManager.Location = new Point(startCenterX + 91 + 151, 8);

        // 🌟 3. 줌 모드 상단 툴바 배치 (동적 선형 정렬 - 겹침 및 가림 0%)
        int xPos = 10;
        btnBackToGrid.Location = new Point(xPos, 8);
        xPos += btnBackToGrid.Width + 12;

        lblZoomPcInfo.Location = new Point(xPos, 13);
        xPos += Math.Max(60, lblZoomPcInfo.PreferredWidth) + 16;

        btnControlToggle.Location = new Point(xPos, 8);
        xPos += btnControlToggle.Width + 8;

        btnMon1.Location = new Point(xPos, 8);
        xPos += btnMon1.Width + 4;
        btnMon2.Location = new Point(xPos, 8);
        xPos += btnMon2.Width + 4;
        btnMon3.Location = new Point(xPos, 8);
        xPos += btnMon3.Width + 10;

        btnDrawToggle.Location = new Point(xPos, 8);
        xPos += btnDrawToggle.Width + 4;
        btnDrawClear.Location = new Point(xPos, 8);
        if (btnDrawClear.Visible) xPos += btnDrawClear.Width + 4;

        btnSendMsg.Location = new Point(xPos, 8);
        xPos += btnSendMsg.Width + 4;
        btnSendFile.Location = new Point(xPos, 8);
        xPos += btnSendFile.Width + 4;
        btnKillTask.Location = new Point(xPos, 8);
        xPos += btnKillTask.Width + 4;
        btnReboot.Location = new Point(xPos, 8);
        xPos += btnReboot.Width + 4;
        btnSingleUpdate.Location = new Point(xPos, 8);
    }

    private Button CreateModernBtn(string text, Color bg, int x, int y, int width) {
        var btn = new Button {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(width, 30),
            BackColor = bg,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Malgun Gothic", 8.5f, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        btn.FlatAppearance.BorderSize = 0;
        return btn;
    }

    private void ToggleRemoteControlMode() {
        isRemoteControlEnabled = !isRemoteControlEnabled;
        if (isRemoteControlEnabled) {
            btnControlToggle.Text = "원격중";
            btnControlToggle.BackColor = Color.FromArgb(16, 185, 129); // 에메랄드 그린
        } else {
            btnControlToggle.Text = "화면보기";
            btnControlToggle.BackColor = Color.FromArgb(51, 65, 85); // 슬레이트 블루
        }
        RepositionToolbarButtons();
    }

    // 🌟 드래그 앤 드롭 핸들러
    private void RenderCanvas_DragEnter(object sender, DragEventArgs e) {
        if (e.Data.GetDataPresent(DataFormats.FileDrop)) {
            e.Effect = DragDropEffects.Copy;
        } else {
            e.Effect = DragDropEffects.None;
        }
    }

    private void RenderCanvas_DragDrop(object sender, DragEventArgs e) {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (files == null || files.Length == 0) return;

        string targetPc = null;
        if (isZoomMode && !string.IsNullOrEmpty(currentZoomPcId)) {
            targetPc = currentZoomPcId;
        } else {
            Point dropPt = renderCanvas.PointToClient(new Point(e.X, e.Y));
            int totalPcs = Math.Max(1, pcList.Count);
            int cols = 3, rows = 2;
            if (viewFilter == "4split") { cols = 2; rows = 2; }
            else if (viewFilter == "1split") { cols = 1; rows = 1; }

            int gap = 8;
            int tileW = (renderCanvas.Width - (cols + 1) * gap) / cols;
            int tileH = (renderCanvas.Height - (rows + 1) * gap) / rows;
            int maxTiles = cols * rows;

            for (int i = 0; i < Math.Min(maxTiles, pcList.Count); i++) {
                int col = i % cols;
                int row = i / cols;
                int x = gap + col * (tileW + gap);
                int y = gap + row * (tileH + gap);
                Rectangle tileRect = new Rectangle(x, y, tileW, tileH);
                if (tileRect.Contains(dropPt)) {
                    targetPc = pcList[i].Id;
                    break;
                }
            }
        }

        if (string.IsNullOrEmpty(targetPc)) {
            MessageBox.Show("파일을 전송할 원격 PC 카드를 향해 드롭해주세요.", "안내", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var pc = pcList.Find(p => p.Id == targetPc);
        string pcDisplayName = (pc != null) ? (!string.IsNullOrEmpty(pc.Nickname) ? pc.Nickname : pc.Name) : targetPc;

        Task.Run(() => {
            foreach (string filePath in files) {
                if (File.Exists(filePath)) {
                    UploadAndSendFile(targetPc, filePath, pcDisplayName);
                }
            }
        });
    }

    private void UploadAndSendFile(string targetPc, string filePath, string pcDisplayName) {
        try {
            string fileName = Path.GetFileName(filePath);
            byte[] fileBytes = File.ReadAllBytes(filePath);
            string uploadUrl = serverUrl + "/api/upload_file?name=" + Uri.EscapeDataString(fileName) + "&pc=" + Uri.EscapeDataString(targetPc);
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(uploadUrl);
            req.Method = "POST";
            req.ContentType = "application/octet-stream";
            req.ContentLength = fileBytes.Length;
            req.Headers["X-File-Name"] = Uri.EscapeDataString(fileName);
            req.Headers["X-Target-PC"] = Uri.EscapeDataString(targetPc);

            using (var reqStream = req.GetRequestStream()) {
                reqStream.Write(fileBytes, 0, fileBytes.Length);
            }
            using (var res = req.GetResponse()) { }

            SendControlFast(targetPc, "download_file", 0, 0, "0", null, fileName);
            this.BeginInvoke((Action)(() => {
                MessageBox.Show("파일이 [" + pcDisplayName + "] 바탕화면으로 전송되었습니다!\n\n파일명: " + fileName, "파일 전송 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }));
        } catch (Exception ex) {
            this.BeginInvoke((Action)(() => {
                MessageBox.Show("파일 전송 실패: " + ex.Message, "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }));
        }
    }

    private void StartBackgroundLoops() {
        uiTimer = new System.Windows.Forms.Timer { Interval = 250 }; // 상태 갱신용 경량 타이머 (썸네일 수신 시 이벤트로 즉시 렌더링)
        uiTimer.Tick += (s, e) => {
            if (!isZoomMode) renderCanvas.Invalidate();
        };
        uiTimer.Start();

        // 1. PC 목록 갱신
        Task.Run(async () => {
            while (!isClosing) {
                try {
                    FetchPcList();
                } catch { }
                await Task.Delay(1500);
            }
        });

        // 🌟 사내 LAN UDP 고속 자동 검색 (0.1초 즉시 발견)
        StartUdpDiscoveryWorker();

        // 2. 그리드 썸네일 수신 (그리드 모드 시 800ms 주기)
        Task.Run(async () => {
            while (!isClosing) {
                try {
                    if (!isZoomMode) FetchGridThumbnailsParallel();
                } catch { }
                await Task.Delay(800);
            }
        });

        // 3. 비차단 백그라운드 LAN 연결성 점검 (10초 주기)
        Task.Run(async () => {
            while (!isClosing) {
                try {
                    CheckLanConnectivityBackground();
                } catch { }
                await Task.Delay(10000);
            }
        });
    }

    private void CheckLanConnectivityBackground() {
        var listCopy = new List<PcItem>(pcList);
        foreach (var pc in listCopy) {
            if (string.IsNullOrEmpty(pc.LanIp) || pc.LanIp == "127.0.0.1" || pc.LanIp == "localhost" || pc.LanIp.StartsWith("127.")) {
                pc.IsLanAlive = false;
                continue;
            }
            try {
                int port = pc.LanPort > 0 ? pc.LanPort : 8001;
                using (var tcp = new System.Net.Sockets.TcpClient()) {
                    var ar = tcp.BeginConnect(pc.LanIp, port, null, null);
                    bool success = ar.AsyncWaitHandle.WaitOne(100); // 100ms 비차단 검사
                    if (success && tcp.Connected) {
                        tcp.EndConnect(ar);
                        pc.IsLanAlive = true;
                    } else {
                        pc.IsLanAlive = false;
                    }
                }
            } catch {
                pc.IsLanAlive = false;
            }
        }
    }

    private void FetchPcList() {
        try {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(serverUrl + "/api/pcs?t=" + DateTime.UtcNow.Ticks);
            req.Timeout = 4000;
            req.KeepAlive = true;
            req.Proxy = null;
            using (var res = req.GetResponse())
            using (var sr = new StreamReader(res.GetResponseStream(), Encoding.UTF8)) {
                string json = sr.ReadToEnd();
                var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var root = serializer.Deserialize<Dictionary<string, object>>(json);

                if (root != null) {
                    if (root.ContainsKey("adminCount") && root["adminCount"] != null) {
                        int.TryParse(root["adminCount"].ToString(), out adminCount);
                    }

                    if (root.ContainsKey("pcs")) {
                        var rawList = root["pcs"] as System.Collections.ArrayList;
                        var newList = new List<PcItem>();

                        if (rawList != null) {
                            foreach (Dictionary<string, object> dict in rawList) {
                                string pId = dict.ContainsKey("id") ? (dict["id"] ?? "").ToString() : "";
                                if (string.IsNullOrEmpty(pId)) continue;

                                string pName = dict.ContainsKey("name") ? (dict["name"] ?? "").ToString() : pId;
                                string pNick = (dict.ContainsKey("nickname") && dict["nickname"] != null) ? dict["nickname"].ToString() : "";
                                if (string.IsNullOrEmpty(pNick) && persistentNicknames.ContainsKey(pId)) {
                                    pNick = persistentNicknames[pId];
                                } else if (!string.IsNullOrEmpty(pNick)) {
                                    persistentNicknames[pId] = pNick;
                                    SavePersistentNicknames();
                                }

                                string pIp = dict.ContainsKey("ip") ? (dict["ip"] ?? "").ToString() : "";
                                string pLanIp = dict.ContainsKey("lanIp") ? (dict["lanIp"] ?? "").ToString() : "";
                                if (string.IsNullOrEmpty(pLanIp) || pLanIp == "127.0.0.1" || pLanIp == "localhost") {
                                    if (pId.Contains("_")) {
                                        string[] parts = pId.Split('_');
                                        int suffixNum;
                                        if (parts.Length > 1 && int.TryParse(parts[parts.Length - 1], out suffixNum) && suffixNum > 0 && suffixNum <= 254) {
                                            pLanIp = "172.30.1." + suffixNum;
                                        }
                                    }
                                }
                                int pLanPort = 8001;
                                if (dict.ContainsKey("lanPort") && dict["lanPort"] != null) {
                                    int.TryParse(dict["lanPort"].ToString(), out pLanPort);
                                }
                                if (pLanPort <= 0) pLanPort = 8001;

                                string pMon = dict.ContainsKey("activeMonitor") ? (dict["activeMonitor"] ?? "0").ToString() : "0";
                                var existingPcMon = pcList.Find(p => p.Id == pId);
                                if (existingPcMon != null && !string.IsNullOrEmpty(existingPcMon.ActiveMonitor)) {
                                    pMon = existingPcMon.ActiveMonitor;
                                }
                                if (isZoomMode && currentZoomPcId == pId && !string.IsNullOrEmpty(currentMonitorIdx)) {
                                    pMon = currentMonitorIdx;
                                }

                                if (dict.ContainsKey("image") && dict["image"] != null) {
                                    string imgB64 = dict["image"].ToString();
                                    if (!string.IsNullOrEmpty(imgB64)) {
                                        try {
                                            byte[] rawB = Convert.FromBase64String(imgB64);
                                            using (var ms = new MemoryStream(rawB)) {
                                                Bitmap bmp = (Bitmap)Image.FromStream(ms);
                                                Bitmap old;
                                                if (thumbnailCache.TryGetValue(pId, out old)) {
                                                    thumbnailCache[pId] = bmp;
                                                    try { old.Dispose(); } catch { }
                                                } else {
                                                    thumbnailCache[pId] = bmp;
                                                }
                                            }
                                        } catch { }
                                    }
                                }

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

                        lock (lanDiscLock) {
                            foreach (var kv in lanDiscoveredPcs) {
                                var existing = newList.Find(p => p.Id == kv.Key || (!string.IsNullOrEmpty(p.LanIp) && p.LanIp == kv.Value.LanIp));
                                if (existing == null) {
                                    newList.Add(kv.Value);
                                }
                            }
                        }

                        if (newList.Count > 0) {
                            pcList = newList;
                            try {
                                string cachePath = Path.Combine(GetConfigDir(), "pc_cache.json");
                                var serializer2 = new JavaScriptSerializer();
                                File.WriteAllText(cachePath, serializer2.Serialize(pcList), Encoding.UTF8);
                            } catch { }
                            try {
                                this.BeginInvoke((Action)(() => {
                                    lblStatus.Text = "연결: " + pcList.Count + "대 온라인";
                                    lblAdminCount.Text = "관리자: " + Math.Max(1, adminCount) + "명 접속 중";
                                }));
                            } catch { }
                        }
                    }
                }
            }
        } catch { }
    }

    private static readonly Dictionary<string, PcItem> lanDiscoveredPcs = new Dictionary<string, PcItem>();
    private static readonly object lanDiscLock = new object();
    private static readonly string[] knownLanIps = new string[] {
        "172.30.1.7", "172.30.1.8", "172.30.1.10", "172.30.1.14", "172.30.1.36", "172.30.1.87", "172.30.1.91", "172.30.1.30", "172.30.1.26"
    };

    private void StartUdpDiscoveryWorker() {
        Task.Run(() => {
            UdpClient udp = null;
            try {
                udp = new UdpClient();
                udp.EnableBroadcast = true;
                udp.Client.ReceiveTimeout = 1500;

                IPEndPoint anyEp = new IPEndPoint(IPAddress.Any, 0);

                while (!isClosing) {
                    try {
                        byte[] discMsg = Encoding.UTF8.GetBytes("DAYEON_DISCOVER");
                        try { udp.Send(discMsg, discMsg.Length, new IPEndPoint(IPAddress.Broadcast, 8889)); } catch { }
                        try { udp.Send(discMsg, discMsg.Length, new IPEndPoint(IPAddress.Parse("172.30.1.255"), 8889)); } catch { }

                        long start = DateTime.UtcNow.Ticks;
                        while (DateTime.UtcNow.Ticks - start < 12000000) {
                            try {
                                byte[] resp = udp.Receive(ref anyEp);
                                string str = Encoding.UTF8.GetString(resp);
                                if (str.StartsWith("DAYEON_OFFER|")) {
                                    string[] parts = str.Split('|');
                                    if (parts.Length >= 4) {
                                        string pName = parts[1];
                                        string pIp = anyEp.Address.ToString();
                                        string pId = pName + "_" + pIp.Split('.').Last();

                                        lock (lanDiscLock) {
                                            string nick = persistentNicknames.ContainsKey(pId) ? persistentNicknames[pId] : (persistentNicknames.ContainsKey(pName) ? persistentNicknames[pName] : "");
                                            lanDiscoveredPcs[pId] = new PcItem {
                                                Id = pId,
                                                Name = pName,
                                                Nickname = nick,
                                                Ip = pIp,
                                                LanIp = pIp,
                                                LanPort = 8001,
                                                Online = true
                                            };
                                        }
                                    }
                                }
                            } catch { break; }
                        }
                    } catch { }

                    // knownLanIps 직통 헬스체크
                    foreach (var ip in knownLanIps) {
                        try {
                            using (var tcp = new TcpClient()) {
                                var ar = tcp.BeginConnect(ip, 8001, null, null);
                                if (ar.AsyncWaitHandle.WaitOne(200)) {
                                    tcp.EndConnect(ar);
                                    string pId = "PC_" + ip.Split('.').Last();
                                    lock (lanDiscLock) {
                                        if (!lanDiscoveredPcs.ContainsKey(pId)) {
                                            string nick = persistentNicknames.ContainsKey(pId) ? persistentNicknames[pId] : "";
                                            lanDiscoveredPcs[pId] = new PcItem {
                                                Id = pId,
                                                Name = pId,
                                                Nickname = nick,
                                                Ip = ip,
                                                LanIp = ip,
                                                LanPort = 8001,
                                                Online = true
                                            };
                                        }
                                    }
                                }
                            }
                        } catch { }
                    }

                    Thread.Sleep(2000);
                }
            } catch {
            } finally {
                if (udp != null) try { udp.Close(); } catch { }
            }
        });
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

    private string GetLanIpForPc(string pcId) {
        var pc = pcList.Find(p => p.Id == pcId);
        if (pc != null && !string.IsNullOrEmpty(pc.LanIp) && pc.LanIp != "127.0.0.1" && pc.LanIp != "localhost") {
            return pc.LanIp;
        }
        if (!string.IsNullOrEmpty(pcId) && pcId.Contains("_")) {
            string[] parts = pcId.Split('_');
            int suffixNum;
            if (parts.Length > 1 && int.TryParse(parts[parts.Length - 1], out suffixNum) && suffixNum > 0 && suffixNum <= 254) {
                return "172.30.1." + suffixNum;
            }
        }
        return null;
    }

    private static Bitmap FetchTcp8888Thumbnail(string ip, byte monIdx) {
        TcpClient client = null;
        try {
            client = new TcpClient();
            client.NoDelay = true;
            var ar = client.BeginConnect(ip, 8888, null, null);
            if (!ar.AsyncWaitHandle.WaitOne(350)) {
                client.Close();
                return null;
            }
            client.EndConnect(ar);
            var ns = client.GetStream();
            ns.ReadTimeout = 600;
            ns.WriteTimeout = 300;

            byte[] cmd = new byte[8];
            cmd[0] = 0x01; // SET_MODE
            cmd[1] = monIdx;
            // param1 = 0 (thumbnail mode)
            ns.Write(cmd, 0, 8);

            byte[] hdr = new byte[12];
            int readH = 0;
            while (readH < 12) {
                int r = ns.Read(hdr, readH, 12 - readH);
                if (r <= 0) break;
                readH += r;
            }
            if (readH < 12 || hdr[0] != 'D' || hdr[1] != 'Y' || hdr[2] != '0' || hdr[3] != '1') return null;

            int frameLen = BitConverter.ToInt32(hdr, 8);
            if (frameLen <= 0 || frameLen > 1024 * 1024 * 5) return null;

            byte[] frameBuf = new byte[frameLen];
            int readP = 0;
            while (readP < frameLen) {
                int r = ns.Read(frameBuf, readP, frameLen - readP);
                if (r <= 0) break;
                readP += r;
            }
            if (readP < frameLen) return null;

            using (var ms = new MemoryStream(frameBuf)) {
                return (Bitmap)Image.FromStream(ms);
            }
        } catch {
            return null;
        } finally {
            if (client != null) try { client.Close(); } catch { }
        }
    }

    private void FetchGridThumbnailsParallel() {
        if (isZoomMode) return;
        var listCopy = new List<PcItem>(pcList);
        if (listCopy.Count == 0) return;

        Parallel.ForEach(listCopy, new ParallelOptions { MaxDegreeOfParallelism = 6 }, pc => {
            if (isZoomMode) return;
            string mon = string.IsNullOrEmpty(pc.ActiveMonitor) ? "0" : pc.ActiveMonitor;
            string lanIp = GetLanIpForPc(pc.Id);
            Bitmap bmp = null;

            if (lanIp != null) {
                // 1. TCP 8888 초고속 네이티브 스트림 우선 (0.01ms)
                byte bMon = 0;
                byte.TryParse(mon, out bMon);
                bmp = FetchTcp8888Thumbnail(lanIp, bMon);

                // 2. HTTP 8001 폴백
                if (bmp == null) {
                    try {
                        int port = pc.LanPort > 0 ? pc.LanPort : 8001;
                        string lanUrl = "http://" + lanIp + ":" + port + "/api/snapshot?monitor=" + mon + "&t=" + DateTime.UtcNow.Ticks;
                        HttpWebRequest lanReq = (HttpWebRequest)WebRequest.Create(lanUrl);
                        lanReq.Timeout = 800;
                        lanReq.KeepAlive = true;
                        lanReq.Proxy = null;
                        using (var res = lanReq.GetResponse())
                        using (var stream = res.GetResponseStream()) {
                            bmp = CreateSafeBitmapFromStream(stream);
                        }
                    } catch { }
                }
            }

            // 3. 클라우드 중계 폴백
            if (bmp == null) {
                try {
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(serverUrl + "/api/snapshot?pc=" + Uri.EscapeDataString(pc.Id) + "&monitor=" + mon + "&t=" + DateTime.UtcNow.Ticks);
                    req.Timeout = 1200;
                    req.KeepAlive = true;
                    req.Proxy = null;
                    using (var res = req.GetResponse())
                    using (var stream = res.GetResponseStream()) {
                        bmp = CreateSafeBitmapFromStream(stream);
                    }
                } catch { }
            }

            if (bmp != null) {
                Bitmap old;
                if (thumbnailCache.TryGetValue(pc.Id, out old)) {
                    thumbnailCache[pc.Id] = bmp;
                    try { old.Dispose(); } catch { }
                } else {
                    thumbnailCache[pc.Id] = bmp;
                }
            }
        });

        if (!isZoomMode) {
            try { renderCanvas.BeginInvoke((Action)(() => renderCanvas.Invalidate())); } catch { }
        }
    }

    private void RenderCanvas_Paint(object sender, PaintEventArgs e) {
        try {
            isRenderPending = false;
            Graphics g = e.Graphics;
            int canvasW = renderCanvas.Width;
            int canvasH = renderCanvas.Height;

            if (isZoomMode) {
                // 🌟 줌 모드: 초고속 0.01ms 무지연 렌더링 (전체 화면 확대 시에도 렉 제로)
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.SmoothingMode = SmoothingMode.None;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.CompositingQuality = CompositingQuality.HighSpeed;

                lock (bmpLock) {
                    if (currentZoomBitmap != null) {
                        Rectangle destRect = GetLetterboxRect(canvasW, canvasH, currentZoomBitmap.Width, currentZoomBitmap.Height);
                        g.DrawImage(currentZoomBitmap, destRect, 0, 0, currentZoomBitmap.Width, currentZoomBitmap.Height, GraphicsUnit.Pixel);
                    } else {
                        using (var font = new Font("Malgun Gothic", 14f, FontStyle.Bold))
                        using (var brush = new SolidBrush(Color.FromArgb(148, 163, 184))) {
                            g.DrawString("원격 PC 화면 로딩 중...", font, brush, new PointF(canvasW / 2 - 80, canvasH / 2 - 10));
                        }
                    }
                }

                // 🌟 판서 / 그리기 스트로크 렌더링
                lock (drawLock) {
                    if (drawingStrokes.Count > 0) {
                        using (var pen = new Pen(Color.FromArgb(239, 68, 68), 4f)) {
                            pen.StartCap = LineCap.Round;
                            pen.EndCap = LineCap.Round;
                            pen.LineJoin = LineJoin.Round;
                            foreach (var stroke in drawingStrokes) {
                                if (stroke != null && stroke.Count > 1) {
                                    g.DrawLines(pen, stroke.ToArray());
                                }
                            }
                        }
                    }
                }
            } else {
                // 그리드 관제 모드 (선명하고 깔끔한 Bilinear 렌더링)
                g.SmoothingMode = SmoothingMode.None;
                g.InterpolationMode = InterpolationMode.Bilinear;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.CompositingQuality = CompositingQuality.HighSpeed;

                int totalPcs = Math.Max(1, pcList.Count);
                int cols = 3;
                int rows = 2;

                if (viewFilter == "4split") { cols = 2; rows = 2; }
                else if (viewFilter == "1split") { cols = 1; rows = 1; }

                int gap = 8;
                int tileW = (canvasW - (cols + 1) * gap) / cols;
                int tileH = (canvasH - (rows + 1) * gap) / rows;
                int maxTiles = cols * rows;

                for (int i = 0; i < maxTiles; i++) {
                    int col = i % cols;
                    int row = i / cols;
                    int x = gap + col * (tileW + gap);
                    int y = gap + row * (tileH + gap);

                    Rectangle tileRect = new Rectangle(x, y, tileW, tileH);
                    using (var bgBrush = new SolidBrush(Color.FromArgb(15, 23, 42)))
                    using (var borderPen = new Pen(Color.FromArgb(30, 41, 59), 1.5f)) {
                        g.FillRectangle(bgBrush, tileRect);
                        g.DrawRectangle(borderPen, tileRect);
                    }

                    if (i < pcList.Count) {
                        var pc = pcList[i];

                        // 1. 카드 상단 바
                        Rectangle headerRect = new Rectangle(x, y, tileW, 26);
                        using (var headerBrush = new SolidBrush(Color.FromArgb(30, 41, 59)))
                        using (var font = new Font("Malgun Gothic", 8.5f, FontStyle.Bold))
                        using (var textBrush = new SolidBrush(Color.FromArgb(226, 232, 240)))
                        using (var dotBrush = new SolidBrush(Color.FromArgb(34, 197, 94))) {
                            g.FillRectangle(headerBrush, headerRect);
                            g.FillEllipse(dotBrush, x + 8, y + 8, 9, 9);

                            int monIdxVal = 0;
                            if (!string.IsNullOrEmpty(pc.ActiveMonitor)) int.TryParse(pc.ActiveMonitor, out monIdxVal);
                            string displayName = !string.IsNullOrEmpty(pc.Nickname) ? pc.Nickname : pc.Name;
                            string titleText = "PC " + (i + 1) + " [" + (monIdxVal + 1) + "번] " + displayName;
                            g.DrawString(titleText, font, textBrush, x + 22, y + 5);

                            string displayIp = (!string.IsNullOrEmpty(pc.LanIp) && pc.LanIp != "127.0.0.1") ? pc.LanIp : pc.Ip;
                            g.DrawString(displayIp, font, new SolidBrush(Color.FromArgb(148, 163, 184)), x + tileW - 95, y + 5);
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
                                g.DrawString("화면 수신 중...", font, brush, x + tileW / 2 - 38, y + tileH / 2 - 10);
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

        // 🌟 기본값: 화면 보기 모드 (안전 뷰어)
        isRemoteControlEnabled = false;
        btnControlToggle.Text = "화면보기";
        btnControlToggle.BackColor = Color.FromArgb(51, 65, 85);

        // 그리드 버튼 숨기기
        lblTitle.Visible = false;
        lblStatus.Visible = false;
        lblAdminCount.Visible = false;
        btnNoticeAll.Visible = false;
        btnUpdateRemotePcs.Visible = false;
        btnDeployManager.Visible = false;
        btnViewAll.Visible = false;
        btnView4.Visible = false;
        btnView1.Visible = false;

        // 줌 버튼 표시
        btnBackToGrid.Visible = true;
        lblZoomPcInfo.Visible = true;
        string displayName = (pc != null) ? (!string.IsNullOrEmpty(pc.Nickname) ? pc.Nickname : pc.Name) : pcId;
        string displayIp = (pc != null && !string.IsNullOrEmpty(pc.LanIp) && pc.LanIp != "127.0.0.1") ? pc.LanIp : (pc != null ? pc.Ip : "");
        lblZoomPcInfo.Text = displayName + " (" + displayIp + ")";

        btnControlToggle.Visible = true;
        btnMon1.Visible = btnMon2.Visible = btnMon3.Visible = true;
        btnDrawToggle.Visible = true;
        btnDrawClear.Visible = isDrawingMode;
        btnSendMsg.Visible = btnSendFile.Visible = btnKillTask.Visible = btnReboot.Visible = btnSingleUpdate.Visible = true;

        UpdateMonitorButtons();
        topBar.BringToFront();
        RepositionToolbarButtons();

        lock (bmpLock) {
            if (thumbnailCache.ContainsKey(pcId) && thumbnailCache[pcId] != null) {
                try { currentZoomBitmap = new Bitmap(thumbnailCache[pcId]); } catch { }
            }
        }

        SendControlFast(currentZoomPcId, "focus_change", 1, 0, currentMonitorIdx, "true");
        SendControlFast(currentZoomPcId, "select_monitor", 0, 0, currentMonitorIdx);
        StartZoomStream(currentZoomPcId, currentMonitorIdx);

        renderCanvas.Invalidate();
    }

    private void ExitZoomMode() {
        if (!string.IsNullOrEmpty(currentZoomPcId)) {
            var pc = pcList.Find(p => p.Id == currentZoomPcId);
            if (pc != null) pc.ActiveMonitor = currentMonitorIdx;
            SendControlFast(currentZoomPcId, "focus_change", 0, 0, currentMonitorIdx, "false");
        }

        if (zoomLoopCts != null) {
            try { zoomLoopCts.Cancel(); } catch { }
            zoomLoopCts = null;
        }

        isZoomMode = false;
        isDrawingMode = false;
        isRemoteControlEnabled = false;
        currentZoomPcId = null;

        lock (bmpLock) {
            if (currentZoomBitmap != null) {
                currentZoomBitmap.Dispose();
                currentZoomBitmap = null;
            }
        }

        lock (drawLock) {
            drawingStrokes.Clear();
            currentStroke = null;
        }

        // 그리드 버튼 복원
        lblTitle.Visible = true;
        lblStatus.Visible = true;
        lblAdminCount.Visible = true;
        btnNoticeAll.Visible = true;
        btnUpdateRemotePcs.Visible = true;
        btnDeployManager.Visible = true;
        btnViewAll.Visible = true;
        btnView4.Visible = true;
        btnView1.Visible = true;

        // 줌 버튼 숨기기
        btnBackToGrid.Visible = false;
        lblZoomPcInfo.Visible = false;
        btnControlToggle.Visible = false;
        btnMon1.Visible = btnMon2.Visible = btnMon3.Visible = false;
        btnDrawToggle.Visible = false;
        btnDrawClear.Visible = false;
        btnSendMsg.Visible = btnSendFile.Visible = btnKillTask.Visible = btnReboot.Visible = btnSingleUpdate.Visible = false;

        renderCanvas.BringToFront();
        RepositionToolbarButtons();
        renderCanvas.Invalidate();
    }

    private void SwitchMonitor(string monIdx) {
        currentMonitorIdx = monIdx;
        UpdateMonitorButtons();
        if (!string.IsNullOrEmpty(currentZoomPcId)) {
            var pc = pcList.Find(p => p.Id == currentZoomPcId);
            if (pc != null) pc.ActiveMonitor = monIdx;

            byte bMon = 0;
            byte.TryParse(monIdx, out bMon);

            // 활성 TCP 8888 스트림에 SET_MONITOR 직통 전송 (재연결 불필요)
            bool sent = false;
            lock (zoomTcpLock) {
                if (activeZoomTcpStream != null && activeZoomTcpStream.CanWrite) {
                    try {
                        byte[] cmd = new byte[8];
                        cmd[0] = 0x02; // SET_MONITOR
                        cmd[1] = bMon;
                        activeZoomTcpStream.Write(cmd, 0, 8);
                        sent = true;
                    } catch { }
                }
            }

            // TCP 8888 없으면 HTTP 재연결
            if (!sent) {
                SendControlFast(currentZoomPcId, "select_monitor", 0, 0, monIdx);
                StartZoomStream(currentZoomPcId, monIdx);
            }
        }
    }

    private void UpdateMonitorButtons() {
        btnMon1.BackColor = (currentMonitorIdx == "0") ? Color.FromArgb(3, 105, 161) : Color.FromArgb(30, 41, 59);
        btnMon2.BackColor = (currentMonitorIdx == "1") ? Color.FromArgb(3, 105, 161) : Color.FromArgb(30, 41, 59);
        btnMon3.BackColor = (currentMonitorIdx == "2") ? Color.FromArgb(3, 105, 161) : Color.FromArgb(30, 41, 59);
    }

    private void StartZoomStream(string pcId, string monIdx) {
        if (zoomLoopCts != null) {
            try { zoomLoopCts.Cancel(); } catch { }
        }
        zoomLoopCts = new CancellationTokenSource();
        var token = zoomLoopCts.Token;
        isRenderPending = false;

        var pc = pcList.Find(p => p.Id == pcId);
        string lanIp = GetLanIpForPc(pcId);
        byte bMon = 0;
        byte.TryParse(monIdx, out bMon);

        Task.Run(async () => {
            while (!token.IsCancellationRequested && isZoomMode && currentZoomPcId == pcId) {
                // 1. TCP 8888 영상 전용 스트림 (제어 명령은 TCP 8002로 분리)
                if (lanIp != null) {
                    TcpClient tcp = null;
                    try {
                        tcp = new TcpClient();
                        tcp.NoDelay = true;
                        tcp.ReceiveBufferSize = 1024 * 1024 * 4;
                        var ar = tcp.BeginConnect(lanIp, 8888, null, null);
                        if (ar.AsyncWaitHandle.WaitOne(500)) {
                            tcp.EndConnect(ar);
                            var ns = tcp.GetStream();
                            ns.ReadTimeout = 3000;
                            ns.WriteTimeout = 500;

                            // 제어 명령 직통 전송용 바인딩
                            lock (zoomTcpLock) { activeZoomTcpStream = ns; }

                            byte[] setModeCmd = new byte[8];
                            setModeCmd[0] = 0x01; // SET_MODE
                            setModeCmd[1] = bMon;
                            BitConverter.GetBytes((short)1).CopyTo(setModeCmd, 4); // zoom
                            ns.Write(setModeCmd, 0, 8);

                            byte[] hdr = new byte[12];
                            byte[] frameBuf = new byte[1024 * 1024 * 4];

                            while (!token.IsCancellationRequested && isZoomMode && currentZoomPcId == pcId && tcp.Connected) {
                                int rH = 0;
                                while (rH < 12) {
                                    int r = ns.Read(hdr, rH, 12 - rH);
                                    if (r <= 0) break;
                                    rH += r;
                                }
                                if (rH < 12 || hdr[0] != 'D' || hdr[1] != 'Y' || hdr[2] != '0' || hdr[3] != '1') break;

                                int frameLen = BitConverter.ToInt32(hdr, 8);
                                if (frameLen <= 0 || frameLen > frameBuf.Length) break;

                                int rP = 0;
                                while (rP < frameLen) {
                                    int r = ns.Read(frameBuf, rP, frameLen - rP);
                                    if (r <= 0) break;
                                    rP += r;
                                }
                                if (rP < frameLen) break;

                                try {
                                    using (var frameMs = new MemoryStream(frameBuf, 0, frameLen, false))
                                    using (var temp = Image.FromStream(frameMs, false, false)) {
                                        Bitmap newBmp = new Bitmap(temp);
                                        lock (bmpLock) {
                                            if (currentZoomBitmap != null) currentZoomBitmap.Dispose();
                                            currentZoomBitmap = newBmp;
                                            if (!isRenderPending) {
                                                isRenderPending = true;
                                                try {
                                                    renderCanvas.BeginInvoke((Action)(() => {
                                                        isRenderPending = false;
                                                        renderCanvas.Invalidate();
                                                    }));
                                                } catch { isRenderPending = false; }
                                            }
                                        }
                                        UpdateStreamFps();
                                    }
                                } catch { }
                            }
                        }
                    } catch {
                    } finally {
                        lock (zoomTcpLock) { activeZoomTcpStream = null; }
                        if (tcp != null) try { tcp.Close(); } catch { }
                    }
                }

                if (token.IsCancellationRequested || !isZoomMode || currentZoomPcId != pcId) break;

                // 2. HTTP 스트림 폴백 (agent.js 8001)
                try {
                    string streamUrl = (lanIp != null) 
                        ? ("http://" + lanIp + ":" + (pc != null && pc.LanPort > 0 ? pc.LanPort : 8001) + "/api/stream?monitor=" + monIdx) 
                        : (serverUrl + "/api/stream?pc=" + Uri.EscapeDataString(pcId) + "&monitor=" + monIdx);

                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(streamUrl);
                    req.Timeout = 2500;
                    req.ReadWriteTimeout = 3000;
                    req.KeepAlive = true;
                    req.Proxy = null;

                    using (var res = req.GetResponse())
                    using (var stream = res.GetResponseStream()) {
                        try { stream.ReadTimeout = 2500; } catch { }
                        byte[] buffer = new byte[64 * 1024];
                        byte[] streamBuf = new byte[1024 * 1024];
                        int streamLen = 0;

                        int bytesRead;
                        while (!token.IsCancellationRequested && isZoomMode && currentZoomPcId == pcId && (bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0) {
                            if (streamLen + bytesRead > streamBuf.Length) {
                                byte[] newBuf = new byte[Math.Max(streamBuf.Length * 2, streamLen + bytesRead)];
                                Buffer.BlockCopy(streamBuf, 0, newBuf, 0, streamLen);
                                streamBuf = newBuf;
                            }
                            Buffer.BlockCopy(buffer, 0, streamBuf, streamLen, bytesRead);
                            streamLen += bytesRead;

                            int soi = -1;
                            int lastEoi = -1;
                            for (int i = 0; i < streamLen - 1; i++) {
                                if (streamBuf[i] == 0xFF && streamBuf[i + 1] == 0xD8) {
                                    soi = i;
                                    break;
                                }
                            }

                            if (soi >= 0) {
                                for (int i = soi + 2; i < streamLen - 1; i++) {
                                    if (streamBuf[i] == 0xFF && streamBuf[i + 1] == 0xD9) {
                                        lastEoi = i + 2;
                                    }
                                }

                                if (lastEoi > soi) {
                                    int jpegLen = lastEoi - soi;
                                    try {
                                        using (var frameMs = new MemoryStream(streamBuf, soi, jpegLen, false))
                                        using (var temp = Image.FromStream(frameMs, false, false)) {
                                            Bitmap newBmp = new Bitmap(temp);
                                            lock (bmpLock) {
                                                if (currentZoomBitmap != null) currentZoomBitmap.Dispose();
                                                currentZoomBitmap = newBmp;
                                                if (!isRenderPending) {
                                                    isRenderPending = true;
                                                    try {
                                                        renderCanvas.BeginInvoke((Action)(() => {
                                                            isRenderPending = false;
                                                            renderCanvas.Invalidate();
                                                        }));
                                                    } catch { isRenderPending = false; }
                                                }
                                            }
                                            UpdateStreamFps();
                                        }
                                    } catch { }

                                    int remaining = streamLen - lastEoi;
                                    if (remaining > 0) {
                                        Buffer.BlockCopy(streamBuf, lastEoi, streamBuf, 0, remaining);
                                    }
                                    streamLen = remaining;
                                }
                            }
                        }
                    }
                } catch {
                    // 폴백: 스냅샷 1회 수신
                    if (lanIp != null) {
                        try {
                            int port = (pc != null && pc.LanPort > 0) ? pc.LanPort : 8001;
                            string lanUrl = "http://" + lanIp + ":" + port + "/api/snapshot?monitor=" + monIdx + "&t=" + DateTime.UtcNow.Ticks;
                            HttpWebRequest sReq = (HttpWebRequest)WebRequest.Create(lanUrl);
                            sReq.Timeout = 300;
                            sReq.KeepAlive = true;
                            sReq.Proxy = null;
                            using (var sRes = sReq.GetResponse())
                            using (var sStream = sRes.GetResponseStream()) {
                                var bmp = CreateSafeBitmapFromStream(sStream);
                                if (bmp != null) {
                                    lock (bmpLock) {
                                        if (currentZoomBitmap != null) currentZoomBitmap.Dispose();
                                        currentZoomBitmap = bmp;
                                    }
                                    UpdateStreamFps();
                                    try { renderCanvas.BeginInvoke((Action)(() => renderCanvas.Invalidate())); } catch { }
                                }
                            }
                        } catch { }
                    }
                    Thread.Sleep(200);
                }
            }
        }, token);
    }

    private void UpdateStreamFps() {
        fpsCounter++;
        if (fpsSw.ElapsedMilliseconds >= 1000) {
            streamFps = (int)(fpsCounter * 1000 / fpsSw.ElapsedMilliseconds);
            fpsCounter = 0;
            fpsSw.Restart();
            try {
                this.BeginInvoke((Action)(() => {
                    lblFps.Text = streamFps + " FPS";
                    lblFps.ForeColor = streamFps >= 45 ? Color.FromArgb(16, 185, 129) : Color.FromArgb(245, 158, 11);
                }));
            } catch { }
        }
    }

    private void RenderCanvas_MouseDown(object sender, MouseEventArgs e) {
        if (!isZoomMode) {
            int totalPcs = Math.Max(1, pcList.Count);
            int cols = 3, rows = 2;
            if (viewFilter == "4split") { cols = 2; rows = 2; }
            else if (viewFilter == "1split") { cols = 1; rows = 1; }

            int gap = 8;
            int tileW = (renderCanvas.Width - (cols + 1) * gap) / cols;
            int tileH = (renderCanvas.Height - (rows + 1) * gap) / rows;
            int maxTiles = cols * rows;

            for (int i = 0; i < Math.Min(maxTiles, pcList.Count); i++) {
                int col = i % cols;
                int row = i / cols;
                int x = gap + col * (tileW + gap);
                int y = gap + row * (tileH + gap);

                Rectangle tileRect = new Rectangle(x, y, tileW, tileH);
                if (tileRect.Contains(e.Location)) {
                    var pc = pcList[i];
                    int btnBarH = 26;
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

        // 🌟 판서 / 그리기 모드
        if (isDrawingMode) {
            lock (drawLock) {
                currentStroke = new List<Point> { e.Location };
                drawingStrokes.Add(currentStroke);
            }
            if (!string.IsNullOrEmpty(currentZoomPcId)) {
                PointF relPt = GetRelCoords(e.Location);
                SendControlFast(currentZoomPcId, "draw_stroke", relPt.X, relPt.Y, currentMonitorIdx, "#ef4444", relPt.X.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) + "," + relPt.Y.ToString("F4", System.Globalization.CultureInfo.InvariantCulture));
            }
            renderCanvas.Invalidate();
            return;
        }

        // 🌟 화면 보기 모드에서는 마우스 클릭을 가로채지 않고 아무런 제어도 하지 않음 (상단 [화면보기] 버튼을 직접 눌러야만 제어 활성화)
        if (!isRemoteControlEnabled) {
            return;
        }

        if (string.IsNullOrEmpty(currentZoomPcId)) return;

        PointF rel = GetRelCoords(e.Location);
        if (e.Button == MouseButtons.Left) {
            isMouseDown = true;
            SendControlFast(currentZoomPcId, "mousedown", rel.X, rel.Y, currentMonitorIdx);
        } else if (e.Button == MouseButtons.Right) {
            SendControlFast(currentZoomPcId, "rightclick", rel.X, rel.Y, currentMonitorIdx);
        }
    }

    private void RenderCanvas_MouseMove(object sender, MouseEventArgs e) {
        if (!isZoomMode) return;

        if (isDrawingMode) {
            if (e.Button == MouseButtons.Left && currentStroke != null) {
                lock (drawLock) { currentStroke.Add(e.Location); }
                if (!string.IsNullOrEmpty(currentZoomPcId)) {
                    PointF relPt = GetRelCoords(e.Location);
                    SendControlFast(currentZoomPcId, "draw_update", relPt.X, relPt.Y, currentMonitorIdx, "#ef4444", relPt.X.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) + "," + relPt.Y.ToString("F4", System.Globalization.CultureInfo.InvariantCulture));
                }
                renderCanvas.Invalidate();
            }
            return;
        }

        if (!isRemoteControlEnabled || string.IsNullOrEmpty(currentZoomPcId)) return;

        long now = DateTime.UtcNow.Ticks;
        if (now - lastMouseMoveTicks > 120000) { // 12ms = 80Hz 초고속 무지연 트래킹
            lastMouseMoveTicks = now;
            PointF rel = GetRelCoords(e.Location);
            SendControlFast(currentZoomPcId, isMouseDown ? "mousemove" : "move", rel.X, rel.Y, currentMonitorIdx);
        }
    }

    private void RenderCanvas_MouseUp(object sender, MouseEventArgs e) {
        if (!isZoomMode) return;

        if (isDrawingMode) {
            currentStroke = null;
            return;
        }

        if (!isRemoteControlEnabled || string.IsNullOrEmpty(currentZoomPcId)) return;

        PointF rel = GetRelCoords(e.Location);
        if (e.Button == MouseButtons.Left && isMouseDown) {
            isMouseDown = false;
            SendControlFast(currentZoomPcId, "mouseup", rel.X, rel.Y, currentMonitorIdx);
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
            SendControlFast(currentZoomPcId, "doubleclick", rel.X, rel.Y, currentMonitorIdx);
        }
    }

    private void ShowChangeNicknameDialog(PcItem pc) {
        string currentNick = !string.IsNullOrEmpty(pc.Nickname) ? pc.Nickname : (!string.IsNullOrEmpty(pc.Name) ? pc.Name : pc.Id);
        string input = Microsoft.VisualBasic.Interaction.InputBox("PC의 새로운 별명을 입력하세요:", "별명 설정", currentNick);
        if (input != null) {
            input = input.Trim();
            pc.Nickname = input;
            persistentNicknames[pc.Id] = input;
            SavePersistentNicknames();

            // 1. 서버에 영구 동기화
            Task.Run(() => {
                try {
                    string url = serverUrl + "/api/control?pc=" + Uri.EscapeDataString(pc.Id) + "&type=set_nickname&nickname=" + Uri.EscapeDataString(input);
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                    req.Timeout = 3000;
                    using (var res = req.GetResponse()) { }
                } catch { }
            });

            // 2. 사내 LAN에도 동기화
            SendControlFast(pc.Id, "set_nickname", 0, 0, "0", null, input);
            renderCanvas.Invalidate();
        }
    }

    private void ShowPcContextMenu(PcItem pc, Point loc) {
        ContextMenuStrip menu = new ContextMenuStrip();
        menu.ShowImageMargin = false;
        menu.Font = new Font("Malgun Gothic", 9f, FontStyle.Regular);

        var itemZoom = menu.Items.Add("확대 (화면 보기 / 제어)");
        itemZoom.Click += (s, e) => EnterZoomMode(pc.Id);

        menu.Items.Add(new ToolStripSeparator());

        var itemNick = menu.Items.Add("PC 별명 변경");
        itemNick.Click += (s, e) => ShowChangeNicknameDialog(pc);

        var itemMsg = menu.Items.Add("1:1 공지 메시지 전송");
        itemMsg.Click += (s, e) => ShowNoticeDialog(pc.Id);

        var itemFile = menu.Items.Add("바탕화면 파일 전송");
        itemFile.Click += (s, e) => SendFileDialogForPc(pc.Id);

        menu.Items.Add(new ToolStripSeparator());

        var itemKill = menu.Items.Add("멈춘 프로그램 강제 정리");
        itemKill.Click += (s, e) => {
            SendControlFast(pc.Id, "kill_hung_tasks", 0, 0, "0");
            MessageBox.Show("응답 없는 멈춘 프로그램 강제 정리 완료!", "정리 완료");
        };

        var itemReboot = menu.Items.Add("원격 PC 재부팅");
        itemReboot.Click += (s, e) => {
            if (MessageBox.Show("이 원격 PC를 재부팅하시겠습니까?", "재부팅", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) {
                SendControlFast(pc.Id, "reboot", 0, 0, "0");
            }
        };

        menu.Show(renderCanvas, loc);
    }

    private void ShowNoticeDialog(string targetPc) {
        string targetName = (targetPc == "all") ? "전체 PC" : targetPc;
        string msg = Microsoft.VisualBasic.Interaction.InputBox("전송할 공지 메시지를 입력하세요 (" + targetName + "):", "공지 메시지 전송", "다연코퍼레이션 공지사항입니다.");
        if (!string.IsNullOrEmpty(msg)) {
            SendControlFast(targetPc, "popup", 0, 0, "0", null, msg);
        }
    }

    private void SendFileDialogForPc(string targetPc) {
        using (OpenFileDialog ofd = new OpenFileDialog()) {
            ofd.Title = "원격 PC로 보낼 파일 선택";
            if (ofd.ShowDialog() == DialogResult.OK) {
                var pc = pcList.Find(p => p.Id == targetPc);
                string pcDisplayName = (pc != null) ? (!string.IsNullOrEmpty(pc.Nickname) ? pc.Nickname : pc.Name) : targetPc;
                UploadAndSendFile(targetPc, ofd.FileName, pcDisplayName);
            }
        }
    }

    private void PerformManagerDeployOnly() {
        if (MessageBox.Show("관리자 프로그램(다연코퍼레이션 관리자.exe)을 최신 버전으로 업데이트하시겠습니까?\n\n(현재 관리자 창이 닫힌 후, 바탕화면 우측 하단에서 최신 파일을 교체하고 자동으로 다시 실행됩니다)", "관리자 프로그램 업데이트", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) {
            try {
                string inputCtrl = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "input_ctrl.exe");
                if (!File.Exists(inputCtrl)) inputCtrl = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "core", "input_ctrl.exe");
                if (!File.Exists(inputCtrl)) inputCtrl = @"C:\Users\user\.gemini\antigravity\scratch\simple_remote_control\input_ctrl.exe";

                string currentExe = Application.ExecutablePath;
                string sUrl = serverUrl;

                ProcessStartInfo psi = new ProcessStartInfo {
                    FileName = inputCtrl,
                    Arguments = string.Format("manager_updater \"{0}\" \"{1}\"", currentExe, sUrl),
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);
            } catch { }

            Environment.Exit(0);
        }
    }

    private static Dictionary<string, TcpClient> lanTcpClients = new Dictionary<string, TcpClient>();
    private static object lanTcpLock = new object();

    // TCP 8888 바이너리 패킷 전송 헬퍼 (zoomTcpLock 안에서 호출)
    private void WriteTcp8888Pkt(byte cmd, byte mon, ushort p1, ushort p2) {
        byte[] pkt = new byte[8];
        pkt[0] = cmd;
        pkt[1] = mon;
        BitConverter.GetBytes(p1).CopyTo(pkt, 4);
        BitConverter.GetBytes(p2).CopyTo(pkt, 6);
        activeZoomTcpStream.Write(pkt, 0, 8);
    }

    private void SendControlFast(string pcId, string type, float relX, float relY, string monIdx, string key = null, string msg = null, int delta = 0) {
        // ⚡ 0. 활성 TCP 8888 직통 전송 (DayeonClient.exe 네이티브 바이너리 프로토콜)
        if (isZoomMode && currentZoomPcId == pcId) {
            lock (zoomTcpLock) {
                if (activeZoomTcpStream != null && activeZoomTcpStream.CanWrite) {
                    try {
                        byte bMon = 0;
                        byte.TryParse(monIdx, out bMon);
                        ushort pX = (ushort)(Math.Max(0f, Math.Min(1f, relX)) * 65535);
                        ushort pY = (ushort)(Math.Max(0f, Math.Min(1f, relY)) * 65535);

                        // DayeonClient.cs ExecuteNativeInput 명령 코드와 1:1 정확 매핑
                        if (type == "move" || type == "mousemove") {
                            // 0x10 = MOUSE_MOVE
                            WriteTcp8888Pkt(0x10, bMon, pX, pY);
                            return;
                        } else if (type == "mousedown" || type == "click") {
                            // 0x11 = MOUSE_LEFT_DOWN
                            WriteTcp8888Pkt(0x11, bMon, pX, pY);
                            return;
                        } else if (type == "mouseup") {
                            // 0x12 = MOUSE_LEFT_UP
                            WriteTcp8888Pkt(0x12, bMon, pX, pY);
                            return;
                        } else if (type == "rightclick") {
                            // 우클릭 = DOWN(0x13) + UP(0x14) 연속 전송
                            WriteTcp8888Pkt(0x13, bMon, pX, pY);
                            WriteTcp8888Pkt(0x14, bMon, pX, pY);
                            return;
                        } else if (type == "doubleclick") {
                            // 더블클릭 = DOWN+UP 2회
                            WriteTcp8888Pkt(0x11, bMon, pX, pY);
                            WriteTcp8888Pkt(0x12, bMon, pX, pY);
                            WriteTcp8888Pkt(0x11, bMon, pX, pY);
                            WriteTcp8888Pkt(0x12, bMon, pX, pY);
                            return;
                        } else if (type == "wheel") {
                            // 0x15 = MOUSE_WHEEL (delta in pX)
                            byte[] pkt = new byte[8];
                            pkt[0] = 0x15;
                            pkt[1] = bMon;
                            BitConverter.GetBytes((short)delta).CopyTo(pkt, 4);
                            activeZoomTcpStream.Write(pkt, 0, 8);
                            return;
                        } else if (type == "keydown") {
                            ushort vk = 0;
                            Keys k;
                            if (Enum.TryParse<Keys>(key, out k)) vk = (ushort)k;
                            WriteTcp8888Pkt(0x20, bMon, vk, 0);
                            return;
                        } else if (type == "keyup") {
                            ushort vk = 0;
                            Keys k;
                            if (Enum.TryParse<Keys>(key, out k)) vk = (ushort)k;
                            WriteTcp8888Pkt(0x21, bMon, vk, 0);
                            return;
                        }
                    } catch { }
                }
            }
        }

        Task.Run(() => {
            try {
                var pc = pcList.Find(p => p.Id == pcId);
                string lanIp = GetLanIpForPc(pcId);

                // ⚡ 1. 사내 LAN 8002번 초저지연 Raw TCP 직통 소켓 (0.001ms 즉시 전송)
                if (type != "set_nickname" && lanIp != null && pcId != "all") {
                    try {
                        TcpClient tcp = null;
                        lock (lanTcpLock) {
                            if (lanTcpClients.ContainsKey(lanIp)) {
                                tcp = lanTcpClients[lanIp];
                                if (tcp != null && !tcp.Connected) {
                                    try { tcp.Close(); } catch { }
                                    tcp = null;
                                }
                            }
                            if (tcp == null) {
                                tcp = new TcpClient();
                                tcp.NoDelay = true;
                                tcp.Connect(lanIp, 8002);
                                lanTcpClients[lanIp] = tcp;
                            }
                        }

                        if (tcp != null && tcp.Connected) {
                            string safeKey = (key ?? "").Replace("\t", " ").Replace("\n", " ");
                            string safeMsg = (msg ?? "").Replace("\t", " ").Replace("\n", " ");
                            string line = type + "\t" + relX.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) + "\t" + relY.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) + "\t" + monIdx + "\t" + safeKey + "\t" + delta + "\t" + safeMsg + "\n";
                            byte[] lineBytes = Encoding.UTF8.GetBytes(line);
                            var stm = tcp.GetStream();
                            stm.Write(lineBytes, 0, lineBytes.Length);
                            stm.Flush();
                            return; // 0.001ms 전송 완료!
                        }
                    } catch {
                        lock (lanTcpLock) {
                            if (lanTcpClients.ContainsKey(lanIp)) lanTcpClients.Remove(lanIp);
                        }
                    }

                    // LAN HTTP 8001 폴백
                    try {
                        int port = (pc != null && pc.LanPort > 0) ? pc.LanPort : 8001;
                        string q = "type=" + type + "&relX=" + relX.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) + "&relY=" + relY.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) + "&monitor=" + monIdx;
                        if (!string.IsNullOrEmpty(key)) q += "&key=" + Uri.EscapeDataString(key);
                        if (!string.IsNullOrEmpty(msg)) q += "&msg=" + Uri.EscapeDataString(msg);
                        if (delta != 0) q += "&delta=" + delta;

                        HttpWebRequest lanReq = (HttpWebRequest)WebRequest.Create("http://" + lanIp + ":" + port + "/api/control?" + q);
                        lanReq.Timeout = 150;
                        lanReq.KeepAlive = true;
                        lanReq.Proxy = null;
                        using (var res = lanReq.GetResponse()) { }
                        return;
                    } catch { }
                }

                // ⚡ 2. 클라우드 중계 제어 (별명 설정 및 일반 제어 영구 동기화)
                string query = "type=" + type + "&relX=" + relX.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) + "&relY=" + relY.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) + "&monitor=" + monIdx;
                if (!string.IsNullOrEmpty(key)) query += "&key=" + Uri.EscapeDataString(key);
                if (!string.IsNullOrEmpty(msg)) query += "&msg=" + Uri.EscapeDataString(msg);
                if (type == "set_nickname" && !string.IsNullOrEmpty(msg)) query += "&nickname=" + Uri.EscapeDataString(msg);
                if (delta != 0) query += "&delta=" + delta;

                string cUrl = serverUrl + "/api/control?pc=" + Uri.EscapeDataString(pcId) + "&" + query;
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(cUrl);
                req.Timeout = 2000;
                req.KeepAlive = true;
                req.Proxy = null;
                using (var res = req.GetResponse()) { }
            } catch { }
        });
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) {
        if (keyData == Keys.Escape && isZoomMode) {
            ExitZoomMode();
            return true;
        }

        if (isZoomMode && isRemoteControlEnabled && !string.IsNullOrEmpty(currentZoomPcId)) {
            string keyName = keyData.ToString();
            SendControlFast(currentZoomPcId, "keydown", 0, 0, currentMonitorIdx, keyName);
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }
}

public class DoubleBufferedPanel : Panel {
    public DoubleBufferedPanel() {
        this.DoubleBuffered = true;
        this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        this.UpdateStyles();
    }
}
