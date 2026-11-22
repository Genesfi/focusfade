using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using FocusFade.Core;
using FocusFade.Mixing;
using FocusFade.Models;
using FocusFade.UI.Controls;

namespace FocusFade.UI
{
    public class PipOverlayForm : Form
    {
        private readonly MainForm mainForm;
        private readonly AudioEngine engine;
        private readonly AppSettings settings;

        private bool isVertical = false;
        private string lastRenderedTrigApp = "";
        private string lastRenderedTargApp = "";

        // UI Controls
        private Label btnOrient;
        private Label btnExpand;
        private Label btnClose;

        private PictureBox pbTrigIcon;
        private Label lblTriggerName;
        private ModernVuMeter vuTrigger;
        private Label lblTriggerVal;

        private PictureBox pbTargIcon;
        private Label lblTargetName;
        private ModernVuMeter vuTarget;
        private Label lblTargetVal;

        private ToolTip toolTip;
        private System.Windows.Forms.Timer updateTimer;

        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HT_CAPTION = 0x2;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        private const int WM_GETMINMAXINFO = 0x0024;
        private const int WM_DPICHANGED = 0x02E0;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW: floating HUD tool window
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_GETMINMAXINFO)
            {
                base.WndProc(ref m);
                try
                {
                    MINMAXINFO mmi = (MINMAXINFO)Marshal.PtrToStructure(m.LParam, typeof(MINMAXINFO));
                    int targetW = isVertical ? 64 : 280;
                    int targetH = isVertical ? 156 : 70;
                    mmi.ptMinTrackSize.x = targetW;
                    mmi.ptMinTrackSize.y = targetH;
                    mmi.ptMaxTrackSize.x = targetW;
                    mmi.ptMaxTrackSize.y = targetH;
                    Marshal.StructureToPtr(mmi, m.LParam, false);
                }
                catch { }
                return;
            }
            if (m.Msg == WM_DPICHANGED)
            {
                // Prevent multi-monitor DPI transitions from stretching our fixed-pixel HUD
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        public PipOverlayForm(MainForm main, AudioEngine eng, AppSettings cfg)
        {
            mainForm = main;
            engine = eng;
            settings = cfg;
            isVertical = cfg.PipIsVertical;

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;

            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            ShowInTaskbar = false;
            BackColor = Color.FromArgb(15, 19, 28);
            ForeColor = Color.FromArgb(230, 237, 243);
            Opacity = 0.88; // Sleek translucent glass HUD
            StartPosition = FormStartPosition.Manual;

            int defaultW = isVertical ? 64 : 280;
            int defaultH = isVertical ? 156 : 70;

            MinimumSize = Size.Empty;
            MaximumSize = Size.Empty;
            Size = new Size(defaultW, defaultH);
            MinimumSize = new Size(defaultW, defaultH);
            MaximumSize = new Size(defaultW, defaultH);

            // Multi-monitor aware positioning:
            // Check if saved coordinates intersect with any currently connected screen
            Screen targetScreen = null;
            if (settings.PipX >= 0 && settings.PipY >= 0)
            {
                var savedRect = new Rectangle(settings.PipX, settings.PipY, defaultW, defaultH);
                foreach (var s in Screen.AllScreens)
                {
                    if (s.WorkingArea.IntersectsWith(savedRect))
                    {
                        targetScreen = s;
                        break;
                    }
                }
            }

            if (targetScreen != null)
            {
                int clampedX = Math.Max(targetScreen.WorkingArea.Left + 8, Math.Min(settings.PipX, targetScreen.WorkingArea.Right - defaultW - 8));
                int clampedY = Math.Max(targetScreen.WorkingArea.Top + 8, Math.Min(settings.PipY, targetScreen.WorkingArea.Bottom - defaultH - 8));
                Location = new Point(clampedX, clampedY);
            }
            else
            {
                // Open on whichever screen the mouse cursor currently resides on!
                var workArea = Screen.FromPoint(Cursor.Position).WorkingArea;
                Location = new Point(workArea.Right - defaultW - 24, workArea.Bottom - defaultH - 40);
            }

            MouseDown += OnFormMouseDown;
            DoubleClick += (s, e) => RestoreMainWindow();

            toolTip = new ToolTip();
            BuildUI();

            updateTimer = new System.Windows.Forms.Timer { Interval = 50 }; // 20 FPS (Fluid & low CPU)
            updateTimer.Tick += UpdateTimer_Tick;
            updateTimer.Start();
        }

        private void ToggleOrientation()
        {
            isVertical = !isVertical;
            settings.PipIsVertical = isVertical;

            // Adjust position so it doesn't clip offscreen on the current monitor
            var screen = Screen.FromPoint(Cursor.Position).WorkingArea;
            int newW = isVertical ? 64 : 280;
            int newH = isVertical ? 156 : 70;
            int newX = Math.Min(Left, screen.Right - newW - 10);
            int newY = Math.Min(Top, screen.Bottom - newH - 10);
            Location = new Point(Math.Max(screen.Left + 10, newX), Math.Max(screen.Top + 10, newY));

            settings.PipX = Location.X;
            settings.PipY = Location.Y;
            settings.Save();

            BuildUI();
        }

        private void BuildUI()
        {
            Controls.Clear();
            lastRenderedTrigApp = "";
            lastRenderedTargApp = "";

            int targetW = isVertical ? 64 : 280;
            int targetH = isVertical ? 156 : 70;

            MinimumSize = Size.Empty;
            MaximumSize = Size.Empty;
            Size = new Size(targetW, targetH);
            MinimumSize = new Size(targetW, targetH);
            MaximumSize = new Size(targetW, targetH);

            if (!isVertical)
            {
                // ================= HORIZONTAL MODE (280 x 70) =================

                // Control buttons
                btnOrient = CreateMiniButton("⇄", 226, 4, (s, e) => ToggleOrientation());
                btnExpand = CreateMiniButton("⤢", 244, 4, (s, e) => RestoreMainWindow());
                btnClose = CreateMiniButton("×", 262, 3, (s, e) => Close());
                Controls.Add(btnOrient);
                Controls.Add(btnExpand);
                Controls.Add(btnClose);

                // Row 1: Priority Sound
                pbTrigIcon = new PictureBox { Size = new Size(16, 16), Location = new Point(8, 14), SizeMode = PictureBoxSizeMode.Zoom };
                pbTrigIcon.MouseDown += OnFormMouseDown;
                Controls.Add(pbTrigIcon);

                lblTriggerName = new Label
                {
                    Text = "TRIG",
                    Font = new Font("Segoe UI", 8.2f, FontStyle.Bold),
                    ForeColor = Color.FromArgb(148, 163, 184),
                    Location = new Point(28, 14),
                    Size = new Size(66, 16),
                    AutoEllipsis = true
                };
                lblTriggerName.MouseDown += OnFormMouseDown;
                Controls.Add(lblTriggerName);

                vuTrigger = new ModernVuMeter { Location = new Point(96, 18), Size = new Size(136, 8) };
                vuTrigger.MouseDown += OnFormMouseDown;
                Controls.Add(vuTrigger);

                lblTriggerVal = new Label
                {
                    Text = "0%",
                    Font = new Font("Segoe UI", 8.2f, FontStyle.Bold),
                    ForeColor = Color.FromArgb(148, 163, 184),
                    Location = new Point(236, 14),
                    Size = new Size(38, 16),
                    TextAlign = ContentAlignment.MiddleRight
                };
                lblTriggerVal.MouseDown += OnFormMouseDown;
                Controls.Add(lblTriggerVal);

                // Row 2: Background Music
                pbTargIcon = new PictureBox { Size = new Size(16, 16), Location = new Point(8, 40), SizeMode = PictureBoxSizeMode.Zoom, Cursor = Cursors.Hand };
                pbTargIcon.Click += (s, e) => ShowQuickTargetMenu(pbTargIcon);
                Controls.Add(pbTargIcon);

                lblTargetName = new Label
                {
                    Text = "BGM",
                    Font = new Font("Segoe UI", 8.2f, FontStyle.Bold),
                    ForeColor = Color.FromArgb(148, 163, 184),
                    Location = new Point(28, 40),
                    Size = new Size(66, 16),
                    AutoEllipsis = true,
                    Cursor = Cursors.Hand
                };
                lblTargetName.Click += (s, e) => ShowQuickTargetMenu(lblTargetName);
                Controls.Add(lblTargetName);

                vuTarget = new ModernVuMeter
                {
                    Location = new Point(96, 44),
                    Size = new Size(136, 8),
                    GradientStart = Color.FromArgb(59, 130, 246),
                    GradientMid = Color.FromArgb(139, 92, 246),
                    GradientEnd = Color.FromArgb(236, 72, 153)
                };
                vuTarget.MouseDown += OnFormMouseDown;
                Controls.Add(vuTarget);

                lblTargetVal = new Label
                {
                    Text = "100%",
                    Font = new Font("Segoe UI", 8.2f, FontStyle.Bold),
                    ForeColor = Color.FromArgb(129, 140, 248),
                    Location = new Point(236, 40),
                    Size = new Size(38, 16),
                    TextAlign = ContentAlignment.MiddleRight
                };
                lblTargetVal.MouseDown += OnFormMouseDown;
                Controls.Add(lblTargetVal);
            }
            else
            {
                // ================= ULTRA-SLIM VERTICAL PILL MODE (64 x 156) =================
                lblTriggerName = null;
                lblTargetName = null;

                // Control buttons centered at top: ⤢ (open dashboard), ⇄ (toggle orientation), × (close)
                btnExpand = CreateMiniButton("⤢", 7, 5, (s, e) => RestoreMainWindow());
                btnOrient = CreateMiniButton("⇄", 25, 5, (s, e) => ToggleOrientation());
                btnClose = CreateMiniButton("×", 43, 5, (s, e) => Close());
                toolTip.SetToolTip(btnExpand, "Open FocusFade Dashboard");
                toolTip.SetToolTip(btnOrient, "Switch Orientation");
                toolTip.SetToolTip(btnClose, "Close PiP");
                Controls.Add(btnExpand);
                Controls.Add(btnOrient);
                Controls.Add(btnClose);

                // Column 1 (Left - Center x=16): Priority Trigger App (e.g. AfterFX, Discord)
                pbTrigIcon = new PictureBox { Size = new Size(16, 16), Location = new Point(8, 24), SizeMode = PictureBoxSizeMode.Zoom };
                pbTrigIcon.MouseDown += OnFormMouseDown;
                Controls.Add(pbTrigIcon);

                vuTrigger = new ModernVuMeter { Location = new Point(12, 44), Size = new Size(8, 84) };
                vuTrigger.MouseDown += OnFormMouseDown;
                Controls.Add(vuTrigger);

                lblTriggerVal = new Label
                {
                    Text = "0%",
                    Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
                    ForeColor = Color.FromArgb(148, 163, 184),
                    Location = new Point(0, 132),
                    Size = new Size(32, 16),
                    TextAlign = ContentAlignment.MiddleCenter
                };
                lblTriggerVal.MouseDown += OnFormMouseDown;
                Controls.Add(lblTriggerVal);

                // Column 2 (Right - Center x=48): Target Music App (e.g. Edge, Spotify)
                pbTargIcon = new PictureBox { Size = new Size(16, 16), Location = new Point(40, 24), SizeMode = PictureBoxSizeMode.Zoom, Cursor = Cursors.Hand };
                pbTargIcon.Click += (s, e) => ShowQuickTargetMenu(pbTargIcon);
                Controls.Add(pbTargIcon);

                vuTarget = new ModernVuMeter
                {
                    Location = new Point(44, 44),
                    Size = new Size(8, 84),
                    GradientStart = Color.FromArgb(59, 130, 246),
                    GradientMid = Color.FromArgb(139, 92, 246),
                    GradientEnd = Color.FromArgb(236, 72, 153)
                };
                vuTarget.MouseDown += OnFormMouseDown;
                Controls.Add(vuTarget);

                lblTargetVal = new Label
                {
                    Text = "100%",
                    Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
                    ForeColor = Color.FromArgb(129, 140, 248),
                    Location = new Point(32, 132),
                    Size = new Size(32, 16),
                    TextAlign = ContentAlignment.MiddleCenter
                };
                lblTargetVal.MouseDown += OnFormMouseDown;
                Controls.Add(lblTargetVal);
            }

            // Allow double-clicking ANY element in PiP to restore dashboard
            foreach (Control c in Controls)
            {
                if (c != btnOrient && c != btnClose && c != btnExpand)
                {
                    c.DoubleClick += (s, e) => RestoreMainWindow();
                }
            }

            Invalidate();
        }

        private Label CreateMiniButton(string text, int x, int y, EventHandler onClick)
        {
            var btn = new Label
            {
                Text = text,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(100, 116, 139),
                Size = new Size(16, 14),
                Location = new Point(x, y),
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter
            };
            btn.MouseEnter += (s, e) => btn.ForeColor = text == "×" ? Color.FromArgb(239, 68, 68) : Color.White;
            btn.MouseLeave += (s, e) => btn.ForeColor = Color.FromArgb(100, 116, 139);
            btn.Click += onClick;
            return btn;
        }

        private void OnFormMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
                settings.PipX = Left;
                settings.PipY = Top;
                settings.Save();
            }
        }

        private void RestoreMainWindow()
        {
            mainForm.ShowDashboard();
        }

        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            // Determine active trigger display name
            string activeTrig = engine.ActiveTriggerName;
            if (string.IsNullOrEmpty(activeTrig))
            {
                var trigs = engine.GetTriggerProcesses();
                activeTrig = trigs.Length == 1 ? trigs[0] : (trigs.Length > 1 ? "Priority" : "None");
            }

            var targets = engine.GetTargetProcesses();
            string targApp = targets.Length == 1 ? targets[0] : (targets.Length > 1 ? string.Format("{0} +{1}", targets[0], targets.Length - 1) : "Music");

            // Update Labels if present (horizontal mode)
            if (lblTriggerName != null && lblTriggerName.Text != activeTrig) lblTriggerName.Text = activeTrig;
            if (lblTargetName != null && lblTargetName.Text != targApp) lblTargetName.Text = targApp;

            // Dynamically load real app icons when app changes
            if (activeTrig != lastRenderedTrigApp)
            {
                lastRenderedTrigApp = activeTrig;
                string lookupName = activeTrig;
                if (lookupName == "Priority" || lookupName == "None")
                {
                    var trigs = engine.GetTriggerProcesses();
                    if (trigs.Length > 0) lookupName = trigs[0];
                }
                pbTrigIcon.Image = ProcessHelper.GetProcessIcon(lookupName);
            }

            if (targApp != lastRenderedTargApp)
            {
                lastRenderedTargApp = targApp;
                string iconName = targets.Length > 0 ? targets[0] : "";
                pbTargIcon.Image = ProcessHelper.GetProcessIcon(iconName);
            }

            // Meter 1: Trigger Peak
            float tPeak = engine.TriggerPeak;
            vuTrigger.Level = tPeak;
            lblTriggerVal.Text = string.Format("{0}%", (int)(tPeak * 100));

            // Meter 2: Target Volume
            float mVol = engine.CurrentTargetVolume;
            vuTarget.Level = mVol;
            lblTargetVal.Text = string.Format("{0}%", (int)(mVol * 100));

            // Visual feedback when ducking / active
            if (engine.IsDucked)
            {
                lblTargetVal.ForeColor = Color.FromArgb(245, 158, 11); // Amber
                if (lblTargetName != null) lblTargetName.ForeColor = Color.FromArgb(245, 158, 11);
                if (lblTriggerName != null) lblTriggerName.ForeColor = Color.FromArgb(52, 211, 153); // Emerald
            }
            else
            {
                lblTargetVal.ForeColor = Color.FromArgb(129, 140, 248); // Indigo
                if (lblTargetName != null) lblTargetName.ForeColor = Color.FromArgb(148, 163, 184);
                if (lblTriggerName != null) lblTriggerName.ForeColor = Color.FromArgb(148, 163, 184);
            }

            // Sleek tooltips for slim vertical mode
            string trigTip = string.Format("Priority: {0} ({1}%)", activeTrig, (int)(tPeak * 100));
            string targListStr = targets.Length > 0 ? string.Join(", ", targets) : "None";
            string targTip = string.Format("Music ({0}%): {1}\nClick icon to toggle targets.", (int)(mVol * 100), targListStr);
            toolTip.SetToolTip(pbTrigIcon, trigTip);
            toolTip.SetToolTip(vuTrigger, trigTip);
            toolTip.SetToolTip(lblTriggerVal, trigTip);
            toolTip.SetToolTip(pbTargIcon, targTip);
            toolTip.SetToolTip(vuTarget, targTip);
            toolTip.SetToolTip(lblTargetVal, targTip);
        }

        private void ShowQuickTargetMenu(Control anchor)
        {
            if (anchor == null || mainForm == null) return;
            try
            {
                var cms = new ContextMenuStrip();
                cms.BackColor = Color.FromArgb(22, 27, 34);
                cms.ForeColor = Color.FromArgb(230, 237, 243);
                cms.ShowImageMargin = true;

                var headerItem = new ToolStripMenuItem("Toggle Background Targets") { Enabled = false };
                headerItem.Font = new Font(cms.Font, FontStyle.Bold);
                cms.Items.Add(headerItem);
                cms.Items.Add(new ToolStripSeparator());

                var candidates = mainForm.GetRecentTargetCandidates();
                var targets = engine.GetTargetProcesses();
                var targetSet = new HashSet<string>(targets, StringComparer.OrdinalIgnoreCase);

                foreach (var app in candidates)
                {
                    string appName = app;
                    bool isCurrent = targetSet.Contains(appName);
                    string title = isCurrent ? (appName + "  ✔ (Active)") : appName;

                    var item = new ToolStripMenuItem(title);
                    item.Image = ProcessHelper.GetProcessIcon(appName);
                    if (isCurrent) item.Font = new Font(cms.Font, FontStyle.Bold);

                    item.Click += (s, e) =>
                    {
                        if (targetSet.Contains(appName))
                        {
                            mainForm.RemoveTargetProcess(appName);
                        }
                        else
                        {
                            mainForm.AddTargetProcess(appName);
                        }
                    };
                    cms.Items.Add(item);
                }

                cms.Show(anchor, new Point(0, anchor.Height + 2));
            }
            catch { }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = isVertical ? 14 : 9;
            using (var path = UiHelper.GetRoundPath(rect, radius))
            {
                using (var b = new SolidBrush(BackColor))
                {
                    e.Graphics.FillPath(b, path);
                }
                using (var pen = new Pen(Color.FromArgb(48, 54, 61), 1.2f))
                {
                    e.Graphics.DrawPath(pen, path);
                }
            }
            base.OnPaint(e);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            settings.PipX = Left;
            settings.PipY = Top;
            settings.Save();
            base.OnFormClosing(e);
        }
    }
}
