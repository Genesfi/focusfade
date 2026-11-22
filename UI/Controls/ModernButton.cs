using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FocusFade.UI.Controls
{
    public class ModernButton : Button
    {
        public Color NormalColor1 { get; set; }
        public Color NormalColor2 { get; set; }
        public Color HoverColor1 { get; set; }
        public Color HoverColor2 { get; set; }
        public Color BorderColor { get; set; }
        public float BorderWidth { get; set; }
        public int Radius { get; set; }
        private bool isHovered = false;

        public ModernButton()
        {
            NormalColor1 = Color.FromArgb(99, 102, 241);
            NormalColor2 = Color.FromArgb(139, 92, 246);
            HoverColor1 = Color.FromArgb(129, 140, 248);
            HoverColor2 = Color.FromArgb(167, 139, 250);
            BorderColor = Color.Empty;
            BorderWidth = 1f;
            Radius = 6;

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            ForeColor = Color.White;
        }

        protected override void OnMouseEnter(EventArgs e) { isHovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { isHovered = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width <= 1 || Height <= 1) return;

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = UiHelper.GetRoundPath(rect, Radius))
            {
                Color c1 = isHovered ? HoverColor1 : NormalColor1;
                Color c2 = isHovered ? HoverColor2 : NormalColor2;
                if (c1 == c2)
                {
                    using (var sb = new SolidBrush(c1))
                    {
                        e.Graphics.FillPath(sb, path);
                    }
                }
                else
                {
                    using (var lgb = new LinearGradientBrush(rect, c1, c2, LinearGradientMode.Horizontal))
                    {
                        e.Graphics.FillPath(lgb, path);
                    }
                }

                if (BorderColor != Color.Empty && BorderWidth > 0)
                {
                    using (var pen = new Pen(BorderColor, BorderWidth))
                    {
                        e.Graphics.DrawPath(pen, path);
                    }
                }
            }

            int imgW = (Image != null) ? Image.Width : 0;
            int imgH = (Image != null) ? Image.Height : 0;
            string text = string.IsNullOrEmpty(Text) ? "" : Text.Trim();
            int textW = string.IsNullOrEmpty(text) ? 0 : TextRenderer.MeasureText(text, Font).Width;
            int spacing = (imgW > 0 && textW > 0) ? 6 : 0;
            int totalW = imgW + spacing + textW;

            int startX = Math.Max(2, (Width - totalW) / 2);
            int imgY = (Height - imgH) / 2;

            if (Image != null)
            {
                e.Graphics.DrawImage(Image, startX, imgY, imgW, imgH);
            }

            if (textW > 0)
            {
                int textX = (imgW > 0) ? (startX + imgW + spacing) : 0;
                int textWidth = (imgW > 0) ? (Width - textX - 2) : Width;
                var textRect = new Rectangle(textX, 0, Math.Max(1, textWidth), Height);
                var flags = (imgW > 0)
                    ? (TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine)
                    : (TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

                TextRenderer.DrawText(e.Graphics, text, Font, textRect, ForeColor, flags);
            }
        }
    }
}
