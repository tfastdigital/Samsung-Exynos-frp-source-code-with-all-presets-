using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ZeroKnoxRemoval
{
    public partial class MainForm : Form
    {
        private ComboBox comboPorts = null!;  
        private ComboBox cmbConfig = null!;  
        private Button _btnRefresh = null!;
        private Button _btnFlash = null!;
        private Button buttonStop = null!;  
        private ProgressBar ProgressBar = null!; 
        private Label _lblStatus = null!;
        private RichTextBox txtLog = null!;  
        private Label _lblDetected = null!;

        // ════════════════════════════════════════════════════════════
        // ENGINE + STATE
        // ════════════════════════════════════════════════════════════
        private readonly ExynosFlashEngine _engine;
        private ExynosDetectResult? _lastDetected;

        // busyState, Watch
        public static bool busyState = false;
        public Stopwatch Watch = new Stopwatch();
        public CancellationTokenSource stop = new CancellationTokenSource();
        private readonly object _stopLock = new object();

        public static string namesoftware = "ZeroKnox Removal";
        public static string version = "1.0";

        // ════════════════════════════════════════════════════════════
        // REMOTE CONFIG
        // ════════════════════════════════════════════════════════════
        private const string ConfigServerUrl = "https://your.site/presets/";
        private static readonly HttpClient _httpClient = new HttpClient
        { Timeout = TimeSpan.FromSeconds(15) };

        private static readonly string[] ConfigNames =
        {
            "exynos850_dpolicy_extract",
            "exynos7884_dpolicy_extract",
            "exynos7885_dpolicy_extract",
            "exynos9610_dpolicy_integrity",
            "exynos9611_dpolicy_integrity",
            "exynos9820_dpolicy_integrity",
            "exynos9825_dpolicy_integrity",
            "exynos990_dpolicy_integrity",
            "exynos1280_dpolicy_extract",
            "exynos1380_dpolicy_extract",
            "exynos1480_dpolicy_extract",
            "exynos1580_dpolicy_extract",
            "exynos2100_dpolicy_extract",
            "exynos2200_dpolicy_extract",
            "exynos2400_dpolicy_extract",
        };
        private bool _configLoaded = false;

        // ════════════════════════════════════════════════════════════
        // CONSTRUCTOR
        // ════════════════════════════════════════════════════════════
        public MainForm()
        {
            _engine = new ExynosFlashEngine();
            _engine.OnLogReceived += OnEngineLog;
            _engine.OnProgressChanged += OnProgress;

            InitializeComponent();
            BuildLayout();
            RefreshAll();
        }

        // ════════════════════════════════════════════════════════════
        // LAYOUT
        // ════════════════════════════════════════════════════════════
        private void BuildLayout()
        {
            Text = "ZeroKnox Removal";
            BackColor = Color.FromArgb(28, 30, 38);
            ForeColor = Color.White;
            ClientSize = new Size(760, 540);
            Font = new Font("Segoe UI", 9.5f);
            StartPosition = FormStartPosition.CenterScreen;

            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
            Controls.Add(panel);

            // Row 1: COM + Config
            var lblPort = MakeLabel("COM Port:", 0, 14);
            comboPorts = new ComboBox
            {
                Left = 90,
                Top = 10,
                Width = 190,
                Height = 26,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(40, 42, 54),
                ForeColor = Color.White,
            };

            var lblConfig = MakeLabel("Config:", 295, 14);
            cmbConfig = new ComboBox
            {
                Left = 360,
                Top = 10,
                Width = 270,
                Height = 26,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(40, 42, 54),
                ForeColor = Color.White,
            };
            // При первом открытии — загружаем с сервера
            cmbConfig.DropDown += CmbConfig_DropDown;

            _btnRefresh = MakeButton("🔄", 640, 9, 60, 28, Color.FromArgb(55, 58, 72));
            _btnRefresh.Click += (_, _) => RefreshAll();

            _btnFlash = MakeButton("▶ Reset FRP", 230, 50, 160, 40, Color.FromArgb(40, 120, 40));
            _btnFlash.Click += BtnFlash_Click;

            // buttonStop 
            buttonStop = MakeButton("⛔ Stop", 400, 50, 90, 40, Color.FromArgb(160, 40, 40));
            buttonStop.Enabled = false;
            buttonStop.Click += buttonStop_Click;

            _lblDetected = new Label
            {
                Left = 500,
                Top = 58,
                Width = 250,
                Height = 24,
                ForeColor = Color.FromArgb(180, 180, 180),
                Text = ""
            };

            // ProgressBar 
            ProgressBar = new ProgressBar
            {
                Left = 0,
                Top = 100,
                Width = 716,
                Height = 10,
                Style = ProgressBarStyle.Blocks,
            };

            _lblStatus = new Label
            {
                Left = 0,
                Top = 115,
                Width = 716,
                Height = 20,
                ForeColor = Color.FromArgb(160, 160, 160),
                Text = "Ready"
            };

            // txtLog
            txtLog = new RichTextBox
            {
                Left = 0,
                Top = 140,
                Width = 716,
                Height = 360,
                BackColor = Color.FromArgb(15, 16, 20),
                ForeColor = Color.White,
                Font = new Font("Consolas", 9.5f),
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                WordWrap = true,
                DetectUrls = false,
            };

            panel.Controls.AddRange(new Control[]
            {
                lblPort, comboPorts, lblConfig, cmbConfig, _btnRefresh,
                _btnFlash, buttonStop, _lblDetected,
                ProgressBar, _lblStatus, txtLog
            });
        }

        private Label MakeLabel(string text, int x, int y) => new Label
        {
            Text = text,
            Left = x,
            Top = y,
            AutoSize = true,
            ForeColor = Color.FromArgb(180, 180, 180)
        };

        private Button MakeButton(string text, int x, int y, int w, int h, Color back) => new Button
        {
            Text = text,
            Left = x,
            Top = y,
            Width = w,
            Height = h,
            BackColor = back,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            Cursor = Cursors.Hand,
        };

        // ════════════════════════════════════════════════════════════
        // SendLog - show log messages in txtLog with optional color and line break
        // ════════════════════════════════════════════════════════════
        public void SendLog(string text, Color? colors = default, bool breakline = false)
        {
            if (txtLog.IsDisposed) return;

            Action logAction = () =>
            {
                if (string.IsNullOrEmpty(text)) return;

                txtLog.SelectionStart = txtLog.TextLength;
                txtLog.SelectionLength = 0;
                txtLog.SelectionColor = colors ?? txtLog.ForeColor;

                if (breakline)
                {
                    string prefix = (txtLog.TextLength > 0) ? Environment.NewLine : "";
                    txtLog.AppendText(prefix + text);
                }
                else
                {
                    txtLog.AppendText(text);
                }

                txtLog.ScrollToCaret();
            };

            if (txtLog.InvokeRequired) txtLog.BeginInvoke(logAction);
            else logAction();
        }

        // ════════════════════════════════════════════════════════════
        // progressBarRunning 
        // ════════════════════════════════════════════════════════════
        public void progressBarRunning(bool isRunning, bool enableStop = false)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => progressBarRunning(isRunning, enableStop)));
                return;
            }

            busyState = isRunning;

            if (enableStop)
            {
                buttonStop.Text = "Stop";
                buttonStop.Enabled = isRunning;
            }

            if (isRunning)
            {
                ProgressBar.Visible = true;
                ProgressBar.Style = ProgressBarStyle.Marquee;
                ProgressBar.MarqueeAnimationSpeed = 30;
            }
            else
            {
                ProgressBar.Style = ProgressBarStyle.Blocks;
                ProgressBar.Value = 0;
            }
        }

        // ════════════════════════════════════════════════════════════
        // Elapseddone2
        // ════════════════════════════════════════════════════════════
        public void Elapseddone2(Stopwatch watch)
        {
            TimeSpan elapsed = watch.Elapsed;
            string str1 = string.Format("{0:00}", elapsed.Minutes);
            string str2 = string.Format("{0:00}", elapsed.Seconds);

            SendLog("____________________________________________________________________________________________________", Color.FromArgb(60, 60, 60), true);
            SendLog(Environment.NewLine, Color.Transparent, false);
            SendLog(namesoftware + " ", Color.FromArgb(180, 180, 180), false);
            SendLog(version, Color.FromArgb(180, 180, 180), false);

            if (str1 == "00")
                SendLog(" Elapsed Time : " + str2 + " Seconds", Color.FromArgb(180, 180, 180), false);
            else
                SendLog(" Elapsed Time : " + str1 + "." + str2 + " Minutes", Color.FromArgb(180, 180, 180), false);
        }

        // ════════════════════════════════════════════════════════════
        // ResetStop 
        // ════════════════════════════════════════════════════════════
        private CancellationTokenSource ResetStop()
        {
            lock (_stopLock)
            {
                var old = stop;
                stop = new CancellationTokenSource();
                try { old?.Cancel(); } catch { }
                try { old?.Dispose(); } catch { }
                return stop;
            }
        }

        // ════════════════════════════════════════════════════════════
        // SetBusy
        // ════════════════════════════════════════════════════════════
        private void SetBusy(bool busy)
        {
            progressBarRunning(busy, enableStop: busy);
            _btnFlash.Enabled = !busy;
            _btnRefresh.Enabled = !busy;
            comboPorts.Enabled = !busy;
            cmbConfig.Enabled = !busy;
        }

        // ════════════════════════════════════════════════════════════
        // buttonStop_Click 
        // ════════════════════════════════════════════════════════════
        private void buttonStop_Click(object? sender, EventArgs e)
        {
            try
            {
                if (stop != null && !stop.IsCancellationRequested)
                    stop.Cancel();
                _engine.TerminateCurrentProcess();
            }
            catch { }
            finally
            {
                busyState = false;
                buttonStop.Enabled = false;
                progressBarRunning(false);
                SendLog("Operation stopped by user.", Color.Orange, true);
            }
        }

        // ════════════════════════════════════════════════════════════
        // RefreshAll
        // ════════════════════════════════════════════════════════════
        private void RefreshAll()
        {
            string? prevPort = comboPorts.SelectedItem?.ToString();
            comboPorts.Items.Clear();
            foreach (string p in _engine.ScanComPorts()) comboPorts.Items.Add(p);
            if (comboPorts.Items.Count > 0)
                comboPorts.SelectedIndex = prevPort != null && comboPorts.Items.Contains(prevPort)
                    ? comboPorts.Items.IndexOf(prevPort) : 0;

            if (_configLoaded)
            {
                string? prev = cmbConfig.SelectedItem?.ToString();
                cmbConfig.Items.Clear();
                foreach (var p in _engine.Presets)
                    cmbConfig.Items.Add(p.DisplayText ?? p.PresetName ?? "");
                if (cmbConfig.Items.Count > 0)
                    cmbConfig.SelectedIndex = prev != null && cmbConfig.Items.Contains(prev)
                        ? cmbConfig.Items.IndexOf(prev) : 0;
            }
            else
            {
                cmbConfig.Items.Clear();
                cmbConfig.Items.Add("▼ Open to load config from server...");
                cmbConfig.SelectedIndex = 0;
            }
        }

        // ════════════════════════════════════════════════════════════
        // REMOTE CONFIG 
        // ════════════════════════════════════════════════════════════
        private void CmbConfig_DropDown(object? sender, EventArgs e)
        {
            if (!_configLoaded)
                _ = LoadConfigFromServerAsync();
        }

        private async Task LoadConfigFromServerAsync()
        {
            if (_configLoaded) return;

            SendLog("Connecting to config server...", Color.Cyan, true);
            cmbConfig.Enabled = false;

            string tempDir = Path.Combine(Path.GetTempPath(), ".tft_cfg_" + Environment.ProcessId);
            Directory.CreateDirectory(tempDir);

            int ok = 0, fail = 0;
            foreach (string name in ConfigNames)
            {
                try
                {
                    string json = await _httpClient.GetStringAsync(
                        ConfigServerUrl.TrimEnd('/') + "/" + name + ".json");
                    await File.WriteAllTextAsync(Path.Combine(tempDir, name + ".json"), json);
                    ok++;
                }
                catch { fail++; }
            }

            _engine.LoadPresetsFromDirectory(tempDir);
            try { Directory.Delete(tempDir, true); } catch { }

            _configLoaded = true;

            if (InvokeRequired) Invoke(UpdateConfigCombo);
            else UpdateConfigCombo();

            SendLog($"Config: {ok} loaded, {fail} failed.",
                fail == 0 ? Color.LimeGreen : Color.Orange, true);
        }

        private void UpdateConfigCombo()
        {
            string? prev = cmbConfig.SelectedItem?.ToString();
            cmbConfig.Items.Clear();
            foreach (var p in _engine.Presets)
                cmbConfig.Items.Add(p.DisplayText ?? p.PresetName ?? "");
            if (cmbConfig.Items.Count > 0)
                cmbConfig.SelectedIndex = prev != null && cmbConfig.Items.Contains(prev)
                    ? cmbConfig.Items.IndexOf(prev) : 0;
            cmbConfig.Enabled = true;
        }

        // ════════════════════════════════════════════════════════════
        // AUTO DETECT & FLASH
        // ════════════════════════════════════════════════════════════
        

        // ════════════════════════════════════════════════════════════
        // MANUAL FLASH
        // ════════════════════════════════════════════════════════════
        private async void BtnFlash_Click(object? sender, EventArgs e)
        {
            if (busyState) return;

            string? comPort = comboPorts.SelectedItem?.ToString();
            string? display = cmbConfig.SelectedItem?.ToString();

            if (string.IsNullOrEmpty(comPort) || string.IsNullOrEmpty(display)
                || display.StartsWith("▼"))
            {
                SendLog("Select target interface and config first!", Color.Orange, true);
                return;
            }

            var cfg = _engine.Presets.FirstOrDefault(p => p.DisplayText == display);
            if (cfg == null)
            {
                SendLog("Selected config is invalid. Open dropdown to reload.", Color.Red, true);
                return;
            }

            Watch.Reset();
            Watch.Start();
            txtLog.Text = null;
            ResetStop();

            SetBusy(true);

            SendLog("___________________________________________", Color.FromArgb(60, 60, 60), true);
            SendLog("INITIALIZING MANUAL FLASH DEPLOYMENT...", Color.Cyan, true);
            SendLog($"  Platform: {cfg.ChipsetName}", Color.FromArgb(200, 200, 200), true);
            SendLog($"  Target:   {comPort}", Color.FromArgb(200, 200, 200), true);
            SendLog("___________________________________________", Color.FromArgb(60, 60, 60), true);

            try
            {
                await Task.Delay(300, stop.Token);

                // Скачиваем JSON с сервера
                SendLog("Fetching config from server...", Color.Cyan, true);
                bool downloaded = await _engine.DownloadPresetAsync(cfg.PresetName!, ConfigServerUrl);
                if (!downloaded)
                {
                    SendLog("Failed to fetch config. Check internet connection.", Color.Red, true);
                    return;
                }
                _engine.LoadExynosPresets();

                // Flash
                bool flashOk = await _engine.FlashAsync(comPort, cfg.PresetName!, stop.Token);

                if (flashOk)
                {
                    SendLog("FRP successfully removed.", Color.LimeGreen, true);
                    SendLog("IF after reboot, usb reconnected in 2 sec every time, PLEASE DO FACTORY RESET.", Color.Orange, true);
                    _lblStatus.Text = "✓ Done";
                    _lblStatus.ForeColor = Color.LimeGreen;
                }
                else
                {
                    SendLog("MANUAL FLASH DEPLOYMENT FAILED.", Color.Red, true);
                    _lblStatus.Text = "✗ Failed";
                    _lblStatus.ForeColor = Color.OrangeRed;
                }
            }
            catch (OperationCanceledException)
            {
                SendLog("Operation cancelled by user.", Color.Orange, true);
            }
            catch (Exception ex)
            {
                SendLog($"Unexpected error: {ex.Message}", Color.Red, true);
            }
            finally
            {
                _engine.ClearPresets();
                Watch.Stop();
                Elapseddone2(Watch);
                SetBusy(false);
            }
        }

        // ════════════════════════════════════════════════════════════
        // ENGINE CALLBACKS
        // ════════════════════════════════════════════════════════════
        private void OnEngineLog(string text, Color color, bool breakline)
            => SendLog(text, color, breakline);

        private void OnProgress(double pct, string status)
        {
            if (InvokeRequired) { Invoke(() => OnProgress(pct, status)); return; }
            if (ProgressBar.Style != ProgressBarStyle.Marquee)
            {
                ProgressBar.Style = ProgressBarStyle.Continuous;
                ProgressBar.Value = Math.Max(0, Math.Min(100, (int)pct));
            }
            _lblStatus.Text = status;
        }
    }
}