using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using FocusFade.Core;
using FocusFade.Mixing;
using FocusFade.Models;
using FocusFade.UI.Controls;

namespace FocusFade.UI
{
    public class MainForm : Form
    {
        private readonly AudioEngine engine = new AudioEngine();
        private readonly AppSettings settings = AppSettings.Load();

        [DllImport("kernel32.dll")]
        private static extern bool SetProcessWorkingSetSize(IntPtr proc, int min, int max);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("uxtheme.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

        private Panel mainPanel;
        private ModernCard cardTrigger;
        private ModernCard cardTarget;
        private ModernCard cardSettings;
        private ModernCard cardOptions;

        private ComboBox cbTriggerInput;
        private ModernButton btnAddTrigger;
        private ModernButton btnRefresh;
        private FlowLayoutPanel flpChips;
        private Label lblEmptyChips;
        private ModernToggle togDuckMic;

        private ComboBox cbTarget;
        private ModernButton btnAddTarget;
        private FlowLayoutPanel flpTargetChips;
        private Label lblEmptyTargetChips;
        private FlowLayoutPanel flpQuickTargets;
        private Label lblTargMeter;
        private ModernButton btnOpenPip;

        private TrackBar tbDuckVolume;
        private ModernButton btnTestDuck;
        private Image headphonesIconLight;
        private Image headphonesIconDark;
        private Label lblDuckVolValue;
        private Label lblSens;
        private TrackBar tbSensitivity;
        private Label lblSensitivityValue;
        private TrackBar tbReleaseDelay;
        private Label lblReleaseValue;
        private string selectedTriggerTarget = "Microphone";

        private ModernToggle togSmoothFade;
        private ModernToggle togMinimizeToTray;
        private ModernToggle togStartWithWindows;
        private ModernToggle togStartMinimized;

        private ModernButton btnToggle;
        private Label lblStatusBadge;

        private ModernVuMeter vuMeterTrigger;
        private Label lblTrigMeterHeader;
        private Label lblVuTriggerText;
        private ModernVuMeter vuMeterTarget;
        private Label lblVuTargetText;

        private NotifyIcon trayIcon;
        private ContextMenuStrip trayMenu;
        private ToolStripMenuItem targetSubMenu;
        private Icon trayIconIdle;
        private Icon trayIconActive;
        private bool lastTrayWasActive = false;
        private int lastRenderedPeakPct = -1;
        private int lastRenderedVolPct = -1;
        private string lastRenderedStatusText = "";
        private int uiTimerTickCounter = 0;

        private PipOverlayForm pipOverlay;
        private System.Windows.Forms.Timer uiTimer;

        private bool initialHide = false;
        private bool isLoadingSettings = false;

        // Master lists for real-time searchable dropdowns
        private readonly List<string> allTriggerApps = new List<string>();
        private readonly List<string> allTargetApps = new List<string>();
        private bool isFilteringTrigger = false;
        private bool isFilteringTarget = false;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x02000000; // WS_EX_COMPOSITED
                return cp;
            }
        }

        protected override void SetVisibleCore(bool value)
        {
            if (initialHide)
            {
                initialHide = false; // Only block the initial Application.Run startup call!
                value = false;
                if (!IsHandleCreated) CreateHandle();
            }
            base.SetVisibleCore(value);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (this.Icon != null)
            {
                try
                {
                    SendMessage(this.Handle, 0x0080 /* WM_SETICON */, (IntPtr)0 /* ICON_SMALL */, this.Icon.Handle);
                    SendMessage(this.Handle, 0x0080 /* WM_SETICON */, (IntPtr)1 /* ICON_BIG */, this.Icon.Handle);
                }
                catch { }
            }
        }

