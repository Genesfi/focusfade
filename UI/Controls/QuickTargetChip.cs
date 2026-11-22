using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using FocusFade.Core;

namespace FocusFade.UI.Controls
{
    public class QuickTargetChip : Panel
    {
        public string AppName { get; private set; }
        public bool IsActive { get; private set; }

        public event Action<string> OnSelectTarget;

        private PictureBox pbIcon;
        private Label lblName;
        private ToolTip toolTip;
        private bool isHovered = false;

        private static readonly Color BgNormal = Color.FromArgb(22, 27, 34);
        private static readonly Color BgHover = Color.FromArgb(33, 38, 45);
        private static readonly Color BgActive = Color.FromArgb(49, 39, 74); // Vibrant Purple/Indigo tint

        private static readonly Color BorderNormal = Color.FromArgb(48, 54, 61);
        private static readonly Color BorderHover = Color.FromArgb(99, 102, 241);
        private static readonly Color BorderActive = Color.FromArgb(168, 85, 247); // Glowing Violet

        private static readonly Color TextNormal = Color.FromArgb(148, 163, 184);
        private static readonly Color TextHover = Color.FromArgb(240, 246, 252);
        private static readonly Color TextActive = Color.FromArgb(238, 242, 255);

        public QuickTargetChip(string name, bool active = false)
        {
            AppName = name;
            IsActive = active;

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Height = 25;
            Padding = new Padding(4, 2, 4, 2);
            Margin = new Padding(0, 0, 5, 0);
            Cursor = Cursors.Hand;
            toolTip = new ToolTip();

            int currentX = 5;

            // App Icon
            Image icon = ProcessHelper.GetProcessIcon(name);
            if (icon != null)
            {
                pbIcon = new PictureBox
                {
                    Image = icon,
                    Size = new Size(14, 14),
                    Location = new Point(currentX, 5),
                    SizeMode = PictureBoxSizeMode.Zoom,
                    Cursor = Cursors.Hand
                };
                pbIcon.Click += (s, e) => { if (OnSelectTarget != null) OnSelectTarget(AppName); };
                pbIcon.MouseEnter += (s, e) => SetHover(true);
                pbIcon.MouseLeave += (s, e) => SetHover(false);
                Controls.Add(pbIcon);
                currentX += 17;
            }

            lblName = new Label
            {
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                AutoSize = false,
                Location = new Point(currentX, 4),
                Cursor = Cursors.Hand
            };
            lblName.Click += (s, e) => { if (OnSelectTarget != null) OnSelectTarget(AppName); };
            lblName.MouseEnter += (s, e) => SetHover(true);
            lblName.MouseLeave += (s, e) => SetHover(false);
            Controls.Add(lblName);

            Click += (s, e) => { if (OnSelectTarget != null) OnSelectTarget(AppName); };
            MouseEnter += (s, e) => SetHover(true);
            MouseLeave += (s, e) => SetHover(false);

            UpdateState();
        }

        public void SetActive(bool active)
        {
            if (IsActive != active)
            {
                IsActive = active;
                UpdateState();
                Invalidate();
            }
        }

        private void SetHover(bool hover)
        {
            if (isHovered != hover)
            {
                isHovered = hover;
                UpdateState();
                Invalidate();
            }
        }

        private void UpdateState()
        {
            string labelText = IsActive ? ("🎯 " + AppName) : AppName;
            lblName.Text = labelText;

            using (var f = new Font("Segoe UI", 8f, FontStyle.Bold))
            {
                Size sz = TextRenderer.MeasureText(labelText, f);
                lblName.Size = new Size(sz.Width + 2, 17);
            }

            this.Size = new Size(lblName.Right + 6, 25);

            if (IsActive)
            {
                lblName.ForeColor = TextActive;
                string tip = string.Format("{0} is currently the active Background Music Target.", AppName);
                toolTip.SetToolTip(this, tip);
                toolTip.SetToolTip(lblName, tip);
            }
            else
            {
                lblName.ForeColor = isHovered ? TextHover : TextNormal;
                string tip = string.Format("Click to quickly set {0} as Background Target", AppName);
                toolTip.SetToolTip(this, tip);
                toolTip.SetToolTip(lblName, tip);
            }

            BackColor = IsActive ? BgActive : (isHovered ? BgHover : BgNormal);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = UiHelper.GetRoundPath(rect, 6))
            {
                Color bg = IsActive ? BgActive : (isHovered ? BgHover : BgNormal);
                using (var b = new SolidBrush(bg))
                {
                    e.Graphics.FillPath(b, path);
                }

                Color border = IsActive ? BorderActive : (isHovered ? BorderHover : BorderNormal);
                float borderW = IsActive ? 1.4f : 1.0f;
                using (var pen = new Pen(border, borderW))
                {
                    e.Graphics.DrawPath(pen, path);
                }
            }
            base.OnPaint(e);
        }
    }
}
