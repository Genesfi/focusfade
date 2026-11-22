using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FocusFade.UI.Controls
{
    public class ModernVuMeter : Control
    {
        private float level = 0.0f;
        private static readonly SolidBrush BgBrush = new SolidBrush(Color.FromArgb(13, 17, 23));
        private static readonly Pen BorderPen = new Pen(Color.FromArgb(48, 54, 61), 1f);

        public Color GradientStart { get; set; }
        public Color GradientMid { get; set; }
        public Color GradientEnd { get; set; }

        public float Threshold { get; set; }
        public bool ShowThresholdMarker { get; set; }

        public float Level
        {
            get { return level; }
            set
            {
                float clamped = Math.Max(0.0f, Math.Min(1.0f, value));
                if (Math.Abs(level - clamped) > 0.008f || (clamped == 0 && level > 0))
                {
                    level = clamped;
                    Invalidate();
                }
            }
        }

        public ModernVuMeter()
        {
            Threshold = -1f;
            ShowThresholdMarker = false;
            GradientStart = Color.FromArgb(16, 185, 129); // Emerald
            GradientMid = Color.FromArgb(245, 158, 11);   // Amber
            GradientEnd = Color.FromArgb(239, 68, 68);    // Red

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Height = 12;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            if (rect.Width <= 2 || rect.Height <= 2) return;

            bool isVertical = Height > Width;
            int cornerRadius = Math.Min(3, Math.Min(rect.Width, rect.Height) / 2);
            if (cornerRadius < 2) cornerRadius = 2;

            using (var path = UiHelper.GetRoundPath(rect, cornerRadius))
            {
                e.Graphics.FillPath(BgBrush, path);
                e.Graphics.SetClip(path);

                if (isVertical)
                {
                    // Vertical Level Tower (fills from bottom to top)
                    int fillHeight = (int)Math.Round(rect.Height * level);
                    if (fillHeight > 0)
                    {
                        int topY = rect.Bottom - fillHeight;
                        var fillRect = new Rectangle(rect.X, topY, rect.Width, fillHeight);
                        Color cEnd = level > 0.6f ? GradientEnd : (level > 0.25f ? GradientMid : GradientStart);
                        using (var lgb = new LinearGradientBrush(rect, cEnd, GradientStart, LinearGradientMode.Vertical))
                        {
                            e.Graphics.FillRectangle(lgb, fillRect);
                        }

                        using (var capPen = new Pen(Color.FromArgb(210, 255, 255, 255), 1.5f))
                        {
                            e.Graphics.DrawLine(capPen, rect.X, topY, rect.Right, topY);
                        }
                    }
                }
                else
                {
                    // Horizontal Level Bar (fills from left to right)
                    int fillWidth = (int)Math.Round(rect.Width * level);
                    if (fillWidth > 0)
                    {
                        int rightX = rect.X + fillWidth;
                        var fillRect = new Rectangle(rect.X, rect.Y, fillWidth, rect.Height);

                        if (ShowThresholdMarker && Threshold >= 0)
                        {
                            // Discord Style: Grey when below threshold, Vibrant Discord Green when voice active!
                            if (level < Threshold)
                            {
                                using (var inactiveBrush = new SolidBrush(Color.FromArgb(71, 85, 105)))
                                {
                                    e.Graphics.FillRectangle(inactiveBrush, fillRect);
                                }
                            }
                            else
                            {
                                using (var activeLgb = new LinearGradientBrush(rect, Color.FromArgb(34, 197, 94), Color.FromArgb(16, 185, 129), LinearGradientMode.Horizontal))
                                {
                                    e.Graphics.FillRectangle(activeLgb, fillRect);
                                }
                            }
                        }
                        else
                        {
                            Color cEnd = level > 0.6f ? GradientEnd : (level > 0.25f ? GradientMid : GradientStart);
                            using (var lgb = new LinearGradientBrush(rect, GradientStart, cEnd, LinearGradientMode.Horizontal))
                            {
                                e.Graphics.FillRectangle(lgb, fillRect);
                            }
                        }

                        // Subtle glowing peak line at right of level
                        using (var capPen = new Pen(Color.FromArgb(210, 255, 255, 255), 1.5f))
                        {
                            e.Graphics.DrawLine(capPen, rightX, rect.Y, rightX, rect.Bottom);
                        }
                    }

                    // Discord Threshold Needle
                    if (ShowThresholdMarker && Threshold > 0.005f && Threshold < 0.995f)
                    {
                        int markerX = rect.X + (int)(rect.Width * Threshold);
                        using (var markerPen = new Pen(Color.FromArgb(240, 246, 252), 2f))
                        {
                            e.Graphics.DrawLine(markerPen, markerX, rect.Y, markerX, rect.Bottom);
                        }
                    }
                }

                e.Graphics.ResetClip();
                e.Graphics.DrawPath(BorderPen, path);
            }
        }
    }
}
