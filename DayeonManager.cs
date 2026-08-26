using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DayeonRemoteManager {
    public class PcSession {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Ip { get; set; }
        public int Port { get; set; }
        public int MonitorCount { get; set; }
        public bool IsOnline { get; set; }
        public DateTime LastSeen { get; set; }
        public Bitmap LastThumbnail { get; set; }
        public int CurrentFps { get; set; }
        public TcpClient Client { get; set; }
        public NetworkStream Stream { get; set; }
        public CancellationTokenSource Cts { get; set; }
        public readonly object LockObj = new object();
    }

    public class RemoteManagerForm : Form {
        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod", SetLastError = true)]
        static extern uint timeBeginPeriod(uint uMilliseconds);

        private readonly List<PcSession> pcList = new List<PcSession>();
        private readonly object listLock = new object();

        private bool isZoomMode = false;
        private string zoomPcId = null;
        private byte zoomMonitor = 0;
        private Bitmap zoomBitmap = null;
        private readonly object zoomLock = new object();

        private Panel topBar;
        private Label lblTitle;
        private Label lblFps;
        private Label lblStatus;
        private Button btnMon1, btnMon2, btnMon3;
        private Button btnExitZoom;
        private Button btnRefresh;
        private Button btnFullscreen;

        private DoubleBufferedPanel renderCanvas;
        private System.Windows.Forms.Timer uiTimer;

        private bool isMouseDown = false;
        private long lastMouseMoveTicks = 0;
        private int fpsCounter = 0;
        private Stopwatch fpsSw = new Stopwatch();
        private int currentStreamFps = 0;

        private readonly string[] knownIps = new string[] {
            "172.30.1.7", "172.30.1.10", "172.30.1.91", "172.30.1.87", "172.30.1.36", "127.0.0.1"
        };

        public RemoteManagerForm() {
            try { timeBeginPeriod(1); } catch { }
            InitializeComponent();
            InitializeKnownPcs();
            StartUdpDiscoveryListener();
            StartGridStreamWorkers();
        }

        private void InitializeComponent() {
            this.Text = "다연코퍼레이션";
            this.Size = new Size(1380, 880);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(15, 23, 42);
            this.ForeColor = Color.White;
            this.KeyPreview = true;

            topBar = new Panel {
                Dock = DockStyle.Top,
                Height = 52,
                BackColor = Color.FromArgb(30, 41, 59),
                Padding = new Padding(12, 8, 12, 8)
            };

            lblTitle = new Label {
                Text = "🏢 다연코퍼레이션",
                Font = new Font("Malgun Gothic", 12, FontStyle.Bold),
                ForeColor = Color.FromArgb(56, 189, 248),
                AutoSize = true,
                Location = new Point(12, 14)
            };
            topBar.Controls.Add(lblTitle);

            lblStatus = new Label {
                Text = "🟢 60~90 FPS 기가비트 직통 연결됨",
                Font = new Font("Malgun Gothic", 10, FontStyle.Bold),
                ForeColor = Color.FromArgb(16, 185, 129),
                AutoSize = true,
                Location = new Point(340, 16)
            };
            topBar.Controls.Add(lblStatus);

            lblFps = new Label {
                Text = "60 FPS",
                Font = new Font("Consolas", 11, FontStyle.Bold),
                ForeColor = Color.FromArgb(52, 211, 153),
                AutoSize = true,
                Location = new Point(620, 16)
            };
            topBar.Controls.Add(lblFps);

            btnMon1 = CreateToolbarButton("모니터 1", 720, (s, e) => SwitchMonitor(0));
            btnMon2 = CreateToolbarButton("모니터 2", 810, (s, e) => SwitchMonitor(1));
            btnMon3 = CreateToolbarButton("모니터 3", 900, (s, e) => SwitchMonitor(2));
            btnMon1.Visible = false;
            btnMon2.Visible = false;
            btnMon3.Visible = false;
            topBar.Controls.Add(btnMon1);
            topBar.Controls.Add(btnMon2);
            topBar.Controls.Add(btnMon3);

            btnExitZoom = CreateToolbarButton("◀ 관제 그리드로 (Esc)", 990, (s, e) => ExitZoomMode());
            btnExitZoom.BackColor = Color.FromArgb(239, 68, 68);
            btnExitZoom.Width = 150;
            btnExitZoom.Visible = false;
            topBar.Controls.Add(btnExitZoom);

            btnRefresh = CreateToolbarButton("🔄 PC 검색", 1160, (s, e) => BroadcastUdpDiscovery());
            btnRefresh.Width = 90;
            topBar.Controls.Add(btnRefresh);

            btnFullscreen = CreateToolbarButton("⛶ 전체화면 (F11)", 1260, (s, e) => ToggleFullscreen());
            btnFullscreen.Width = 110;
            topBar.Controls.Add(btnFullscreen);

            this.Controls.Add(topBar);

            renderCanvas = new DoubleBufferedPanel {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(15, 23, 42)
            };
            renderCanvas.Paint += RenderCanvas_Paint;
            renderCanvas.MouseDown += RenderCanvas_MouseDown;
            renderCanvas.MouseMove += RenderCanvas_MouseMove;
            renderCanvas.MouseUp += RenderCanvas_MouseUp;
            renderCanvas.MouseWheel += RenderCanvas_MouseWheel;
            this.Controls.Add(renderCanvas);

            uiTimer = new System.Windows.Forms.Timer { Interval = 16 };
            uiTimer.Tick += (s, e) => {
                if (!isZoomMode) renderCanvas.Invalidate();
            };
            uiTimer.Start();

            fpsSw.Start();
        }

        private Button CreateToolbarButton(string text, int x, EventHandler onClick) {
            var btn = new Button {
                Text = text,
                Location = new Point(x, 10),
                Size = new Size(82, 32),
                BackColor = Color.FromArgb(51, 65, 85),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Malgun Gothic", 9, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.Click += onClick;
            return btn;
        }

        private void InitializeKnownPcs() {
            lock (listLock) {
                int idx = 1;
                foreach (var ip in knownIps) {
                    var pc = new PcSession {
                        Id = "PC-" + idx,
                        Name = "원격 PC " + idx + " (" + ip + ")",
                        Ip = ip,
                        Port = 8888,
                        MonitorCount = 1,
                        IsOnline = false,
                        LastSeen = DateTime.MinValue
                    };
                    pcList.Add(pc);
                    idx++;
                }
            }
        }

        private void StartUdpDiscoveryListener() {
            Task.Run(() => {
                UdpClient udp = null;
                try {
                    udp = new UdpClient(0);
                    udp.EnableBroadcast = true;
                    IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);

                    Task.Run(async () => {
                        while (true) {
                            try {
                                byte[] d = Encoding.UTF8.GetBytes("DAYEON_DISCOVER");
                                udp.Send(d, d.Length, new IPEndPoint(IPAddress.Broadcast, 8889));
                            } catch { }
                            await Task.Delay(4000);
                        }
                    });

                    while (true) {
                        byte[] res = udp.Receive(ref ep);
                        string msg = Encoding.UTF8.GetString(res);
                        if (msg.StartsWith("DAYEON_OFFER|")) {
                            string[] parts = msg.Split('|');
                            if (parts.Length >= 4) {
                                string rName = parts[1];
                                int rPort = int.Parse(parts[2]);
                                int rMon = int.Parse(parts[3]);
                                string rIp = ep.Address.ToString();

                                lock (listLock) {
                                    var existing = pcList.Find(p => p.Ip == rIp);
                                    if (existing == null) {
                                        existing = new PcSession {
                                            Id = "PC-" + (pcList.Count + 1),
                                            Name = rName + " (" + rIp + ")",
                                            Ip = rIp,
                                            Port = rPort,
                                            MonitorCount = rMon
                                        };
                                        pcList.Add(existing);
                                    } else {
                                        existing.Name = rName + " (" + rIp + ")";
                                        existing.Port = rPort;
                                        existing.MonitorCount = rMon;
                                    }
                                    existing.IsOnline = true;
                                    existing.LastSeen = DateTime.UtcNow;
                                }
                            }
                        }
                    }
                } catch {
                } finally {
                    if (udp != null) try { udp.Close(); } catch { }
                }
            });
        }

        private void BroadcastUdpDiscovery() {
            Task.Run(() => {
                try {
                    using (UdpClient u = new UdpClient()) {
                        u.EnableBroadcast = true;
                        byte[] d = Encoding.UTF8.GetBytes("DAYEON_DISCOVER");
                        u.Send(d, d.Length, new IPEndPoint(IPAddress.Broadcast, 8889));
                    }
                } catch { }
            });
        }

        private void StartGridStreamWorkers() {
            Task.Run(async () => {
                while (true) {
                    List<PcSession> pcs;
                    lock (listLock) { pcs = new List<PcSession>(pcList); }

                    foreach (var pc in pcs) {
                        if (!isZoomMode && (pc.Client == null || !pc.Client.Connected)) {
                            StartPcStream(pc, false, 0);
                        }
                    }
                    await Task.Delay(1500);
                }
            });
        }

        private void StartPcStream(PcSession pc, bool zoom, byte monIdx) {
            lock (pc.LockObj) {
                if (pc.Cts != null) { try { pc.Cts.Cancel(); } catch { } }
                pc.Cts = new CancellationTokenSource();
                var token = pc.Cts.Token;

                Task.Run(() => {
                    TcpClient client = null;
                    NetworkStream ns = null;

                    try {
                        client = new TcpClient();
                        client.NoDelay = true;
                        client.ReceiveBufferSize = 1024 * 1024 * 8;
                        client.SendBufferSize = 1024 * 128;

                        var ar = client.BeginConnect(pc.Ip, pc.Port, null, null);
                        if (!ar.AsyncWaitHandle.WaitOne(600)) {
                            client.Close();
                            return;
                        }
                        client.EndConnect(ar);
                        ns = client.GetStream();

                        pc.Client = client;
                        pc.Stream = ns;
                        pc.IsOnline = true;

                        byte[] setModeCmd = new byte[8];
                        setModeCmd[0] = 0x01;
                        setModeCmd[1] = monIdx;
                        BitConverter.GetBytes((short)(zoom ? 1 : 0)).CopyTo(setModeCmd, 4);
                        ns.Write(setModeCmd, 0, 8);

                        byte[] header = new byte[12];
                        byte[] frameBuf = new byte[1024 * 1024 * 4];

                        while (!token.IsCancellationRequested && client.Connected) {
                            int r = ReadExact(ns, header, 0, 12);
                            if (r < 12) break;

                            if (header[0] != 'D' || header[1] != 'Y' || header[2] != '0' || header[3] != '1') break;

                            byte rMon = header[4];
                            int frameLen = BitConverter.ToInt32(header, 8);
                            if (frameLen <= 0 || frameLen > frameBuf.Length) break;

                            int rPayload = ReadExact(ns, frameBuf, 0, frameLen);
                            if (rPayload < frameLen) break;

                            using (var ms = new MemoryStream(frameBuf, 0, frameLen)) {
                                Bitmap bmp = (Bitmap)Image.FromStream(ms);
                                if (zoom && isZoomMode && zoomPcId == pc.Id) {
                                    lock (zoomLock) {
                                        if (zoomBitmap != null) zoomBitmap.Dispose();
                                        zoomBitmap = bmp;
                                    }
                                    UpdateFps();
                                    try { renderCanvas.BeginInvoke((Action)(() => renderCanvas.Invalidate())); } catch { }
                                } else {
                                    lock (pc.LockObj) {
                                        if (pc.LastThumbnail != null) pc.LastThumbnail.Dispose();
                                        pc.LastThumbnail = bmp;
                                    }
                                }
                            }
                        }
                    } catch {
                    } finally {
                        pc.IsOnline = false;
                        if (ns != null) try { ns.Close(); } catch { }
                        if (client != null) try { client.Close(); } catch { }
                    }
                }, token);
            }
        }

        private static int ReadExact(NetworkStream ns, byte[] buf, int offset, int count) {
            int total = 0;
            while (total < count) {
                int r = ns.Read(buf, offset + total, count - total);
                if (r <= 0) return total;
                total += r;
            }
            return total;
        }

        private void UpdateFps() {
            fpsCounter++;
            if (fpsSw.ElapsedMilliseconds >= 1000) {
                currentStreamFps = (int)(fpsCounter * 1000 / fpsSw.ElapsedMilliseconds);
                fpsCounter = 0;
                fpsSw.Restart();
                try {
                    this.BeginInvoke((Action)(() => {
                        lblFps.Text = currentStreamFps + " FPS";
                        lblFps.ForeColor = currentStreamFps >= 50 ? Color.FromArgb(52, 211, 153) : Color.FromArgb(245, 158, 11);
                    }));
                } catch { }
            }
        }

        private void EnterZoomMode(string pcId) {
            isZoomMode = true;
            zoomPcId = pcId;
            zoomMonitor = 0;

            PcSession targetPc;
            lock (listLock) { targetPc = pcList.Find(p => p.Id == pcId); }
            if (targetPc == null) return;

            lblTitle.Text = "⚡ [원격 제어] " + targetPc.Name;
            lblStatus.Text = "🟢 실시간 60 FPS 마우스 동기화 제어 중";
            btnMon1.Visible = true;
            btnMon2.Visible = true;
            btnMon3.Visible = true;
            btnExitZoom.Visible = true;
            UpdateMonitorButtons();

            StartPcStream(targetPc, true, zoomMonitor);
        }

        private void ExitZoomMode() {
            isZoomMode = false;
            zoomPcId = null;

            lock (zoomLock) {
                if (zoomBitmap != null) {
                    zoomBitmap.Dispose();
                    zoomBitmap = null;
                }
            }

            lblTitle.Text = "⚡ 다연코퍼레이션 2.0 (실시간 마우스 동기화)";
            lblStatus.Text = "🟢 60~90 FPS 기가비트 직통 연결됨";
            btnMon1.Visible = false;
            btnMon2.Visible = false;
            btnMon3.Visible = false;
            btnExitZoom.Visible = false;

            renderCanvas.Invalidate();
        }

        private void SwitchMonitor(byte monIdx) {
            zoomMonitor = monIdx;
            UpdateMonitorButtons();

            PcSession targetPc;
            lock (listLock) { targetPc = pcList.Find(p => p.Id == zoomPcId); }
            if (targetPc != null && targetPc.Stream != null && targetPc.Client != null && targetPc.Client.Connected) {
                Task.Run(() => {
                    try {
                        byte[] cmd = new byte[8];
                        cmd[0] = 0x02;
                        cmd[1] = monIdx;
                        targetPc.Stream.Write(cmd, 0, 8);
                    } catch { }
                });
            }
        }

        private void UpdateMonitorButtons() {
            btnMon1.BackColor = (zoomMonitor == 0) ? Color.FromArgb(3, 105, 161) : Color.FromArgb(51, 65, 85);
            btnMon2.BackColor = (zoomMonitor == 1) ? Color.FromArgb(3, 105, 161) : Color.FromArgb(51, 65, 85);
            btnMon3.BackColor = (zoomMonitor == 2) ? Color.FromArgb(3, 105, 161) : Color.FromArgb(51, 65, 85);
        }

        private void ToggleFullscreen() {
            if (this.FormBorderStyle == FormBorderStyle.None) {
                this.FormBorderStyle = FormBorderStyle.Sizable;
                this.WindowState = FormWindowState.Normal;
            } else {
                this.FormBorderStyle = FormBorderStyle.None;
                this.WindowState = FormWindowState.Maximized;
            }
        }

        private void RenderCanvas_Paint(object sender, PaintEventArgs e) {
            Graphics g = e.Graphics;
            g.InterpolationMode = InterpolationMode.Bilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            if (isZoomMode) {
                lock (zoomLock) {
                    if (zoomBitmap != null) {
                        Rectangle destRect = GetLetterboxRect(zoomBitmap.Width, zoomBitmap.Height, renderCanvas.ClientRectangle);
                        g.DrawImage(zoomBitmap, destRect);
                    } else {
                        using (var brush = new SolidBrush(Color.FromArgb(148, 163, 184))) {
                            g.DrawString("원격 PC 60 FPS 화면 로딩 중...", new Font("Malgun Gothic", 14, FontStyle.Bold), brush, new PointF(renderCanvas.Width / 2 - 120, renderCanvas.Height / 2));
                        }
                    }
                }
            } else {
                List<PcSession> list;
                lock (listLock) { list = new List<PcSession>(pcList); }

                int cols = 3, rows = 2;
                int margin = 12;
                int tileW = (renderCanvas.Width - margin * (cols + 1)) / cols;
                int tileH = (renderCanvas.Height - margin * (rows + 1)) / rows;

                for (int i = 0; i < Math.Min(6, list.Count); i++) {
                    int col = i % cols;
                    int row = i / cols;
                    int x = margin + col * (tileW + margin);
                    int y = margin + row * (tileH + margin);
                    var pc = list[i];

                    Rectangle tileRect = new Rectangle(x, y, tileW, tileH);

                    using (var cardBrush = new SolidBrush(Color.FromArgb(24, 33, 50))) {
                        g.FillRectangle(cardBrush, tileRect);
                    }
                    Color borderColor = pc.IsOnline ? Color.FromArgb(16, 185, 129) : Color.FromArgb(71, 85, 105);
                    using (var pen = new Pen(borderColor, 2f)) {
                        g.DrawRectangle(pen, tileRect);
                    }

                    lock (pc.LockObj) {
                        if (pc.LastThumbnail != null) {
                            Rectangle imgRect = new Rectangle(x + 4, y + 4, tileW - 8, tileH - 36);
                            g.DrawImage(pc.LastThumbnail, imgRect);
                        } else {
                            using (var txtBrush = new SolidBrush(Color.FromArgb(100, 116, 139))) {
                                g.DrawString(pc.IsOnline ? "화면 수신 중..." : "오프라인 (대기 중)", new Font("Malgun Gothic", 10, FontStyle.Bold), txtBrush, x + 20, y + tileH / 2 - 20);
                            }
                        }
                    }

                    Rectangle barRect = new Rectangle(x, y + tileH - 30, tileW, 30);
                    using (var barBrush = new SolidBrush(Color.FromArgb(15, 23, 42))) {
                        g.FillRectangle(barBrush, barRect);
                    }

                    using (var nameBrush = new SolidBrush(Color.White)) {
                        g.DrawString(pc.Name, new Font("Malgun Gothic", 9, FontStyle.Bold), nameBrush, x + 8, y + tileH - 24);
                    }

                    Rectangle zoomBtnRect = new Rectangle(x + tileW - 60, y + tileH - 26, 54, 22);
                    using (var btnBrush = new SolidBrush(Color.FromArgb(3, 105, 161))) {
                        g.FillRectangle(btnBrush, zoomBtnRect);
                    }
                    using (var btnTxtBrush = new SolidBrush(Color.White)) {
                        g.DrawString("확대 제어", new Font("Malgun Gothic", 8, FontStyle.Bold), btnTxtBrush, x + tileW - 55, y + tileH - 22);
                    }
                }
            }
        }

        private Rectangle GetLetterboxRect(int srcW, int srcH, Rectangle clientRect) {
            double scale = Math.Min((double)clientRect.Width / srcW, (double)clientRect.Height / srcH);
            int dw = (int)(srcW * scale);
            int dh = (int)(srcH * scale);
            int dx = clientRect.X + (clientRect.Width - dw) / 2;
            int dy = clientRect.Y + (clientRect.Height - dh) / 2;
            return new Rectangle(dx, dy, dw, dh);
        }

        private PointF GetNormalizedCoords(Point mousePt) {
            lock (zoomLock) {
                if (zoomBitmap == null) return PointF.Empty;
                Rectangle dest = GetLetterboxRect(zoomBitmap.Width, zoomBitmap.Height, renderCanvas.ClientRectangle);
                if (dest.Width <= 0 || dest.Height <= 0) return PointF.Empty;

                float nx = (float)(mousePt.X - dest.X) / dest.Width;
                float ny = (float)(mousePt.Y - dest.Y) / dest.Height;
                nx = Math.Max(0f, Math.Min(1f, nx));
                ny = Math.Max(0f, Math.Min(1f, ny));
                return new PointF(nx, ny);
            }
        }

        private void SendNativeInput(byte cmdType, PointF normPt, int p1 = 0, int p2 = 0) {
            if (!isZoomMode || string.IsNullOrEmpty(zoomPcId)) return;

            PcSession pc;
            lock (listLock) { pc = pcList.Find(p => p.Id == zoomPcId); }
            if (pc == null || pc.Stream == null || pc.Client == null || !pc.Client.Connected) return;

            int normX = (int)Math.Round(normPt.X * 65535.0);
            int normY = (int)Math.Round(normPt.Y * 65535.0);

            byte[] packet = new byte[8];
            packet[0] = cmdType;
            packet[1] = zoomMonitor;
            packet[2] = 0; packet[3] = 0;
            BitConverter.GetBytes((short)normX).CopyTo(packet, 4);
            BitConverter.GetBytes((short)normY).CopyTo(packet, 6);

            Task.Run(() => {
                try {
                    pc.Stream.Write(packet, 0, 8);
                } catch { }
            });
        }

        private void RenderCanvas_MouseDown(object sender, MouseEventArgs e) {
            if (!isZoomMode) {
                List<PcSession> list;
                lock (listLock) { list = new List<PcSession>(pcList); }

                int cols = 3, rows = 2, margin = 12;
                int tileW = (renderCanvas.Width - margin * (cols + 1)) / cols;
                int tileH = (renderCanvas.Height - margin * (rows + 1)) / rows;

                for (int i = 0; i < Math.Min(6, list.Count); i++) {
                    int col = i % cols;
                    int row = i / cols;
                    int x = margin + col * (tileW + margin);
                    int y = margin + row * (tileH + margin);
                    Rectangle tileRect = new Rectangle(x, y, tileW, tileH);
                    if (tileRect.Contains(e.Location)) {
                        EnterZoomMode(list[i].Id);
                        return;
                    }
                }
                return;
            }

            PointF norm = GetNormalizedCoords(e.Location);
            if (e.Button == MouseButtons.Left) {
                isMouseDown = true;
                // 🌟 클릭하는 순간에만 원격 PC의 마우스가 즉시 이동하여 정확하게 클릭!
                SendNativeInput(0x11, norm); // MOUSE_LEFT_DOWN
            } else if (e.Button == MouseButtons.Right) {
                SendNativeInput(0x13, norm); // MOUSE_RIGHT_DOWN
            }
        }

        private void RenderCanvas_MouseMove(object sender, MouseEventArgs e) {
            if (!isZoomMode) return;

            // 🌟 단순 마우스 호버(이동) 시에는 원격 PC의 마우스를 강제로 끌어당기지 않음!
            // 🌟 오직 마우스 버튼을 누른 채 '드래그'하거나 '창을 흔들 때'만 8ms(125Hz) 초고속 추종!
            if (isMouseDown) {
                long now = DateTime.UtcNow.Ticks;
                if (now - lastMouseMoveTicks > 80000) { // 8ms = 125Hz 드래그
                    lastMouseMoveTicks = now;
                    PointF norm = GetNormalizedCoords(e.Location);
                    SendNativeInput(0x10, norm); // MOUSE_MOVE
                }
            }
        }

        private void RenderCanvas_MouseUp(object sender, MouseEventArgs e) {
            if (!isZoomMode) return;

            PointF norm = GetNormalizedCoords(e.Location);
            if (e.Button == MouseButtons.Left && isMouseDown) {
                isMouseDown = false;
                SendNativeInput(0x12, norm); // MOUSE_LEFT_UP
            } else if (e.Button == MouseButtons.Right) {
                SendNativeInput(0x14, norm); // MOUSE_RIGHT_UP
            }
        }

        private void RenderCanvas_MouseWheel(object sender, MouseEventArgs e) {
            if (!isZoomMode) return;
            PointF norm = GetNormalizedCoords(e.Location);
            SendNativeInput(0x15, norm, e.Delta, 0); // MOUSE_WHEEL
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData) {
            if (keyData == Keys.Escape && isZoomMode) {
                ExitZoomMode();
                return true;
            }
            if (keyData == Keys.F11) {
                ToggleFullscreen();
                return true;
            }
            if (keyData >= Keys.F1 && keyData <= Keys.F6 && !isZoomMode) {
                int idx = keyData - Keys.F1;
                lock (listLock) {
                    if (idx < pcList.Count) EnterZoomMode(pcList[idx].Id);
                }
                return true;
            }
            if (keyData == (Keys.Alt | Keys.D1) && isZoomMode) { SwitchMonitor(0); return true; }
            if (keyData == (Keys.Alt | Keys.D2) && isZoomMode) { SwitchMonitor(1); return true; }
            if (keyData == (Keys.Alt | Keys.D3) && isZoomMode) { SwitchMonitor(2); return true; }

            if (isZoomMode) {
                byte vk = (byte)(keyData & Keys.KeyCode);
                SendNativeInput(0x20, PointF.Empty, vk, 0); // KEY_DOWN
                Task.Run(async () => {
                    await Task.Delay(10);
                    SendNativeInput(0x21, PointF.Empty, vk, 0); // KEY_UP
                });
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        [STAThread]
        public static void Main() {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new RemoteManagerForm());
        }
    }

    public class DoubleBufferedPanel : Panel {
        public DoubleBufferedPanel() {
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            this.UpdateStyles();
        }
    }
}
