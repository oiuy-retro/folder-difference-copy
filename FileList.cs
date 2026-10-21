using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace FolderDiffCopy
{
    internal delegate void JobHandler(FileJob job);

    internal sealed class RemainingList : Control
    {
        CopySession session;
        FileJob[] demo;
        FileJob[] window = new FileJob[1];
        VScrollBar scroll;
        int total;
        int first;
        int hover = -1;
        int selected = -1;
        bool userScroll;
        FileJob anchor;
        FileJob selectedJob;
        string lastTip = "";
        ToolTip tip;

        public string EmptyText = "Files that still need to be copied will show up here.";
        public event JobHandler AttentionChanged;

        public RemainingList()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Color.White;
            TabStop = true;
            scroll = new VScrollBar();
            scroll.Dock = DockStyle.Right;
            scroll.Visible = false;
            scroll.ValueChanged += OnScrollValue;
            Controls.Add(scroll);
            tip = new ToolTip();
            tip.InitialDelay = 400;
            tip.ReshowDelay = 200;
            tip.ShowAlways = true;
        }

        public int ItemCount
        {
            get { return total; }
        }

        public void SetSession(CopySession value)
        {
            session = value;
            demo = null;
            ResetView();
        }

        public void SetDemo(FileJob[] jobs)
        {
            session = null;
            demo = jobs ?? new FileJob[0];
            ResetView();
        }

        void ResetView()
        {
            first = 0;
            anchor = null;
            selectedJob = null;
            hover = -1;
            selected = -1;
            total = 0;
            lastTip = "";
            tip.Hide(this);
            Pull();
            Invalidate();
        }

        public void Pull()
        {
            int visible = Math.Max(1, VisibleRows());
            int take = visible + 1;
            if (window.Length < take) window = new FileJob[take];

            if (session != null && selectedJob != null)
            {
                int sel = session.IndexOf(selectedJob);
                if (sel >= 0) selected = sel;
                else
                {
                    selected = -1;
                    selectedJob = null;
                }
            }

            if (session != null && anchor != null && !userScroll)
            {
                int idx = session.IndexOf(anchor);
                if (idx >= 0) first = idx;
                else
                {
                    int idx2 = IndexOfNextVisible(anchor);
                    if (idx2 >= 0) first = idx2;
                }
            }
            userScroll = false;

            int count = SourceCount();
            int limit = Math.Max(0, count - visible);
            if (first > limit) first = limit;
            if (first < 0) first = 0;
            int copiedFrom = first;
            SourceCopy(first, window);
            total = count;
            ConfigureScroll(total, visible);
            if (first != copiedFrom)
                SourceCopy(first, window);
            anchor = JobAt(first);
        }

        int IndexOfNextVisible(FileJob job)
        {
            bool seen = false;
            for (int i = 0; i < window.Length; i++)
            {
                FileJob item = window[i];
                if (!seen)
                {
                    if (object.ReferenceEquals(item, job)) seen = true;
                    continue;
                }
                if (item == null) continue;
                int idx = session.IndexOf(item);
                if (idx >= 0) return idx;
            }
            return -1;
        }

        public void Wheel(int delta)
        {
            int lines = SystemInformation.MouseWheelScrollLines;
            if (lines < 0) lines = VisibleRows();
            if (lines < 1) lines = 3;
            int direction = delta > 0 ? -1 : 1;
            ScrollTo(first + direction * lines);
        }

        int HeaderHeight
        {
            get { return Math.Max(22, Font.Height + 10); }
        }

        int RowHeight
        {
            get { return Math.Max(26, Font.Height + 12); }
        }

        int VisibleRows()
        {
            int height = Height - HeaderHeight - 2;
            if (height < RowHeight) return 1;
            return Math.Max(1, height / RowHeight);
        }

        int ContentWidth
        {
            get { return scroll.Visible ? Math.Max(1, scroll.Left) : Width; }
        }

        int SourceCount()
        {
            if (session != null) return session.Count;
            if (demo != null) return demo.Length;
            return 0;
        }

        void SourceCopy(int start, FileJob[] buffer)
        {
            if (session != null)
            {
                session.Snapshot(start, buffer);
                return;
            }
            for (int i = 0; i < buffer.Length; i++)
            {
                int index = start + i;
                buffer[i] = demo != null && index >= 0 && index < demo.Length ? demo[index] : null;
            }
        }

        FileJob JobAt(int index)
        {
            if (index < 0 || index >= SourceCount()) return null;
            if (session != null) return session.Get(index);
            if (demo != null && index < demo.Length) return demo[index];
            return null;
        }

        void ConfigureScroll(int count, int visibleRows)
        {
            try
            {
                if (count <= visibleRows)
                {
                    if (scroll.Visible) scroll.Visible = false;
                    return;
                }
                if (!scroll.Visible) scroll.Visible = true;
                int maximum = Math.Max(0, count - 1);
                int large = Math.Max(1, Math.Min(visibleRows, maximum + 1));
                if (scroll.Value > maximum) scroll.Value = 0;
                scroll.SmallChange = 1;
                scroll.LargeChange = 1;
                scroll.Maximum = Math.Max(maximum, 1);
                scroll.LargeChange = large;
                int limit = Math.Max(0, count - visibleRows);
                if (first > limit) first = limit;
                if (scroll.Value != first && first <= scroll.Maximum)
                    scroll.Value = first;
            }
            catch (ArgumentException)
            {
            }
        }

        void OnScrollValue(object sender, EventArgs e)
        {
            if (!scroll.Visible) return;
            if (first == scroll.Value) return;
            userScroll = true;
            first = scroll.Value;
            Pull();
            Invalidate();
        }

        void ScrollTo(int row)
        {
            int visible = VisibleRows();
            int limit = Math.Max(0, total - visible);
            if (row < 0) row = 0;
            if (row > limit) row = limit;
            userScroll = true;
            first = row;
            Pull();
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            Wheel(e.Delta);
            base.OnMouseWheel(e);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Pull();
            Invalidate();
        }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            if (key == Keys.Up || key == Keys.Down || key == Keys.PageUp || key == Keys.PageDown || key == Keys.Home || key == Keys.End)
                return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            int visible = VisibleRows();
            if (e.KeyCode == Keys.Down) MoveSelection(selected < 0 ? first : selected + 1);
            else if (e.KeyCode == Keys.Up) MoveSelection(selected < 0 ? first : selected - 1);
            else if (e.KeyCode == Keys.PageDown) MoveSelection((selected < 0 ? first : selected) + visible);
            else if (e.KeyCode == Keys.PageUp) MoveSelection((selected < 0 ? first : selected) - visible);
            else if (e.KeyCode == Keys.Home) MoveSelection(0);
            else if (e.KeyCode == Keys.End) MoveSelection(total - 1);
            else
            {
                base.OnKeyDown(e);
                return;
            }
            e.Handled = true;
        }

        void MoveSelection(int index)
        {
            if (total <= 0) return;
            if (index < 0) index = 0;
            if (index >= total) index = total - 1;
            selected = index;
            selectedJob = JobAt(index);
            int visible = VisibleRows();
            if (selected < first) ScrollTo(selected);
            else if (selected >= first + visible) ScrollTo(selected - visible + 1);
            RaiseAttention();
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int hit = HitTest(e.Y);
            if (hit != hover)
            {
                hover = hit;
                RaiseAttention();
                UpdateTip(e.Location);
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = -1;
            lastTip = "";
            tip.Hide(this);
            RaiseAttention();
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            int hit = HitTest(e.Y);
            if (hit >= 0)
            {
                selected = hit;
                selectedJob = JobAt(hit);
                RaiseAttention();
                Invalidate();
            }
            base.OnMouseDown(e);
        }

        void RaiseAttention()
        {
            if (AttentionChanged == null) return;
            FileJob job = null;
            if (hover >= 0) job = JobAt(hover);
            else if (selected >= 0) job = JobAt(selected);
            AttentionChanged(job);
        }

        void UpdateTip(Point location)
        {
            FileJob job = hover >= 0 ? JobAt(hover) : null;
            if (job == null)
            {
                if (lastTip.Length != 0)
                {
                    lastTip = "";
                    tip.Hide(this);
                }
                return;
            }
            string text = job.DisplayPath;
            if (!string.IsNullOrEmpty(job.Error))
                text = text + "\r\n" + job.Error;
            if (text == lastTip) return;
            lastTip = text;
            tip.Show(text, this, location.X + 12, location.Y + 18, 5000);
        }

        int HitTest(int y)
        {
            if (y < HeaderHeight + 1) return -1;
            int index = first + (y - HeaderHeight - 1) / RowHeight;
            if (index < first || index >= total) return -1;
            return index;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Color.White);
            int width = ContentWidth;
            Rectangle header = new Rectangle(1, 1, Math.Max(1, width - 2), HeaderHeight);
            using (System.Drawing.Drawing2D.LinearGradientBrush brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                header,
                Color.White,
                Color.FromArgb(236, 236, 236),
                System.Drawing.Drawing2D.LinearGradientMode.Vertical))
            {
                g.FillRectangle(brush, header);
            }
            using (Pen pen = new Pen(Color.FromArgb(213, 213, 213)))
                g.DrawLine(pen, header.X, header.Bottom - 1, header.Right, header.Bottom - 1);

            int progressW;
            int sizeW;
            int nameW;
            Columns(header.Width, out nameW, out sizeW, out progressW);
            TextFormatFlags headerFlags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.Left;
            TextRenderer.DrawText(g, "File", Font, new Rectangle(8, header.Y, Math.Max(1, nameW - 12), header.Height), Win7.Text, headerFlags);
            TextRenderer.DrawText(g, "Size", Font, new Rectangle(nameW, header.Y, Math.Max(1, sizeW - 8), header.Height), Win7.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.Right);
            TextRenderer.DrawText(g, "Progress", Font, new Rectangle(nameW + sizeW + 8, header.Y, Math.Max(1, progressW - 12), header.Height), Win7.Text, headerFlags);

            if (total <= 0)
            {
                Rectangle empty = new Rectangle(8, HeaderHeight + 8, Math.Max(1, width - 16), Math.Max(1, Height - HeaderHeight - 16));
                TextRenderer.DrawText(g, EmptyText ?? "", Font, empty, Win7.Subtle, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            }
            else
            {
                int y = header.Bottom;
                int rows = VisibleRows();
                for (int i = 0; i < rows; i++)
                {
                    int index = first + i;
                    if (index >= total) break;
                    FileJob job = i < window.Length ? window[i] : null;
                    PaintRow(g, job, index, new Rectangle(1, y, Math.Max(1, width - 2), RowHeight), nameW, sizeW, progressW);
                    y += RowHeight;
                }
            }

            Color border = Focused ? Win7.Focus : Win7.Border;
            using (Pen pen = new Pen(border))
                g.DrawRectangle(pen, 0, 0, width - 1, Height - 1);
        }

        void Columns(int width, out int name, out int size, out int progress)
        {
            progress = Math.Min(250, Math.Max(150, width / 3));
            size = Math.Min(110, Math.Max(72, width / 6));
            name = width - progress - size;
            if (name < 80)
            {
                progress = Math.Max(120, width - 80 - 72);
                size = 72;
                name = Math.Max(40, width - progress - size);
            }
        }

        void PaintRow(Graphics g, FileJob job, int index, Rectangle row, int nameW, int sizeW, int progressW)
        {
            bool isSelected = index == selected;
            bool isHover = index == hover && !isSelected;
            bool failed = job != null && job.State == FileJob.Failed;
            Color back = isSelected ? Win7.Select : (failed ? Win7.FailBack : (isHover ? Win7.Hover : Color.White));
            using (SolidBrush brush = new SolidBrush(back))
                g.FillRectangle(brush, row);

            if (job == null) return;
            Color fore = failed && !isSelected ? Win7.FailText : Win7.Text;
            TextRenderer.DrawText(
                g,
                job.DisplayPath,
                Font,
                new Rectangle(row.X + 8, row.Y, Math.Max(1, nameW - 12), row.Height),
                fore,
                back,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.Left);
            TextRenderer.DrawText(
                g,
                Readout.FormatBytes(job.Size),
                Font,
                new Rectangle(row.X + nameW, row.Y, Math.Max(1, sizeW - 8), row.Height),
                fore,
                back,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.Right);

            int barH = Math.Max(12, Math.Min(16, row.Height - 10));
            int percentW = 52;
            int barW = progressW - percentW - 16;
            if (barW < 20) barW = 20;
            Rectangle bar = new Rectangle(row.X + nameW + sizeW + 8, row.Y + (row.Height - barH) / 2, barW, barH);
            double ratio = Ratio(job);
            bool copying = job.State == FileJob.Copying;
            Win7Paint.Bar(g, bar, ratio, false, failed, copying && ratio > 0 && ratio < 1);

            string percent = failed ? "Failed" : (Readout.Percent(ratio).ToString() + "%");
            TextRenderer.DrawText(
                g,
                percent,
                Font,
                new Rectangle(bar.Right + 4, row.Y, Math.Max(1, row.Right - bar.Right - 8), row.Height),
                failed ? Win7.FailText : Win7.Muted,
                back,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.Left);
        }

        static double Ratio(FileJob job)
        {
            if (job.State == FileJob.Done) return 1;
            if (job.Size <= 0) return 0;
            long copied = Interlocked.Read(ref job.Copied);
            if (copied < 0) return 0;
            double ratio = copied / (double)job.Size;
            if (ratio < 0) return 0;
            if (ratio > 1) return 1;
            return ratio;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && tip != null)
            {
                tip.Dispose();
                tip = null;
            }
            base.Dispose(disposing);
        }
    }
}
