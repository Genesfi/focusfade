using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace FocusFade.Installer
{
    public class InstallerForm : Form
    {
        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HT_CAPTION = 0x2;

        private Panel pnlHeader;
        private Label lblAppTitle;
        private Label lblAppSubtitle;
        private PictureBox pbAppIcon;
        private Label btnClose;
        private Label btnMinimize;

        private Panel pnlContent;

        // Step 1: Options
        private Panel pnlStepOptions;
        private TextBox txtInstallPath;
        private Button btnBrowse;
        private CheckBox chkDesktopShortcut;
        private CheckBox chkStartMenu;
        private CheckBox chkAutoStart;
        private Button btnInstall;
        private Label lblSpaceInfo;

        // Step 2: Progress
        private Panel pnlStepProgress;
        private ProgressBar progressBar;
        private Label lblProgressStatus;

        // Step 3: Finished
        private Panel pnlStepFinish;
        private CheckBox chkLaunchNow;
        private Button btnFinish;

        private string targetInstallPath;

        public InstallerForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(540, 430);
            BackColor = Color.FromArgb(15, 19, 28);
            ForeColor = Color.FromArgb(230, 237, 243);
            Font = new Font("Segoe UI", 9f, FontStyle.Regular);
            DoubleBuffered = true;

            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using (var stream = asm.GetManifestResourceStream("app.ico"))
                {
                    if (stream != null) this.Icon = new Icon(stream);
                }
            }
            catch { }

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            targetInstallPath = Path.Combine(localAppData, "FocusFade");

            BuildUI();
        }

        private void BuildUI()
        {
            Controls.Clear();

            // Header Panel
            pnlHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 70,
                BackColor = Color.FromArgb(22, 27, 39)
            };
            pnlHeader.MouseDown += (s, e) => DragWindow();
            Controls.Add(pnlHeader);

            // Icon
            pbAppIcon = new PictureBox
            {
                Size = new Size(36, 36),
                Location = new Point(18, 17),
                SizeMode = PictureBoxSizeMode.Zoom
            };
            try
            {
                if (this.Icon != null) pbAppIcon.Image = this.Icon.ToBitmap();
            }
            catch { }
            pbAppIcon.MouseDown += (s, e) => DragWindow();
            pnlHeader.Controls.Add(pbAppIcon);

            // Title
            lblAppTitle = new Label
            {
                Text = "FocusFade Setup",
                Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(62, 14),
                AutoSize = true
            };
            lblAppTitle.MouseDown += (s, e) => DragWindow();
            pnlHeader.Controls.Add(lblAppTitle);

            // Subtitle
            lblAppSubtitle = new Label
            {
                Text = "Smart Audio Dimmer for Windows • v1.2.0",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(64, 38),
                AutoSize = true
            };
            lblAppSubtitle.MouseDown += (s, e) => DragWindow();
            pnlHeader.Controls.Add(lblAppSubtitle);

            // Close & Minimize
            btnClose = CreateTopButton("×", 508, 12, (s, e) => Close());
            btnMinimize = CreateTopButton("—", 480, 12, (s, e) => WindowState = FormWindowState.Minimized);
            pnlHeader.Controls.Add(btnClose);
            pnlHeader.Controls.Add(btnMinimize);

            // Main Content Area
            pnlContent = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(24, 18, 24, 18)
            };
            Controls.Add(pnlContent);

            InitStepOptions();
            InitStepProgress();
            InitStepFinish();

            ShowStep(1);
        }

        private void InitStepOptions()
        {
            pnlStepOptions = new Panel { Dock = DockStyle.Fill };
            pnlContent.Controls.Add(pnlStepOptions);

            var lblIntro = new Label
            {
                Text = "Install FocusFade to automatically duck background audio when priority apps make sound.",
                Location = new Point(0, 4),
                Size = new Size(490, 36),
                ForeColor = Color.FromArgb(203, 213, 225)
            };
            pnlStepOptions.Controls.Add(lblIntro);

            // Install Path Card
            var pnlPathCard = new Panel
            {
                Location = new Point(0, 44),
                Size = new Size(492, 70),
                BackColor = Color.FromArgb(22, 27, 39)
            };
            pnlPathCard.Paint += (s, e) => DrawSubtleBorder(e.Graphics, pnlPathCard.ClientRectangle);
            pnlStepOptions.Controls.Add(pnlPathCard);

            var lblPathHeader = new Label
            {
                Text = "Installation Folder:",
                Location = new Point(12, 10),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(148, 163, 184),
                AutoSize = true
            };
            pnlPathCard.Controls.Add(lblPathHeader);

            txtInstallPath = new TextBox
            {
                Text = targetInstallPath,
                Location = new Point(14, 32),
                Size = new Size(378, 24),
                BackColor = Color.FromArgb(13, 17, 23),
                ForeColor = Color.FromArgb(230, 237, 243),
                BorderStyle = BorderStyle.FixedSingle
            };
            pnlPathCard.Controls.Add(txtInstallPath);

            btnBrowse = new Button
            {
                Text = "Browse...",
                Location = new Point(400, 31),
                Size = new Size(78, 25),
                BackColor = Color.FromArgb(37, 43, 67),
                ForeColor = Color.FromArgb(224, 231, 255),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnBrowse.FlatAppearance.BorderColor = Color.FromArgb(56, 68, 96);
            btnBrowse.Click += (s, e) =>
            {
                using (var fbd = new FolderBrowserDialog())
                {
                    fbd.SelectedPath = txtInstallPath.Text;
                    fbd.Description = "Select Installation Folder for FocusFade";
                    if (fbd.ShowDialog() == DialogResult.OK)
                    {
                        txtInstallPath.Text = fbd.SelectedPath;
                    }
                }
            };
            pnlPathCard.Controls.Add(btnBrowse);

            // Shortcuts & Startup Options
            var pnlOptionsCard = new Panel
            {
                Location = new Point(0, 124),
                Size = new Size(492, 120),
                BackColor = Color.FromArgb(22, 27, 39)
            };
            pnlOptionsCard.Paint += (s, e) => DrawSubtleBorder(e.Graphics, pnlOptionsCard.ClientRectangle);
            pnlStepOptions.Controls.Add(pnlOptionsCard);

            var lblOptHeader = new Label
            {
                Text = "Preferences:",
                Location = new Point(12, 10),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(148, 163, 184),
                AutoSize = true
            };
            pnlOptionsCard.Controls.Add(lblOptHeader);

            chkDesktopShortcut = CreateCheckbox("Create Desktop shortcut", 14, 32, true);
            chkStartMenu = CreateCheckbox("Add to Start Menu programs", 14, 58, true);
            chkAutoStart = CreateCheckbox("Launch automatically when Windows starts (Minimized)", 14, 84, true);

            pnlOptionsCard.Controls.Add(chkDesktopShortcut);
            pnlOptionsCard.Controls.Add(chkStartMenu);
            pnlOptionsCard.Controls.Add(chkAutoStart);

            lblSpaceInfo = new Label
            {
                Text = "Space required: ~3.5 MB   •   Zero Python / Zero dependencies",
                Location = new Point(2, 254),
                ForeColor = Color.FromArgb(100, 116, 139),
                AutoSize = true
            };
            pnlStepOptions.Controls.Add(lblSpaceInfo);

            // Action Button
            btnInstall = new Button
            {
                Text = "Install FocusFade",
                Location = new Point(322, 285),
                Size = new Size(170, 38),
                BackColor = Color.FromArgb(99, 102, 241),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnInstall.FlatAppearance.BorderSize = 0;
            btnInstall.Click += (s, e) => StartInstallation();
            pnlStepOptions.Controls.Add(btnInstall);
        }

        private void InitStepProgress()
        {
            pnlStepProgress = new Panel { Dock = DockStyle.Fill, Visible = false };
            pnlContent.Controls.Add(pnlStepProgress);

            var lblProgTitle = new Label
            {
                Text = "Installing FocusFade...",
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(0, 50),
                AutoSize = true
            };
            pnlStepProgress.Controls.Add(lblProgTitle);

            progressBar = new ProgressBar
            {
                Location = new Point(0, 90),
                Size = new Size(492, 18),
                Style = ProgressBarStyle.Continuous,
                Minimum = 0,
                Maximum = 100,
                Value = 0
            };
            pnlStepProgress.Controls.Add(progressBar);

            lblProgressStatus = new Label
            {
                Text = "Preparing installation...",
                Location = new Point(2, 118),
                Size = new Size(490, 24),
                ForeColor = Color.FromArgb(148, 163, 184)
            };
            pnlStepProgress.Controls.Add(lblProgressStatus);
        }

        private void InitStepFinish()
        {
            pnlStepFinish = new Panel { Dock = DockStyle.Fill, Visible = false };
            pnlContent.Controls.Add(pnlStepFinish);

            var pbSuccess = new PictureBox
            {
                Location = new Point(0, 30),
                Size = new Size(48, 48),
                SizeMode = PictureBoxSizeMode.CenterImage
            };
            pbSuccess.Image = CreateCheckmarkIcon(44);
            pnlStepFinish.Controls.Add(pbSuccess);

            var lblFinishTitle = new Label
            {
                Text = "FocusFade Successfully Installed!",
                Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                ForeColor = Color.FromArgb(52, 211, 153),
                Location = new Point(56, 32),
                AutoSize = true
            };
            pnlStepFinish.Controls.Add(lblFinishTitle);

            var lblFinishDesc = new Label
            {
                Text = "FocusFade is now installed and ready to intelligently dim your background audio.\nYou can control it from the System Tray or using the floating PiP HUD.",
                Location = new Point(58, 64),
                Size = new Size(430, 48),
                ForeColor = Color.FromArgb(203, 213, 225)
            };
            pnlStepFinish.Controls.Add(lblFinishDesc);

            chkLaunchNow = CreateCheckbox("Launch FocusFade now", 58, 140, true);
            chkLaunchNow.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            chkLaunchNow.ForeColor = Color.FromArgb(199, 210, 254);
            pnlStepFinish.Controls.Add(chkLaunchNow);

            btnFinish = new Button
            {
                Text = "Finish",
                Location = new Point(352, 285),
                Size = new Size(140, 38),
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnFinish.FlatAppearance.BorderSize = 0;
            btnFinish.Click += (s, e) =>
            {
                if (chkLaunchNow.Checked)
                {
                    string exePath = Path.Combine(targetInstallPath, "FocusFade.exe");
                    if (File.Exists(exePath))
                    {
                        Process.Start(new ProcessStartInfo(exePath) { WorkingDirectory = targetInstallPath });
                    }
                }
                Close();
            };
            pnlStepFinish.Controls.Add(btnFinish);
        }

        private void ShowStep(int step)
        {
            pnlStepOptions.Visible = (step == 1);
            pnlStepProgress.Visible = (step == 2);
            pnlStepFinish.Visible = (step == 3);
        }

        private void StartInstallation()
        {
            targetInstallPath = txtInstallPath.Text.Trim();
            if (string.IsNullOrEmpty(targetInstallPath)) return;

            ShowStep(2);

            var thread = new Thread(() =>
            {
                try
                {
                    UpdateStatus("Closing running instances of FocusFade...", 10);
                    KillRunningInstances();

                    UpdateStatus("Creating destination directories...", 25);
                    Directory.CreateDirectory(targetInstallPath);

                    UpdateStatus("Extracting application binaries...", 45);
                    ExtractResourceToFile("FocusFade.exe", Path.Combine(targetInstallPath, "FocusFade.exe"));
                    ExtractResourceToFile("app.ico", Path.Combine(targetInstallPath, "app.ico"));
                    ExtractResourceToFile("GUIDE.txt", Path.Combine(targetInstallPath, "GUIDE.txt"));

                    UpdateStatus("Generating uninstaller...", 65);
                    // Copy current running installer as Uninstall.exe
                    string currentExe = Application.ExecutablePath;
                    string uninstallerDest = Path.Combine(targetInstallPath, "Uninstall.exe");
                    File.Copy(currentExe, uninstallerDest, true);

                    UpdateStatus("Configuring shortcuts & preferences...", 80);
                    string targetExe = Path.Combine(targetInstallPath, "FocusFade.exe");

                    if (chkDesktopShortcut.Checked)
                    {
                        string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                        string lnk = Path.Combine(desktopPath, "FocusFade.lnk");
                        ShortcutHelper.CreateShortcut(lnk, targetExe, "", "FocusFade — Smart Audio Dimmer", targetExe);
                    }

                    if (chkStartMenu.Checked)
                    {
                        string startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
                        string folder = Path.Combine(startMenu, "FocusFade");
                        Directory.CreateDirectory(folder);
                        string lnk = Path.Combine(folder, "FocusFade.lnk");
                        ShortcutHelper.CreateShortcut(lnk, targetExe, "", "FocusFade — Smart Audio Dimmer", targetExe);

                        string uninstallLnk = Path.Combine(folder, "Uninstall FocusFade.lnk");
                        ShortcutHelper.CreateShortcut(uninstallLnk, uninstallerDest, "/uninstall", "Uninstall FocusFade", uninstallerDest);
                    }

                    UpdateStatus("Registering in Windows Control Panel...", 90);
                    RegisterUninstaller(targetInstallPath, uninstallerDest, targetExe);

                    if (chkAutoStart.Checked)
                    {
                        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                        {
                            if (key != null) key.SetValue("FocusFade", "\"" + targetExe + "\" --minimized");
                        }
                    }

                    Thread.Sleep(300);
                    UpdateStatus("Installation complete!", 100);

                    Invoke(new Action(() => ShowStep(3)));
                }
                catch (Exception ex)
                {
                    Invoke(new Action(() =>
                    {
                        MessageBox.Show("Installation failed:\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        ShowStep(1);
                    }));
                }
            });
            thread.IsBackground = true;
            thread.Start();
        }

        private void KillRunningInstances()
        {
            try
            {
                foreach (var p in Process.GetProcessesByName("FocusFade"))
                {
                    try
                    {
                        p.Kill();
                        p.WaitForExit(2000);
                    }
                    catch { }
                }
            }
            catch { }
        }

        private void ExtractResourceToFile(string resourceName, string destPath)
        {
            var asm = Assembly.GetExecutingAssembly();
            using (var stream = asm.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    // Fallback to local files if running inside source tree
                    string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, resourceName);
                    if (File.Exists(local))
                    {
                        File.Copy(local, destPath, true);
                        return;
                    }
                    throw new FileNotFoundException("Embedded resource not found: " + resourceName);
                }

                using (var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write))
                {
                    stream.CopyTo(fs);
                }
            }
        }

        private void RegisterUninstaller(string installDir, string uninstallerPath, string targetExe)
        {
            try
            {
                string regPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\FocusFade";
                using (var key = Registry.CurrentUser.CreateSubKey(regPath))
                {
                    if (key != null)
                    {
                        key.SetValue("DisplayName", "FocusFade — Smart Audio Dimmer");
                        key.SetValue("DisplayVersion", "1.2.0");
                        key.SetValue("Publisher", "FocusFade Audio Systems");
                        key.SetValue("InstallLocation", installDir);
                        key.SetValue("DisplayIcon", targetExe + ",0");
                        key.SetValue("UninstallString", "\"" + uninstallerPath + "\" /uninstall");
                        key.SetValue("QuietUninstallString", "\"" + uninstallerPath + "\" /uninstall /silent");
                        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                        key.SetValue("EstimatedSize", 3500, RegistryValueKind.DWord);
                    }
                }
            }
            catch { }
        }

        private void UpdateStatus(string status, int progress)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => UpdateStatus(status, progress)));
                return;
            }
            lblProgressStatus.Text = status;
            progressBar.Value = Math.Max(0, Math.Min(100, progress));
        }

        private CheckBox CreateCheckbox(string text, int x, int y, bool isChecked)
        {
            return new CheckBox
            {
                Text = text,
                Location = new Point(x, y),
                AutoSize = true,
                Checked = isChecked,
                ForeColor = Color.FromArgb(226, 232, 240),
                Cursor = Cursors.Hand
            };
        }

        private Label CreateTopButton(string text, int x, int y, EventHandler onClick)
        {
            var btn = new Label
            {
                Text = text,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(148, 163, 184),
                Size = new Size(20, 20),
                Location = new Point(x, y),
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter
            };
            btn.MouseEnter += (s, e) => btn.ForeColor = text == "×" ? Color.FromArgb(239, 68, 68) : Color.White;
            btn.MouseLeave += (s, e) => btn.ForeColor = Color.FromArgb(148, 163, 184);
            btn.Click += onClick;
            return btn;
        }

        private void DrawSubtleBorder(Graphics g, Rectangle r)
        {
            using (var pen = new Pen(Color.FromArgb(40, 48, 66), 1f))
            {
                g.DrawRectangle(pen, 0, 0, r.Width - 1, r.Height - 1);
            }
        }

        private Bitmap CreateCheckmarkIcon(int size)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var rect = new Rectangle(2, 2, size - 5, size - 5);
                using (var brush = new SolidBrush(Color.FromArgb(16, 185, 129)))
                {
                    g.FillEllipse(brush, rect);
                }
                using (var pen = new Pen(Color.White, 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawLine(pen, size * 0.28f, size * 0.50f, size * 0.44f, size * 0.68f);
                    g.DrawLine(pen, size * 0.44f, size * 0.68f, size * 0.72f, size * 0.35f);
                }
            }
            return bmp;
        }

        private void DragWindow()
        {
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(Color.FromArgb(56, 68, 96), 1.2f))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
            base.OnPaint(e);
        }
    }
}
