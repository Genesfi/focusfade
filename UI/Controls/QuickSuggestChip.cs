using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using FocusFade.Core;

namespace FocusFade.UI.Controls
{
    public class QuickSuggestChip : Panel
    {
        public string AppName { get; private set; }
        public event Action<string> OnAdd;

        private PictureBox pbIcon;
        private Label lblText;
        private ToolTip toolTip;
        private bool isHovered = false;

        private static readonly Color BgNormal = Color.FromArgb(22, 27, 34);
        private static readonly Color BgHover = Color.FromArgb(38, 44, 54);
        private static readonly Color BorderNormal = Color.FromArgb(48, 54, 61);
        private static readonly Color BorderHover = Color.FromArgb(99, 102, 241);
        private static readonly Color TextNormal = Color.FromArgb(139, 148, 158);
        private static readonly Color TextHover = Color.FromArgb(240, 246, 252);

        public QuickSuggestChip(string name)
        {
            AppName = name;

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Height = 24;
            Padding = new Padding(4, 2, 4, 2);
            Margin = new Padding(0, 0, 5, 4);
            Cursor = Cursors.Hand;
            toolTip = new ToolTip();

            int currentX = 5;

            Image icon = ProcessHelper.GetProcessIcon(name);
            if (icon != null)
            {
                pbIcon = new PictureBox
                {
                    Image = icon,
                    Size = new Size(13, 13),
                    Location = new Point(currentX, 5),
                    SizeMode = PictureBoxSizeMode.Zoom,
                    Cursor = Cursors.Hand
                };
                pbIcon.Click += (s, e) => { if (OnAdd != null) OnAdd(AppName); };
                pbIcon.MouseEnter += (s, e) => SetHover(true);
                pbIcon.MouseLeave += (s, e) => SetHover(false);
                Controls.Add(pbIcon);
                currentX += 16;
            }

            lblText = new Label
            {
                Text = "+ " + AppName,
                Font = new Font("Segoe UI", 8f, FontStyle.Regular),
                ForeColor = TextNormal,
                AutoSize = false,
                Location = new Point(currentX, 3),
                Cursor = Cursors.Hand
            };
            lblText.Click += (s, e) => { if (OnAdd != null) OnAdd(AppName); };
            lblText.MouseEnter += (s, e) => SetHover(true);
            lblText.MouseLeave += (s, e) => SetHover(false);
            Controls.Add(lblText);

            using (var f = new Font("Segoe UI", 8f, FontStyle.Regular))
            {
                Size sz = TextRenderer.MeasureText("+ " + AppName, f);
                lblText.Size = new Size(sz.Width + 2, 16);
            }

            Size = new Size(lblText.Right + 5, 24);

            Click += (s, e) => { if (OnAdd != null) OnAdd(AppName); };
            MouseEnter += (s, e) => SetHover(true);
            MouseLeave += (s, e) => SetHover(false);

            string tip = string.Format("Click to add {0} to Background Music Targets", AppName);
            toolTip.SetToolTip(this, tip);
            toolTip.SetToolTip(lblText, tip);
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

        private void SetHover(bool hover)
        {
            if (isHovered != hover)
            {
                isHovered = hover;
                lblText.ForeColor = isHovered ? TextHover : TextNormal;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = UiHelper.GetRoundPath(rect, 5))
            {
                using (var b = new SolidBrush(isHovered ? BgHover : BgNormal))
                {
                    e.Graphics.FillPath(b, path);
                }
                using (var pen = new Pen(isHovered ? BorderHover : BorderNormal, 1.0f))
                {
                    e.Graphics.DrawPath(pen, path);
                }
            }
            base.OnPaint(e);
        }
    }
}
