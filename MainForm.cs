using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace FolderDiffCopy
{
    internal sealed class MainForm : Form
    {
        const int PhaseIdle = 0;
        const int PhaseScan = 1;
        const int PhaseCopy = 2;
        const int PhaseWait = 3;

        readonly Label labelA = new Label();
        readonly Label labelB = new Label();
        readonly Label labelParallel = new Label();
        readonly Label labelProfile = new Label();
        readonly ComboBox profileCombo = new ComboBox();
        readonly CheckBox folderOrder = new CheckBox();
        readonly CheckBox largeSmall = new CheckBox();
        readonly CheckBox smallestFirst = new CheckBox();
        readonly CheckBox largestFirst = new CheckBox();
        readonly CheckBox bothWays = new CheckBox();
        bool applyingOptions;
        readonly Label filesLabel = new Label();
        readonly Label percentLabel = new Label();
        readonly Label detailLabel = new Label();
        readonly TextBox folderA = new TextBox();
        readonly TextBox folderB = new TextBox();
        readonly NumericUpDown parallel = new NumericUpDown();
        readonly CheckBox startAt = new CheckBox();
        readonly DateTimePicker startWhen = new DateTimePicker();
        readonly Win7Button browseA = new Win7Button();
        readonly Win7Button browseB = new Win7Button();
        readonly Win7Button previewButton = new Win7Button();
        readonly Win7Button startButton = new Win7Button();
        readonly Win7Button stopButton = new Win7Button();
        readonly Win7ProgressBar progress = new Win7ProgressBar();
        readonly MetricStrip metrics = new MetricStrip();
        readonly RemainingList list = new RemainingList();
        readonly Win7Status status = new Win7Status();
        readonly Timer timer = new Timer();
        readonly ToolTip tips = new ToolTip();
        readonly List<SpeedSample> samples = new List<SpeedSample>();
        readonly List<int> itemTicks = new List<int>();

        int phase;
        ScanStatus scan;
        CopySession session;
        bool closeAfterStop;
        bool finishing;
        string persistentStatus = "Choose Folder A and Folder B.";
        string hoverStatus;
        bool statusError;
        int dirsCreated;
        int linksSkipped;
        int unreadable;
        string firstProblem = "";
        int sampleStart;
        bool sizedForDpi;
        bool runClock;
        DateTime runStarted;
        DateTime scheduledFor;
        bool previewOnly;
        bool suppressFolderEvent;
        string previewedA;
        string previewedB;
        FolderSpot phoneA;
        FolderSpot phoneB;

        struct SpeedSample
        {
            public int Tick;
            public long Bytes;
        }

        public MainForm()
        {
            Font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
            Text = "Folder Difference Copy";
            BackColor = Win7.Window;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(920, 680);
            MinimumSize = new Size(840, 640);
            FormBorderStyle = FormBorderStyle.Sizable;
            KeyPreview = true;

            Build();
            LayoutControls();
            ShowIdleMetrics();
            SetStatus(persistentStatus, false);
            timer.Interval = 100;
            timer.Tick += OnTick;
        }

        void Build()
        {
            tips.ShowAlways = true;
            tips.InitialDelay = 500;

            labelA.Text = "Folder &A";
            labelA.AutoSize = true;
            labelA.ForeColor = Win7.Text;
            labelB.Text = "Folder &B";
            labelB.AutoSize = true;
            labelB.ForeColor = Win7.Text;
            labelParallel.Text = "Para&llelization";
            labelParallel.AutoSize = true;
            labelParallel.ForeColor = Win7.Text;
            labelProfile.Text = "&Profile";
            labelProfile.AutoSize = true;
            labelProfile.ForeColor = Win7.Text;
            folderOrder.Text = "Folder order";
            folderOrder.AutoSize = true;
            folderOrder.CheckedChanged += OnOrderOptionChanged;
            largeSmall.Text = "Large beside small";
            largeSmall.AutoSize = true;
            largeSmall.CheckedChanged += OnOrderOptionChanged;
            smallestFirst.Text = "Smallest to largest";
            smallestFirst.AutoSize = true;
            smallestFirst.CheckedChanged += OnOrderOptionChanged;
            largestFirst.Text = "Largest to smallest";
            largestFirst.AutoSize = true;
            largestFirst.CheckedChanged += OnOrderOptionChanged;
            bothWays.Text = "Copy both &ways";
            bothWays.AutoSize = true;
            bothWays.CheckedChanged += OnBothWaysChanged;
            filesLabel.Text = "Files remaining";
            filesLabel.AutoSize = true;
            filesLabel.ForeColor = Win7.Text;
            percentLabel.AutoSize = false;
            percentLabel.TextAlign = ContentAlignment.MiddleRight;
            percentLabel.ForeColor = Win7.Text;
            detailLabel.AutoSize = false;
            detailLabel.AutoEllipsis = true;
            detailLabel.ForeColor = Win7.Muted;

            StyleBox(folderA);
            StyleBox(folderB);
            folderA.AllowDrop = true;
            folderB.AllowDrop = true;
            folderA.DragEnter += OnDragEnter;
            folderB.DragEnter += OnDragEnter;
            folderA.DragDrop += OnDropA;
            folderB.DragDrop += OnDropB;
            folderA.TextChanged += OnFolderTextChanged;
            folderB.TextChanged += OnFolderTextChanged;

            parallel.Minimum = 1;
            parallel.Maximum = 32;
            parallel.Value = 1;
            parallel.TextAlign = HorizontalAlignment.Center;
            parallel.ValueChanged += OnParallelChanged;
            tips.SetToolTip(labelA, "Copy from here.");
            tips.SetToolTip(folderA, "Copy from here.");
            tips.SetToolTip(browseA, "Choose Folder A.");
            tips.SetToolTip(labelB, "Copy to here.");
            tips.SetToolTip(folderB, "Copy to here.");
            tips.SetToolTip(browseB, "Choose Folder B.");
            tips.SetToolTip(labelParallel, "Files copied at the same time.");
            tips.SetToolTip(parallel, "1 to 32.");

            profileCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            profileCombo.FlatStyle = FlatStyle.Standard;
            int dropWidth = 0;
            for (int i = 0; i < DriveChoices.Length; i++)
            {
                profileCombo.Items.Add(DriveChoices[i].Name);
                int textWidth = TextRenderer.MeasureText(DriveChoices[i].Name, Font).Width;
                if (textWidth > dropWidth) dropWidth = textWidth;
            }
            profileCombo.DropDownWidth = dropWidth + 28;
            profileCombo.SelectedIndexChanged += OnDriveProfileChanged;
            profileCombo.SelectedIndex = 0;
            tips.SetToolTip(labelProfile, "Drive preset.");
            tips.SetToolTip(profileCombo, "Drive preset.");
            tips.SetToolTip(folderOrder, "Copy in folder order.");
            tips.SetToolTip(largeSmall, "Large file beside small ones.");
            tips.SetToolTip(smallestFirst, "Copy smallest files first.");
            tips.SetToolTip(largestFirst, "Copy largest files first.");
            tips.SetToolTip(bothWays, "Each folder gets the other's files.");

            startAt.Text = "Start at";
            startAt.AutoSize = true;
            startAt.ForeColor = Win7.Text;
            startAt.CheckedChanged += OnStartAtChanged;
            DateTime soon = DateTime.Now.AddHours(1);
            startWhen.Format = DateTimePickerFormat.Custom;
            startWhen.CustomFormat = "M/d/yyyy  h:mm tt";
            startWhen.Value = new DateTime(soon.Year, soon.Month, soon.Day, soon.Hour, 0, 0);
            startWhen.Enabled = false;
            tips.SetToolTip(startAt, "Start later.");
            tips.SetToolTip(startWhen, "When to start.");

            browseA.Text = "Browse...";
            browseB.Text = "Browse...";
            previewButton.Text = "Pre&view";
            previewButton.Enabled = false;
            startButton.Text = "&Start";
            stopButton.Text = "S&top";
            stopButton.Enabled = false;
            browseA.Click += delegate { Browse(folderA, "Select Folder A"); };
            browseB.Click += delegate { Browse(folderB, "Select Folder B"); };
            previewButton.Click += OnPreview;
            startButton.Click += OnStart;
            stopButton.Click += OnStop;
            tips.SetToolTip(previewButton, "List the files, size, and count.");
            tips.SetToolTip(stopButton, "Stop. Finished files stay.");
            UpdateWayTips();
            tips.SetToolTip(progress, "Copy progress.");
            tips.SetToolTip(percentLabel, "Copy progress.");
            tips.SetToolTip(detailLabel, "Bytes copied.");
            tips.SetToolTip(filesLabel, "Still to copy.");
            tips.SetToolTip(metrics, "Speed, files per second, left, time left, elapsed.");

            list.AttentionChanged += OnListAttention;
            AcceptButton = startButton;
            CancelButton = stopButton;

            Control[] controls = new Control[] {
                labelA, folderA, browseA, labelB, folderB, browseB,
                labelParallel, parallel, labelProfile, profileCombo, bothWays, folderOrder, largeSmall, smallestFirst, largestFirst, previewButton, startButton, stopButton,
                startAt, startWhen,
                progress, percentLabel, detailLabel, metrics, filesLabel, list, status
            };
            for (int i = 0; i < controls.Length; i++)
                Controls.Add(controls[i]);

            int tab = 0;
            labelA.TabIndex = tab++;
            folderA.TabIndex = tab++;
            browseA.TabIndex = tab++;
            labelB.TabIndex = tab++;
            folderB.TabIndex = tab++;
            browseB.TabIndex = tab++;
            labelParallel.TabIndex = tab++;
            parallel.TabIndex = tab++;
            labelProfile.TabIndex = tab++;
            profileCombo.TabIndex = tab++;
            bothWays.TabIndex = tab++;
            folderOrder.TabIndex = tab++;
            largeSmall.TabIndex = tab++;
            smallestFirst.TabIndex = tab++;
            largestFirst.TabIndex = tab++;
            startAt.TabIndex = tab++;
            startWhen.TabIndex = tab++;
            previewButton.TabIndex = tab++;
            startButton.TabIndex = tab++;
            stopButton.TabIndex = tab++;
            list.TabIndex = tab++;
        }

        static void StyleBox(TextBox box)
        {
            box.BorderStyle = BorderStyle.FixedSingle;
            box.BackColor = Color.White;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (sizedForDpi) return;
            sizedForDpi = true;
            using (Graphics g = CreateGraphics())
            {
                float scale = g.DpiX / 96f;
                if (scale > 1.05f)
                {
                    ClientSize = new Size((int)(920 * scale), (int)(680 * scale));
                    MinimumSize = new Size((int)(840 * scale), (int)(640 * scale));
                }
            }
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            LayoutControls();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (phase != PhaseIdle)
            {
                e.Cancel = true;
                closeAfterStop = true;
                OnStop(this, EventArgs.Empty);
                SetStatus("Stopping...", false);
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void WndProc(ref Message m)
        {
            const int Wheel = 0x020A;
            if (m.Msg == Wheel && list != null && !list.IsDisposed)
            {
                Point point = list.PointToClient(Cursor.Position);
                if (list.ClientRectangle.Contains(point))
                {
                    int word = (int)(m.WParam.ToInt64() & 0xFFFFFFFF);
                    int delta = (short)((word >> 16) & 0xFFFF);
                    list.Wheel(delta);
                    return;
                }
            }
            base.WndProc(ref m);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutControls();
        }

        void LayoutControls()
        {
            if (folderA == null || ClientSize.Width < 40) return;
            int pad = 12;
            int width = ClientSize.Width;
            int height = ClientSize.Height;
            int editH = Math.Max(23, folderA.PreferredHeight);
            int labelH = Font.Height + 2;
            int buttonW = 88;

            int y = pad;

            labelA.Location = new Point(pad, y);
            y += labelA.Height + 2;
            browseA.SetBounds(width - pad - buttonW, y, buttonW, editH);
            folderA.SetBounds(pad, y, Math.Max(40, browseA.Left - pad - 8), editH);
            y += editH + 8;

            labelB.Location = new Point(pad, y);
            y += labelB.Height + 2;
            browseB.SetBounds(width - pad - buttonW, y, buttonW, editH);
            folderB.SetBounds(pad, y, Math.Max(40, browseB.Left - pad - 8), editH);
            y += editH + 12;

            stopButton.SetBounds(width - pad - buttonW, y, buttonW, editH);
            startButton.SetBounds(stopButton.Left - 8 - buttonW, y, buttonW, editH);
            previewButton.SetBounds(startButton.Left - 8 - buttonW, y, buttonW, editH);
            labelParallel.Location = new Point(pad, y + Math.Max(0, (editH - labelParallel.Height) / 2));
            parallel.SetBounds(labelParallel.Right + 8, y + Math.Max(0, (editH - 23) / 2), 64, editH);
            labelProfile.Location = new Point(parallel.Right + 12, y + Math.Max(0, (editH - labelProfile.Height) / 2));
            int comboLeft = labelProfile.Right + 8;
            int comboWidth = Math.Max(80, previewButton.Left - comboLeft - 8);
            profileCombo.SetBounds(comboLeft, y + Math.Max(0, (editH - 23) / 2), comboWidth, editH);
            y += editH + 6;

            int optionX = pad;
            int optionY = y;
            PlaceOption(bothWays, ref optionX, ref optionY, pad, width, editH);
            PlaceOption(folderOrder, ref optionX, ref optionY, pad, width, editH);
            PlaceOption(largeSmall, ref optionX, ref optionY, pad, width, editH);
            PlaceOption(smallestFirst, ref optionX, ref optionY, pad, width, editH);
            PlaceOption(largestFirst, ref optionX, ref optionY, pad, width, editH);
            y = optionY + editH + 8;

            startAt.Location = new Point(pad, y + Math.Max(0, (editH - Math.Max(startAt.Height, labelH)) / 2));
            int whenLeft = Math.Max(startAt.Right, pad + 72) + 8;
            startWhen.SetBounds(whenLeft, y, 210, editH);
            y += editH + 14;

            int percentW = 52;
            int barH = Math.Max(18, Font.Height + 6);
            progress.SetBounds(pad, y, Math.Max(40, width - pad * 2 - percentW - 8), barH);
            percentLabel.SetBounds(progress.Right + 6, y, percentW, barH);
            y += barH + 2;
            detailLabel.SetBounds(pad, y, Math.Max(40, width - pad * 2), labelH);
            y += labelH + 8;

            metrics.SetBounds(pad, y, Math.Max(40, width - pad * 2), metrics.PreferredHeight);
            y += metrics.Height + 10;
            filesLabel.Location = new Point(pad, y);
            y += filesLabel.Height + 4;

            int statusH = Font.Height + 14;
            status.SetBounds(0, height - statusH, width, statusH);
            int listH = status.Top - 8 - y;
            if (listH < 48) listH = 48;
            list.SetBounds(pad, y, Math.Max(40, width - pad * 2), listH);
        }

        void Browse(TextBox box, string title)
        {
            FolderSpot phone = PhoneFor(box);
            string current = phone != null ? null : Paths.Normalize(box.Text);
            FolderSpot picked = FolderDialog.Pick(this, title, current, phone == null ? null : phone.PhoneItem);
            if (picked == null) return;
            if (picked.Error != null)
            {
                SetStatus(picked.Error, true);
                MessageBox.Show(this, picked.Error, "Folder Difference Copy", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (picked.IsPhone)
            {
                if (box == folderA) phoneA = picked;
                else phoneB = picked;
                box.Text = picked.Text;
            }
            else if (!string.IsNullOrEmpty(picked.Path))
            {
                box.Text = picked.Path;
            }
            ApplyOptionLocks();
        }

        // The phone folder picked for this box, as long as the box still shows it.
        FolderSpot PhoneFor(TextBox box)
        {
            FolderSpot spot = box == folderA ? phoneA : phoneB;
            if (spot == null) return null;
            if (!string.Equals(box.Text.Trim(), spot.Text, StringComparison.Ordinal)) return null;
            return spot;
        }

        bool PhoneInvolved()
        {
            return SelectedDrive().Phone || PhoneFor(folderA) != null || PhoneFor(folderB) != null;
        }

        void OnDragEnter(object sender, DragEventArgs e)
        {
            e.Effect = DropFolder(e) == null ? DragDropEffects.None : DragDropEffects.Copy;
        }

        void OnDropA(object sender, DragEventArgs e)
        {
            string path = DropFolder(e);
            if (path != null) folderA.Text = path;
        }

        void OnDropB(object sender, DragEventArgs e)
        {
            string path = DropFolder(e);
            if (path != null) folderB.Text = path;
        }

        static string DropFolder(DragEventArgs e)
        {
            if (e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop)) return null;
            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0) return null;
            if (!Paths.Exists(files[0])) return null;
            return files[0];
        }

        void OnStartAtChanged(object sender, EventArgs e)
        {
            startWhen.Enabled = startAt.Checked && phase == PhaseIdle;
        }

        void OnDriveProfileChanged(object sender, EventArgs e)
        {
            if (phase != PhaseIdle) return;
            DriveChoice choice = SelectedDrive();
            decimal files = choice.FilesAtOnce;
            if (files < parallel.Minimum) files = parallel.Minimum;
            if (files > parallel.Maximum) files = parallel.Maximum;
            if (parallel.Value != files) parallel.Value = files;
            applyingOptions = true;
            folderOrder.Checked = choice.Order == CopyPlan.PathOrder;
            largeSmall.Checked = choice.Order == CopyPlan.LargeWithSmall;
            smallestFirst.Checked = choice.Order == CopyPlan.SmallestFirst;
            largestFirst.Checked = choice.Order == CopyPlan.LargestFirst;
            applyingOptions = false;
            ApplyOptionLocks();
            if (previewedA != null) ClearPreview();
        }

        void OnOrderOptionChanged(object sender, EventArgs e)
        {
            if (applyingOptions) return;
            CheckBox box = (CheckBox)sender;
            if (box.Checked)
            {
                applyingOptions = true;
                if (box != folderOrder) folderOrder.Checked = false;
                if (box != largeSmall) largeSmall.Checked = false;
                if (box != smallestFirst) smallestFirst.Checked = false;
                if (box != largestFirst) largestFirst.Checked = false;
                applyingOptions = false;
            }
            ApplyOptionLocks();
            if (phase != PhaseIdle) return;
            if (previewedA != null) ClearPreview();
        }

        void ApplyOptionLocks()
        {
            bool open = phase == PhaseIdle && profileCombo.SelectedIndex <= 0;
            folderOrder.Enabled = open && !largeSmall.Checked && !smallestFirst.Checked && !largestFirst.Checked;
            largeSmall.Enabled = open && !folderOrder.Checked && !smallestFirst.Checked && !largestFirst.Checked;
            smallestFirst.Enabled = open && !folderOrder.Checked && !largeSmall.Checked && !largestFirst.Checked;
            largestFirst.Enabled = open && !folderOrder.Checked && !largeSmall.Checked && !smallestFirst.Checked;
            bothWays.Enabled = phase == PhaseIdle;
            ApplyPhoneLock();
        }

        // Phones copy one file at a time. Parallelization is locked at 1 and greyed out
        // while a phone profile is picked or either folder is on a phone.
        void ApplyPhoneLock()
        {
            bool phone = PhoneInvolved();
            if (phone && parallel.Value != 1) parallel.Value = 1;
            if (parallel.Enabled == phone)
            {
                parallel.Enabled = !phone;
                labelParallel.Enabled = !phone;
                tips.SetToolTip(labelParallel, phone ? "Phones copy one file at a time." : "Files copied at the same time.");
                tips.SetToolTip(parallel, phone ? "Phones copy one file at a time." : "1 to 32.");
            }
        }

        int SelectedOrder()
        {
            if (largeSmall.Checked) return CopyPlan.LargeWithSmall;
            if (smallestFirst.Checked) return CopyPlan.SmallestFirst;
            if (largestFirst.Checked) return CopyPlan.LargestFirst;
            return CopyPlan.PathOrder;
        }

        static void PlaceOption(CheckBox box, ref int x, ref int y, int pad, int width, int rowH)
        {
            int w = box.PreferredSize.Width;
            if (x > pad && x + w > width - pad)
            {
                x = pad;
                y += rowH;
            }
            box.Location = new Point(x, y + Math.Max(0, (rowH - box.Height) / 2));
            x = box.Right + 18;
        }

        void OnBothWaysChanged(object sender, EventArgs e)
        {
            UpdateWayTips();
            if (phase != PhaseIdle) return;
            if (previewedA != null) ClearPreview();
        }

        void UpdateWayTips()
        {
            if (bothWays.Checked)
            {
                tips.SetToolTip(labelA, "Copy from and to here.");
                tips.SetToolTip(folderA, "Copy from and to here.");
                tips.SetToolTip(browseA, "Choose Folder A.");
                tips.SetToolTip(labelB, "Copy from and to here.");
                tips.SetToolTip(folderB, "Copy from and to here.");
                tips.SetToolTip(browseB, "Choose Folder B.");
                tips.SetToolTip(startButton, "Copy differences both ways.");
            }
            else
            {
                tips.SetToolTip(labelA, "Copy from here.");
                tips.SetToolTip(folderA, "Copy from here.");
                tips.SetToolTip(browseA, "Choose Folder A.");
                tips.SetToolTip(labelB, "Copy to here.");
                tips.SetToolTip(folderB, "Copy to here.");
                tips.SetToolTip(browseB, "Choose Folder B.");
                tips.SetToolTip(startButton, "Copy differences from A to B.");
            }
        }

        DriveChoice SelectedDrive()
        {
            int index = profileCombo.SelectedIndex;
            if (index < 0 || index >= DriveChoices.Length) return DriveChoices[0];
            return DriveChoices[index];
        }

        void OnParallelChanged(object sender, EventArgs e)
        {
            if (session != null && phase == PhaseCopy)
                session.SetParallelism((int)parallel.Value);
        }

        void OnStart(object sender, EventArgs e)
        {
            if (phase != PhaseIdle) return;
            FolderSpot a;
            FolderSpot b;
            string error;
            if (!TryReadFolders(out a, out b, out error))
            {
                SetStatus(error, true);
                MessageBox.Show(this, error, "Folder Difference Copy", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if ((!a.IsPhone && Paths.IsDriveRoot(a.Path)) || (!b.IsPhone && Paths.IsDriveRoot(b.Path)))
            {
                DialogResult answer = MessageBox.Show(
                    this,
                    "One of these folders is an entire drive. Copy the differences anyway?",
                    "Folder Difference Copy",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);
                if (answer != DialogResult.Yes) return;
            }

            if (!b.IsPhone && !Paths.Exists(b.Path))
            {
                try { Native.EnsureDirectory(Native.ToExtended(b.Path)); }
                catch (Exception ex)
                {
                    SetStatus(ex.Message, true);
                    return;
                }
            }

            if (startAt.Checked)
            {
                DateTime when = startWhen.Value;
                if (when <= DateTime.Now)
                {
                    string message = "That start time has already passed.";
                    SetStatus(message, true);
                    MessageBox.Show(this, message, "Folder Difference Copy", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                scheduledFor = when;
                phase = PhaseWait;
                runClock = false;
                SetBusy(true);
                progress.Value = 0;
                progress.Marquee = false;
                percentLabel.Text = "";
                detailLabel.Text = "";
                ShowIdleMetrics();
                metrics.SetValue(4, "Waiting");
                list.SetSession(null);
                list.EmptyText = "Waiting to start.";
                list.Invalidate();
                Text = "Waiting - Folder Difference Copy";
                SetStatus("Waiting to start at " + when.ToString("h:mm tt on dddd, MMMM d") + ".", false);
                timer.Start();
                return;
            }

            BeginScan(false);
        }

        void OnPreview(object sender, EventArgs e)
        {
            if (phase != PhaseIdle) return;
            FolderSpot a;
            FolderSpot b;
            string error;
            if (!TryReadFolders(out a, out b, out error))
            {
                SetStatus(error, true);
                MessageBox.Show(this, error, "Folder Difference Copy", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            BeginScan(true);
        }

        void OnFolderTextChanged(object sender, EventArgs e)
        {
            UpdatePreviewButton();
            ApplyPhoneLock();
            if (suppressFolderEvent || phase != PhaseIdle) return;
            if (previewedA == null) return;
            if (folderA.Text == previewedA && folderB.Text == previewedB) return;
            ClearPreview();
        }

        void UpdatePreviewButton()
        {
            previewButton.Enabled = phase == PhaseIdle
                && folderA.Text.Trim().Length > 0
                && folderB.Text.Trim().Length > 0;
        }

        void ClearPreview()
        {
            previewedA = null;
            previewedB = null;
            session = null;
            list.SetSession(null);
            list.EmptyText = "Files that still need to be copied will show up here.";
            list.Invalidate();
            detailLabel.Text = "";
            percentLabel.Text = "";
            progress.Value = 0;
            runClock = false;
            ShowIdleMetrics();
            if (Text != "Folder Difference Copy") Text = "Folder Difference Copy";
            SetStatus("Choose Folder A and Folder B.", false);
        }

        void BeginScan(bool preview)
        {
            FolderSpot a;
            FolderSpot b;
            string error;
            if (!TryReadFolders(out a, out b, out error))
            {
                phase = PhaseIdle;
                SetBusy(false);
                timer.Stop();
                SetStatus(error, true);
                Text = "Folder Difference Copy";
                return;
            }

            phase = PhaseScan;
            previewOnly = preview;
            SetBusy(true);
            progress.Value = 0;
            progress.Marquee = true;
            percentLabel.Text = "";
            detailLabel.Text = "Looking through both folders.";
            ShowIdleMetrics();
            list.SetSession(null);
            list.EmptyText = "Comparing folders...";
            list.Invalidate();
            ResetSpeed();
            BeginClock();
            metrics.SetValue(4, ElapsedText());
            dirsCreated = 0;
            linksSkipped = 0;
            unreadable = 0;
            firstProblem = "";
            SetStatus("Comparing...", false);
            Text = "Comparing - Folder Difference Copy";
            timer.Start();

            scan = new ScanStatus();
            FolderSpot folderFrom = a;
            FolderSpot folderTo = b;
            bool writeFolders = !preview;
            bool copyBothWays = bothWays.Checked;
            ScanStatus scanning = scan;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                CompareResult result = null;
                Exception failed = null;
                try
                {
                    if (folderFrom.IsPhone || folderTo.IsPhone)
                        result = PhoneDiff.Compare(folderFrom, folderTo, scanning, writeFolders, copyBothWays);
                    else
                        result = Diff.Compare(folderFrom.Path, folderTo.Path, scanning, writeFolders, copyBothWays);
                }
                catch (Exception ex) { failed = ex; }
                try
                {
                    if (!IsDisposed)
                        BeginInvoke(new Action(delegate { OnCompareDone(result, failed); }));
                }
                catch (InvalidOperationException)
                {
                }
            });
        }

        bool TryReadFolders(out FolderSpot a, out FolderSpot b, out string error)
        {
            a = ReadFolder(folderA);
            b = ReadFolder(folderB);
            if (a.Text.Length == 0 || b.Text.Length == 0)
            {
                error = "Choose Folder A and Folder B.";
                return false;
            }
            if (a.IsPhone && b.IsPhone)
            {
                error = "Only one folder can be on a phone. Pick a folder on this PC for the other side.";
                return false;
            }
            if (a.IsPhone || b.IsPhone)
            {
                error = null;
                return true;
            }
            if (!Paths.Exists(a.Path))
            {
                error = "Folder A does not exist.";
                return false;
            }
            if (Paths.Same(a.Path, b.Path) || Paths.OneInsideTheOther(a.Path, b.Path))
            {
                error = "Pick two folders that do not contain each other.";
                return false;
            }
            error = null;
            return true;
        }

        // Reads a folder box. A picked phone folder stays as it is; typed text that names a
        // phone folder (for example "This PC\Phone\Internal shared storage\Music") is
        // looked up; anything else is a normal path.
        FolderSpot ReadFolder(TextBox box)
        {
            FolderSpot phone = PhoneFor(box);
            if (phone != null) return phone;
            string text = box.Text.Trim().Trim('"').Trim();
            if (text.Length > 0 && !LooksLikePath(text))
            {
                FolderSpot typed = FolderDialog.ResolvePhone(text);
                if (typed != null)
                {
                    typed.Text = text;
                    if (box == folderA) phoneA = typed;
                    else phoneB = typed;
                    ApplyPhoneLock();
                    return typed;
                }
            }
            string path = Paths.Normalize(box.Text);
            suppressFolderEvent = true;
            box.Text = path;
            suppressFolderEvent = false;
            return FolderSpot.ForPath(path);
        }

        static bool LooksLikePath(string text)
        {
            if (text.StartsWith("::")) return false;
            try
            {
                return System.IO.Path.IsPathRooted(text);
            }
            catch (ArgumentException)
            {
                return true;
            }
        }

        void OnCompareDone(CompareResult result, Exception error)
        {
            if (IsDisposed) return;
            if (error != null)
            {
                previewOnly = false;
                previewedA = null;
                previewedB = null;
                phase = PhaseIdle;
                SetBusy(false);
                timer.Stop();
                progress.Marquee = false;
                list.EmptyText = "Files that still need to be copied will show up here.";
                SetStatus(error.Message, true);
                Text = "Folder Difference Copy";
                if (closeAfterStop) Close();
                return;
            }
            if (result == null || result.Cancelled)
            {
                previewOnly = false;
                previewedA = null;
                previewedB = null;
                phase = PhaseIdle;
                SetBusy(false);
                timer.Stop();
                progress.Marquee = false;
                list.EmptyText = "Files that still need to be copied will show up here.";
                SetStatus("Stopped.", false);
                Text = "Folder Difference Copy";
                if (closeAfterStop) Close();
                return;
            }

            dirsCreated = result.DirsCreated;
            linksSkipped = result.LinksSkipped;
            unreadable = result.Unreadable;
            firstProblem = result.FirstProblem ?? "";
            if (previewOnly)
            {
                previewOnly = false;
                ShowPreview(result);
                return;
            }
            session = new CopySession();
            ApplyPhoneLock();
            CopyPlan.Arrange(result.Jobs, SelectedOrder(), (int)parallel.Value);
            session.SetJobs(result.Jobs);
            list.EmptyText = "Nothing left to copy.";
            list.SetSession(session);
            ResetSpeed();
            phase = PhaseCopy;
            if (!timer.Enabled) timer.Start();
            session.Start((int)parallel.Value);
            if (session.IsFinished)
                FinishCopy();
            else
                SetStatus("Copying..." + Notes(), unreadable > 0 || result.DirCreateFailed > 0);
        }

        void OnStop(object sender, EventArgs e)
        {
            if (phase == PhaseWait)
            {
                phase = PhaseIdle;
                timer.Stop();
                SetBusy(false);
                progress.Marquee = false;
                list.EmptyText = "Files that still need to be copied will show up here.";
                metrics.SetValue(4, "-");
                SetStatus("Start cancelled.", false);
                Text = "Folder Difference Copy";
                if (closeAfterStop)
                {
                    closeAfterStop = false;
                    Close();
                }
                return;
            }
            if (phase == PhaseScan && scan != null) scan.Cancel = true;
            if (phase == PhaseCopy && session != null) session.Cancel();
            stopButton.Enabled = false;
            SetStatus("Stopping...", false);
        }

        void OnTick(object sender, EventArgs e)
        {
            if (phase == PhaseWait)
            {
                if (DateTime.Now >= scheduledFor)
                    BeginScan(false);
                else
                    SetStatus(WaitStatus(), false);
                return;
            }
            if (phase == PhaseScan && scan != null)
            {
                progress.Marquee = true;
                progress.Animate();
                metrics.SetValue(4, ElapsedText());
                SetStatus(string.Format(
                    "Comparing... {0:N0} files in Folder A, {1:N0} in Folder B",
                    scan.FilesA,
                    scan.FilesB), false);
                return;
            }
            if (phase == PhaseCopy && session != null)
            {
                UpdateReadouts();
                list.Pull();
                list.Invalidate();
                if (session.IsFinished)
                    FinishCopy();
            }
        }

        void UpdateReadouts()
        {
            long total;
            long copied;
            int completed;
            int remaining;
            int failed;
            int jobs;
            session.ReadTotals(out total, out copied, out completed, out remaining, out failed, out jobs);

            int now = Environment.TickCount;
            samples.Add(new SpeedSample { Tick = now, Bytes = copied });
            while (samples.Count > 2 && unchecked(now - samples[0].Tick) > 2000)
                samples.RemoveAt(0);

            double speed = 0;
            if (samples.Count >= 2)
            {
                SpeedSample older = samples[0];
                SpeedSample newer = samples[samples.Count - 1];
                double seconds = unchecked(newer.Tick - older.Tick) / 1000.0;
                if (seconds < 0.2) seconds = 0.2;
                speed = (newer.Bytes - older.Bytes) / seconds;
                if (speed < 0) speed = 0;
            }

            session.DrainTicks(itemTicks);
            while (itemTicks.Count > 0 && unchecked(now - itemTicks[0]) > 2000)
                itemTicks.RemoveAt(0);
            double runSeconds = unchecked(now - sampleStart) / 1000.0;
            if (runSeconds < 0.25) runSeconds = 0.25;
            double divisor = runSeconds < 2 ? runSeconds : 2;
            double items = itemTicks.Count / divisor;

            double ratio = 0;
            if (total > 0) ratio = copied / (double)total;
            else if (jobs > 0) ratio = completed / (double)jobs;
            if (ratio < 0) ratio = 0;
            if (ratio > 1) ratio = 1;
            progress.Marquee = false;
            progress.Value = ratio;
            progress.Animate();
            string pct = Readout.Percent(ratio).ToString() + "%";
            if (percentLabel.Text != pct) percentLabel.Text = pct;

            long remainBytes = total - copied;
            if (remainBytes < 0) remainBytes = 0;
            string eta = speed < 1024 || copied < 256 * 1024
                ? "Calculating..."
                : Readout.FormatEta(remainBytes / speed);

            metrics.SetValue(0, Readout.FormatRate(speed));
            metrics.SetValue(1, Readout.FormatItems(items));
            metrics.SetValue(2, remaining.ToString("N0"));
            metrics.SetValue(3, eta);
            metrics.SetValue(4, ElapsedText());
            detailLabel.Text = Readout.FormatBytes(Math.Max(0, copied)) + " of " + Readout.FormatBytes(Math.Max(0, total)) + " copied";

            string title = "Copying - " + remaining.ToString("N0") + " left";
            if (Text != title) Text = title;
        }

        void FinishCopy()
        {
            if (finishing) return;
            finishing = true;
            try
            {
                timer.Stop();
                phase = PhaseIdle;
                progress.Marquee = false;
                if (session != null)
                    UpdateReadouts();
                list.Pull();
                list.Invalidate();

                long total;
                long copied;
                int completed;
                int remaining;
                int failed;
                int jobs;
                if (session != null)
                    session.ReadTotals(out total, out copied, out completed, out remaining, out failed, out jobs);
                else
                {
                    completed = 0;
                    remaining = 0;
                    failed = 0;
                }
                int notCopied = remaining - failed;
                if (notCopied < 0) notCopied = 0;
                string note = Notes();
                string message;
                bool problem = failed > 0 || unreadable > 0;
                if (session != null && session.WasCancelled)
                    message = string.Format("Stopped. {0:N0} copied, {1:N0} not copied, {2:N0} failed.", completed, notCopied, failed);
                else if (failed > 0 && completed == 0 && notCopied == 0)
                    message = string.Format("{0:N0} files could not be copied.", failed);
                else if (failed > 0)
                    message = string.Format("Finished with problems. {0:N0} copied, {1:N0} failed.", completed, failed);
                else if (completed == 0 && dirsCreated == 0)
                    message = "The folders already match. Nothing to copy.";
                else if (completed == 0)
                    message = string.Format("Created {0:N0} folders. No files needed copying.", dirsCreated);
                else
                {
                    message = string.Format("Finished. {0:N0} files copied.", completed);
                    if (dirsCreated > 0)
                        message += " Created " + dirsCreated.ToString("N0") + " folders.";
                }
                SetStatus(message + note, problem);
                if (session == null || !session.WasCancelled)
                {
                    if (failed == 0 && completed > 0)
                    {
                        progress.Value = 1;
                        percentLabel.Text = "100%";
                        progress.Animate();
                    }
                }
                Text = "Folder Difference Copy";
                SetBusy(false);
            }
            finally
            {
                finishing = false;
            }
            if (closeAfterStop)
            {
                closeAfterStop = false;
                Close();
            }
        }

        string Notes()
        {
            string note = "";
            if (linksSkipped > 0)
                note += " Skipped " + linksSkipped.ToString("N0") + " links.";
            if (unreadable > 0)
                note += " Could not read " + unreadable.ToString("N0") + " folders.";
            if (!string.IsNullOrEmpty(firstProblem) && (unreadable > 0 || note.Length == 0))
            {
                if (firstProblem.Length > 180)
                    note += " " + firstProblem.Substring(0, 180);
                else if (unreadable > 0)
                    note += " " + firstProblem;
            }
            return note;
        }

        void ResetSpeed()
        {
            samples.Clear();
            itemTicks.Clear();
            sampleStart = Environment.TickCount;
        }

        void BeginClock()
        {
            runStarted = DateTime.Now;
            runClock = true;
        }

        string ElapsedText()
        {
            if (!runClock) return "-";
            return Readout.FormatElapsed(DateTime.Now - runStarted);
        }

        string WaitStatus()
        {
            TimeSpan left = scheduledFor - DateTime.Now;
            if (left < TimeSpan.Zero) left = TimeSpan.Zero;
            return "Waiting to start at " + scheduledFor.ToString("h:mm tt on dddd, MMMM d") + ". " + Readout.FormatElapsed(left) + " left.";
        }

        void ShowIdleMetrics()
        {
            metrics.SetValue(0, "-");
            metrics.SetValue(1, "-");
            metrics.SetValue(2, "-");
            metrics.SetValue(3, "-");
            metrics.SetValue(4, "-");
        }

        void SetBusy(bool busy)
        {
            folderA.ReadOnly = busy;
            folderB.ReadOnly = busy;
            browseA.Enabled = !busy;
            browseB.Enabled = !busy;
            startButton.Enabled = !busy;
            stopButton.Enabled = busy;
            startAt.Enabled = !busy;
            startWhen.Enabled = !busy && startAt.Checked;
            profileCombo.Enabled = !busy;
            ApplyOptionLocks();
            UpdatePreviewButton();
        }

        void ShowPreview(CompareResult result)
        {
            timer.Stop();
            progress.Marquee = false;
            progress.Value = 0;
            percentLabel.Text = "";
            CopyPlan.Arrange(result.Jobs, SelectedOrder(), (int)parallel.Value);
            session = new CopySession();
            session.SetJobs(result.Jobs);
            list.EmptyText = "Nothing to copy.";
            list.SetSession(session);
            list.Pull();
            list.Invalidate();

            long bytes = 0;
            int count = 0;
            int broken = 0;
            for (int i = 0; i < result.Jobs.Count; i++)
            {
                if (result.Jobs[i].State == FileJob.Failed) broken++;
                else
                {
                    count++;
                    bytes += result.Jobs[i].Size;
                }
            }
            metrics.SetValue(0, "-");
            metrics.SetValue(1, "-");
            metrics.SetValue(2, count.ToString("N0"));
            metrics.SetValue(3, "-");
            detailLabel.Text = Readout.FormatBytes(bytes);
            string message = count == 0
                ? "Nothing to copy."
                : string.Format("{0:N0} files, {1}.", count, Readout.FormatBytes(bytes));
            if (broken > 0)
                message += string.Format(" {0:N0} cannot be copied.", broken);
            previewedA = folderA.Text;
            previewedB = folderB.Text;
            phase = PhaseIdle;
            SetStatus(message + Notes(), unreadable > 0 || broken > 0);
            Text = count == 0 ? "Folder Difference Copy" : "Preview - " + count.ToString("N0") + " files";
            SetBusy(false);
        }

        void SetStatus(string text, bool error)
        {
            persistentStatus = text ?? "";
            statusError = error;
            PaintStatus();
        }

        void OnListAttention(FileJob job)
        {
            if (job == null)
                hoverStatus = null;
            else if (!string.IsNullOrEmpty(job.Error))
                hoverStatus = job.DisplayPath + " - " + job.Error;
            else
                hoverStatus = job.DisplayPath;
            PaintStatus();
        }

        void PaintStatus()
        {
            string text = string.IsNullOrEmpty(hoverStatus) ? persistentStatus : hoverStatus;
            bool error = string.IsNullOrEmpty(hoverStatus) && statusError;
            if (status.Message != text || status.IsError != error)
            {
                status.Message = text;
                status.IsError = error;
                status.Invalidate();
            }
            tips.SetToolTip(status, text);
        }

        internal void LoadDemo()
        {
            folderA.Text = @"C:\Music\Halo";
            folderB.Text = @"D:\Backup\Halo";
            parallel.Value = 2;
            SetBusy(true);
            progress.Marquee = false;
            progress.Value = 0.37;
            percentLabel.Text = "37%";
            detailLabel.Text = "1.18 GB of 3.20 GB copied";
            metrics.SetValue(0, "18.40 MB/s");
            metrics.SetValue(1, "1.50");
            metrics.SetValue(2, "17");
            metrics.SetValue(3, "About 2 minutes");
            metrics.SetValue(4, "12:08");
            SetStatus("Copying...", false);
            Text = "Copying - 17 left";

            FileJob[] jobs = new FileJob[17];
            string[] names = new string[] {
                @"Halo 2 Soundtrack\01 - Halo Theme.m4a",
                @"Halo 2 Soundtrack\02 - Opening Suite (Remastered Anniversary Edition).m4a",
                @"Halo 2 Soundtrack\03 - Peril.m4a",
                @"Halo 3 ODST\01 - Overture.m4a",
                @"Halo 3 ODST\02 - The Rookie.m4a",
                @"Reach\01 - Overture.m4a",
                @"Reach\02 - Winter Contingency.m4a",
                @"Reach\03 - ONI Sword Base.m4a",
                @"Reach\04 - Nightfall.m4a",
                @"Reach\05 - Tip of the Spear.m4a",
                @"Reach\06 - Long Night of Solace.m4a",
                @"Reach\07 - Exodus.m4a",
                @"Reach\08 - New Alexandria.m4a",
                @"Reach\09 - The Package.m4a",
                @"Reach\10 - The Pillar of Autumn.m4a",
                @"Reach\11 - Epilogue.m4a",
                @"Reach\12 - From the Vault.m4a"
            };
            long[] sizes = new long[] {
                8601600, 25165824, 7340032, 5242880, 10485760,
                6291456, 9437184, 4718592, 8388608, 12582912,
                15728640, 3145728, 6815744, 9961472, 11534336,
                2097152, 4194304
            };
            for (int i = 0; i < jobs.Length; i++)
            {
                FileJob job = new FileJob();
                job.RelativePath = names[i];
                job.Size = sizes[i];
                job.State = FileJob.Waiting;
                jobs[i] = job;
            }
            jobs[0].State = FileJob.Copying;
            jobs[0].Copied = (long)(jobs[0].Size * 0.62);
            jobs[1].State = FileJob.Copying;
            jobs[1].Copied = (long)(jobs[1].Size * 0.15);
            jobs[2].State = FileJob.Failed;
            jobs[2].Error = "The process cannot access the file because it is being used by another process.";
            jobs[16].State = FileJob.Failed;
            jobs[16].Error = "The process cannot access the file because it is being used by another process.";
            list.EmptyText = "Nothing left to copy.";
            list.SetDemo(jobs);
            LayoutControls();
            Invalidate(true);
        }

        sealed class DriveChoice
        {
            public string Name;
            public int FilesAtOnce;
            public int Order;
            public bool Phone;

            public DriveChoice(string name, int filesAtOnce, int order)
                : this(name, filesAtOnce, order, false)
            {
            }

            public DriveChoice(string name, int filesAtOnce, int order, bool phone)
            {
                Name = name;
                FilesAtOnce = filesAtOnce;
                Order = order;
                Phone = phone;
            }
        }

        // Hard drives lose speed when the head jumps between files, so those stay in folder order.
        // Two solid-state drives can keep a large file moving while small files copy beside it.
        // Phones take one file at a time over USB, so phone profiles copy one file at a time in
        // folder order. That keeps a hard drive reading in order and keeps each phone folder together.
        static readonly DriveChoice[] DriveChoices = new DriveChoice[] {
            new DriveChoice("Default", 1, CopyPlan.PathOrder),
            new DriveChoice("Hard drive to hard drive", 2, CopyPlan.PathOrder),
            new DriveChoice("Hard drive to solid-state drive", 2, CopyPlan.PathOrder),
            new DriveChoice("Solid-state drive to hard drive", 2, CopyPlan.PathOrder),
            new DriveChoice("Solid-state drive to solid-state drive", 8, CopyPlan.LargeWithSmall),
            new DriveChoice("Same hard drive", 1, CopyPlan.PathOrder),
            new DriveChoice("Same solid-state drive", 4, CopyPlan.LargeWithSmall),
            new DriveChoice("Hard drive to phone", 1, CopyPlan.PathOrder, true),
            new DriveChoice("Solid-state drive to phone", 1, CopyPlan.PathOrder, true),
            new DriveChoice("Phone to hard drive", 1, CopyPlan.PathOrder, true),
            new DriveChoice("Phone to solid-state drive", 1, CopyPlan.PathOrder, true)
        };

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Stop();
                timer.Dispose();
                tips.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