        // Handle single instance popup notification message
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == (int)Program.WM_SHOWFOCUSFADE)
            {
                RestoreFromTray();
                return;
            }
            base.WndProc(ref m);
        }

        public MainForm(string[] args = null)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            UpdateStyles();

            // Set Form Icon so Taskbar, Task Manager, Alt+Tab and Titlebar show FocusFade's real icon!
            try
            {
                string icoPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
                if (System.IO.File.Exists(icoPath))
                {
                    this.Icon = new Icon(icoPath);
                }
                else
                {
                    this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                }
            }
            catch
            {
                this.Icon = UiHelper.GenerateAppIcon();
            }

            isLoadingSettings = true;
            bool startMinimized = false;
            try
            {
                startMinimized = (args != null && Array.IndexOf(args, "--minimized") >= 0) || settings.StartMinimized;
                if (startMinimized)
                {
                    initialHide = true;
                }

                InitTrayIcons();
                InitUI();
                RefreshProcesses();
                ApplyLoadedSettings();
            }
            finally
            {
                isLoadingSettings = false;
            }

            engine.Initialize();

            // ALWAYS ON BY DEFAULT! No manual start required!
            StartFocusFade();

            uiTimer = new System.Windows.Forms.Timer { Interval = 45 };
            uiTimer.Tick += UiTimer_Tick;

            if (startMinimized)
            {
                TrimMemory();
            }
            else
            {
                uiTimer.Start();
            }
        }

        private void InitTrayIcons()
        {
            trayIconIdle = GenerateFocusFadeTrayIcon(false, 0.0f);
            trayIconActive = GenerateFocusFadeTrayIcon(true, 0.8f);
        }

        // ================= FOCUSFADE ORIGINAL AUDIO-REACTIVE TRAY ICON =================
        // Distinctive purple soundwave squircle badge (100% unique brand identity, not Discord's green circle!)
        private Icon GenerateFocusFadeTrayIcon(bool isDuckingOrActive, float soundLevel)
        {
            using (var bmp = new Bitmap(16, 16))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                var rect = new Rectangle(0, 0, 15, 15);
                using (var path = UiHelper.GetRoundPath(rect, 4))
                {
                    if (isDuckingOrActive)
                    {
                        // Active / Reacting: Vibrant Purple-Indigo Gradient Badge
                        using (var lgb = new LinearGradientBrush(rect, Color.FromArgb(99, 102, 241), Color.FromArgb(168, 85, 247), LinearGradientMode.ForwardDiagonal))
                        {
                            g.FillPath(lgb, path);
                        }
                        using (var pen = new Pen(Color.FromArgb(216, 180, 254), 1f))
                        {
                            g.DrawPath(pen, path);
                        }

                        // Glowing White Soundwave Bars
                        using (var barBrush = new SolidBrush(Color.White))
                        {
                            int boost = (int)(soundLevel * 3);
                            // Bar 1 (Left)
                            int h1 = Math.Min(10, 6 + boost);
                            g.FillRectangle(barBrush, 3, 8 - (h1 / 2), 2, h1);

                            // Bar 2 (Center - Tallest)
                            int h2 = Math.Min(12, 10 + boost);
                            g.FillRectangle(barBrush, 7, 8 - (h2 / 2), 2, h2);

                            // Bar 3 (Right)
                            int h3 = Math.Min(9, 5 + boost);
                            g.FillRectangle(barBrush, 11, 8 - (h3 / 2), 2, h3);
                        }
                    }
                    else
                    {
                        // Idle: Dark Indigo/Navy Badge with Muted Lavender Bars
                        using (var bgBrush = new SolidBrush(Color.FromArgb(15, 23, 42)))
                        {
                            g.FillPath(bgBrush, path);
                        }
                        using (var pen = new Pen(Color.FromArgb(99, 102, 241), 1.2f))
                        {
                            g.DrawPath(pen, path);
                        }

                        using (var barBrush = new SolidBrush(Color.FromArgb(129, 140, 248)))
                        {
                            // Bar 1
                            g.FillRectangle(barBrush, 3, 5, 2, 6);
                            // Bar 2 (Center)
                            g.FillRectangle(barBrush, 7, 3, 2, 10);
                            // Bar 3
                            g.FillRectangle(barBrush, 11, 6, 2, 4);
                        }
                    }
                }

                IntPtr hIcon = bmp.GetHicon();
                return Icon.FromHandle(hIcon);
            }
        }

        private void InitUI()
        {
            Text = "FocusFade — Smart Audio Dimmer";
            ClientSize = new Size(520, 864);
            ShowInTaskbar = true;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;

            // Multi-monitor cursor-aware startup placement
            StartPosition = FormStartPosition.Manual;
            Screen currentScreen = Screen.FromPoint(Cursor.Position);
            Rectangle workArea = currentScreen.WorkingArea;
            Location = new Point(
                workArea.Left + Math.Max(0, (workArea.Width - 520) / 2),
                workArea.Top + Math.Max(0, (workArea.Height - 864) / 2)
            );

            BackColor = Color.FromArgb(13, 17, 23);
            ForeColor = Color.FromArgb(230, 237, 243);
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            headphonesIconLight = UiHelper.CreateHeadphonesVectorIcon(Color.FromArgb(199, 210, 254), 16);
            headphonesIconDark = UiHelper.CreateHeadphonesVectorIcon(Color.FromArgb(20, 20, 20), 16);

            mainPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(22, 12, 22, 12)
            };
            Controls.Add(mainPanel);

            // ================= HEADER =================
            var headerPanel = new Panel { Location = new Point(22, 12), Size = new Size(476, 44) };
            mainPanel.Controls.Add(headerPanel);

            var lblTitle = new Label
            {
                Text = "FocusFade",
                Font = new Font("Segoe UI", 16.5f, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point(0, 0)
            };
            headerPanel.Controls.Add(lblTitle);

            btnOpenPip = new ModernButton
            {
                Text = "PiP Overlay",
                Image = UiHelper.CreatePipVectorIcon(Color.FromArgb(199, 210, 254)),
                Location = new Point(340, 6),
                Size = new Size(136, 28),
                NormalColor1 = Color.FromArgb(37, 43, 67),
                NormalColor2 = Color.FromArgb(37, 43, 67),
                HoverColor1 = Color.FromArgb(50, 58, 90),
                HoverColor2 = Color.FromArgb(50, 58, 90),
                BorderColor = Color.FromArgb(56, 68, 96),
                BorderWidth = 1f,
                ForeColor = Color.FromArgb(199, 210, 254),
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Radius = 6
            };
            btnOpenPip.Click += (s, e) => TogglePip();
            headerPanel.Controls.Add(btnOpenPip);

            var lblSubtitle = new Label
            {
                Text = "Auto-dim background music when priority apps make sound",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(139, 148, 158),
                AutoSize = true,
                Location = new Point(1, 26)
            };
            headerPanel.Controls.Add(lblSubtitle);

            // ================= CARD 1: PRIORITY APPS (CHIP TAGS + SEARCH + MIC GATE) =================
            cardTrigger = new ModernCard { Location = new Point(22, 62), Size = new Size(476, 286) };
            mainPanel.Controls.Add(cardTrigger);

            cardTrigger.Controls.Add(CreateHeaderPanel("PRIORITY APPS (TRIGGERS)", UiHelper.CreateSpeakerVectorIcon(Color.FromArgb(139, 148, 158)), 14, 12));

            var lblTrigSub = new Label
            {
                Text = "Sound from ANY of these apps will lower the background music:",
                Font = new Font("Segoe UI", 8f),
                ForeColor = Color.FromArgb(139, 148, 158),
                Location = new Point(14, 34),
                AutoSize = true
            };
            cardTrigger.Controls.Add(lblTrigSub);

            // Searchable Dropdown for Priority Apps
            cbTriggerInput = new ComboBox
            {
                Location = new Point(14, 54),
                Size = new Size(254, 28),
                DropDownStyle = ComboBoxStyle.DropDown,
                BackColor = Color.FromArgb(33, 38, 45),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9f)
            };
            cbTriggerInput.TextUpdate += CbTriggerInput_TextUpdate;
            cbTriggerInput.DropDown += (s, e) => UpdateRunningAppsList();
            cbTriggerInput.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    AddTriggerFromInput();
                }
            };
            cardTrigger.Controls.Add(cbTriggerInput);

            btnAddTrigger = new ModernButton
            {
                Text = "+ Add App",
                Location = new Point(274, 54),
                Size = new Size(92, 28),
                NormalColor1 = Color.FromArgb(79, 70, 229),
                NormalColor2 = Color.FromArgb(79, 70, 229),
                HoverColor1 = Color.FromArgb(99, 102, 241),
                HoverColor2 = Color.FromArgb(99, 102, 241),
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Radius = 6
            };
            btnAddTrigger.Click += (s, e) => AddTriggerFromInput();
            cardTrigger.Controls.Add(btnAddTrigger);

            btnRefresh = new ModernButton
            {
                Text = "Refresh",
                Image = UiHelper.CreateRefreshVectorIcon(Color.FromArgb(230, 237, 243)),
                Location = new Point(372, 54),
                Size = new Size(90, 28),
                NormalColor1 = Color.FromArgb(48, 54, 61),
                NormalColor2 = Color.FromArgb(48, 54, 61),
                HoverColor1 = Color.FromArgb(65, 73, 83),
                HoverColor2 = Color.FromArgb(65, 73, 83),
                BorderColor = Color.FromArgb(65, 73, 83),
                BorderWidth = 1f,
                ForeColor = Color.FromArgb(230, 237, 243),
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Radius = 6
            };
            btnRefresh.Click += (s, e) => RefreshProcesses();
            cardTrigger.Controls.Add(btnRefresh);

            // Chip Tags Flow Panel (1-Click Removal!)
            flpChips = new FlowLayoutPanel
            {
                Location = new Point(14, 88),
                Size = new Size(448, 34),
                AutoScroll = false,
                WrapContents = true,
                BackColor = Color.FromArgb(22, 27, 34)
            };
            cardTrigger.Controls.Add(flpChips);

            lblEmptyChips = new Label
            {
                Text = "No apps added yet. Search or select an app above and click '+ Add App'!",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Italic),
                ForeColor = Color.FromArgb(100, 116, 139),
                AutoSize = true,
                Padding = new Padding(4, 5, 4, 4)
            };
            flpChips.Controls.Add(lblEmptyChips);

            // Microphone Voice Modern Toggle (No emojis, clean English)
            togDuckMic = new ModernToggle
            {
                Text = "Auto-Dim on Voice (Microphone)",
                Location = new Point(14, 138),
                Size = new Size(448, 22),
                ActiveColor1 = Color.FromArgb(16, 185, 129),
                ActiveColor2 = Color.FromArgb(5, 150, 105),
                Checked = settings.DuckOnMicrophone
            };
            togDuckMic.CheckedChanged += (s, e) =>
            {
                engine.MonitorMicrophone = togDuckMic.Checked;
                if (togDuckMic.Checked)
                {
                    SelectTriggerTarget("Microphone");
                }
                else if (settings.TriggerApps.Count > 0)
                {
                    SelectTriggerTarget(settings.TriggerApps[0]);
                }
                SaveCurrentSettings();
            };
            cardTrigger.Controls.Add(togDuckMic);

            // Per-App Sensitivity / Discord-style Noise Gate Slider
            lblSens = new Label
            {
                Text = "Microphone Sensitivity (Noise Gate):",
                Location = new Point(14, 168),
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(201, 209, 217)
            };
            cardTrigger.Controls.Add(lblSens);

            lblSensitivityValue = new Label
            {
                Text = settings.SensitivityPercent + "%",
                Location = new Point(415, 168),
                AutoSize = true,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(52, 211, 153)
            };
            cardTrigger.Controls.Add(lblSensitivityValue);

            tbSensitivity = new TrackBar
            {
                Location = new Point(10, 188),
                Size = new Size(456, 45),
                Minimum = 1,
                Maximum = 90,
                Value = Math.Max(1, Math.Min(90, settings.SensitivityPercent)),
                TickFrequency = 5
            };
            tbSensitivity.ValueChanged += (s, e) =>
            {
                int val = tbSensitivity.Value;
                lblSensitivityValue.Text = val + "%";
                float thresh = val / 100.0f;
                vuMeterTrigger.Threshold = thresh;
                vuMeterTrigger.Invalidate();

                if (!string.IsNullOrEmpty(selectedTriggerTarget))
                {
                    settings.SetAppSensitivity(selectedTriggerTarget, val);
                    engine.SetAppThreshold(selectedTriggerTarget, thresh);

                    foreach (Control c in flpChips.Controls)
                    {
                        var chip = c as AppChip;
                        if (chip != null && string.Equals(chip.AppName, selectedTriggerTarget, StringComparison.OrdinalIgnoreCase))
                        {
                            chip.UpdateThreshold(val);
                            break;
                        }
                    }
                }
                SaveCurrentSettings();
            };
            cardTrigger.Controls.Add(tbSensitivity);

            // Signal meter with real-time gate evaluation & Discord marker
            lblTrigMeterHeader = new Label
            {
                Text = "Sound Activity:",
                Location = new Point(14, 240),
                AutoSize = true,
                Font = new Font("Segoe UI", 8f),
                ForeColor = Color.FromArgb(139, 148, 158)
            };
            cardTrigger.Controls.Add(lblTrigMeterHeader);

            lblVuTriggerText = new Label
            {
                Text = "0% [Idle]",
                Location = new Point(220, 240),
                Size = new Size(242, 16),
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                ForeColor = Color.FromArgb(139, 148, 158)
            };
            cardTrigger.Controls.Add(lblVuTriggerText);

            vuMeterTrigger = new ModernVuMeter
            {
                Location = new Point(14, 260),
                Size = new Size(448, 12),
                ShowThresholdMarker = true,
                Threshold = settings.SensitivityPercent / 100.0f
            };
            cardTrigger.Controls.Add(vuMeterTrigger);

            // ================= CARD 2: BACKGROUND MUSIC (PERSISTENT TARGETS) =================
            cardTarget = new ModernCard { Location = new Point(22, 360), Size = new Size(476, 210) };
            mainPanel.Controls.Add(cardTarget);

            cardTarget.Controls.Add(CreateHeaderPanel("BACKGROUND MUSIC (TARGETS)", UiHelper.CreateHeadphonesVectorIcon(Color.FromArgb(139, 148, 158)), 14, 12));

            var lblTargSub = new Label
            {
                Text = "Music from ANY of these apps will be lowered when priority sounds play:",
                Font = new Font("Segoe UI", 8f),
                ForeColor = Color.FromArgb(139, 148, 158),
                Location = new Point(14, 34),
                AutoSize = true
            };
            cardTarget.Controls.Add(lblTargSub);

            // Searchable Dropdown for Target App
            cbTarget = new ComboBox
            {
                Location = new Point(14, 54),
                Size = new Size(348, 28),
                DropDownStyle = ComboBoxStyle.DropDown,
                BackColor = Color.FromArgb(33, 38, 45),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9f)
            };
            cbTarget.TextUpdate += CbTarget_TextUpdate;
            cbTarget.DropDown += (s, e) => UpdateRunningAppsList();
            cbTarget.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    string sel = cbTarget.Text.Trim();
                    if (!string.IsNullOrEmpty(sel))
                    {
                        AddTargetProcess(sel);
                        cbTarget.DroppedDown = false;
                    }
                }
            };
            cardTarget.Controls.Add(cbTarget);

            btnAddTarget = new ModernButton
            {
                Text = "+ Add Target",
                Location = new Point(370, 54),
                Size = new Size(92, 28),
                NormalColor1 = Color.FromArgb(124, 58, 237),
                NormalColor2 = Color.FromArgb(109, 40, 217),
                HoverColor1 = Color.FromArgb(139, 92, 246),
                HoverColor2 = Color.FromArgb(124, 58, 237),
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Radius = 6
            };
            btnAddTarget.Click += (s, e) =>
            {
                string sel = cbTarget.Text.Trim();
                if (!string.IsNullOrEmpty(sel))
                {
                    AddTargetProcess(sel);
                }
            };
            cardTarget.Controls.Add(btnAddTarget);

            // Multi-Target Chip Tags Flow Panel (With 1-click 'x' removal)
            flpTargetChips = new FlowLayoutPanel
            {
                Location = new Point(14, 88),
                Size = new Size(448, 34),
                AutoScroll = false,
                WrapContents = true,
                BackColor = Color.FromArgb(22, 27, 34)
            };
            cardTarget.Controls.Add(flpTargetChips);

            lblEmptyTargetChips = new Label
            {
                Text = "No target music apps added yet. Search or select above and click '+ Add Target'!",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Italic),
                ForeColor = Color.FromArgb(100, 116, 139),
                AutoSize = true,
                Padding = new Padding(4, 5, 4, 4)
            };
            flpTargetChips.Controls.Add(lblEmptyTargetChips);

            // Quick Suggestions Chips (1-Click to add detected / common music apps)
            flpQuickTargets = new FlowLayoutPanel
            {
                Location = new Point(14, 128),
                Size = new Size(448, 26),
                AutoScroll = false,
                WrapContents = false,
                BackColor = Color.Transparent
            };
            cardTarget.Controls.Add(flpQuickTargets);

            lblTargMeter = new Label
            {
                Text = "Music Output Level & Current Volume:",
                Location = new Point(14, 158),
                AutoSize = true,
                UseMnemonic = false,
                Font = new Font("Segoe UI", 8f),
                ForeColor = Color.FromArgb(139, 148, 158)
            };
            cardTarget.Controls.Add(lblTargMeter);

            lblVuTargetText = new Label
            {
                Text = "Vol: 100%",
                Location = new Point(350, 158),
                Size = new Size(112, 16),
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                ForeColor = Color.FromArgb(129, 140, 248)
            };
            cardTarget.Controls.Add(lblVuTargetText);

            vuMeterTarget = new ModernVuMeter
            {
                Location = new Point(14, 178),
                Size = new Size(448, 12),
                GradientStart = Color.FromArgb(59, 130, 246),
                GradientMid = Color.FromArgb(139, 92, 246),
                GradientEnd = Color.FromArgb(236, 72, 153)
            };
            cardTarget.Controls.Add(vuMeterTarget);

            // ================= CARD 3: AUDIO FADE SETTINGS =================
            cardSettings = new ModernCard { Location = new Point(22, 486), Size = new Size(476, 193) };
            mainPanel.Controls.Add(cardSettings);

            cardSettings.Controls.Add(CreateHeaderPanel("AUDIO FADE SETTINGS", UiHelper.CreateSlidersVectorIcon(Color.FromArgb(139, 148, 158)), 14, 12));

            // Slider 1: Dimmed Music Volume + LIVE PREVIEW BUTTON
            var lblDuckVol = new Label
            {
                Text = "Dimmed Music Volume:",
                Location = new Point(14, 34),
                AutoSize = true,
                ForeColor = Color.FromArgb(201, 209, 217)
            };
            cardSettings.Controls.Add(lblDuckVol);

            // Dedicated Preview Test Button with headphones vector icon
            btnTestDuck = new ModernButton
            {
                Text = "Test Dim",
                Image = headphonesIconLight,
                Location = new Point(266, 28),
                Size = new Size(120, 26),
                NormalColor1 = Color.FromArgb(37, 43, 67),
                NormalColor2 = Color.FromArgb(37, 43, 67),
                HoverColor1 = Color.FromArgb(50, 58, 90),
                HoverColor2 = Color.FromArgb(50, 58, 90),
                BorderColor = Color.FromArgb(56, 68, 96),
                BorderWidth = 1f,
                ForeColor = Color.FromArgb(199, 210, 254),
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Radius = 6
            };
            btnTestDuck.Click += (s, e) => ToggleVolumePreview();
            cardSettings.Controls.Add(btnTestDuck);

            lblDuckVolValue = new Label
            {
                Text = "15%",
                Location = new Point(410, 32),
                AutoSize = true,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(129, 140, 248)
            };
            cardSettings.Controls.Add(lblDuckVolValue);

            tbDuckVolume = new TrackBar
            {
                Location = new Point(10, 58),
                Size = new Size(456, 45),
                Minimum = 0,
                Maximum = 60,
                Value = 15,
                TickFrequency = 5
            };
            tbDuckVolume.ValueChanged += (s, e) =>
            {
                lblDuckVolValue.Text = tbDuckVolume.Value + "%";
                engine.DuckVolume = tbDuckVolume.Value / 100.0f;
                if (engine.IsPreviewing)
                {
                    engine.PreviewVolume = tbDuckVolume.Value / 100.0f;
                }
                SaveCurrentSettings();
            };
            cardSettings.Controls.Add(tbDuckVolume);

            // Slider 2: Return Delay (Hold Time) (Ample vertical gap, no overlap!)
            var lblDelay = new Label
            {
                Text = "Return Delay (Hold Time):",
                Location = new Point(14, 112),
                AutoSize = true,
                ForeColor = Color.FromArgb(201, 209, 217)
            };
            cardSettings.Controls.Add(lblDelay);

            lblReleaseValue = new Label
            {
                Text = "700 ms",
                Location = new Point(405, 112),
                AutoSize = true,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(129, 140, 248)
            };
            cardSettings.Controls.Add(lblReleaseValue);

            tbReleaseDelay = new TrackBar
            {
                Location = new Point(10, 134),
                Size = new Size(456, 45),
                Minimum = 200,
                Maximum = 2000,
                Value = 700,
                SmallChange = 50,
                LargeChange = 200,
                TickFrequency = 200
            };
            tbReleaseDelay.ValueChanged += (s, e) =>
            {
                lblReleaseValue.Text = tbReleaseDelay.Value + " ms";
                engine.ReleaseDelayMs = tbReleaseDelay.Value;
                SaveCurrentSettings();
            };
            cardSettings.Controls.Add(tbReleaseDelay);

            // ================= CARD 4: PREFERENCES (MODERN TOGGLE SWITCHES) =================
            cardOptions = new ModernCard { Location = new Point(22, 691), Size = new Size(476, 72) };
            mainPanel.Controls.Add(cardOptions);

            togSmoothFade = new ModernToggle
            {
                Text = "Smooth Ease Transition",
                Location = new Point(14, 12),
                Size = new Size(214, 22),
                Checked = settings.SmoothFade
            };
            togSmoothFade.CheckedChanged += (s, e) => { engine.SmoothFade = togSmoothFade.Checked; SaveCurrentSettings(); };
            cardOptions.Controls.Add(togSmoothFade);

            togMinimizeToTray = new ModernToggle
            {
                Text = "Close [X] minimizes to tray",
                Location = new Point(240, 12),
                Size = new Size(222, 22),
                Checked = settings.MinimizeToTray
            };
            togMinimizeToTray.CheckedChanged += (s, e) => SaveCurrentSettings();
            cardOptions.Controls.Add(togMinimizeToTray);

            togStartWithWindows = new ModernToggle
            {
                Text = "Start with Windows",
                Location = new Point(14, 38),
                Size = new Size(214, 22),
                Checked = settings.StartWithWindows
            };
            togStartWithWindows.CheckedChanged += (s, e) => SaveCurrentSettings();
            cardOptions.Controls.Add(togStartWithWindows);

            togStartMinimized = new ModernToggle
            {
                Text = "Start Minimized in Tray",
                Location = new Point(240, 38),
                Size = new Size(222, 22),
                Checked = settings.StartMinimized
            };
            togStartMinimized.CheckedChanged += (s, e) => SaveCurrentSettings();
            cardOptions.Controls.Add(togStartMinimized);

            // ================= TOGGLE BUTTON =================
            btnToggle = new ModernButton
            {
                Text = "STOP FOCUSFADE",
                Image = UiHelper.CreateStopVectorIcon(Color.White),
                Location = new Point(22, 777),
                Size = new Size(476, 44),
                NormalColor1 = Color.FromArgb(220, 38, 38),
                NormalColor2 = Color.FromArgb(185, 28, 28),
                HoverColor1 = Color.FromArgb(239, 68, 68),
                HoverColor2 = Color.FromArgb(220, 38, 38)
            };
            btnToggle.Click += BtnToggle_Click;
            mainPanel.Controls.Add(btnToggle);

            // ================= STATUS BADGE =================
            lblStatusBadge = new Label
            {
                Text = "ACTIVE: Monitoring " + string.Join(", ", settings.TriggerApps),
                Location = new Point(22, 829),
                Size = new Size(476, 20),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(52, 211, 153)
            };
            mainPanel.Controls.Add(lblStatusBadge);

            // ================= SYSTEM TRAY SETUP =================
            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("Open FocusFade", null, (s, e) => RestoreFromTray());
            trayMenu.Items.Add("Open Volume Track PiP", null, (s, e) => OpenPip());
            trayMenu.Items.Add(new ToolStripSeparator());

            targetSubMenu = new ToolStripMenuItem("🎯 Switch Background Target");
            trayMenu.Items.Add(targetSubMenu);

            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("Exit FocusFade Completely", null, (s, e) => ForceExit());
            trayMenu.Opening += (s, e) => PopulateTrayTargetMenu();

            trayIcon = new NotifyIcon
            {
                Text = "FocusFade — Audio Dimmer",
                Icon = trayIconIdle,
                ContextMenuStrip = trayMenu,
                Visible = true
            };

            // SINGLE LEFT-CLICK OR DOUBLE CLICK TO OPEN DIRECTLY!
            trayIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) RestoreFromTray();
            };
            trayIcon.DoubleClick += (s, e) => RestoreFromTray();

            // MINIMIZE [_] SENDS TO TRAY CLEANLY WITHOUT RECREATING HANDLE
            Resize += (s, e) =>
            {
                if ((togMinimizeToTray.Checked || togStartMinimized.Checked) && WindowState == FormWindowState.Minimized)
                {
                    Hide();
                    uiTimer.Stop(); // Drops CPU usage to near 0%!
                    TrimMemory();
                }
            };

            // CLOSE BUTTON [X] HANDLING
            FormClosing += (s, e) =>
            {
                if (togMinimizeToTray.Checked && e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    Hide();
                    uiTimer.Stop(); // Drops CPU usage to 0%!
                    TrimMemory();
                }
                else
                {
                    // Fully exit and terminate process so it vanishes completely from Task Manager!
                    ForceExit();
                }
            };

            UpdateDynamicLayout();
        }

        private void ToggleVolumePreview()
        {
            if (engine.IsPreviewing)
            {
                engine.StopPreview();
            }
            else
            {
                if (settings.TargetApps.Count == 0 && string.IsNullOrEmpty(cbTarget.Text))
                {
                    MessageBox.Show("Please select a Background Music app (e.g. msedge, chrome, Spotify) above first to test the volume!", "FocusFade", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                if (settings.TargetApps.Count > 0)
                {
                    engine.SetTargetProcesses(settings.TargetApps);
                }
                else if (!string.IsNullOrEmpty(cbTarget.Text))
                {
                    engine.SetTargetProcesses(new[] { cbTarget.Text.Trim() });
                }

                engine.TriggerPreview(tbDuckVolume.Value / 100.0f, 4000); // 4 seconds preview
            }
        }

        // ================= SEARCHABLE DROPDOWNS =================
        private void CbTriggerInput_TextUpdate(object sender, EventArgs e)
        {
            if (isFilteringTrigger) return;
            isFilteringTrigger = true;
            try
            {
                string query = cbTriggerInput.Text;
                int selStart = cbTriggerInput.SelectionStart;
                int selLen = cbTriggerInput.SelectionLength;

                cbTriggerInput.BeginUpdate();
                cbTriggerInput.SelectedIndex = -1;
                cbTriggerInput.Items.Clear();

                if (string.IsNullOrEmpty(query))
                {
                    foreach (var item in allTriggerApps) cbTriggerInput.Items.Add(item);
                }
                else
                {
                    foreach (var item in allTriggerApps)
                    {
                        if (item.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            cbTriggerInput.Items.Add(item);
                        }
                    }

                    string trimmed = query.Trim();
                    if (!string.IsNullOrEmpty(trimmed) && !cbTriggerInput.Items.Contains(trimmed))
                    {
                        cbTriggerInput.Items.Add(trimmed);
                    }
                }
                cbTriggerInput.EndUpdate();

                cbTriggerInput.SelectionStart = selStart;
                cbTriggerInput.SelectionLength = selLen;

                if (cbTriggerInput.Items.Count > 0)
                {
                    cbTriggerInput.DroppedDown = true;
                }
                else
                {
                    cbTriggerInput.DroppedDown = false;
                }
                Cursor.Current = Cursors.Default;
            }
            catch { }
            finally
            {
                isFilteringTrigger = false;
            }
        }

        private void CbTarget_TextUpdate(object sender, EventArgs e)
        {
            if (isFilteringTarget) return;
            isFilteringTarget = true;
            try
            {
                string query = cbTarget.Text;
                int selStart = cbTarget.SelectionStart;
                int selLen = cbTarget.SelectionLength;

                cbTarget.BeginUpdate();
                cbTarget.SelectedIndex = -1;
                cbTarget.Items.Clear();

                if (string.IsNullOrEmpty(query))
                {
                    foreach (var item in allTargetApps) cbTarget.Items.Add(item);
                }
                else
                {
                    foreach (var item in allTargetApps)
                    {
                        if (item.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            cbTarget.Items.Add(item);
                        }
                    }

                    string trimmed = query.Trim();
                    if (!string.IsNullOrEmpty(trimmed) && !cbTarget.Items.Contains(trimmed))
                    {
                        cbTarget.Items.Add(trimmed);
                    }
                }
                cbTarget.EndUpdate();

                cbTarget.SelectionStart = selStart;
                cbTarget.SelectionLength = selLen;

                if (cbTarget.Items.Count > 0)
                {
                    cbTarget.DroppedDown = true;
                }
                else
                {
                    cbTarget.DroppedDown = false;
                }
                Cursor.Current = Cursors.Default;
            }
            catch { }
            finally
            {
                isFilteringTarget = false;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            BeginInvoke(new Action(() => TrimMemory()));
        }

        private void TrimMemory()
        {
            try
            {
                GC.Collect(2, GCCollectionMode.Forced, false);
                GC.WaitForPendingFinalizers();
                SetProcessWorkingSetSize((IntPtr)(-1), -1, -1);
            }
            catch { }
        }

        private void ForceExit()
        {
            SaveCurrentSettings();
            engine.Shutdown();
            if (pipOverlay != null && !pipOverlay.IsDisposed) pipOverlay.Close();
            if (trayIcon != null)
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
            }
            Environment.Exit(0);
        }

        private Panel CreateHeaderPanel(string text, Image icon, int x, int y)
        {
            var pnl = new Panel
            {
                Location = new Point(x, y),
                Height = 18,
                AutoSize = true,
                BackColor = Color.Transparent
            };
            int currX = 0;
            if (icon != null)
            {
                var pb = new PictureBox
                {
                    Image = icon,
                    Size = new Size(16, 16),
                    Location = new Point(0, 1),
                    SizeMode = PictureBoxSizeMode.Zoom
                };
                pnl.Controls.Add(pb);
                currX += 22;
            }
            var lbl = new Label
            {
                Text = text,
                Location = new Point(currX, 0),
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(139, 148, 158)
            };
            pnl.Controls.Add(lbl);
            return pnl;
        }

        private Label CreateHeaderLabel(string text, int x, int y)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(139, 148, 158)
            };
        }

        private void AddTriggerFromInput()
        {
            string app = cbTriggerInput.Text.Trim();
            if (string.IsNullOrEmpty(app)) return;

            if (app.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                app = app.Substring(0, app.Length - 4).Trim();
            }

            int existingIdx = -1;
            for (int i = 0; i < settings.TriggerApps.Count; i++)
            {
                if (string.Equals(settings.TriggerApps[i], app, StringComparison.OrdinalIgnoreCase))
                {
                    existingIdx = i;
                    break;
                }
            }

            if (existingIdx == -1)
            {
                settings.TriggerApps.Add(app);
                RebuildChipList();
                SyncTriggersToEngine();
                SelectTriggerTarget(app);
                SaveCurrentSettings();
            }
            else
            {
                SelectTriggerTarget(settings.TriggerApps[existingIdx]);
            }
            cbTriggerInput.Text = "";
        }

        private void RemoveTrigger(string app)
        {
            int idx = -1;
            for (int i = 0; i < settings.TriggerApps.Count; i++)
            {
                if (string.Equals(settings.TriggerApps[i], app, StringComparison.OrdinalIgnoreCase))
                {
                    idx = i;
                    break;
                }
            }

            if (idx >= 0)
            {
                settings.TriggerApps.RemoveAt(idx);
                if (string.Equals(selectedTriggerTarget, app, StringComparison.OrdinalIgnoreCase))
                {
                    if (settings.TriggerApps.Count > 0) SelectTriggerTarget(settings.TriggerApps[0]);
                    else SelectTriggerTarget("Microphone");
                }
                RebuildChipList();
                SyncTriggersToEngine();
                SaveCurrentSettings();
            }
        }

        private void RebuildChipList()
        {
            flpChips.SuspendLayout();
            flpChips.Controls.Clear();
            if (settings.TriggerApps.Count == 0)
            {
                flpChips.Controls.Add(lblEmptyChips);
            }
            else
            {
                foreach (var app in settings.TriggerApps)
                {
                    int thresh = settings.GetAppSensitivity(app, 20);
                    bool isSel = string.Equals(app, selectedTriggerTarget, StringComparison.OrdinalIgnoreCase);
                    bool isTarget = settings.TargetApps != null && settings.TargetApps.Exists(t => string.Equals(t, app, StringComparison.OrdinalIgnoreCase));
                    var chip = new AppChip(app, thresh, isSel, isTarget);
                    chip.OnRemove += RemoveTrigger;
                    chip.OnSelect += SelectTriggerTarget;
                    flpChips.Controls.Add(chip);
                }
            }
            flpChips.ResumeLayout(true);
            UpdateDynamicLayout();
        }

        private int CalculateChipsHeight()
        {
            int count = settings.TriggerApps.Count;
            if (count <= 0) return 34;

            // In 448px width, each chip (icon + text + threshold + close button + margins)
            // is ~150-180px wide. Maximum 2 chips can fit on a single row.
            // 1-2 chips = 1 row (34px)
            // 3-4 chips = 2 rows (68px)
            // 5-6 chips = 3 rows (102px)
            // 7-8 chips = 4 rows (136px)
            int rows = (int)Math.Ceiling(count / 2.0);
            return Math.Min(136, rows * 34);
        }

        private int CalculateTargetChipsHeight()
        {
            int count = settings.TargetApps.Count;
            if (count <= 0) return 34;
            int rows = (int)Math.Ceiling(count / 2.0);
            return Math.Min(136, rows * 34);
        }

        private void UpdateDynamicLayout()
        {
            if (flpChips == null || cardTrigger == null || cardTarget == null || cardSettings == null || cardOptions == null || btnToggle == null || lblStatusBadge == null)
            {
                return;
            }

            this.SuspendLayout();
            cardTrigger.SuspendLayout();

            int neededHeight = CalculateChipsHeight();
            if (neededHeight > 140)
            {
                flpChips.AutoScroll = true;
                flpChips.Height = 140;
                try { SetWindowTheme(flpChips.Handle, "DarkMode_Explorer", null); } catch { }
            }
            else
            {
                flpChips.AutoScroll = false;
                flpChips.Height = neededHeight;
            }

            // Dynamically reposition elements inside cardTrigger based on flpChips.Bottom
            togDuckMic.Location = new Point(14, flpChips.Bottom + 10);
            lblSens.Location = new Point(14, togDuckMic.Bottom + 10);
            lblSensitivityValue.Location = new Point(415, togDuckMic.Bottom + 10);
            tbSensitivity.Location = new Point(10, lblSens.Bottom + 4);
            lblTrigMeterHeader.Location = new Point(14, tbSensitivity.Bottom + 6);
            lblVuTriggerText.Location = new Point(220, tbSensitivity.Bottom + 6);
            vuMeterTrigger.Location = new Point(14, lblTrigMeterHeader.Bottom + 6);

            cardTrigger.Height = vuMeterTrigger.Bottom + 14;
            cardTrigger.ResumeLayout(true);

            // Dynamically reposition elements inside cardTarget based on flpTargetChips.Bottom
            if (flpTargetChips != null)
            {
                cardTarget.SuspendLayout();
                int targChipsH = CalculateTargetChipsHeight();
                if (targChipsH > 140)
                {
                    flpTargetChips.AutoScroll = true;
                    flpTargetChips.Height = 140;
                    try { SetWindowTheme(flpTargetChips.Handle, "DarkMode_Explorer", null); } catch { }
                }
                else
                {
                    flpTargetChips.AutoScroll = false;
                    flpTargetChips.Height = targChipsH;
                }

                flpQuickTargets.Location = new Point(14, flpTargetChips.Bottom + 6);
                lblTargMeter.Location = new Point(14, flpQuickTargets.Bottom + 8);
                lblVuTargetText.Location = new Point(350, flpQuickTargets.Bottom + 8);
                vuMeterTarget.Location = new Point(14, lblTargMeter.Bottom + 6);

                cardTarget.Height = vuMeterTarget.Bottom + 14;
                cardTarget.ResumeLayout(true);
            }

            // Dynamically reposition cards below cardTrigger
            cardTarget.Location = new Point(22, cardTrigger.Bottom + 12);
            cardSettings.Location = new Point(22, cardTarget.Bottom + 12);
            cardOptions.Location = new Point(22, cardSettings.Bottom + 12);
            btnToggle.Location = new Point(22, cardOptions.Bottom + 14);
            lblStatusBadge.Location = new Point(22, btnToggle.Bottom + 8);

            // Compute ideal form client height
            int targetHeight = lblStatusBadge.Bottom + 18;

            // Screen safety clamp so form never extends beyond display bounds
            Screen scr = Screen.FromPoint(this.Location);
            int maxScreenH = scr.WorkingArea.Height - 40;
            if (targetHeight > maxScreenH)
            {
                targetHeight = maxScreenH;
                mainPanel.AutoScroll = true;
                try { SetWindowTheme(mainPanel.Handle, "DarkMode_Explorer", null); } catch { }
            }
            else
            {
                mainPanel.AutoScroll = false;
            }

            if (ClientSize.Height != targetHeight)
            {
                ClientSize = new Size(520, targetHeight);
            }

            // Ensure window bottom doesn't sink beneath taskbar
            if (this.Bottom > scr.WorkingArea.Bottom)
            {
                this.Top = Math.Max(scr.WorkingArea.Top, scr.WorkingArea.Bottom - this.Height);
            }

            this.ResumeLayout(true);
        }

        private void SelectTriggerTarget(string targetName)
        {
            if (string.IsNullOrEmpty(targetName)) return;
            selectedTriggerTarget = targetName;

            foreach (Control c in flpChips.Controls)
            {
                var chip = c as AppChip;
                if (chip != null)
                {
                    chip.SetSelected(string.Equals(chip.AppName, targetName, StringComparison.OrdinalIgnoreCase));
                }
            }

            int sens = settings.GetAppSensitivity(targetName, 20);
            if (string.Equals(targetName, "Microphone", StringComparison.OrdinalIgnoreCase))
            {
                if (lblSens != null) lblSens.Text = "Microphone Sensitivity (Noise Gate):";
                if (lblTrigMeterHeader != null) lblTrigMeterHeader.Text = "Microphone Input Level:";
            }
            else
            {
                bool isTarget = settings.TargetApps != null && settings.TargetApps.Exists(t => string.Equals(t, targetName, StringComparison.OrdinalIgnoreCase));
                if (lblSens != null)
                {
                    lblSens.Text = isTarget 
                        ? string.Format("Threshold for [{0}] (Paused — Active Target):", targetName)
                        : string.Format("Threshold for [{0}]:", targetName);
                }
                if (lblTrigMeterHeader != null)
                {
                    lblTrigMeterHeader.Text = isTarget 
                        ? string.Format("Audio Level ({0}) [Bypassed]:", targetName)
                        : string.Format("Audio Level ({0}):", targetName);
                }
            }

            if (tbSensitivity != null)
            {
                tbSensitivity.Value = Math.Max(1, Math.Min(90, sens));
                lblSensitivityValue.Text = tbSensitivity.Value + "%";
                vuMeterTrigger.Threshold = tbSensitivity.Value / 100.0f;
                vuMeterTrigger.Invalidate();
            }
        }

        private void SyncTriggersToEngine()
        {
            engine.SetTriggerProcesses(settings.TriggerApps);
            foreach (var app in settings.TriggerApps)
            {
                int s = settings.GetAppSensitivity(app, 20);
                engine.SetAppThreshold(app, s / 100.0f);
            }
            int micSens = settings.GetAppSensitivity("Microphone", settings.SensitivityPercent);
            engine.SetAppThreshold("Microphone", micSens / 100.0f);
        }

        public void ShowDashboard()
        {
            initialHide = false;
            if (WindowState != FormWindowState.Normal)
            {
                WindowState = FormWindowState.Normal;
            }

            UpdateDynamicLayout();

            // Ensure window is visible on a currently active screen
            Screen curScreen = Screen.FromPoint(Cursor.Position);
            Rectangle work = curScreen.WorkingArea;
            bool onScreen = false;
            foreach (var s in Screen.AllScreens)
            {
                if (s.WorkingArea.IntersectsWith(this.Bounds))
                {
                    onScreen = true;
                    break;
                }
            }
            if (!onScreen)
            {
                Location = new Point(
                    work.Left + Math.Max(0, (work.Width - this.Width) / 2),
                    work.Top + Math.Max(0, (work.Height - this.Height) / 2)
                );
            }

            Show();
            BringToFront();
            Activate();

            // Win32 Force Restore and Foreground Focus
            try
            {
                IntPtr hWnd = this.Handle;
                ShowWindow(hWnd, 9 /* SW_RESTORE */);
                ShowWindow(hWnd, 5 /* SW_SHOW */);
                SetForegroundWindow(hWnd);

                if (this.Icon != null)
                {
                    SendMessage(this.Handle, 0x0080 /* WM_SETICON */, (IntPtr)0 /* ICON_SMALL */, this.Icon.Handle);
                    SendMessage(this.Handle, 0x0080 /* WM_SETICON */, (IntPtr)1 /* ICON_BIG */, this.Icon.Handle);
                }
            }
            catch { }

            if (!uiTimer.Enabled) uiTimer.Start();
            BeginInvoke(new Action(() => TrimMemory()));
        }

        private void RestoreFromTray()
        {
            ShowDashboard();
        }

        public void OpenPip()
        {
            if (pipOverlay == null || pipOverlay.IsDisposed)
            {
                pipOverlay = new PipOverlayForm(this, engine, settings);
                pipOverlay.Show();
                TrimMemory();
            }
            else
            {
                pipOverlay.BringToFront();
            }
        }

        private void TogglePip()
        {
            if (pipOverlay == null || pipOverlay.IsDisposed) OpenPip();
            else pipOverlay.Close();
        }

        private void StartFocusFade()
        {
            SyncTriggersToEngine();
            string[] trigs = engine.GetTriggerProcesses();

            if (trigs.Length == 0) return;
            if (settings.TargetApps.Count == 0 && string.IsNullOrEmpty(settings.TargetApp) && (cbTarget == null || string.IsNullOrEmpty(cbTarget.Text))) return;

            // Maintain ALL persistent target apps! Never truncate to a single app!
            if (settings.TargetApps.Count > 0)
            {
                engine.SetTargetProcesses(settings.TargetApps);
            }
            else if (!string.IsNullOrEmpty(settings.TargetApp))
            {
                engine.SetTargetProcesses(new[] { settings.TargetApp });
            }
            else if (cbTarget != null && !string.IsNullOrEmpty(cbTarget.Text))
            {
                AddTargetProcess(cbTarget.Text.Trim());
            }

            engine.DuckVolume = tbDuckVolume != null ? tbDuckVolume.Value / 100.0f : settings.DuckVolumePercent / 100.0f;
            engine.TriggerThreshold = tbSensitivity != null ? tbSensitivity.Value / 100.0f : settings.SensitivityPercent / 100.0f;
            engine.ReleaseDelayMs = tbReleaseDelay != null ? tbReleaseDelay.Value : settings.ReleaseDelayMs;
            engine.SmoothFade = togSmoothFade != null ? togSmoothFade.Checked : settings.SmoothFade;

            engine.Start();

            if (btnToggle != null)
            {
                btnToggle.Text = "STOP FOCUSFADE";
                btnToggle.Image = UiHelper.CreateStopVectorIcon(Color.White);
                btnToggle.NormalColor1 = Color.FromArgb(220, 38, 38);
                btnToggle.NormalColor2 = Color.FromArgb(185, 28, 28);
                btnToggle.HoverColor1 = Color.FromArgb(239, 68, 68);
                btnToggle.HoverColor2 = Color.FromArgb(220, 38, 38);
                btnToggle.Invalidate();
            }

            UpdateStatusBadge();

            SaveCurrentSettings();
        }

        private void StopFocusFade()
        {
            engine.Stop();
            btnToggle.Text = "START FOCUSFADE";
            btnToggle.Image = UiHelper.CreatePlayVectorIcon(Color.White);
            btnToggle.NormalColor1 = Color.FromArgb(99, 102, 241);
            btnToggle.NormalColor2 = Color.FromArgb(139, 92, 246);
            btnToggle.HoverColor1 = Color.FromArgb(129, 140, 248);
            btnToggle.HoverColor2 = Color.FromArgb(167, 139, 250);
            btnToggle.Invalidate();

            lblStatusBadge.Text = "Status: Stopped";
            lblStatusBadge.ForeColor = Color.FromArgb(139, 148, 158);
        }

        private void BtnToggle_Click(object sender, EventArgs e)
        {
            if (!engine.IsActive)
            {
                SyncTriggersToEngine();
                string[] trigs = engine.GetTriggerProcesses();
                string target = cbTarget.Text.Trim();

                if (trigs.Length == 0)
                {
                    MessageBox.Show("Please add at least one Priority Trigger app (e.g. Discord, AfterFX)!", "FocusFade", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                if (settings.TargetApps.Count == 0 && string.IsNullOrEmpty(target))
                {
                    MessageBox.Show("Please select or add a background music player or browser as the Target!", "FocusFade", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                StartFocusFade();
            }
            else
            {
                StopFocusFade();
            }
        }

        private void UpdateRunningAppsList()
        {
            try
            {
                var discovered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // 1. WASAPI Active Audio Streams (Across all active playback devices)
                try
                {
                    var enumerator = (IMMDeviceEnumerator)(new MMDeviceEnumerator());
                    try
                    {
                        var wasapiApps = ProcessHelper.GetActiveAudioProcessNames(enumerator);
                        foreach (var app in wasapiApps) discovered.Add(app);
                    }
                    finally
                    {
                        try { Marshal.ReleaseComObject(enumerator); } catch { }
                    }
                }
                catch { }

                // 2. Scan Running Desktop Applications
                try
                {
                    var systemExclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "System", "Idle", "Registry", "smss", "csrss", "wininit", "services", "lsass",
                        "svchost", "fontdrvhost", "dwm", "spoolsv", "SearchIndexer", "conhost",
                        "explorer", "ShellExperienceHost", "ScreenClippingHost", "SnippingTool",
                        "audiodg", "ctfmon", "TextInputHost", "SearchHost", "StartMenuExperienceHost",
                        "SecurityHealthService", "sihost", "taskhostw", "rundll32", "dllhost", "wlanext",
                        "ApplicationFrameHost", "SystemSettings"
                    };

                    Process[] procs = Process.GetProcesses();
                    for (int pIdx = 0; pIdx < procs.Length; pIdx++)
                    {
                        try
                        {
                            var proc = procs[pIdx];
                            string pName = proc.ProcessName;
                            if (string.IsNullOrEmpty(pName) || systemExclusions.Contains(pName)) continue;

                            if (proc.MainWindowHandle != IntPtr.Zero || ProcessHelper.IsKnownApp(pName))
                            {
                                discovered.Add(pName);
                            }
                        }
                        catch { }
                        finally
                        {
                            try { procs[pIdx].Dispose(); } catch { }
                        }
                    }
                }
                catch { }

                // Add newly opened apps to master lists and combo box choices
                foreach (var app in discovered)
                {
                    if (!allTriggerApps.Contains(app))
                    {
                        allTriggerApps.Add(app);
                        if (cbTriggerInput != null && !cbTriggerInput.Items.Contains(app))
                        {
                            cbTriggerInput.Items.Add(app);
                        }
                    }

                    if (!allTargetApps.Contains(app))
                    {
                        allTargetApps.Add(app);
                        if (cbTarget != null && !cbTarget.Items.Contains(app))
                        {
                            cbTarget.Items.Add(app);
                        }
                    }
                }
            }
            catch { }
        }

        private void RefreshProcesses()
        {
            string oldInput = cbTriggerInput != null ? cbTriggerInput.Text : "";
            string oldTarg = cbTarget != null ? cbTarget.Text : "";

            var runningApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. WASAPI Active Audio Streams (Across all active playback devices)
            try
            {
                var enumerator = (IMMDeviceEnumerator)(new MMDeviceEnumerator());
                try
                {
                    var wasapiApps = ProcessHelper.GetActiveAudioProcessNames(enumerator);
                    foreach (var app in wasapiApps) runningApps.Add(app);
                }
                finally
                {
                    try { Marshal.ReleaseComObject(enumerator); } catch { }
                }
            }
            catch { }

            // 2. Scan Running Desktop Applications & Media Players (so VLC, Spotify, browsers, editors are detected even when paused or idle!)
            try
            {
                var systemExclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "System", "Idle", "Registry", "smss", "csrss", "wininit", "services", "lsass",
                    "svchost", "fontdrvhost", "dwm", "spoolsv", "SearchIndexer", "conhost",
                    "explorer", "ShellExperienceHost", "ScreenClippingHost", "SnippingTool",
                    "audiodg", "ctfmon", "TextInputHost", "SearchHost", "StartMenuExperienceHost",
                    "SecurityHealthService", "sihost", "taskhostw", "rundll32", "dllhost", "wlanext",
                    "ApplicationFrameHost", "SystemSettings"
                };

                Process[] procs = Process.GetProcesses();
                for (int pIdx = 0; pIdx < procs.Length; pIdx++)
                {
                    try
                    {
                        var proc = procs[pIdx];
                        string pName = proc.ProcessName;
                        if (string.IsNullOrEmpty(pName) || systemExclusions.Contains(pName)) continue;

                        if (proc.MainWindowHandle != IntPtr.Zero || ProcessHelper.IsKnownApp(pName))
                        {
                            runningApps.Add(pName);
                        }
                    }
                    catch { }
                    finally
                    {
                        try { procs[pIdx].Dispose(); } catch { }
                    }
                }
            }
            catch { }

            string[] commonTriggers = { "Microphone", "Discord", "Telegram", "WhatsApp", "Zoom", "OBS", "AfterFX", "Adobe Premiere Pro", "CapCut", "Resolve", "vegas", "Audition", "blender" };
            string[] mediaPresets = { "Spotify", "ytm-tauri", "vlc", "msedge", "chrome", "brave", "firefox", "opera", "wmplayer", "mpv", "foobar2000", "aimp", "MusicBee", "iTunes", "AppleMusic", "YouTube Music" };

            allTriggerApps.Clear();
            foreach (var app in runningApps)
            {
                // Exclude system background processes from trigger search by default
                if (!app.Equals("ShellExperienceHost", StringComparison.OrdinalIgnoreCase) &&
                    !app.Equals("ScreenClippingHost", StringComparison.OrdinalIgnoreCase) &&
                    !app.Equals("explorer", StringComparison.OrdinalIgnoreCase))
                {
                    allTriggerApps.Add(app);
                }
            }
            foreach (var p in commonTriggers) if (!allTriggerApps.Contains(p)) allTriggerApps.Add(p);

            allTargetApps.Clear();
            // Migrate legacy generic "msedgewebview2" target to specific host app (e.g. ytm-tauri) if present
            if (string.Equals(settings.TargetApp, "msedgewebview2", StringComparison.OrdinalIgnoreCase))
            {
                if (runningApps.Contains("ytm-tauri") || Process.GetProcessesByName("ytm-tauri").Length > 0)
                {
                    settings.TargetApp = "ytm-tauri";
                    settings.Save();
                }
            }

            // ALWAYS remember the saved target app first so it's never lost even when closed!
            if (!string.IsNullOrEmpty(settings.TargetApp))
            {
                allTargetApps.Add(settings.TargetApp);
            }
            foreach (var app in runningApps) if (!allTargetApps.Contains(app)) allTargetApps.Add(app);
            foreach (var p in mediaPresets) if (!allTargetApps.Contains(p)) allTargetApps.Add(p);

            cbTriggerInput.BeginUpdate();
            cbTriggerInput.SelectedIndex = -1;
            cbTriggerInput.Items.Clear();
            foreach (var app in allTriggerApps) cbTriggerInput.Items.Add(app);
            cbTriggerInput.EndUpdate();

            cbTarget.BeginUpdate();
            cbTarget.SelectedIndex = -1;
            cbTarget.Items.Clear();
            foreach (var app in allTargetApps) cbTarget.Items.Add(app);
            cbTarget.EndUpdate();

            if (!string.IsNullOrEmpty(oldInput)) cbTriggerInput.Text = oldInput;
            else if (cbTriggerInput.Items.Count > 0) cbTriggerInput.SelectedIndex = 0;

            // PRESERVE SAVED TARGET APPS ALWAYS!
            if (settings.TargetApps != null && settings.TargetApps.Count > 0)
            {
                cbTarget.Text = settings.TargetApps[0];
                engine.SetTargetProcesses(settings.TargetApps);
            }
            else if (!string.IsNullOrEmpty(settings.TargetApp))
            {
                cbTarget.Text = settings.TargetApp;
                engine.SetTargetProcesses(new[] { settings.TargetApp });
            }
            else if (!string.IsNullOrEmpty(oldTarg))
            {
                cbTarget.Text = oldTarg;
                engine.SetTargetProcesses(new[] { oldTarg });
            }
            else
            {
                foreach (var p in mediaPresets)
                {
                    if (runningApps.Contains(p)) { cbTarget.Text = p; engine.SetTargetProcesses(new[] { p }); break; }
                }
                if (string.IsNullOrEmpty(cbTarget.Text) && cbTarget.Items.Count > 0)
                {
                    cbTarget.SelectedIndex = 0;
                    engine.SetTargetProcesses(new[] { cbTarget.Text });
                }
            }

            RebuildQuickTargetChips();
            RefreshAllChipIcons();
        }

        private void RefreshAllChipIcons()
        {
            try
            {
                if (flpTargetChips != null)
                {
                    foreach (Control c in flpTargetChips.Controls)
                    {
                        var chip = c as TargetAppChip;
                        if (chip != null) chip.RefreshIcon();
                    }
                }
                if (flpChips != null)
                {
                    foreach (Control c in flpChips.Controls)
                    {
                        var chip = c as AppChip;
                        if (chip != null) chip.RefreshIcon();
                    }
                }
                if (flpQuickTargets != null)
                {
                    foreach (Control c in flpQuickTargets.Controls)
                    {
                        var chip = c as QuickSuggestChip;
                        if (chip != null) chip.RefreshIcon();
                    }
                }
            }
            catch { }
        }

        private void UiTimer_Tick(object sender, EventArgs e)
        {
            uiTimerTickCounter++;
            if (uiTimerTickCounter >= 1000)
            {
                uiTimerTickCounter = 0;
                TrimMemory();
            }

            if (uiTimerTickCounter % 45 == 0)
            {
                RefreshAllChipIcons();
            }

            // Read peak of the currently selected target (or microphone)
            float selectedPeak = 0.0f;
            if (string.Equals(selectedTriggerTarget, "Microphone", StringComparison.OrdinalIgnoreCase))
            {
                if (togDuckMic != null && !togDuckMic.Checked)
                {
                    selectedPeak = 0.0f;
                }
                else
                {
                    selectedPeak = engine.GetAppPeak("Microphone");
                }
            }
            else
            {
                selectedPeak = engine.GetAppPeak(selectedTriggerTarget);
            }

            vuMeterTrigger.Level = selectedPeak;

            // Live Gate Evaluation Feedback
            int peakPct = (int)(selectedPeak * 100);
            int gatePct = tbSensitivity.Value;

            if (peakPct != lastRenderedPeakPct)
            {
                lastRenderedPeakPct = peakPct;
                bool isSelectedAppTarget = !string.Equals(selectedTriggerTarget, "Microphone", StringComparison.OrdinalIgnoreCase) &&
                                           settings.TargetApps != null && settings.TargetApps.Exists(t => string.Equals(t, selectedTriggerTarget, StringComparison.OrdinalIgnoreCase));

                if (isSelectedAppTarget)
                {
                    lblVuTriggerText.Text = string.Format("{0}% [Target - Bypassed]", peakPct);
                    lblVuTriggerText.ForeColor = Color.FromArgb(168, 85, 247); // Violet
                }
                else if (peakPct >= gatePct && peakPct > 0)
                {
                    if (string.Equals(selectedTriggerTarget, "Microphone", StringComparison.OrdinalIgnoreCase))
                    {
                        lblVuTriggerText.Text = string.Format("{0}% [VOICE ACTIVE - DIMMING]", peakPct);
                        lblVuTriggerText.ForeColor = Color.FromArgb(52, 211, 153); // Emerald
                    }
                    else
                    {
                        lblVuTriggerText.Text = string.Format("{0}% [ACTIVE - DIMMING]", peakPct);
                        lblVuTriggerText.ForeColor = Color.FromArgb(245, 158, 11); // Amber
                    }
                }
                else if (peakPct > 0)
                {
                    lblVuTriggerText.Text = string.Format("{0}% [Below Threshold]", peakPct);
                    lblVuTriggerText.ForeColor = Color.FromArgb(100, 116, 139); // Muted
                }
                else
                {
                    lblVuTriggerText.Text = "0% [Quiet]";
                    lblVuTriggerText.ForeColor = Color.FromArgb(139, 148, 158);
                }
            }

            float curTargetVol = engine.CurrentTargetVolume;
            vuMeterTarget.Level = curTargetVol;
            int volPct = (int)(curTargetVol * 100);
            if (volPct != lastRenderedVolPct)
            {
                lastRenderedVolPct = volPct;
                lblVuTargetText.Text = string.Format("Vol: {0}%", volPct);
            }

            // Preview button UI state
            if (engine.IsPreviewing)
            {
                string previewText = string.Format("{0}% (Testing)", volPct);
                if (btnTestDuck.Text != previewText) btnTestDuck.Text = previewText;
                btnTestDuck.NormalColor1 = Color.FromArgb(245, 158, 11);
                btnTestDuck.NormalColor2 = Color.FromArgb(245, 158, 11);
                btnTestDuck.HoverColor1 = Color.FromArgb(251, 191, 36);
                btnTestDuck.HoverColor2 = Color.FromArgb(251, 191, 36);
                btnTestDuck.BorderColor = Color.FromArgb(217, 119, 6);
                btnTestDuck.ForeColor = Color.FromArgb(20, 20, 20);
                btnTestDuck.Image = headphonesIconDark;
                string newStatus = string.Format("PREVIEW: Dimming music to {0}% ({1})", volPct, engine.TargetProcess);
                if (lblStatusBadge.Text != newStatus)
                {
                    lblStatusBadge.Text = newStatus;
                    lblStatusBadge.ForeColor = Color.FromArgb(245, 158, 11);
                }
            }
            else
            {
                if (btnTestDuck.Text != "Test Dim")
                {
                    btnTestDuck.Text = "Test Dim";
                    btnTestDuck.NormalColor1 = Color.FromArgb(37, 43, 67);
                    btnTestDuck.NormalColor2 = Color.FromArgb(37, 43, 67);
                    btnTestDuck.HoverColor1 = Color.FromArgb(50, 58, 90);
                    btnTestDuck.HoverColor2 = Color.FromArgb(50, 58, 90);
                    btnTestDuck.BorderColor = Color.FromArgb(56, 68, 96);
                    btnTestDuck.ForeColor = Color.FromArgb(199, 210, 254);
                    btnTestDuck.Image = headphonesIconLight;
                }
            }

            // FOCUSFADE ORIGINAL AUDIO REACTIVE TRAY ICON!
            bool isAudioActive = (engine.IsActive || engine.IsPreviewing) && (engine.IsDucked || engine.TriggerPeak >= engine.TriggerThreshold);
            if (isAudioActive != lastTrayWasActive)
            {
                lastTrayWasActive = isAudioActive;
                trayIcon.Icon = isAudioActive ? trayIconActive : trayIconIdle;
            }

            if (!engine.IsActive && !engine.IsPreviewing) return;

            if (engine.IsDucked && !engine.IsPreviewing)
            {
                string who = string.IsNullOrEmpty(engine.ActiveTriggerName) ? "Priority Audio" : engine.ActiveTriggerName;
                if (who == "Microphone")
                {
                    string newStatus = string.Format("VOICE DETECTED ({0}%): Dimming background music", volPct);
                    if (lblStatusBadge.Text != newStatus)
                    {
                        lblStatusBadge.Text = newStatus;
                        lblStatusBadge.ForeColor = Color.FromArgb(52, 211, 153);
                        lastRenderedStatusText = newStatus;
                    }
                }
                else
                {
                    string newStatus = string.Format("ACTIVE (DIMMED {0}%): Triggered by {1}", volPct, who);
                    if (lblStatusBadge.Text != newStatus)
                    {
                        lblStatusBadge.Text = newStatus;
                        lblStatusBadge.ForeColor = Color.FromArgb(245, 158, 11);
                        lastRenderedStatusText = newStatus;
                    }
                }
            }
            else if (!engine.IsPreviewing)
            {
                if (lblStatusBadge.Text != lastRenderedStatusText || string.IsNullOrEmpty(lastRenderedStatusText))
                {
                    UpdateStatusBadge();
                    lastRenderedStatusText = lblStatusBadge.Text;
                }
            }
        }

        private void ApplyLoadedSettings()
        {
            RebuildChipList();
            SyncTriggersToEngine();

            engine.SetTargetProcesses(settings.TargetApps);
            RebuildTargetChips();
            UpdateTriggerChipsTargetState();

            tbDuckVolume.Value = settings.DuckVolumePercent;
            lblDuckVolValue.Text = tbDuckVolume.Value + "%";
            engine.DuckVolume = tbDuckVolume.Value / 100.0f;

            tbReleaseDelay.Value = settings.ReleaseDelayMs;
            lblReleaseValue.Text = tbReleaseDelay.Value + " ms";
            engine.ReleaseDelayMs = tbReleaseDelay.Value;

            if (togSmoothFade != null)
            {
                togSmoothFade.Checked = settings.SmoothFade;
                engine.SmoothFade = settings.SmoothFade;
            }

            if (togMinimizeToTray != null) togMinimizeToTray.Checked = settings.MinimizeToTray;
            if (togStartWithWindows != null) togStartWithWindows.Checked = settings.StartWithWindows;
            if (togStartMinimized != null) togStartMinimized.Checked = settings.StartMinimized;

            if (togDuckMic != null)
            {
                togDuckMic.Checked = settings.DuckOnMicrophone;
                engine.MonitorMicrophone = settings.DuckOnMicrophone;
            }

            // Prioritize App Chip if Microphone is OFF
            if (!settings.DuckOnMicrophone && settings.TriggerApps.Count > 0)
            {
                SelectTriggerTarget(settings.TriggerApps[0]);
            }
            else if (settings.DuckOnMicrophone)
            {
                SelectTriggerTarget("Microphone");
            }
            else if (settings.TriggerApps.Count > 0)
            {
                SelectTriggerTarget(settings.TriggerApps[0]);
            }
            else
            {
                SelectTriggerTarget("Microphone");
            }

            UpdateStatusBadge();
        }

        private void SaveCurrentSettings()
        {
            if (isLoadingSettings) return;

            if (tbDuckVolume != null) settings.DuckVolumePercent = tbDuckVolume.Value;
            if (tbSensitivity != null) settings.SensitivityPercent = tbSensitivity.Value;
            if (tbReleaseDelay != null) settings.ReleaseDelayMs = tbReleaseDelay.Value;
            if (togSmoothFade != null) settings.SmoothFade = togSmoothFade.Checked;
            if (togMinimizeToTray != null) settings.MinimizeToTray = togMinimizeToTray.Checked;
            if (togStartWithWindows != null) settings.StartWithWindows = togStartWithWindows.Checked;
            if (togStartMinimized != null) settings.StartMinimized = togStartMinimized.Checked;
            if (togDuckMic != null) settings.DuckOnMicrophone = togDuckMic.Checked;

            settings.Save();
        }

        public void AddTargetProcess(string appName)
        {
            if (string.IsNullOrWhiteSpace(appName)) return;
            string target = appName.Trim();
            if (target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                target = target.Substring(0, target.Length - 4).Trim();
            }

            bool exists = false;
            foreach (var t in settings.TargetApps)
            {
                if (string.Equals(t, target, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }

            if (!exists)
            {
                settings.TargetApps.Add(target);
            }

            engine.SetTargetProcesses(settings.TargetApps);
            UpdateTriggerChipsTargetState();
            RebuildTargetChips();
            UpdateStatusBadge();
            SaveCurrentSettings();
        }

        public void RemoveTargetProcess(string appName)
        {
            if (string.IsNullOrWhiteSpace(appName)) return;
            string target = appName.Trim();

            settings.TargetApps.RemoveAll(x => string.Equals(x, target, StringComparison.OrdinalIgnoreCase));
            engine.SetTargetProcesses(settings.TargetApps);
            UpdateTriggerChipsTargetState();
            RebuildTargetChips();
            UpdateStatusBadge();
            SaveCurrentSettings();
        }

        public void SwitchTargetProcess(string newTarget)
        {
            AddTargetProcess(newTarget);
        }

        private void UpdateTriggerChipsTargetState()
        {
            if (flpChips == null) return;
            var targetSet = new HashSet<string>(settings.TargetApps, StringComparer.OrdinalIgnoreCase);
            foreach (Control c in flpChips.Controls)
            {
                var chip = c as AppChip;
                if (chip != null)
                {
                    chip.SetActiveTarget(targetSet.Contains(chip.AppName));
                }
            }
        }

        public void RebuildTargetChips()
        {
            if (flpTargetChips == null) return;

            flpTargetChips.SuspendLayout();
            flpTargetChips.Controls.Clear();

            if (settings.TargetApps.Count == 0)
            {
                flpTargetChips.Controls.Add(lblEmptyTargetChips);
            }
            else
            {
                foreach (var app in settings.TargetApps)
                {
                    var chip = new TargetAppChip(app);
                    chip.OnRemove += (removedApp) => RemoveTargetProcess(removedApp);
                    flpTargetChips.Controls.Add(chip);
                }
            }

            flpTargetChips.ResumeLayout(true);
            RebuildQuickTargetChips();
            UpdateDynamicLayout();
        }

        public List<string> GetRecentTargetCandidates()
        {
            var list = new List<string>();
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string[] common = { "ytm-tauri", "Spotify", "msedge", "msedgewebview2", "chrome", "vlc", "brave", "firefox" };
            foreach (var c in common)
            {
                if (!set.Contains(c))
                {
                    try
                    {
                        if (Process.GetProcessesByName(c).Length > 0 || allTargetApps.Contains(c))
                        {
                            list.Add(c);
                            set.Add(c);
                        }
                    }
                    catch { }
                }
            }

            // Active WASAPI Audio Streams
            try
            {
                var enumerator = (IMMDeviceEnumerator)(new MMDeviceEnumerator());
                try
                {
                    var wasapiApps = ProcessHelper.GetActiveAudioProcessNames(enumerator);
                    foreach (var a in wasapiApps)
                    {
                        if (!set.Contains(a))
                        {
                            list.Add(a);
                            set.Add(a);
                        }
                    }
                }
                finally
                {
                    try { Marshal.ReleaseComObject(enumerator); } catch { }
                }
            }
            catch { }

            return list;
        }

        public void RebuildQuickTargetChips()
        {
            if (flpQuickTargets == null) return;

            flpQuickTargets.SuspendLayout();
            flpQuickTargets.Controls.Clear();

            var candidates = GetRecentTargetCandidates();
            var currentTargets = new HashSet<string>(settings.TargetApps, StringComparer.OrdinalIgnoreCase);

            int availableWidth = flpQuickTargets.Width > 0 ? flpQuickTargets.Width : 448;
            int currentWidth = 0;

            foreach (var candidate in candidates)
            {
                if (currentTargets.Contains(candidate)) continue; // Already an active target!

                var sChip = new QuickSuggestChip(candidate);
                sChip.OnAdd += (appName) => AddTargetProcess(appName);

                int chipTotalWidth = sChip.Width + sChip.Margin.Right;
                if (currentWidth + chipTotalWidth > availableWidth && currentWidth > 0)
                {
                    sChip.Dispose();
                    break;
                }

                flpQuickTargets.Controls.Add(sChip);
                currentWidth += chipTotalWidth;
            }

            flpQuickTargets.ResumeLayout(true);
        }

        private void PopulateTrayTargetMenu()
        {
            if (targetSubMenu == null) return;

            targetSubMenu.DropDownItems.Clear();
            var candidates = GetRecentTargetCandidates();
            var targetSet = new HashSet<string>(settings.TargetApps, StringComparer.OrdinalIgnoreCase);

            foreach (var app in candidates)
            {
                string appName = app;
                bool isCurrent = targetSet.Contains(appName);
                string text = isCurrent ? (appName + "  ✔ (Active)") : appName;

                var item = new ToolStripMenuItem(text);
                item.Image = ProcessHelper.GetProcessIcon(appName);
                if (isCurrent)
                {
                    item.Font = new Font(targetSubMenu.Font, FontStyle.Bold);
                }

                item.Click += (s, e) =>
                {
                    if (targetSet.Contains(appName))
                    {
                        RemoveTargetProcess(appName);
                    }
                    else
                    {
                        AddTargetProcess(appName);
                    }
                };
                targetSubMenu.DropDownItems.Add(item);
            }

            if (targetSubMenu.DropDownItems.Count == 0)
            {
                targetSubMenu.DropDownItems.Add(new ToolStripMenuItem("No audio apps detected") { Enabled = false });
            }
        }

        private void UpdateStatusBadge()
        {
            if (lblStatusBadge == null) return;

            if (!engine.IsActive && !engine.IsPreviewing)
            {
                lblStatusBadge.Text = "Status: Stopped";
                lblStatusBadge.ForeColor = Color.FromArgb(139, 148, 158);
                return;
            }

            var trigs = engine.GetTriggerProcesses();
            var targets = engine.GetTargetProcesses();
            string targetText = targets.Length > 0 ? string.Join(", ", targets) : "None";

            var targetSet = new HashSet<string>(targets, StringComparer.OrdinalIgnoreCase);
            var monitored = new List<string>();
            foreach (var t in trigs)
            {
                if (targetSet.Contains(t))
                {
                    monitored.Add(t + " (Bypassed)");
                }
                else
                {
                    monitored.Add(t);
                }
            }

            if (monitored.Count == 0)
            {
                lblStatusBadge.Text = string.Format("ACTIVE: Targets [{0}] • Waiting for priority apps...", targetText);
            }
            else
            {
                lblStatusBadge.Text = string.Format("ACTIVE: Targets [{0}] • Monitoring: {1}", targetText, string.Join(", ", monitored));
            }
            lblStatusBadge.ForeColor = Color.FromArgb(52, 211, 153);
        }
    }
}
