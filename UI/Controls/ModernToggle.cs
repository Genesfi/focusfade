using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FocusFade.UI.Controls
{
    public class ModernToggle : Control
    {
        private bool isChecked = false;
        private bool isHovered = false;

        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get { return isChecked; }
            set
            {
                if (isChecked != value)
                {
                    isChecked = value;
                    Invalidate();
                    if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
                }
            }
        }

        public Color ActiveColor1 { get; set; }
        public Color ActiveColor2 { get; set; }

        public ModernToggle()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            UpdateStyles();

            Size = new Size(210, 24);
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI", 8.5f, FontStyle.Regular);
            ForeColor = Color.FromArgb(201, 209, 217);
            ActiveColor1 = Color.FromArgb(99, 102, 241); // Indigo
            ActiveColor2 = Color.FromArgb(139, 92, 246); // Purple

            MouseEnter += (s, e) => { isHovered = true; Invalidate(); };
            MouseLeave += (s, e) => { isHovered = false; Invalidate(); };
            Click += (s, e) => { Checked = !Checked; };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            // 1. Draw Pill Switch (Width 36, Height 18)
            int pillW = 34;
            int pillH = 18;
            int pillY = (Height - pillH) / 2;
            var pillRect = new Rectangle(0, pillY, pillW, pillH);

            using (var path = UiHelper.GetRoundPath(pillRect, pillH / 2))
            {
                if (isChecked)
                {
                    using (var lgb = new LinearGradientBrush(pillRect, ActiveColor1, ActiveColor2, LinearGradientMode.Horizontal))
                    {
                        e.Graphics.FillPath(lgb, path);
                    }
                }
                else
                {
                    using (var bgBrush = new SolidBrush(isHovered ? Color.FromArgb(33, 38, 45) : Color.FromArgb(22, 27, 34)))
                    {
                        e.Graphics.FillPath(bgBrush, path);
                    }
                    using (var borderPen = new Pen(Color.FromArgb(48, 54, 61), 1f))
                    {
                        e.Graphics.DrawPath(borderPen, path);
                    }
                }

                // 2. Draw Knob (Circle diameter 14)
                int knobSize = 14;
                int knobX = isChecked ? (pillRect.Right - knobSize - 2) : (pillRect.Left + 2);
                int knobY = pillY + 2;
                var knobRect = new Rectangle(knobX, knobY, knobSize, knobSize);

                using (var knobBrush = new SolidBrush(isChecked ? Color.White : (isHovered ? Color.FromArgb(200, 205, 215) : Color.FromArgb(148, 163, 184))))
                {
                    e.Graphics.FillEllipse(knobBrush, knobRect);
                }
            }

            // 3. Draw Label Text
            if (!string.IsNullOrEmpty(Text))
            {
                Color textColor = isChecked ? Color.FromArgb(240, 246, 252) : (isHovered ? Color.White : ForeColor);
                using (var textBrush = new SolidBrush(textColor))
                {
                    var textFormat = new StringFormat
                    {
                        LineAlignment = StringAlignment.Center,
                        Alignment = StringAlignment.Near
                    };
                    var textRect = new Rectangle(pillW + 8, 0, Width - (pillW + 8), Height);
                    e.Graphics.DrawString(Text, Font, textBrush, textRect, textFormat);
                }
            }
        }
    }
}
