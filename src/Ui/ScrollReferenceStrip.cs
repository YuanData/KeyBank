using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KeyBank.Ui
{
    internal sealed class StripMarker
    {
        public string Label;
        public int Real;
        public int Pos;
        public int Clamp;
        public bool IsPage;
    }

    // Shows where 1/4, 1/3, 1/2 and a full page land on the single-press slider's scale.
    internal sealed class ScrollReferenceStrip : Control
    {
        const TextFormatFlags TextFlags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter
            | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
        const int TbmGetThumbRect = 0x0419;
        const int TbmGetChannelRect = 0x041A;

        static readonly Color Accent = Color.FromArgb(0x2B, 0x6C, 0xD9);
        static readonly Color PageAccent = Color.FromArgb(0x1B, 0x7A, 0x3A);
        static readonly Color Axis = Color.FromArgb(0xA0, 0xA6, 0xB0);

        [StructLayout(LayoutKind.Sequential)]
        struct Rect
        {
            public int Left, Top, Right, Bottom;
        }

        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, out Rect lParam);

        TrackBar track;
        double pageTicks = 8;
        int min = 1;
        int max = 30;

        public ScrollReferenceStrip()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            TabStop = false;
        }

        public void Attach(TrackBar t, int minValue, int maxValue)
        {
            track = t;
            min = minValue;
            max = maxValue;
            track.SizeChanged += delegate { Invalidate(); };
            Invalidate();
        }

        public double PageTicks
        {
            get { return pageTicks; }
            set
            {
                pageTicks = value;
                Invalidate();
            }
        }

        internal List<StripMarker> Markers
        {
            get { return ComputeMarkers(pageTicks, min, max); }
        }

        public static List<StripMarker> ComputeMarkers(double page, int min, int max)
        {
            List<StripMarker> list = new List<StripMarker>();
            string[] names = { "1/4", "1/3", "1/2" };
            double[] fractions = { 0.25, 1.0 / 3, 0.5 };
            for (int i = 0; i < names.Length; i++)
            {
                int real = (int)Math.Round(page * fractions[i], MidpointRounding.AwayFromZero);
                list.Add(Make(names[i] + " ≈ " + real.ToString(CultureInfo.InvariantCulture) + " 格", real, min, max, false));
            }
            int whole = (int)Math.Round(page, MidpointRounding.AwayFromZero);
            list.Add(Make("整頁 = " + RuleModel.TickText(page) + " 格", whole, min, max, true));
            return list;
        }

        static StripMarker Make(string label, int real, int min, int max, bool isPage)
        {
            StripMarker m = new StripMarker();
            m.Real = real;
            m.IsPage = isPage;
            m.Pos = Math.Max(min, Math.Min(max, real));
            m.Clamp = real > max ? 1 : (real < min ? -1 : 0);
            m.Label = label + (m.Clamp > 0 ? " →" : (m.Clamp < 0 ? " ←" : ""));
            return m;
        }

        int Pad
        {
            get { return Math.Max(2, Font.Height / 4); }
        }

        int RowHeight
        {
            get { return Font.Height + Pad / 2; }
        }

        int WantedHeight
        {
            get { return Pad * 3 + RowHeight * 4; }
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            return new Size(Math.Max(100, proposedSize.Width), WantedHeight);
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            MinimumSize = new Size(0, WantedHeight);
            Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            MinimumSize = new Size(0, WantedHeight);
        }

        // x positions (in this control's coordinates) of the thumb centre at min and at max.
        internal bool TrackCentres(out float left, out float right)
        {
            left = right = 0;
            if (track == null || !track.IsHandleCreated)
                return false;
            Rect channel, thumb;
            SendMessage(track.Handle, TbmGetChannelRect, IntPtr.Zero, out channel);
            SendMessage(track.Handle, TbmGetThumbRect, IntPtr.Zero, out thumb);
            int thumbW = thumb.Right - thumb.Left;
            if (channel.Right <= channel.Left || thumbW <= 0)
                return false;
            float offset = track.Left - Left;
            left = offset + channel.Left + thumbW / 2f;
            right = offset + channel.Right - thumbW / 2f;
            return right > left;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);

            float lo, hi;
            if (!TrackCentres(out lo, out hi))
            {
                lo = Pad * 3;
                hi = Width - Pad * 3;
            }

            int axisY = Pad;
            using (Pen axis = new Pen(Axis))
                g.DrawLine(axis, lo, axisY, hi, axisY);

            List<StripMarker> markers = ComputeMarkers(pageTicks, min, max);
            List<int> order = new List<int>();
            for (int i = 0; i < markers.Count; i++)
                order.Add(i);
            // Leftmost marker gets the deepest row, so a lower marker's line never crosses a label to its right.
            order.Sort(delegate(int a, int b)
            {
                int c = markers[a].Pos.CompareTo(markers[b].Pos);
                return c != 0 ? c : a.CompareTo(b);
            });

            int n = order.Count;
            for (int k = 0; k < n; k++)
            {
                StripMarker m = markers[order[k]];
                int row = n - 1 - k;
                float x = lo + (hi - lo) * (m.Pos - min) / (float)(max - min);
                int rowTop = axisY + Pad + row * RowHeight;
                Color color = m.IsPage ? PageAccent : Accent;

                using (Pen pen = new Pen(color))
                {
                    g.DrawLine(pen, x, axisY - Pad / 2, x, rowTop + RowHeight / 2);
                    g.DrawLine(pen, x, rowTop + RowHeight / 2, x + Pad / 2f, rowTop + RowHeight / 2);
                }

                Size ts = TextRenderer.MeasureText(g, m.Label, Font, Size.Empty, TextFlags);
                int gap = Pad;
                float tx = x + gap;
                if (tx + ts.Width > Width - 1)
                    tx = Math.Max(0, x - gap - ts.Width);
                Rectangle box = new Rectangle((int)tx, rowTop, ts.Width, RowHeight);
                TextRenderer.DrawText(g, m.Label, Font, box, color, BackColor, TextFlags);
            }
        }
    }
}
