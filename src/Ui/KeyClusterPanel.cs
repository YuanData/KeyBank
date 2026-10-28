using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using KeyBank.Core;

namespace KeyBank.Ui
{
    public sealed class KeyClusterPanel : Control
    {
        const TextFormatFlags Centered = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
        const TextFormatFlags LeftAligned = TextFormatFlags.Left | TextFormatFlags.VerticalCenter
            | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

        static readonly Color CapFace = Color.White;
        static readonly Color CapBorder = Color.FromArgb(0xA0, 0xA6, 0xB0);
        static readonly Color DecoFace = Color.FromArgb(0xEE, 0xF0, 0xF3);
        static readonly Color DecoText = Color.FromArgb(0x80, 0x86, 0x90);
        static readonly Color ChangedFace = Color.FromArgb(0xFF, 0xEC, 0xC8);
        static readonly Color ChangedBorder = Color.FromArgb(0xE0, 0x8A, 0x10);
        static readonly Color BadgeFace = Color.FromArgb(0xDD, 0xE9, 0xFB);
        static readonly Color BadgeText = Color.FromArgb(0x1D, 0x4E, 0xA8);
        static readonly Color Ink = Color.FromArgb(0x20, 0x24, 0x2B);

        ClusterCell[][] cells;
        bool hasRules;
        bool hasBadge;
        Font small;

        public KeyClusterPanel()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            AutoSize = true;
            MinimumSize = new Size(1, 1);
        }

        public void SetRules(KeyRules p)
        {
            cells = RuleModel.BuildCluster(p);
            hasRules = p.Remap.Count > 0 || p.TapHold.Count > 0;
            hasBadge = p.TapHold.Count > 0;
            PerformLayout();
            Invalidate();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            if (small != null)
                small.Dispose();
            small = null;
            base.OnFontChanged(e);
            PerformLayout();
        }

        Font Small
        {
            get
            {
                if (small == null)
                    small = new Font(Font.FontFamily, Font.Size * 0.85f);
                return small;
            }
        }

        int U { get { return Font.Height; } }
        int CapW { get { return U * 7 / 2; } }
        int CapH { get { return U * 5 / 2; } }
        int Gap { get { return Math.Max(3, U / 3); } }
        int Pad { get { return U * 2 / 3; } }
        int LabelH { get { return U * 3 / 2; } }
        int BadgeH { get { return hasBadge ? U * 3 / 2 : 0; } }

        public override Size GetPreferredSize(Size proposedSize)
        {
            int gridW = 4 * CapW + 3 * Gap;
            int w = gridW;
            if (cells != null)
                foreach (ClusterCell c in cells[0])
                    if (c.Badge != null)
                    {
                        int idx = Array.IndexOf(cells[0], c);
                        w = Math.Max(w, idx * (CapW + Gap) + BadgeWidth(c.Badge));
                    }
            int h = LabelH + 2 * CapH + Gap
                + Pad
                + LabelH + CapH + BadgeH + (BadgeH > 0 ? Gap : 0) + Gap + CapH
                + Pad + LabelH;
            return new Size(w + 2 * Pad, h + Pad);
        }

        int BadgeWidth(string text)
        {
            return TextRenderer.MeasureText(text, Small, new Size(int.MaxValue, int.MaxValue), LeftAligned).Width + U;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            if (cells == null)
                return;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int x0 = Pad;
            int y = Pad / 2;
            Font bold = new Font(Font, FontStyle.Bold);
            try
            {
                TextRenderer.DrawText(g, "目前實體", bold, new Rectangle(x0, y, 10 * U, LabelH), Ink, LeftAligned);
                y += LabelH;
                DrawGrid(g, x0, y, false, 0);
                y += 2 * CapH + Gap + Pad;

                TextRenderer.DrawText(g, "改後", bold, new Rectangle(x0, y, 10 * U, LabelH), Ink, LeftAligned);
                y += LabelH;
                DrawGrid(g, x0, y, true, BadgeH > 0 ? BadgeH + Gap : 0);
                int gridBottom = y + 2 * CapH + Gap + (BadgeH > 0 ? BadgeH + Gap : 0);

                if (!hasRules)
                    TextRenderer.DrawText(g, "此鍵盤沒有位置對調", Font,
                        new Rectangle(x0, gridBottom + Pad / 2, Width - 2 * Pad, LabelH), DecoText, LeftAligned);
            }
            finally
            {
                bold.Dispose();
            }
        }

        void DrawGrid(Graphics g, int x0, int y0, bool after, int rowExtra)
        {
            for (int r = 0; r < 2; r++)
            {
                int y = y0 + r * (CapH + Gap) + (r == 1 ? rowExtra : 0);
                for (int c = 0; c < 4; c++)
                {
                    ClusterCell cell = cells[r][c];
                    Rectangle rect = new Rectangle(x0 + c * (CapW + Gap), y, CapW, CapH);
                    bool changed = after && cell.Changed;
                    Color face = cell.Fixed ? DecoFace : (changed ? ChangedFace : CapFace);
                    Color border = changed ? ChangedBorder : CapBorder;
                    using (GraphicsPath path = Rounded(rect, Math.Max(3, U / 4)))
                    using (Brush b = new SolidBrush(face))
                    using (Pen p = new Pen(border, changed ? 2f : 1f))
                    {
                        g.FillPath(b, path);
                        g.DrawPath(p, path);
                    }
                    string text = after ? cell.AfterLabel : cell.Label;
                    Color ink = cell.Fixed ? DecoText : Ink;
                    using (Font f = new Font(Font, changed ? FontStyle.Bold : FontStyle.Regular))
                        TextRenderer.DrawText(g, text, f, rect, ink, Centered);

                    if (after && r == 0 && cell.Badge != null)
                    {
                        Rectangle br = new Rectangle(rect.X, rect.Bottom + Gap, BadgeWidth(cell.Badge), BadgeH);
                        using (GraphicsPath bp = Rounded(br, BadgeH / 2))
                        using (Brush bb = new SolidBrush(BadgeFace))
                            g.FillPath(bb, bp);
                        TextRenderer.DrawText(g, cell.Badge, Small, br, BadgeText, Centered);
                    }
                }
            }
        }

        static GraphicsPath Rounded(Rectangle r, int radius)
        {
            int d = Math.Max(2, radius * 2);
            GraphicsPath p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d - 1, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d - 1, r.Bottom - d - 1, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d - 1, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && small != null)
                small.Dispose();
            base.Dispose(disposing);
        }
    }
}
