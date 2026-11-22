using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using FocusFade.Core;

namespace FocusFade.UI.Controls
{
    public class AppChip : Panel
    {
        public string AppName { get; private set; }
        public int ThresholdPercent { get; private set; }
        public bool IsSelected { get; set; }

        public bool IsActiveTarget { get; private set; }

        public event Action<string> OnRemove;
        public event Action<string> OnSelect;

        private PictureBox pbIcon;
        private Label lblName;
        private Label btnClose;
        private ToolTip toolTip;

        private static readonly Color BgNormal = Color.FromArgb(24, 30, 46);
        private static readonly Color BgSelected = Color.FromArgb(42, 50, 80);
        private static readonly Color BgTarget = Color.FromArgb(48, 38, 76);
        private static readonly Color BorderNormal = Color.FromArgb(56, 68, 96);
        private static readonly Color BorderSelected = Color.FromArgb(99, 102, 241);
        private static readonly Color BorderTarget = Color.FromArgb(168, 85, 247);

        public AppChip(string name, int threshold = 20, bool selected = false, bool isTarget = false)
        {
            AppName = name;
            ThresholdPercent = threshold;
            IsSelected = selected;
            IsActiveTarget = isTarget;

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Height = 28;
            Padding = new Padding(6, 4, 6, 4);
            Margin = new Padding(0, 0, 8, 6);
            BackColor = GetCurrentBgColor();
            Cursor = Cursors.Hand;
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
                    SizeMode = PictureBoxSizeMode.Zoom,
                    Cursor = Cursors.Hand
                };
                pbIcon.Click += (s, e) => { if (OnSelect != null) OnSelect(AppName); };
                Controls.Add(pbIcon);
                currentX += 20;
            }

            // App Name & Threshold Label
            lblName = new Label
            {
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                AutoSize = false,
                Location = new Point(currentX, 5),
                Cursor = Cursors.Hand
            };
            lblName.Click += (s, e) => { if (OnSelect != null) OnSelect(AppName); };
            Controls.Add(lblName);

            // Close (Remove) Button
            btnClose = new Label
            {
                Text = "×",
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Color.FromArgb(148, 163, 184),
                AutoSize = false,
                Size = new Size(16, 18),
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

            Click += (s, e) => { if (OnSelect != null) OnSelect(AppName); };

            UpdateLabelAndSize();
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

        private Color GetCurrentBgColor()
        {
            if (IsActiveTarget) return BgTarget;
            return IsSelected ? BgSelected : BgNormal;
        }

        private void UpdateLabelAndSize()
        {
            string labelText;
            if (IsActiveTarget)
            {
                labelText = string.Format("{0} • 🎯 Target", AppName);
                lblName.ForeColor = Color.FromArgb(216, 180, 254);
                string tip = string.Format("{0} is currently the active Background Target.\nTrigger ducking is paused automatically to prevent collision.", AppName);
                toolTip.SetToolTip(this, tip);
                toolTip.SetToolTip(lblName, tip);
            }
            else
            {
                labelText = string.Format("{0} • {1}%", AppName, ThresholdPercent);
                lblName.ForeColor = IsSelected ? Color.FromArgb(240, 246, 252) : Color.FromArgb(201, 209, 217);
                string tip = string.Format("Priority Trigger: {0} (Threshold: {1}%)\nClick to adjust sensitivity or click × to remove.", AppName, ThresholdPercent);
                toolTip.SetToolTip(this, tip);
                toolTip.SetToolTip(lblName, tip);
            }

            lblName.Text = labelText;
            using (var f = new Font("Segoe UI", 8.5f, FontStyle.Bold))
            {
                Size sz = TextRenderer.MeasureText(labelText, f);
                lblName.Size = new Size(sz.Width + 2, 18);
            }

            btnClose.Location = new Point(lblName.Right + 4, 3);
            this.Size = new Size(btnClose.Right + 8, 28);
            BackColor = GetCurrentBgColor();
        }

        public void SetSelected(bool selected)
        {
            if (IsSelected != selected)
            {
                IsSelected = selected;
                UpdateLabelAndSize();
                Invalidate();
            }
        }

        public void SetActiveTarget(bool isTarget)
        {
            if (IsActiveTarget != isTarget)
            {
                IsActiveTarget = isTarget;
                UpdateLabelAndSize();
                Invalidate();
            }
        }

        public void UpdateThreshold(int percent)
        {
            ThresholdPercent = percent;
            UpdateLabelAndSize();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = UiHelper.GetRoundPath(rect, 8))
            {
                using (var b = new SolidBrush(GetCurrentBgColor()))
                {
                    e.Graphics.FillPath(b, path);
                }

                Color border = IsActiveTarget ? BorderTarget : (IsSelected ? BorderSelected : BorderNormal);
                float borderW = (IsActiveTarget || IsSelected) ? 1.6f : 1.0f;
                using (var pen = new Pen(border, borderW))
                {
                    e.Graphics.DrawPath(pen, path);
                }
            }
            base.OnPaint(e);
        }
    }
}
