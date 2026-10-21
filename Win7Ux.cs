using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FolderDiffCopy
{
    internal static class Win7
    {
        public static readonly Color Window = Color.FromArgb(240, 240, 240);
        public static readonly Color Header = Color.FromArgb(244, 248, 252);
        public static readonly Color HeaderLine = Color.FromArgb(196, 214, 228);
        public static readonly Color Instruction = Color.FromArgb(0, 51, 153);
        public static readonly Color Text = Color.FromArgb(32, 32, 32);
        public static readonly Color Muted = Color.FromArgb(70, 70, 70);
        public static readonly Color Subtle = Color.FromArgb(96, 96, 96);
        public static readonly Color Border = Color.FromArgb(171, 173, 179);
        public static readonly Color Focus = Color.FromArgb(60, 127, 177);
        public static readonly Color Select = Color.FromArgb(204, 232, 255);
        public static readonly Color Hover = Color.FromArgb(242, 249, 255);
        public static readonly Color FailBack = Color.FromArgb(255, 244, 244);
        public static readonly Color FailText = Color.FromArgb(164, 0, 0);
        public static readonly Color Line = Color.FromArgb(213, 213, 213);

        public static Font ValueFont = new Font("Segoe UI", 12f, FontStyle.Regular, GraphicsUnit.Point);
        public static Font SmallFont = new Font("Segoe UI", 8.25f, FontStyle.Regular, GraphicsUnit.Point);
    }

    internal static class Readout
    {
        public static string FormatBytes(long bytes)
        {
            if (bytes < 0) bytes = 0;
            if (bytes < 1024)
                return bytes.ToString("N0") + (bytes == 1 ? " byte" : " bytes");
            return FormatScaled(bytes) ;
        }

        public static string FormatRate(double bytesPerSec)
        {
            if (bytesPerSec < 0 || double.IsNaN(bytesPerSec) || double.IsInfinity(bytesPerSec))
                bytesPerSec = 0;
            if (bytesPerSec < 1024)
                return ((long)Math.Round(bytesPerSec)).ToString("N0") + " B/s";
            return FormatScaled((long)Math.Round(bytesPerSec)) + "/s";
        }

        static string FormatScaled(long bytes)
        {
            double value = bytes;
            string[] units = new string[] { "KB", "MB", "GB", "TB" };
            int unit = -1;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }
            string number;
            if (value >= 100) number = value.ToString("0");
            else if (value >= 10) number = value.ToString("0.0");
            else number = value.ToString("0.00");
            return number + " " + units[unit];
        }

        public static string FormatItems(double perSecond)
        {
            if (perSecond < 0 || double.IsNaN(perSecond) || double.IsInfinity(perSecond))
                perSecond = 0;
            if (perSecond >= 100) return perSecond.ToString("0");
            if (perSecond >= 10) return perSecond.ToString("0.0");
            return perSecond.ToString("0.00");
        }

        public static string FormatEta(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
                return "Calculating...";
            if (seconds < 4) return "A few seconds";
            if (seconds < 60)
                return ((int)Math.Round(seconds)).ToString() + " seconds";
            if (seconds < 3600)
            {
                int minutes = (int)Math.Round(seconds / 60.0);
                if (minutes < 1) minutes = 1;
                if (minutes == 1) return "About 1 minute";
                return "About " + minutes.ToString() + " minutes";
            }
            int hours = (int)(seconds / 3600);
            int mins = (int)Math.Round((seconds - hours * 3600) / 60.0);
            if (mins >= 60)
            {
                hours++;
                mins = 0;
            }
            if (mins <= 0)
            {
                if (hours == 1) return "About 1 hour";
                return "About " + hours.ToString() + " hours";
            }
            return "About " + hours.ToString() + " h " + mins.ToString() + " min";
        }

        public static string FormatElapsed(TimeSpan span)
        {
            if (span.Ticks < 0) span = TimeSpan.Zero;
            int hours = (int)span.TotalHours;
            if (hours >= 1)
                return hours.ToString() + ":" + span.Minutes.ToString("00") + ":" + span.Seconds.ToString("00");
            return span.Minutes.ToString() + ":" + span.Seconds.ToString("00");
        }

        public static int Percent(double ratio)
        {
            if (ratio < 0) ratio = 0;
            if (ratio > 1) ratio = 1;
            int pct = (int)Math.Round(ratio * 100.0);
            if (pct >= 100 && ratio < 1) pct = 99;
            if (ratio >= 1) pct = 100;
            if (pct < 0) pct = 0;
            return pct;
        }
    }

    internal static class Win7Paint
    {
        public static void Bar(Graphics g, Rectangle bounds, double value, bool marquee, bool failed, bool animate)
        {
            if (bounds.Width < 4 || bounds.Height < 4) return;
            Rectangle track = bounds;
            using (LinearGradientBrush background = new LinearGradientBrush(
                track,
                Color.FromArgb(246, 246, 246),
                Color.FromArgb(226, 226, 226),
                LinearGradientMode.Vertical))
            {
                g.FillRectangle(background, track);
            }
            using (Pen border = new Pen(Color.FromArgb(188, 188, 188)))
                g.DrawRectangle(border, track.X, track.Y, track.Width - 1, track.Height - 1);
            using (Pen inset = new Pen(Color.FromArgb(255, 255, 255)))
            {
                if (track.Width > 3)
                    g.DrawLine(inset, track.X + 1, track.Bottom - 2, track.Right - 2, track.Bottom - 2);
            }

            Rectangle fill = Rectangle.Empty;
            Rectangle inner = new Rectangle(track.X + 1, track.Y + 1, track.Width - 2, track.Height - 2);
            if (inner.Width <= 0 || inner.Height <= 0) return;
            if (marquee)
            {
                int width = Math.Max(28, inner.Width / 3);
                float phase = (Environment.TickCount % 1400) / 1400f;
                int travel = inner.Width + width;
                int x = inner.X - width + (int)(phase * travel);
                fill = Rectangle.Intersect(new Rectangle(x, inner.Y, width, inner.Height), inner);
            }
            else if (value > 0)
            {
                int width = (int)Math.Round(inner.Width * Math.Min(1.0, value));
                if (width < 1) width = 1;
                if (width > inner.Width) width = inner.Width;
                fill = new Rectangle(inner.X, inner.Y, width, inner.Height);
            }

            if (fill.Width <= 0 || fill.Height <= 0) return;
            Color top = failed ? Color.FromArgb(244, 170, 170) : Color.FromArgb(186, 236, 150);
            Color bottom = failed ? Color.FromArgb(176, 32, 32) : Color.FromArgb(16, 158, 42);
            using (LinearGradientBrush brush = new LinearGradientBrush(fill, top, bottom, LinearGradientMode.Vertical))
                g.FillRectangle(brush, fill);

            int glossH = Math.Max(1, fill.Height / 2);
            Rectangle gloss = new Rectangle(fill.X, fill.Y, fill.Width, glossH);
            if (gloss.Width > 0 && gloss.Height > 0)
            {
                using (LinearGradientBrush shine = new LinearGradientBrush(
                    gloss,
                    Color.FromArgb(170, 255, 255, 255),
                    Color.FromArgb(20, 255, 255, 255),
                    LinearGradientMode.Vertical))
                {
                    g.FillRectangle(shine, gloss);
                }
            }

            if (animate && !failed && fill.Width > 8)
            {
                float phase = (Environment.TickCount % 1700) / 1700f;
                int sheenW = Math.Max(14, fill.Width / 3);
                int sx = fill.X + (int)(phase * (fill.Width + sheenW)) - sheenW;
                Rectangle sheen = Rectangle.Intersect(new Rectangle(sx, fill.Y, sheenW, fill.Height), fill);
                if (sheen.Width > 0 && sheen.Height > 0)
                {
                    using (LinearGradientBrush band = new LinearGradientBrush(
                        sheen,
                        Color.FromArgb(0, 255, 255, 255),
                        Color.FromArgb(110, 255, 255, 255),
                        LinearGradientMode.Horizontal))
                    {
                        g.FillRectangle(band, sheen);
                    }
                }
            }
        }

        public static void ButtonFace(Graphics g, Rectangle bounds, string text, Font font, bool enabled, bool hover, bool pressed, bool focused, bool isDefault)
        {
            g.Clear(Win7.Window);
            Rectangle face = bounds;
            face.Inflate(-1, -1);
            if (face.Width < 2 || face.Height < 2) return;

            Color border;
            Color top;
            Color bottom;
            Color fore;
            if (!enabled)
            {
                border = Color.FromArgb(198, 198, 198);
                top = Color.FromArgb(246, 246, 246);
                bottom = Color.FromArgb(236, 236, 236);
                fore = Color.FromArgb(160, 160, 160);
            }
            else if (pressed)
            {
                border = Color.FromArgb(44, 98, 139);
                top = Color.FromArgb(166, 214, 242);
                bottom = Color.FromArgb(214, 236, 248);
                fore = Color.Black;
            }
            else if (hover)
            {
                border = Color.FromArgb(60, 127, 177);
                top = Color.FromArgb(234, 246, 253);
                bottom = Color.FromArgb(188, 228, 252);
                fore = Color.Black;
            }
            else
            {
                border = isDefault ? Color.FromArgb(28, 81, 128) : Color.FromArgb(112, 112, 112);
                top = Color.FromArgb(252, 252, 252);
                bottom = Color.FromArgb(225, 225, 225);
                fore = Color.Black;
            }

            using (LinearGradientBrush brush = new LinearGradientBrush(face, top, bottom, LinearGradientMode.Vertical))
                g.FillRectangle(brush, face);
            if (enabled && !pressed)
            {
                using (Pen shine = new Pen(Color.FromArgb(200, 255, 255, 255)))
                    g.DrawLine(shine, face.X + 1, face.Y + 1, face.Right - 2, face.Y + 1);
            }
            using (Pen pen = new Pen(border))
                g.DrawRectangle(pen, face.X, face.Y, face.Width - 1, face.Height - 1);

            Rectangle textRect = face;
            if (pressed) textRect.Offset(0, 1);
            TextRenderer.DrawText(
                g,
                StripMnemonic(text),
                font,
                textRect,
                fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

            if (focused && enabled)
            {
                Rectangle cue = face;
                cue.Inflate(-3, -3);
                if (cue.Width > 0 && cue.Height > 0)
                    ControlPaint.DrawFocusRectangle(g, cue);
            }
        }

        static string StripMnemonic(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Replace("&", "");
        }
    }

    internal sealed class Win7Button : Button
    {
        bool hover;
        bool pressed;
        bool isDefault;

        public Win7Button()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            BackColor = Win7.Window;
            ForeColor = Color.Black;
            TabStop = true;
        }

        public override void NotifyDefault(bool value)
        {
            base.NotifyDefault(value);
            isDefault = value;
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = false;
            pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                pressed = true;
                Invalidate();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Win7Paint.ButtonFace(e.Graphics, ClientRectangle, Text, Font, Enabled, hover, pressed, Focused, isDefault);
        }
    }

    internal sealed class Win7ProgressBar : Control
    {
        double value;
        bool marquee;

        public Win7ProgressBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            TabStop = false;
            Height = 20;
            BackColor = Win7.Window;
        }

        public double Value
        {
            get { return value; }
            set
            {
                if (value < 0) value = 0;
                if (value > 1) value = 1;
                this.value = value;
            }
        }

        public bool Marquee
        {
            get { return marquee; }
            set { marquee = value; }
        }

        public void Animate()
        {
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Win7Paint.Bar(e.Graphics, new Rectangle(0, 0, Width, Height), value, marquee, false, marquee || value > 0 && value < 1);
        }
    }

    internal sealed class MetricStrip : Control
    {
        const int Slots = 5;
        readonly Label[] captions = new Label[Slots];
        readonly Label[] values = new Label[Slots];
        static readonly string[] Names = new string[] { "Speed", "Items per second", "Files left", "Time left", "Elapsed" };

        public MetricStrip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Color.White;
            TabStop = false;
            for (int i = 0; i < Slots; i++)
            {
                Label caption = new Label();
                caption.AutoSize = false;
                caption.AutoEllipsis = true;
                caption.BackColor = Color.White;
                caption.ForeColor = Win7.Subtle;
                caption.Font = Win7.SmallFont;
                caption.Text = Names[i];
                captions[i] = caption;
                Controls.Add(caption);

                Label value = new Label();
                value.AutoSize = false;
                value.AutoEllipsis = true;
                value.BackColor = Color.White;
                value.ForeColor = Win7.Instruction;
                value.Font = Win7.ValueFont;
                value.Text = "-";
                values[i] = value;
                Controls.Add(value);
            }
        }

        public int PreferredHeight
        {
            get { return 8 + Win7.SmallFont.Height + 2 + Win7.ValueFont.Height + 8; }
        }

        public void SetValue(int index, string text)
        {
            if (index < 0 || index >= Slots) return;
            if (values[index].Text != text)
                values[index].Text = text;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            int col = Math.Max(1, Width / Slots);
            int captionH = Win7.SmallFont.Height + 2;
            int valueH = Win7.ValueFont.Height + 2;
            for (int i = 0; i < Slots; i++)
            {
                int x = i * col + 10;
                int w = Math.Max(10, col - 18);
                captions[i].SetBounds(x, 6, w, captionH);
                values[i].SetBounds(x, 6 + captionH, w, valueH);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Color.White);
            using (Pen border = new Pen(Color.FromArgb(176, 176, 176)))
                g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
            int col = Math.Max(1, Width / Slots);
            using (Pen line = new Pen(Color.FromArgb(230, 230, 230)))
            {
                for (int i = 1; i < Slots; i++)
                    g.DrawLine(line, i * col, 6, i * col, Height - 6);
            }
        }
    }

    internal sealed class Win7Status : Control
    {
        public string Message = "";
        public bool IsError;

        public Win7Status()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            TabStop = false;
            BackColor = Win7.Window;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Win7.Window);
            using (Pen pen = new Pen(Color.FromArgb(192, 192, 192)))
                g.DrawLine(pen, 0, 0, Width, 0);
            Color color = IsError ? Win7.FailText : Color.FromArgb(48, 48, 48);
            Rectangle rect = new Rectangle(8, 1, Math.Max(1, Width - 16), Math.Max(1, Height - 2));
            TextRenderer.DrawText(
                g,
                Message ?? "",
                Font,
                rect,
                color,
                Win7.Window,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }
    }
}
