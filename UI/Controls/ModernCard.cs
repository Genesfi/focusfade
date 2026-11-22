using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FocusFade.UI.Controls
{
    public class ModernCard : Panel
    {
        public int Radius { get; set; }
        private static readonly Color BgColor = Color.FromArgb(22, 27, 34);
        private static readonly Pen BorderPen = new Pen(Color.FromArgb(48, 54, 61), 1f);

        public ModernCard()
        {
            Radius = 10;
            BackColor = BgColor;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            UpdateStyles();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = UiHelper.GetRoundPath(rect, Radius))
            {
                using (var b = new SolidBrush(BgColor))
                {
                    e.Graphics.FillPath(b, path);
                }
                e.Graphics.DrawPath(BorderPen, path);
            }
            base.OnPaint(e);
        }
    }
}
