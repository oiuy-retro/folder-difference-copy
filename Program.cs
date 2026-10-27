using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace FolderDiffCopy
{
    internal static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            AppContext.SetSwitch("Switch.System.IO.UseLegacyPathHandling", false);
            AppContext.SetSwitch("Switch.System.IO.BlockLongPaths", false);
            bool selfTest = false;
            bool phoneProbe = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--selftest", StringComparison.OrdinalIgnoreCase))
                    selfTest = true;
                if (string.Equals(args[i], "--phone-probe", StringComparison.OrdinalIgnoreCase))
                    phoneProbe = true;
            }
            if (selfTest)
                return RunSelfTest();
            if (phoneProbe)
                return RunPhoneProbe();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
            {
                try
                {
                    MessageBox.Show(e.Exception.Message, "Folder Difference Copy", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch
                {
                }
            };
            Application.Run(new MainForm());
            return 0;
        }

        static int RunSelfTest()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var log = new StringBuilder();
            int failed = 0;
            string root = Path.Combine(Path.GetTempPath(), "FolderDiffCopyTest-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);
                failed += Check(log, "compare and copy", delegate { TestCompareAndCopy(root); });
                failed += Check(log, "changed file is replaced", delegate { TestChanged(root); });
                failed += Check(log, "time tolerance", delegate { TestTimeTolerance(root); });
                failed += Check(log, "readonly destination", delegate { TestReadOnly(root); });
                failed += Check(log, "empty folders and extra files", delegate { TestExtras(root); });
                failed += Check(log, "progress stays accurate", delegate { TestProgress(root); });
                failed += Check(log, "cancel removes the partial file", delegate { TestCancel(root); });
                failed += Check(log, "parallel cap", delegate { TestParallel(root); });
                failed += Check(log, "long path", delegate { TestLongPath(root); });
                failed += Check(log, "refuses nested folders", delegate { TestRejectNested(root); });
                failed += Check(log, "explorer dialog opens", delegate { TestDialogProbe(); });
                failed += Check(log, "window paints", delegate { TestScreenshot(); });
                failed += Check(log, "list stays in copy order", delegate { TestListOrder(); });
                failed += Check(log, "drive copy order", delegate { TestDriveOrder(); });
                failed += Check(log, "size copy order", delegate { TestSizeOrder(); });
                failed += Check(log, "size order options", delegate { TestSizeOrderOptions(); });
                failed += Check(log, "large file list", delegate { TestLargeList(); });
                failed += Check(log, "preview does not write", delegate { TestPreviewReadOnly(root); });
                failed += Check(log, "copy both ways", delegate { TestBothWays(root); });
                failed += Check(log, "copy both ways preview", delegate { TestBothWaysPreview(root); });
                failed += Check(log, "copy both ways option", delegate { TestBothWaysOption(); });
                failed += Check(log, "phone profiles lock parallelization", delegate { TestPhoneProfiles(); });
                failed += Check(log, "phone compare by size", delegate { TestPhonePlan(); });
                failed += Check(log, "phone folder names", delegate { TestPhoneNames(); });
                failed += Check(log, "phone copy is one at a time", delegate { TestPhoneOneAtATime(); });
                failed += Check(log, "phone path is not a disk folder", delegate { TestPhonePathNotDisk(); });
                failed += Check(log, "phone shell item can be kept", delegate { TestPhoneGitCookie(); });
            }
            finally
            {
                try { Directory.Delete(root, true); }
                catch { }
            }
            log.AppendLine(failed == 0 ? "ALL PASSED" : "FAILED " + failed.ToString());
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest.log");
            File.WriteAllText(path, log.ToString());
            Console.WriteLine(log.ToString());
            return failed == 0 ? 0 : 1;
        }

        static int Check(StringBuilder log, string name, Action test)
        {
            try
            {
                test();
                log.AppendLine("PASS " + name);
                return 0;
            }
            catch (Exception ex)
            {
                log.AppendLine("FAIL " + name);
                log.AppendLine(ex.ToString());
                return 1;
            }
        }

        static void Need(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        static void Write(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text);
        }

        static void TestCompareAndCopy(string root)
        {
            string a = Path.Combine(root, "basic-a");
            string b = Path.Combine(root, "basic-b");
            Write(Path.Combine(a, "keep.txt"), "same");
            Write(Path.Combine(a, "sub", "new.txt"), "hello");
            Write(Path.Combine(b, "keep.txt"), "same");
            Write(Path.Combine(b, "only-b.txt"), "leave me");
            DateTime stamp = new DateTime(2020, 5, 1, 12, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(Path.Combine(a, "keep.txt"), stamp);
            File.SetLastWriteTimeUtc(Path.Combine(b, "keep.txt"), stamp);

            CompareResult result = Diff.Compare(a, b, new ScanStatus());
            Need(result.Unchanged == 1, "unchanged count " + result.Unchanged);
            Need(result.Jobs.Count == 1, "job count " + result.Jobs.Count);
            Need(result.Jobs[0].RelativePath == "sub\\new.txt", result.Jobs[0].RelativePath);

            CopySession session = new CopySession();
            session.SetJobs(result.Jobs);
            session.Start(1);
            Wait(session, 20000);
            Need(session.IsFinished, "not finished");
            Need(File.ReadAllText(Path.Combine(b, "sub", "new.txt")) == "hello", "copied text");
            Need(File.ReadAllText(Path.Combine(b, "only-b.txt")) == "leave me", "extra file in B was changed");
            Need(File.GetLastWriteTimeUtc(Path.Combine(a, "sub", "new.txt")) == File.GetLastWriteTimeUtc(Path.Combine(b, "sub", "new.txt")), "time not kept");

            CompareResult again = Diff.Compare(a, b, new ScanStatus());
            Need(again.Jobs.Count == 0, "second compare still has work");
            Need(again.Unchanged == 2, "unchanged " + again.Unchanged);
        }

        static void TestChanged(string root)
        {
            string a = Path.Combine(root, "changed-a");
            string b = Path.Combine(root, "changed-b");
            Write(Path.Combine(a, "song.m4a"), "from-a");
            Write(Path.Combine(b, "song.m4a"), "old-b-is-newer-and-longer!!");
            File.SetLastWriteTimeUtc(Path.Combine(a, "song.m4a"), new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(Path.Combine(b, "song.m4a"), new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            CompareResult result = Diff.Compare(a, b, new ScanStatus());
            Need(result.Jobs.Count == 1, "expected a difference");
            CopySession session = new CopySession();
            session.SetJobs(result.Jobs);
            session.Start(1);
            Wait(session, 20000);
            Need(File.ReadAllText(Path.Combine(b, "song.m4a")) == "from-a", "A did not replace B");
        }

        static void TestTimeTolerance(string root)
        {
            string a = Path.Combine(root, "time-a");
            string b = Path.Combine(root, "time-b");
            Write(Path.Combine(a, "near.txt"), "aaaa");
            Write(Path.Combine(b, "near.txt"), "aaaa");
            Write(Path.Combine(a, "far.txt"), "bbbb");
            Write(Path.Combine(b, "far.txt"), "BBBB");
            DateTime t = new DateTime(2021, 6, 1, 8, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(Path.Combine(a, "near.txt"), t);
            File.SetLastWriteTimeUtc(Path.Combine(b, "near.txt"), t.AddSeconds(2));
            File.SetLastWriteTimeUtc(Path.Combine(a, "far.txt"), t);
            File.SetLastWriteTimeUtc(Path.Combine(b, "far.txt"), t.AddSeconds(3));
            CompareResult result = Diff.Compare(a, b, new ScanStatus());
            Need(result.Jobs.Count == 1, "tolerance jobs " + result.Jobs.Count);
            Need(result.Jobs[0].RelativePath == "far.txt", result.Jobs[0].RelativePath);
        }

        static void TestReadOnly(string root)
        {
            string a = Path.Combine(root, "ro-a");
            string b = Path.Combine(root, "ro-b");
            Write(Path.Combine(a, "track.m4a"), "new-bytes");
            string dest = Path.Combine(b, "track.m4a");
            Write(dest, "old");
            File.SetLastWriteTimeUtc(Path.Combine(a, "track.m4a"), DateTime.UtcNow);
            File.SetLastWriteTimeUtc(dest, DateTime.UtcNow.AddHours(-5));
            File.SetAttributes(dest, FileAttributes.ReadOnly);
            CompareResult result = Diff.Compare(a, b, new ScanStatus());
            Need(result.Jobs.Count == 1, "readonly not queued");
            CopySession session = new CopySession();
            session.SetJobs(result.Jobs);
            session.Start(1);
            Wait(session, 20000);
            Need(File.ReadAllText(dest) == "new-bytes", "readonly file was not replaced");
        }

        static void TestExtras(string root)
        {
            string a = Path.Combine(root, "extra-a");
            string b = Path.Combine(root, "extra-b");
            Directory.CreateDirectory(Path.Combine(a, "empty", "child"));
            Write(Path.Combine(a, "real.txt"), "x");
            Write(Path.Combine(b, "stray.txt"), "keep");
            CompareResult result = Diff.Compare(a, b, new ScanStatus());
            Need(Directory.Exists(Path.Combine(b, "empty", "child")), "empty folder was not created");
            Need(result.DirsCreated >= 2, "dirs created " + result.DirsCreated);
            Need(File.Exists(Path.Combine(b, "stray.txt")), "stray file deleted");
            CopySession session = new CopySession();
            session.SetJobs(result.Jobs);
            session.Start(1);
            Wait(session, 20000);
            Need(File.ReadAllText(Path.Combine(b, "real.txt")) == "x", "real file missing");
        }

        static void TestProgress(string root)
        {
            string a = Path.Combine(root, "prog-a");
            string b = Path.Combine(root, "prog-b");
            string source = Path.Combine(a, "big.bin");
            Directory.CreateDirectory(a);
            byte[] chunk = new byte[256 * 1024];
            for (int i = 0; i < chunk.Length; i++) chunk[i] = (byte)(i * 17);
            using (FileStream stream = new FileStream(source, FileMode.Create, FileAccess.Write))
            {
                for (int i = 0; i < 8; i++) stream.Write(chunk, 0, chunk.Length);
            }
            Directory.CreateDirectory(b);
            CompareResult result = Diff.Compare(a, b, new ScanStatus());
            Need(result.Jobs.Count == 1, "progress job missing");
            FileJob job = result.Jobs[0];
            CopySession session = new CopySession();
            session.TestPauseMs = 20;
            session.SetJobs(result.Jobs);
            session.Start(1);
            long previous = 0;
            bool sawMiddle = false;
            int deadline = Environment.TickCount + 20000;
            while (!session.IsFinished && unchecked(deadline - Environment.TickCount) > 0)
            {
                long copied = Interlocked.Read(ref job.Copied);
                long sessionBytes = 0;
                long total;
                long copiedTotal;
                int completed;
                int remaining;
                int failed;
                int jobs;
                session.ReadTotals(out total, out copiedTotal, out completed, out remaining, out failed, out jobs);
                sessionBytes = copiedTotal;
                Need(copied >= previous, "progress went backwards");
                Need(sessionBytes >= 0, "byte total went negative");
                if (copied > 0 && copied < job.Size) sawMiddle = true;
                previous = copied;
                Thread.Sleep(5);
            }
            Wait(session, 20000);
            Need(sawMiddle, "never reported a partial file");
            Need(Native.Fnv(Native.ToExtended(source)) == Native.Fnv(Native.ToExtended(Path.Combine(b, "big.bin"))), "checksum mismatch");
            long finalTotal;
            long finalCopied;
            int done;
            int left;
            int bad;
            int count;
            session.ReadTotals(out finalTotal, out finalCopied, out done, out left, out bad, out count);
            Need(done == 1, "completed " + done);
            Need(bad == 0, "failed progress copy");
            Need(finalCopied == finalTotal, "copied " + finalCopied + " of " + finalTotal);
        }

        static void TestCancel(string root)
        {
            string a = Path.Combine(root, "cancel-a");
            string b = Path.Combine(root, "cancel-b");
            Directory.CreateDirectory(a);
            Directory.CreateDirectory(b);
            string source = Path.Combine(a, "slow.bin");
            using (FileStream stream = new FileStream(source, FileMode.Create, FileAccess.Write))
            {
                stream.SetLength(2 * 1024 * 1024);
            }
            CompareResult result = Diff.Compare(a, b, new ScanStatus());
            Need(result.Jobs.Count == 1, "cancel job missing");
            CopySession session = new CopySession();
            session.TestPauseMs = 80;
            session.SetJobs(result.Jobs);
            session.Start(1);
            int deadline = Environment.TickCount + 10000;
            while (Interlocked.Read(ref result.Jobs[0].Copied) == 0 && !session.IsFinished && unchecked(deadline - Environment.TickCount) > 0)
                Thread.Sleep(5);
            session.Cancel();
            Wait(session, 20000);
            string dest = Path.Combine(b, "slow.bin");
            Need(!File.Exists(dest), "cancelled file was committed");
            Need(!File.Exists(dest + ".fdc-partial"), "partial file left behind");
            Need(session.WasCancelled, "cancel flag");
            Need(session.Count >= 1, "cancelled file left the list");
        }

        static void TestParallel(string root)
        {
            string a = Path.Combine(root, "par-a");
            string b = Path.Combine(root, "par-b");
            Directory.CreateDirectory(b);
            byte[] chunk = new byte[256 * 1024];
            for (int i = 0; i < 9; i++)
            {
                chunk[0] = (byte)i;
                string path = Path.Combine(a, "f" + i.ToString("00") + ".bin");
                Directory.CreateDirectory(a);
                File.WriteAllBytes(path, chunk);
            }
            CompareResult result = Diff.Compare(a, b, new ScanStatus());
            Need(result.Jobs.Count == 9, "parallel jobs " + result.Jobs.Count);
            CopySession session = new CopySession();
            session.TestPauseMs = 40;
            session.SetJobs(result.Jobs);
            session.Start(3);
            Wait(session, 20000);
            Need(session.PeakActive <= 3, "peak " + session.PeakActive);
            Need(session.PeakActive == 3, "did not reach 3, peak " + session.PeakActive);
            for (int i = 0; i < 9; i++)
            {
                string name = "f" + i.ToString("00") + ".bin";
                Need(File.ReadAllBytes(Path.Combine(a, name)).Length == File.ReadAllBytes(Path.Combine(b, name)).Length, name);
                Need(File.ReadAllBytes(Path.Combine(b, name))[0] == (byte)i, "content " + name);
            }
        }

        static void TestLongPath(string root)
        {
            string a = root + "\\long-a\\" + new string('d', 140) + "\\" + new string('n', 80);
            string b = root + "\\long-b\\" + new string('e', 140) + "\\" + new string('n', 80);
            Native.EnsureDirectory(Native.ToExtended(a));
            Native.EnsureDirectory(Native.ToExtended(b));
            byte[] data = Encoding.UTF8.GetBytes("long-path-ok");
            Native.WriteAllBytes(Native.ToExtended(a + "\\track.m4a"), data);
            CompareResult result = Diff.Compare(a, b, new ScanStatus());
            Need(result.Jobs.Count == 1, "long path jobs " + result.Jobs.Count + " a=" + a.Length);
            CopySession session = new CopySession();
            session.SetJobs(result.Jobs);
            session.Start(1);
            Wait(session, 20000);
            Need(Native.Fnv(Native.ToExtended(a + "\\track.m4a")) == Native.Fnv(Native.ToExtended(b + "\\track.m4a")), "long path checksum");
        }

        static void TestRejectNested(string root)
        {
            string a = Path.Combine(root, "nest");
            string b = Path.Combine(a, "inside");
            Directory.CreateDirectory(b);
            bool threw = false;
            try { Diff.Compare(a, b, new ScanStatus()); }
            catch (InvalidOperationException) { threw = true; }
            Need(threw, "nested folders were accepted");
            threw = false;
            try { Diff.Compare(a, a, new ScanStatus()); }
            catch (InvalidOperationException) { threw = true; }
            Need(threw, "same folder was accepted");
        }

        static void TestBothWays(string root)
        {
            string a = Path.Combine(root, "two-a");
            string b = Path.Combine(root, "two-b");
            Write(Path.Combine(a, "A.txt"), "from-a");
            Write(Path.Combine(a, "C.txt"), "from-c");
            Write(Path.Combine(b, "B.txt"), "from-b");
            Write(Path.Combine(a, "keep.txt"), "same");
            Write(Path.Combine(b, "keep.txt"), "same");
            DateTime stamp = new DateTime(2020, 5, 1, 12, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(Path.Combine(a, "keep.txt"), stamp);
            File.SetLastWriteTimeUtc(Path.Combine(b, "keep.txt"), stamp);
            Directory.CreateDirectory(Path.Combine(b, "only-b", "child"));

            CompareResult result = Diff.Compare(a, b, new ScanStatus(), true, true);
            Need(result.Unchanged == 1, "unchanged " + result.Unchanged);
            Need(result.Jobs.Count == 3, "job count " + result.Jobs.Count);
            Need(Directory.Exists(Path.Combine(a, "only-b", "child")), "empty folder from B was not created");

            bool sawA = false, sawB = false, sawC = false;
            for (int i = 0; i < result.Jobs.Count; i++)
            {
                FileJob job = result.Jobs[i];
                if (job.RelativePath == "A.txt") { Need(job.Way == 1, "A.txt way"); sawA = true; }
                if (job.RelativePath == "B.txt") { Need(job.Way == 2, "B.txt way"); sawB = true; }
                if (job.RelativePath == "C.txt") { Need(job.Way == 1, "C.txt way"); sawC = true; }
            }
            Need(sawA && sawB && sawC, "missing A/B/C jobs");

            CopySession session = new CopySession();
            session.SetJobs(result.Jobs);
            session.Start(1);
            Wait(session, 20000);
            Need(File.ReadAllText(Path.Combine(a, "A.txt")) == "from-a", "A lost A.txt");
            Need(File.ReadAllText(Path.Combine(a, "B.txt")) == "from-b", "A missing B.txt");
            Need(File.ReadAllText(Path.Combine(a, "C.txt")) == "from-c", "A lost C.txt");
            Need(File.ReadAllText(Path.Combine(b, "A.txt")) == "from-a", "B missing A.txt");
            Need(File.ReadAllText(Path.Combine(b, "B.txt")) == "from-b", "B lost B.txt");
            Need(File.ReadAllText(Path.Combine(b, "C.txt")) == "from-c", "B missing C.txt");
            Need(File.ReadAllText(Path.Combine(a, "keep.txt")) == "same", "keep on A");
            Need(File.ReadAllText(Path.Combine(b, "keep.txt")) == "same", "keep on B");

            CompareResult again = Diff.Compare(a, b, new ScanStatus(), true, true);
            Need(again.Jobs.Count == 0, "second two-way compare still has work");
            Need(again.Unchanged == 4, "unchanged after " + again.Unchanged);

            string newerA = Path.Combine(root, "newer-a");
            string newerB = Path.Combine(root, "newer-b");
            Write(Path.Combine(newerA, "song.m4a"), "old-a");
            Write(Path.Combine(newerB, "song.m4a"), "new-b");
            File.SetLastWriteTimeUtc(Path.Combine(newerA, "song.m4a"), new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(Path.Combine(newerB, "song.m4a"), new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            CompareResult newer = Diff.Compare(newerA, newerB, new ScanStatus(), true, true);
            Need(newer.Jobs.Count == 1, "newer jobs " + newer.Jobs.Count);
            Need(newer.Jobs[0].Way == 2, "newer should copy B to A");
            CopySession newerSession = new CopySession();
            newerSession.SetJobs(newer.Jobs);
            newerSession.Start(1);
            Wait(newerSession, 20000);
            Need(File.ReadAllText(Path.Combine(newerA, "song.m4a")) == "new-b", "older A was not replaced");
            Need(File.ReadAllText(Path.Combine(newerB, "song.m4a")) == "new-b", "newer B was changed");
        }

        static void TestBothWaysPreview(string root)
        {
            string a = Path.Combine(root, "twoprev-a");
            string b = Path.Combine(root, "twoprev-b");
            Write(Path.Combine(a, "keep", "song.m4a"), "abcdef");
            Write(Path.Combine(b, "other", "only-b.m4a"), "xyz");
            CompareResult result = Diff.Compare(a, b, new ScanStatus(), false, true);
            Need(result.Jobs.Count == 2, "preview jobs " + result.Jobs.Count);
            Need(!Directory.Exists(Path.Combine(b, "keep")), "preview created a folder on B");
            Need(!Directory.Exists(Path.Combine(a, "other")), "preview created a folder on A");
            Need(result.DirsCreated == 0, "dirs created");
            Need(!File.Exists(Path.Combine(a, "other", "only-b.m4a")), "preview copied B to A");
            Need(!File.Exists(Path.Combine(b, "keep", "song.m4a")), "preview copied A to B");
        }

        static void TestBothWaysOption()
        {
            using (MainForm form = new MainForm())
            {
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-2400, -2400);
                form.Show();
                Application.DoEvents();
                CheckBox found = null;
                foreach (Control c in form.Controls)
                {
                    CheckBox box = c as CheckBox;
                    if (box != null && box.Text.Replace("&", "") == "Copy both ways")
                        found = box;
                }
                Need(found != null, "missing Copy both ways");
                Need(!found.Checked, "both ways should start off");
                Need(found.Enabled, "both ways should be editable");
                form.Hide();
            }
        }

        static void TestPhoneProfiles()
        {
            using (MainForm form = new MainForm())
            {
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-2400, -2400);
                form.Show();
                Application.DoEvents();
                ComboBox profile = null;
                NumericUpDown parallel = null;
                foreach (Control c in form.Controls)
                {
                    if (c is ComboBox) profile = (ComboBox)c;
                    if (c is NumericUpDown) parallel = (NumericUpDown)c;
                }
                Need(profile != null && parallel != null, "missing profile or parallelization");
                string[] phones = new string[] {
                    "Hard drive to phone",
                    "Solid-state drive to phone",
                    "Phone to hard drive",
                    "Phone to solid-state drive"
                };
                foreach (string name in phones)
                {
                    int index = profile.Items.IndexOf(name);
                    Need(index > 0, "missing profile " + name);
                    profile.SelectedIndex = profile.Items.IndexOf("Solid-state drive to solid-state drive");
                    Application.DoEvents();
                    Need(parallel.Enabled, "parallelization should unlock for drives");
                    profile.SelectedIndex = index;
                    Application.DoEvents();
                    Need(!parallel.Enabled, name + " should grey out parallelization");
                    Need(parallel.Value == 1, name + " should copy one file at a time");
                }
                profile.SelectedIndex = 0;
                Application.DoEvents();
                Need(parallel.Enabled, "parallelization should unlock again");
                form.Hide();
            }
        }

        static void TestPhonePlan()
        {
            var a = new Dictionary<string, PhoneEntry>(StringComparer.OrdinalIgnoreCase);
            var b = new Dictionary<string, PhoneEntry>(StringComparer.OrdinalIgnoreCase);
            long hour = 36000000000L;
            long now = DateTime.UtcNow.ToFileTimeUtc();
            a["same.jpg"] = Entry(100, now);
            b["same.jpg"] = Entry(100, now - 5 * hour);
            a["bigger.jpg"] = Entry(200, now);
            b["bigger.jpg"] = Entry(150, now);
            a["only-a.jpg"] = Entry(10, now);
            b["only-b.jpg"] = Entry(20, now);
            a["phone-newer.txt"] = Entry(5, now - 3 * hour);
            b["phone-newer.txt"] = Entry(6, now);
            a["close.txt"] = Entry(7, now);
            b["close.txt"] = Entry(8, now + hour / 2);

            int unchanged;
            List<PlannedCopy> oneWay = PhoneDiff.Plan(a, b, false, out unchanged);
            Need(unchanged == 1, "one way unchanged " + unchanged);
            Need(oneWay.Count == 4, "one way count " + oneWay.Count);
            foreach (PlannedCopy copy in oneWay)
                Need(!copy.ToA, "one way copied toward A: " + copy.Rel);

            List<PlannedCopy> both = PhoneDiff.Plan(a, b, true, out unchanged);
            Need(unchanged == 1, "both ways unchanged " + unchanged);
            Need(both.Count == 5, "both ways count " + both.Count);
            foreach (PlannedCopy copy in both)
            {
                if (copy.Rel == "only-b.jpg" || copy.Rel == "phone-newer.txt")
                    Need(copy.ToA, copy.Rel + " should go to A");
                else
                    Need(!copy.ToA, copy.Rel + " should go to B");
            }
        }

        static PhoneEntry Entry(long size, long time)
        {
            PhoneEntry entry = new PhoneEntry();
            entry.Size = size;
            entry.WriteTime = time;
            return entry;
        }

        static void TestPhoneNames()
        {
            string device = @"::{20D04FE0-3AEA-1069-A2D8-08002B30309D}\\\?\usb#vid_18d1&pid_4ee1#0123456789#{6ac27878-a6fa-4155-ba85-f98f491d4f33}";
            string folder = device + @"\SID-{10001,,123456789}\{00000016-0001-0001-0000-000000000000}";
            Need(PhoneShell.IsPhoneItem(device), "device is a phone item");
            Need(PhoneShell.IsPhoneDevice(device), "device root not detected");
            Need(PhoneShell.IsPhoneItem(folder), "folder is a phone item");
            Need(!PhoneShell.IsPhoneDevice(folder), "folder taken for the device");
            Need(!PhoneShell.IsPhoneItem(@"::{031E4825-7B94-4DC3-B131-E946B44C8DD5}\Documents.library-ms"), "library taken for a phone");
            Need(!PhoneShell.IsPhoneItem(@"C:\Music"), "drive folder taken for a phone");
            Need(PhoneShell.IsThisPcOrPortableDevices("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}"), "This PC not recognized");
            Need(PhoneShell.IsThisPcOrPortableDevices("::{35786D3C-B075-49B9-88DD-029876E11C01}"), "portable devices folder not recognized");
            Need(!PhoneShell.IsThisPcOrPortableDevices(device), "phone device taken for This PC");
        }

        static void TestPhonePathNotDisk()
        {
            Need(PhoneShell.UseAsDiskFolder(Environment.GetFolderPath(Environment.SpecialFolder.Windows)), "Windows folder should count as a disk folder");
            Need(!PhoneShell.UseAsDiskFolder(null), "null counted as a disk folder");
            Need(!PhoneShell.UseAsDiskFolder(""), "empty counted as a disk folder");
            Need(!PhoneShell.UseAsDiskFolder(@"\\?\usb#vid_18d1&pid_4ee1#missing#{6ac27878-a6fa-4155-ba85-f98f491d4f33}"), "missing phone path counted as a disk folder");
            Need(!PhoneShell.UseAsDiskFolder(@"This PC\Phone\Internal shared storage"), "phone display name counted as a disk folder");
            Paths.Normalize("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}");
            Paths.Normalize(@"This PC\Phone\Internal shared storage");
        }

        static void TestPhoneGitCookie()
        {
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            IShellItem item = PhoneShell.Open(windows);
            Need(item != null, "could not open Windows folder");
            uint cookie = 0;
            IShellItem again = null;
            try
            {
                cookie = PhoneShell.Register(item);
                Need(cookie != 0, "git cookie was 0");
                FolderSpot spot = FolderSpot.ForPhone("windows", windows, cookie);
                again = PhoneShell.FromCookie(spot.GitCookie);
                Need(again != null, "git retrieve failed");
                PhoneShell.Revoke(spot);
                cookie = 0;
            }
            finally
            {
                PhoneShell.Release(again);
                PhoneShell.Release(item);
                if (cookie != 0)
                {
                    FolderSpot leftover = FolderSpot.ForPhone("windows", windows, cookie);
                    PhoneShell.Revoke(leftover);
                }
            }
        }

        static int RunPhoneProbe()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "phone-probe.log");
            var log = new StringBuilder();
            log.AppendLine("Folder Difference Copy phone probe");
            try
            {
                Type type = Type.GetTypeFromProgID("Shell.Application");
                Need(type != null, "Shell.Application missing");
                object shell = Activator.CreateInstance(type);
                object thisPc = type.InvokeMember("NameSpace", BindingFlags.InvokeMethod, null, shell, new object[] { 0x11 });
                Need(thisPc != null, "This PC missing");
                object items = thisPc.GetType().InvokeMember("Items", BindingFlags.GetProperty, null, thisPc, null);
                int count = Convert.ToInt32(items.GetType().InvokeMember("Count", BindingFlags.GetProperty, null, items, null));
                log.AppendLine("This PC items: " + count.ToString());
                int phones = 0;
                for (int i = 0; i < count; i++)
                {
                    object fi = items.GetType().InvokeMember("Item", BindingFlags.InvokeMethod, null, items, new object[] { i });
                    if (fi == null) continue;
                    string name = fi.GetType().InvokeMember("Name", BindingFlags.GetProperty, null, fi, null) as string;
                    string itemPath = fi.GetType().InvokeMember("Path", BindingFlags.GetProperty, null, fi, null) as string;
                    bool isFolder = false;
                    try { isFolder = Convert.ToBoolean(fi.GetType().InvokeMember("IsFolder", BindingFlags.GetProperty, null, fi, null)); }
                    catch { }
                    bool disk = PhoneShell.UseAsDiskFolder(itemPath);
                    if (disk) continue;
                    phones++;
                    log.AppendLine("PHONE " + name);
                    log.AppendLine("  path: " + itemPath);
                    log.AppendLine("  folder: " + isFolder.ToString());
                    if (string.IsNullOrEmpty(itemPath)) continue;
                    object ns = type.InvokeMember("NameSpace", BindingFlags.InvokeMethod, null, shell, new object[] { itemPath });
                    if (ns == null)
                    {
                        log.AppendLine("  children: could not open");
                        continue;
                    }
                    object kids = ns.GetType().InvokeMember("Items", BindingFlags.GetProperty, null, ns, null);
                    int kidCount = Convert.ToInt32(kids.GetType().InvokeMember("Count", BindingFlags.GetProperty, null, kids, null));
                    log.AppendLine("  children: " + kidCount.ToString());
                    int show = kidCount > 12 ? 12 : kidCount;
                    for (int k = 0; k < show; k++)
                    {
                        object kid = kids.GetType().InvokeMember("Item", BindingFlags.InvokeMethod, null, kids, new object[] { k });
                        if (kid == null) continue;
                        string kidName = kid.GetType().InvokeMember("Name", BindingFlags.GetProperty, null, kid, null) as string;
                        log.AppendLine("    " + kidName);
                    }
                }
                if (phones == 0) log.AppendLine("No phone or portable device under This PC.");
                log.AppendLine("DONE");
            }
            catch (Exception ex)
            {
                log.AppendLine("ERROR " + ex.GetType().Name + ": " + ex.Message);
            }
            File.WriteAllText(path, log.ToString());
            return 0;
        }

        static void TestPhoneOneAtATime()
        {
            CopySession session = new CopySession();
            var jobs = new List<FileJob>();
            FileJob phone = Named("phone.jpg");
            phone.PhoneSource = "phone";
            jobs.Add(phone);
            session.SetJobs(jobs);
            Need(session.OneAtATime, "a phone job should force one file at a time");
            jobs.Clear();
            jobs.Add(Named("pc.jpg"));
            session.SetJobs(jobs);
            Need(!session.OneAtATime, "a PC copy should not be limited");
        }

        static void TestPreviewReadOnly(string root)
        {
            string a = Path.Combine(root, "prev-a");
            string b = Path.Combine(root, "prev-b");
            Write(Path.Combine(a, "keep", "song.m4a"), "abcdef");
            Directory.CreateDirectory(b);
            CompareResult result = Diff.Compare(a, b, new ScanStatus(), false);
            Need(result.Jobs.Count == 1, "preview jobs " + result.Jobs.Count);
            Need(result.Jobs[0].Size == 6, "preview size " + result.Jobs[0].Size);
            Need(!Directory.Exists(Path.Combine(b, "keep")), "preview created a folder");
            Need(result.DirsCreated == 0, "dirs created");
        }

        static void TestListOrder()
        {
            CopySession session = new CopySession();
            FileJob a = Named("a.txt");
            FileJob mid = Named("m.txt");
            FileJob z = Named("z.txt");
            mid.State = FileJob.Failed;
            var jobs = new List<FileJob>();
            jobs.Add(a);
            jobs.Add(mid);
            jobs.Add(z);
            session.SetJobs(jobs);
            Need(SameOrder(session, new FileJob[] { a, mid, z }), "failed file left its place");

            a.State = FileJob.Done;
            mid.State = FileJob.Copying;
            z.State = FileJob.Done;
            session.ApplyVisibility();
            Need(session.IndexOf(a) < 0, "finished file above the current one stayed");
            Need(session.IndexOf(z) < 0, "a later finished file stayed on the list");
            Need(SameOrder(session, new FileJob[] { mid }), "only the file still copying should remain");

            mid.State = FileJob.Done;
            session.ApplyVisibility();
            Need(session.Count == 0, "finished files stayed on the list");

            a.State = FileJob.Failed;
            mid.State = FileJob.Done;
            z.State = FileJob.Waiting;
            session.ApplyVisibility();
            Need(SameOrder(session, new FileJob[] { a, z }), "done file under a failure stayed in the list");
        }

        static void TestDriveOrder()
        {
            FileJob big = Named("big.bin");
            FileJob mid = Named("mid.bin");
            FileJob small = Named("a-small.bin");
            FileJob tiny = Named("z-tiny.bin");
            big.Size = 1000;
            mid.Size = 100;
            small.Size = 1;
            tiny.Size = 1;
            var jobs = new List<FileJob>();
            jobs.Add(small);
            jobs.Add(big);
            jobs.Add(tiny);
            jobs.Add(mid);
            CopyPlan.Arrange(jobs, CopyPlan.PathOrder, 2);
            Need(jobs[0].RelativePath == "a-small.bin", "path 0 " + jobs[0].RelativePath);
            Need(jobs[1].RelativePath == "big.bin", "path 1 " + jobs[1].RelativePath);
            Need(jobs[2].RelativePath == "mid.bin", "path 2 " + jobs[2].RelativePath);
            Need(jobs[3].RelativePath == "z-tiny.bin", "path 3 " + jobs[3].RelativePath);

            jobs.Clear();
            jobs.Add(small);
            jobs.Add(big);
            jobs.Add(tiny);
            jobs.Add(mid);
            CopyPlan.Arrange(jobs, CopyPlan.LargeWithSmall, 2);
            Need(jobs[0].RelativePath == "big.bin", "mix 0 " + jobs[0].RelativePath);
            Need(jobs[1].RelativePath == "z-tiny.bin", "mix 1 " + jobs[1].RelativePath);
            Need(jobs[2].RelativePath == "mid.bin", "mix 2 " + jobs[2].RelativePath);
            Need(jobs[3].RelativePath == "a-small.bin", "mix 3 " + jobs[3].RelativePath);
        }

        static void TestSizeOrder()
        {
            FileJob big = Named("big.bin");
            FileJob mid = Named("mid.bin");
            FileJob small = Named("a-small.bin");
            FileJob tiny = Named("z-tiny.bin");
            big.Size = 1000;
            mid.Size = 100;
            small.Size = 1;
            tiny.Size = 1;
            var jobs = new List<FileJob>();
            jobs.Add(small);
            jobs.Add(big);
            jobs.Add(tiny);
            jobs.Add(mid);
            CopyPlan.Arrange(jobs, CopyPlan.SmallestFirst, 1);
            Need(jobs[0].RelativePath == "a-small.bin", "small 0 " + jobs[0].RelativePath);
            Need(jobs[1].RelativePath == "z-tiny.bin", "small 1 " + jobs[1].RelativePath);
            Need(jobs[2].RelativePath == "mid.bin", "small 2 " + jobs[2].RelativePath);
            Need(jobs[3].RelativePath == "big.bin", "small 3 " + jobs[3].RelativePath);

            jobs.Clear();
            jobs.Add(small);
            jobs.Add(big);
            jobs.Add(tiny);
            jobs.Add(mid);
            CopyPlan.Arrange(jobs, CopyPlan.LargestFirst, 1);
            Need(jobs[0].RelativePath == "big.bin", "large 0 " + jobs[0].RelativePath);
            Need(jobs[1].RelativePath == "mid.bin", "large 1 " + jobs[1].RelativePath);
            Need(jobs[2].RelativePath == "a-small.bin", "large 2 " + jobs[2].RelativePath);
            Need(jobs[3].RelativePath == "z-tiny.bin", "large 3 " + jobs[3].RelativePath);
        }

        static void TestSizeOrderOptions()
        {
            using (MainForm form = new MainForm())
            {
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-2400, -2400);
                form.Show();
                Application.DoEvents();
                CheckBox smallest = null;
                CheckBox largest = null;
                CheckBox folder = null;
                CheckBox mix = null;
                foreach (Control c in form.Controls)
                {
                    CheckBox box = c as CheckBox;
                    if (box == null) continue;
                    string text = box.Text.Replace("&", "");
                    if (text == "Smallest to largest") smallest = box;
                    if (text == "Largest to smallest") largest = box;
                    if (text == "Folder order") folder = box;
                    if (text == "Large beside small") mix = box;
                }
                Need(smallest != null, "missing Smallest to largest");
                Need(largest != null, "missing Largest to smallest");
                Need(folder != null, "missing Folder order");
                Need(mix != null, "missing Large beside small");
                Need(folder.Checked, "folder order should start on");
                Need(!smallest.Checked && !largest.Checked && !mix.Checked, "size options should start off");
                Need(!smallest.Enabled, "smallest should be greyed while folder order is on");
                Need(!largest.Enabled, "largest should be greyed while folder order is on");
                folder.Checked = false;
                Application.DoEvents();
                Need(smallest.Enabled && largest.Enabled && mix.Enabled, "size options should unlock");
                smallest.Checked = true;
                Application.DoEvents();
                Need(smallest.Checked, "smallest stayed on");
                Need(!folder.Checked && !largest.Checked && !mix.Checked, "others should uncheck");
                Need(!folder.Enabled && !largest.Enabled && !mix.Enabled, "others should grey");
                smallest.Checked = false;
                largest.Checked = true;
                Application.DoEvents();
                Need(largest.Checked, "largest stayed on");
                Need(!smallest.Checked, "smallest should uncheck");
                Need(!smallest.Enabled, "smallest should grey");
                form.Hide();
            }
        }

        static void TestLargeList()
        {
            var jobs = new List<FileJob>();
            for (int i = 0; i < 50000; i++)
            {
                FileJob job = Named("f" + i.ToString("000000"));
                job.Size = i;
                jobs.Add(job);
            }
            CopySession session = new CopySession();
            session.SetJobs(jobs);
            Need(session.Count == 50000, "count " + session.Count);
            Need(session.Get(0).RelativePath == "f000000", "first");
            Need(session.Get(49999).RelativePath == "f049999", "last");
            Need(session.IndexOf(jobs[12345]) == 12345, "index " + session.IndexOf(jobs[12345]));

            jobs[10].State = FileJob.Done;
            jobs[11].State = FileJob.Done;
            jobs[40000].State = FileJob.Done;
            session.ApplyVisibility();
            Need(session.Count == 49997, "after hide " + session.Count);
            Need(session.IndexOf(jobs[10]) < 0, "hidden");
            Need(session.Get(10).RelativePath == "f000012", "shifted " + session.Get(10).RelativePath);
            Need(session.IndexOf(jobs[12]) == 10, "rank " + session.IndexOf(jobs[12]));
            FileJob[] buffer = new FileJob[3];
            int count = session.Snapshot(39997, buffer);
            Need(count == 49997, "snap " + count);
            Need(buffer[0].RelativePath == "f039999", buffer[0].RelativePath);
            Need(buffer[1].RelativePath == "f040001", buffer[1].RelativePath);
            Need(buffer[2].RelativePath == "f040002", buffer[2].RelativePath);
        }

        static FileJob Named(string path)
        {
            FileJob job = new FileJob();
            job.RelativePath = path;
            job.SourcePath = path;
            job.DestPath = path;
            job.Size = 10;
            job.State = FileJob.Waiting;
            return job;
        }

        static bool SameOrder(CopySession session, FileJob[] expected)
        {
            if (session.Count != expected.Length) return false;
            for (int i = 0; i < expected.Length; i++)
            {
                if (!object.ReferenceEquals(session.Get(i), expected[i])) return false;
            }
            return true;
        }

        static void TestDialogProbe()
        {
            FolderDialog.Probe();
        }

        static void TestScreenshot()
        {
            using (MainForm form = new MainForm())
            {
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-2400, -2400);
                form.Show();
                Application.DoEvents();
                form.LoadDemo();
                form.Refresh();
                Application.DoEvents();
                using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
                    string png = Path.Combine(Path.GetTempPath(), "folderdiffcopy-preview.png");
                    bitmap.Save(png, System.Drawing.Imaging.ImageFormat.Png);
                }
                form.Hide();
            }
        }

        static void Wait(CopySession session, int timeoutMs)
        {
            int deadline = Environment.TickCount + timeoutMs;
            while (!session.IsFinished && unchecked(deadline - Environment.TickCount) > 0)
                Thread.Sleep(10);
            Need(session.IsFinished, "timed out");
        }
    }
}
