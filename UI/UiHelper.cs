using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

namespace FocusFade.UI
{
    public static class UiHelper
    {
        public static GraphicsPath GetRoundPath(Rectangle rect, int radius)
        {
            int r2 = radius * 2;
            var path = new GraphicsPath();
            if (radius <= 0)
            {
                path.AddRectangle(rect);
                return path;
            }
            path.AddArc(rect.X, rect.Y, r2, r2, 180, 90);
            path.AddArc(rect.Right - r2, rect.Y, r2, r2, 270, 90);
            path.AddArc(rect.Right - r2, rect.Bottom - r2, r2, r2, 0, 90);
            path.AddArc(rect.X, rect.Bottom - r2, r2, r2, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static Icon GenerateAppIcon()
        {
            try
            {
                string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
                if (File.Exists(icoPath))
                {
                    return new Icon(icoPath);
                }
            }
            catch { }

            using (var bmp = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                var r = new Rectangle(1, 1, 30, 30);
                using (var path = GetRoundPath(r, 8))
                using (var lgb = new LinearGradientBrush(r, Color.FromArgb(99, 102, 241), Color.FromArgb(168, 85, 247), LinearGradientMode.ForwardDiagonal))
                {
                    g.FillPath(lgb, path);
                }

                using (var barBrush = new SolidBrush(Color.White))
                {
                    g.FillRectangle(barBrush, 7, 13, 3, 7);
                    g.FillRectangle(barBrush, 12, 8, 3, 16);
                    g.FillRectangle(barBrush, 17, 11, 3, 11);
                    g.FillRectangle(barBrush, 22, 15, 3, 4);
                }

                IntPtr hIcon = bmp.GetHicon();
                return Icon.FromHandle(hIcon);
            }
        }

        public static Bitmap CreateSpeakerVectorIcon(Color color, int size = 16)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var brush = new SolidBrush(color))
                using (var pen = new Pen(color, 1.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    var pts = new Point[] {
                        new Point(2, 6),
                        new Point(5, 6),
                        new Point(9, 3),
                        new Point(9, 13),
                        new Point(5, 10),
                        new Point(2, 10)
                    };
                    g.FillPolygon(brush, pts);
                    g.DrawArc(pen, 9, 5, 4, 6, -60, 120);
                    g.DrawArc(pen, 11, 3, 5, 10, -60, 120);
                }
            }
            return bmp;
        }

        public static Bitmap CreateMicVectorIcon(Color color, int size = 16)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var brush = new SolidBrush(color))
                using (var pen = new Pen(color, 1.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    var r = new Rectangle(5, 2, 6, 8);
                    using (var path = GetRoundPath(r, 3))
                    {
                        g.FillPath(brush, path);
                    }
                    g.DrawArc(pen, 3, 4, 10, 8, 0, 180);
                    g.DrawLine(pen, 8, 12, 8, 14);
                    g.DrawLine(pen, 5, 14, 11, 14);
                }
            }
            return bmp;
        }

        public static Bitmap CreateHeadphonesVectorIcon(Color color, int size = 16)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                using (var brush = new SolidBrush(color))
                using (var pen = new Pen(color, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawArc(pen, 3f, 3f, 10f, 9f, 180, 180);
                    g.FillRectangle(brush, 2, 7, 3, 6);
                    g.FillRectangle(brush, 11, 7, 3, 6);
                }
            }
            return bmp;
        }

        public static Bitmap CreateSlidersVectorIcon(Color color, int size = 16)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                using (var brush = new SolidBrush(color))
                using (var pen = new Pen(color, 1.3f))
                {
                    g.DrawLine(pen, 4, 2, 4, 14);
                    g.FillEllipse(brush, 2, 4, 5, 5);
                    g.DrawLine(pen, 11, 2, 11, 14);
                    g.FillEllipse(brush, 9, 8, 5, 5);
                }
            }
            return bmp;
        }

        public static Bitmap CreatePipVectorIcon(Color color, int size = 16)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                using (var pen = new Pen(color, 1.4f))
                using (var brush = new SolidBrush(color))
                {
                    g.DrawRectangle(pen, 2, 3, 11, 9);
                    g.FillRectangle(brush, 7, 7, 5, 4);
                }
            }
            return bmp;
        }

        public static Bitmap CreateRefreshVectorIcon(Color color, int size = 16)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                using (var pen = new Pen(color, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                using (var brush = new SolidBrush(color))
                {
                    g.DrawArc(pen, 3f, 3.5f, 10f, 10f, 45, 270);
                    var pts = new PointF[] {
                        new PointF(10f, 1.5f),
                        new PointF(13.5f, 4.5f),
                        new PointF(10f, 7f)
                    };
                    g.FillPolygon(brush, pts);
                }
            }
            return bmp;
        }

        public static Bitmap CreateStopVectorIcon(Color color, int size = 14)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var brush = new SolidBrush(color))
                {
                    var r = new Rectangle(2, 2, 10, 10);
                    using (var path = GetRoundPath(r, 2))
                    {
                        g.FillPath(brush, path);
                    }
                }
            }
            return bmp;
        }

        public static Bitmap CreatePlayVectorIcon(Color color, int size = 14)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var brush = new SolidBrush(color))
                {
                    var pts = new Point[] {
                        new Point(3, 2),
                        new Point(12, 7),
                        new Point(3, 12)
                    };
                    g.FillPolygon(brush, pts);
                }
            }
            return bmp;
        }
    }
}
