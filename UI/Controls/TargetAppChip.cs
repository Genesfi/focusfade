using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using FocusFade.Core;

namespace FocusFade.UI.Controls
{
    public class TargetAppChip : Panel
    {
        public string AppName { get; private set; }
        public event Action<string> OnRemove;

        private PictureBox pbIcon;
        private Label lblName;
        private Label btnClose;
        private ToolTip toolTip;
        private bool isHovered = false;

        private static readonly Color BgNormal = Color.FromArgb(28, 22, 42); // Indigo/Purple Dark
        private static readonly Color BgHover = Color.FromArgb(42, 33, 64);
        private static readonly Color BorderNormal = Color.FromArgb(76, 56, 120);
        private static readonly Color BorderHover = Color.FromArgb(168, 85, 247); // Violet Glow
        private static readonly Color TextColor = Color.FromArgb(238, 242, 255);

        public TargetAppChip(string name)
        {
            AppName = name;

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Height = 28;
            Padding = new Padding(6, 4, 6, 4);
            Margin = new Padding(0, 0, 6, 6);
            Cursor = Cursors.Default;
            toolTip = new ToolTip();

            int currentX = 6;

            // App Icon
            Image icon = ProcessHelper.GetProcessIcon(name);
            if (icon != null)
            {
                pbIcon = new PictureBox
                {
                    Image = icon,
                    Size = new Size(16, 16),
                    Location = new Point(currentX, 6),
                    SizeMode = PictureBoxSizeMode.Zoom
                };
                Controls.Add(pbIcon);
                currentX += 20;
            }

            // Target App Name Label
            lblName = new Label
            {
                Text = AppName,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = TextColor,
                AutoSize = false,
                Location = new Point(currentX, 5)
            };
            Controls.Add(lblName);

            using (var f = new Font("Segoe UI", 8.5f, FontStyle.Bold))
            {
                Size sz = TextRenderer.MeasureText(AppName, f);
                lblName.Size = new Size(sz.Width + 2, 18);
            }

            // Close (Remove) Button
            btnClose = new Label
            {
                Text = "×",
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Color.FromArgb(148, 163, 184),
                AutoSize = false,
                Size = new Size(16, 18),
                Location = new Point(lblName.Right + 4, 4),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            btnClose.MouseEnter += (s, e) => btnClose.ForeColor = Color.FromArgb(239, 68, 68);
            btnClose.MouseLeave += (s, e) => btnClose.ForeColor = Color.FromArgb(148, 163, 184);
            btnClose.Click += (s, e) =>
            {
                if (OnRemove != null) OnRemove(AppName);
            };
            Controls.Add(btnClose);

            this.Size = new Size(btnClose.Right + 6, 28);

            string tip = string.Format("Background Music Target: {0}\nClick × to remove this app.", AppName);
            toolTip.SetToolTip(this, tip);
            toolTip.SetToolTip(lblName, tip);
            toolTip.SetToolTip(btnClose, tip);

            MouseEnter += (s, e) => { isHovered = true; Invalidate(); };
            MouseLeave += (s, e) => { isHovered = false; Invalidate(); };
        }

        public void RefreshIcon()
        {
            try
            {
                if (pbIcon != null)
                {
                    Image icon = ProcessHelper.GetProcessIcon(AppName);
                    if (icon != null && pbIcon.Image != icon)
                    {
                        pbIcon.Image = icon;
                        pbIcon.Invalidate();
                    }
                }
            }
            catch { }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = UiHelper.GetRoundPath(rect, 6))
            {
                using (var b = new SolidBrush(isHovered ? BgHover : BgNormal))
                {
                    e.Graphics.FillPath(b, path);
                }
                using (var pen = new Pen(isHovered ? BorderHover : BorderNormal, 1.2f))
                {
                    e.Graphics.DrawPath(pen, path);
                }
            }
            base.OnPaint(e);
        }
    }
}
