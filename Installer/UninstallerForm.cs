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
    public class UninstallerForm : Form
    {
        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HT_CAPTION = 0x2;

        private Panel pnlHeader;
        private Label lblTitle;
        private Label btnClose;
        private Panel pnlContent;

        private Panel pnlConfirm;
        private Panel pnlProgress;
        private Panel pnlFinish;

        private ProgressBar progressBar;
        private Label lblStatus;

        private string installDir;

        public UninstallerForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(480, 260);
            BackColor = Color.FromArgb(15, 19, 28);
            ForeColor = Color.FromArgb(230, 237, 243);
            Font = new Font("Segoe UI", 9f, FontStyle.Regular);
            DoubleBuffered = true;

            installDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');

            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using (var stream = asm.GetManifestResourceStream("app.ico"))
                {
                    if (stream != null) this.Icon = new Icon(stream);
                }
            }
            catch { }

            BuildUI();
        }

        private void BuildUI()
        {
            Controls.Clear();

            pnlHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 50,
                BackColor = Color.FromArgb(22, 27, 39)
            };
            pnlHeader.MouseDown += (s, e) => DragWindow();
            Controls.Add(pnlHeader);

            lblTitle = new Label
            {
                Text = "Uninstall FocusFade",
                Font = new Font("Segoe UI", 11.5f, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(18, 14),
                AutoSize = true
            };
            lblTitle.MouseDown += (s, e) => DragWindow();
            pnlHeader.Controls.Add(lblTitle);

            btnClose = new Label
            {
                Text = "×",
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Color.FromArgb(148, 163, 184),
                Size = new Size(20, 20),
                Location = new Point(448, 12),
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter
            };
            btnClose.MouseEnter += (s, e) => btnClose.ForeColor = Color.FromArgb(239, 68, 68);
            btnClose.MouseLeave += (s, e) => btnClose.ForeColor = Color.FromArgb(148, 163, 184);
            btnClose.Click += (s, e) => Close();
            pnlHeader.Controls.Add(btnClose);

            pnlContent = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(22, 16, 22, 16)
            };
            Controls.Add(pnlContent);

            InitConfirmStep();
            InitProgressStep();
            InitFinishStep();

            ShowStep(1);
        }

        private void InitConfirmStep()
        {
            pnlConfirm = new Panel { Dock = DockStyle.Fill };
            pnlContent.Controls.Add(pnlConfirm);

            var lblPrompt = new Label
            {
                Text = "Are you sure you want to completely uninstall FocusFade and remove all of its components from your computer?",
                Location = new Point(0, 10),
                Size = new Size(436, 48),
                ForeColor = Color.FromArgb(226, 232, 240)
            };
            pnlConfirm.Controls.Add(lblPrompt);

            var lblLocation = new Label
            {
                Text = "Location: " + installDir,
                Location = new Point(0, 68),
                Size = new Size(436, 24),
                Font = new Font("Segoe UI", 8f),
                ForeColor = Color.FromArgb(148, 163, 184)
            };
            pnlConfirm.Controls.Add(lblLocation);

            var btnCancel = new Button
            {
                Text = "Cancel",
                Location = new Point(236, 125),
                Size = new Size(95, 34),
                BackColor = Color.FromArgb(37, 43, 67),
                ForeColor = Color.FromArgb(224, 231, 255),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnCancel.FlatAppearance.BorderColor = Color.FromArgb(56, 68, 96);
            btnCancel.Click += (s, e) => Close();
            pnlConfirm.Controls.Add(btnCancel);

            var btnUninstall = new Button
            {
                Text = "Uninstall",
                Location = new Point(341, 125),
                Size = new Size(95, 34),
                BackColor = Color.FromArgb(239, 68, 68),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnUninstall.FlatAppearance.BorderSize = 0;
            btnUninstall.Click += (s, e) => StartUninstall();
            pnlConfirm.Controls.Add(btnUninstall);
        }

        private void InitProgressStep()
        {
            pnlProgress = new Panel { Dock = DockStyle.Fill, Visible = false };
            pnlContent.Controls.Add(pnlProgress);

            var lblProgTitle = new Label
            {
                Text = "Uninstalling FocusFade...",
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(0, 20),
                AutoSize = true
            };
            pnlProgress.Controls.Add(lblProgTitle);

            progressBar = new ProgressBar
            {
                Location = new Point(0, 56),
                Size = new Size(436, 16),
                Style = ProgressBarStyle.Continuous,
                Minimum = 0,
                Maximum = 100,
                Value = 0
            };
            pnlProgress.Controls.Add(progressBar);

            lblStatus = new Label
            {
                Text = "Removing components...",
                Location = new Point(2, 82),
                Size = new Size(434, 24),
                ForeColor = Color.FromArgb(148, 163, 184)
            };
            pnlProgress.Controls.Add(lblStatus);
        }

        private void InitFinishStep()
        {
            pnlFinish = new Panel { Dock = DockStyle.Fill, Visible = false };
            pnlContent.Controls.Add(pnlFinish);

            var lblSuccess = new Label
            {
                Text = "FocusFade Has Been Removed",
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                ForeColor = Color.FromArgb(52, 211, 153),
                Location = new Point(0, 24),
                AutoSize = true
            };
            pnlFinish.Controls.Add(lblSuccess);

            var lblFinishDesc = new Label
            {
                Text = "FocusFade and its shortcuts were successfully removed from your system.",
                Location = new Point(2, 58),
                Size = new Size(434, 40),
                ForeColor = Color.FromArgb(203, 213, 225)
            };
            pnlFinish.Controls.Add(lblFinishDesc);

            var btnCloseFinish = new Button
            {
                Text = "Close",
                Location = new Point(341, 125),
                Size = new Size(95, 34),
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnCloseFinish.FlatAppearance.BorderSize = 0;
            btnCloseFinish.Click += (s, e) =>
            {
                // Self-delete install directory after exit
                PerformSelfDelete();
                Close();
            };
            pnlFinish.Controls.Add(btnCloseFinish);
        }

        private void ShowStep(int step)
        {
            pnlConfirm.Visible = (step == 1);
            pnlProgress.Visible = (step == 2);
            pnlFinish.Visible = (step == 3);
        }

        private void StartUninstall()
        {
            ShowStep(2);

            var thread = new Thread(() =>
            {
                try
                {
                    UpdateStatus("Closing running instances...", 20);
                    foreach (var p in Process.GetProcessesByName("FocusFade"))
                    {
                        try { p.Kill(); p.WaitForExit(1500); } catch { }
                    }

                    UpdateStatus("Removing shortcuts...", 40);
                    try
                    {
                        string desktopLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "FocusFade.lnk");
                        if (File.Exists(desktopLnk)) File.Delete(desktopLnk);

                        string startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "FocusFade");
                        if (Directory.Exists(startMenu)) Directory.Delete(startMenu, true);
                    }
                    catch { }

                    UpdateStatus("Removing registry configuration...", 70);
                    try
                    {
                        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                        {
                            if (key != null) key.DeleteValue("FocusFade", false);
                        }
                    }
                    catch { }

                    try
                    {
                        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\FocusFade", false);
                    }
                    catch { }

                    UpdateStatus("Cleaning files...", 90);
                    try
                    {
                        string[] filesToDelete = { "FocusFade.exe", "app.ico", "GUIDE.txt", "focusfade_settings.ini" };
                        foreach (var f in filesToDelete)
                        {
                            string p = Path.Combine(installDir, f);
                            if (File.Exists(p))
                            {
                                try { File.Delete(p); } catch { }
                            }
                        }
                    }
                    catch { }

                    Thread.Sleep(300);
                    UpdateStatus("Finished!", 100);

                    Invoke(new Action(() => ShowStep(3)));
                }
                catch (Exception ex)
                {
                    Invoke(new Action(() =>
                    {
                        MessageBox.Show("Uninstallation encountered an issue:\n" + ex.Message, "Notice", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        ShowStep(3);
                    }));
                }
            });
            thread.IsBackground = true;
            thread.Start();
        }

        private void PerformSelfDelete()
        {
            try
            {
                // Launch background cmd process to delete the directory after this process exits
                string currentExe = Application.ExecutablePath;
                string cmdArgs = string.Format("/c timeout /t 1 /nobreak >nul & del /f /q \"{0}\" & rmdir /s /q \"{1}\"", currentExe, installDir);
                var psi = new ProcessStartInfo("cmd.exe", cmdArgs)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(psi);
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
            lblStatus.Text = status;
            progressBar.Value = Math.Max(0, Math.Min(100, progress));
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
