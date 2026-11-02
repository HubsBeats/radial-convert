using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace RadialConvert
{
    // Click-through, never-focused overlay with per-pixel alpha, centred on the click.
    class RadialMenu : Form
    {
        static readonly Color Ring = Color.FromArgb(232, 20, 21, 27);
        static readonly Color Accent = Color.FromArgb(245, 0, 145, 255);
        static readonly Color Hub = Color.FromArgb(250, 34, 35, 44);

        string[] labels = new string[4];
        string centre = "";
        Point origin;
        int selected = -1;
        int deadZone = 22;
        float scale = 1f;
        int size;
        readonly Timer timer;
        public bool IsOpen { get; private set; }

        public RadialMenu()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            timer = new Timer();
            timer.Interval = 12;
            timer.Tick += delegate { Track(); };
            CreateHandle();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                // layered | topmost | toolwindow | noactivate | transparent (click-through)
                cp.ExStyle |= 0x80000 | 0x8 | 0x80 | 0x8000000 | 0x20;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        public void Open(Point at, string[] formatLabels, string centreText, int dead)
        {
            origin = at;
            labels = formatLabels;
            centre = centreText;
            scale = Native.DpiScaleAt(at);
            deadZone = (int)(dead * scale);
            size = (int)(300 * scale);
            selected = -1;
            Render();
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
            IsOpen = true;
            timer.Start();
        }

        // Hides the menu and returns the chosen direction (0 up, 1 right, 2 down, 3 left) or -1.
        public int Finish()
        {
            timer.Stop();
            if (IsOpen) selected = DirectionFor(origin, Native.CursorPos(), deadZone);
            Native.ShowWindow(Handle, 0);
            IsOpen = false;
            return selected;
        }

        void Track()
        {
            int d = DirectionFor(origin, Native.CursorPos(), deadZone);
            if (d != selected) { selected = d; Render(); }
        }

        public static int DirectionFor(Point origin, Point p, int dead)
        {
            int dx = p.X - origin.X, dy = p.Y - origin.Y;
            if ((long)dx * dx + (long)dy * dy < (long)dead * dead) return -1;
            if (Math.Abs(dx) > Math.Abs(dy)) return dx > 0 ? 1 : 3;
            return dy < 0 ? 0 : 2;
        }

        void Render()
        {
            using (var bmp = Draw(size, scale, labels, centre, selected))
                Push(bmp);
        }

        public static Bitmap Draw(int size, float scale, string[] labels, string centre, int selected)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);
                float c = size / 2f;
                float R = 128 * scale, r = 40 * scale;

                using (var shadow = new SolidBrush(Color.FromArgb(60, 0, 0, 0)))
                    g.FillEllipse(shadow, c - R - 2 * scale, c - R + 3 * scale, 2 * R + 4 * scale, 2 * R + 4 * scale);

                using (var ring = new GraphicsPath())
                {
                    ring.AddEllipse(c - R, c - R, 2 * R, 2 * R);
                    ring.AddEllipse(c - r, c - r, 2 * r, 2 * r);
                    using (var b = new SolidBrush(Ring)) g.FillPath(b, ring);
                }

                float[] centres = { 270, 0, 90, 180 };
                if (selected >= 0)
                {
                    using (var wedge = new GraphicsPath())
                    {
                        float start = centres[selected] - 45;
                        float gap = 1.5f;
                        wedge.AddArc(c - R, c - R, 2 * R, 2 * R, start + gap, 90 - 2 * gap);
                        wedge.AddArc(c - r, c - r, 2 * r, 2 * r, start + 90 - gap * 3, -(90 - gap * 6));
                        wedge.CloseFigure();
                        using (var b = new SolidBrush(Accent)) g.FillPath(b, wedge);
                    }
                }

                using (var sep = new Pen(Color.FromArgb(55, 255, 255, 255), 1.5f * scale))
                {
                    for (int i = 0; i < 4; i++)
                    {
                        double a = (45 + 90 * i) * Math.PI / 180;
                        g.DrawLine(sep, c + (float)Math.Cos(a) * (r + 2), c + (float)Math.Sin(a) * (r + 2),
                                        c + (float)Math.Cos(a) * (R - 2), c + (float)Math.Sin(a) * (R - 2));
                    }
                }
                using (var rim = new Pen(Color.FromArgb(80, 255, 255, 255), 1.2f * scale))
                    g.DrawEllipse(rim, c - R, c - R, 2 * R, 2 * R);

                using (var hub = new SolidBrush(Hub)) g.FillEllipse(hub, c - r, c - r, 2 * r, 2 * r);
                using (var hubRim = new Pen(Color.FromArgb(70, 255, 255, 255), 1.2f * scale)) g.DrawEllipse(hubRim, c - r, c - r, 2 * r, 2 * r);

                var sf = new StringFormat();
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                float mid = (R + r) / 2;
                using (var big = new Font("Segoe UI Semibold", 17 * scale, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var small = new Font("Segoe UI", 11.5f * scale, FontStyle.Regular, GraphicsUnit.Pixel))
                {
                    for (int i = 0; i < 4; i++)
                    {
                        if (string.IsNullOrEmpty(labels[i])) continue;
                        double a = centres[i] * Math.PI / 180;
                        var at = new PointF(c + (float)Math.Cos(a) * mid, c + (float)Math.Sin(a) * mid);
                        var col = i == selected ? Color.White : Color.FromArgb(215, 235, 238, 245);
                        using (var b = new SolidBrush(col))
                            g.DrawString(labels[i].ToUpperInvariant(), big, b, at, sf);
                    }
                    using (var b = new SolidBrush(Color.FromArgb(200, 190, 196, 210)))
                        g.DrawString(centre, small, b, new RectangleF(c - r, c - r, 2 * r, 2 * r), sf);
                }
            }
            return bmp;
        }

        void Push(Bitmap bmp)
        {
            IntPtr screen = Native.GetDC(IntPtr.Zero);
            IntPtr mem = Native.CreateCompatibleDC(screen);
            IntPtr hbmp = bmp.GetHbitmap(Color.FromArgb(0));
            IntPtr old = Native.SelectObject(mem, hbmp);
            try
            {
                var sz = new Native.SIZE(); sz.cx = bmp.Width; sz.cy = bmp.Height;
                var pos = new Native.POINT(); pos.X = origin.X - bmp.Width / 2; pos.Y = origin.Y - bmp.Height / 2;
                var src = new Native.POINT();
                var blend = new Native.BLENDFUNCTION(); blend.BlendOp = 0; blend.SourceConstantAlpha = 255; blend.AlphaFormat = 1;
                Native.UpdateLayeredWindow(Handle, screen, ref pos, ref sz, mem, ref src, 0, ref blend, 2);
            }
            finally
            {
                Native.SelectObject(mem, old);
                Native.DeleteObject(hbmp);
                Native.DeleteDC(mem);
                Native.ReleaseDC(IntPtr.Zero, screen);
            }
        }
    }
}
