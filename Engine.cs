using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace FolderDiffCopy
{
    internal static class Paths
    {
        public static string Normalize(string path)
        {
            if (path == null) return "";
            path = path.Trim().Trim('"').Trim();
            if (path.Length == 0) return "";
            try
            {
                path = Path.GetFullPath(path);
            }
            catch (PathTooLongException)
            {
            }
            catch (IOException)
            {
            }
            if (path.Length > 3)
                path = path.TrimEnd('\\');
            return path;
        }

        public static bool Exists(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return Native.DirExists(Native.ToExtended(path));
        }

        public static bool Same(string a, string b)
        {
            return string.Equals(WithSlash(a), WithSlash(b), StringComparison.OrdinalIgnoreCase);
        }

        public static bool OneInsideTheOther(string a, string b)
        {
            string left = WithSlash(a);
            string right = WithSlash(b);
            if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase)) return false;
            return right.StartsWith(left, StringComparison.OrdinalIgnoreCase)
                || left.StartsWith(right, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsDriveRoot(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string trimmed = path.TrimEnd('\\');
            if (trimmed.Length == 2 && trimmed[1] == ':') return true;
            if (trimmed.StartsWith(@"\\"))
            {
                string rest = trimmed.Substring(2);
                int slash = rest.IndexOf('\\');
                if (slash > 0 && rest.IndexOf('\\', slash + 1) < 0) return true;
            }
            return false;
        }

        static string WithSlash(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            if (path.EndsWith("\\")) return path;
            return path + "\\";
        }
    }

    internal sealed class FileJob
    {
        public const int Waiting = 0;
        public const int Copying = 1;
        public const int Done = 2;
        public const int Failed = 3;

        public string RelativePath;
        public string SourcePath;
        public string DestPath;
        public long Size;
        public long Copied;
        public volatile int State;
        public string Error;
        public int Way;
        internal int Slot = -1;

        public string DisplayPath
        {
            get
            {
                if (Way == 2) return "B → A  " + RelativePath;
                if (Way == 1) return "A → B  " + RelativePath;
                return RelativePath ?? "";
            }
        }
    }

    internal struct FileMeta
    {
        public long Size;
        public long WriteTime;
    }

    internal sealed class ScanStatus
    {
        public volatile bool Cancel;
        public volatile int FilesA;
        public volatile int FilesB;
        public volatile int Links;
        public volatile int Unreadable;
        public string FirstProblem;
    }

    internal sealed class CompareResult
    {
        public List<FileJob> Jobs = new List<FileJob>();
        public int Unchanged;
        public int DirsCreated;
        public int DirCreateFailed;
        public int LinksSkipped;
        public int Unreadable;
        public string FirstProblem;
        public bool Cancelled;
    }

    internal static class Diff
    {
        const long TwoSeconds = 20000000L;

        public static CompareResult Compare(string folderA, string folderB, ScanStatus status)
        {
            return Compare(folderA, folderB, status, true, false);
        }

        public static CompareResult Compare(string folderA, string folderB, ScanStatus status, bool writeFolders)
        {
            return Compare(folderA, folderB, status, writeFolders, false);
        }

        public static CompareResult Compare(string folderA, string folderB, ScanStatus status, bool writeFolders, bool bothWays)
        {
            if (status == null) status = new ScanStatus();
            folderA = Paths.Normalize(folderA);
            folderB = Paths.Normalize(folderB);
            if (folderA.Length == 0 || folderB.Length == 0)
                throw new InvalidOperationException("Choose Folder A and Folder B.");
            if (!Paths.Exists(folderA))
                throw new InvalidOperationException("Folder A does not exist.");
            if (writeFolders && !Paths.Exists(folderB))
                throw new InvalidOperationException("Folder B does not exist.");
            if (Paths.Same(folderA, folderB) || Paths.OneInsideTheOther(folderA, folderB))
                throw new InvalidOperationException("Pick two folders that do not contain each other.");

            CompareResult result = new CompareResult();
            var filesA = bothWays ? new Dictionary<string, FileMeta>(StringComparer.OrdinalIgnoreCase) : null;
            var filesB = new Dictionary<string, FileMeta>(StringComparer.OrdinalIgnoreCase);
            var dirsB = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dirsA = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (Paths.Exists(folderB))
            {
                ScanTree(folderB, dirsB, false, status, delegate(string rel, FileMeta meta)
                {
                    filesB[rel] = meta;
                });
            }
            if (status.Cancel)
            {
                result.Cancelled = true;
                return result;
            }

            if (bothWays)
            {
                ScanTree(folderA, dirsA, true, status, delegate(string rel, FileMeta meta)
                {
                    if (!filesA.ContainsKey(rel)) filesA[rel] = meta;
                });
                if (status.Cancel)
                {
                    result.Cancelled = true;
                    return result;
                }

                foreach (KeyValuePair<string, FileMeta> pair in filesA)
                {
                    if (status.Cancel)
                    {
                        result.Cancelled = true;
                        return result;
                    }
                    string rel = pair.Key;
                    FileMeta meta = pair.Value;
                    FileMeta other;
                    bool exists = filesB.TryGetValue(rel, out other);
                    if (exists && other.Size == meta.Size && !TimesDiffer(meta.WriteTime, other.WriteTime))
                    {
                        result.Unchanged++;
                        continue;
                    }
                    if (exists && other.WriteTime > meta.WriteTime)
                        result.Jobs.Add(MakeJob(folderB, folderA, rel, other.Size, dirsA, filesA, true, true));
                    else
                        result.Jobs.Add(MakeJob(folderA, folderB, rel, meta.Size, dirsB, filesB, false, true));
                }
                foreach (KeyValuePair<string, FileMeta> pair in filesB)
                {
                    if (status.Cancel)
                    {
                        result.Cancelled = true;
                        return result;
                    }
                    if (filesA.ContainsKey(pair.Key)) continue;
                    result.Jobs.Add(MakeJob(folderB, folderA, pair.Key, pair.Value.Size, dirsA, filesA, true, true));
                }
            }
            else
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                ScanTree(folderA, dirsA, true, status, delegate(string rel, FileMeta meta)
                {
                    if (!seen.Add(rel)) return;
                    FileMeta other;
                    bool exists = filesB.TryGetValue(rel, out other);
                    if (exists && other.Size == meta.Size && !TimesDiffer(meta.WriteTime, other.WriteTime))
                    {
                        result.Unchanged++;
                        return;
                    }
                    result.Jobs.Add(MakeJob(folderA, folderB, rel, meta.Size, dirsB, filesB, false, false));
                });
            }
            if (status.Cancel)
            {
                result.Cancelled = true;
                return result;
            }

            result.Jobs.Sort(delegate(FileJob x, FileJob y)
            {
                return string.Compare(x.RelativePath, y.RelativePath, StringComparison.OrdinalIgnoreCase);
            });

            if (writeFolders)
            {
                CreateMissingDirs(dirsA, dirsB, filesB, folderB, status, result, "Folder B has a file where Folder A has a folder: ");
                if (result.Cancelled) return result;
                if (bothWays)
                {
                    CreateMissingDirs(dirsB, dirsA, filesA, folderA, status, result, "Folder A has a file where Folder B has a folder: ");
                    if (result.Cancelled) return result;
                }
            }

            result.LinksSkipped = status.Links;
            result.Unreadable = status.Unreadable;
            if (result.FirstProblem == null) result.FirstProblem = status.FirstProblem;
            return result;
        }

        static FileJob MakeJob(string fromRoot, string toRoot, string rel, long size, HashSet<string> destDirs, Dictionary<string, FileMeta> destFiles, bool toA, bool labelWay)
        {
            FileJob job = new FileJob();
            job.RelativePath = rel;
            job.SourcePath = Native.ToExtended(Join(fromRoot, rel));
            job.DestPath = Native.ToExtended(Join(toRoot, rel));
            job.Size = size;
            job.State = FileJob.Waiting;
            if (labelWay) job.Way = toA ? 2 : 1;
            if (destDirs.Contains(rel) || ParentIsFile(rel, destFiles))
            {
                job.State = FileJob.Failed;
                string destName = toA ? "Folder A" : "Folder B";
                job.Error = destDirs.Contains(rel)
                    ? destName + " has a folder with this name."
                    : destName + " has a file in the way of this path.";
            }
            return job;
        }

        static void CreateMissingDirs(HashSet<string> sourceDirs, HashSet<string> destDirs, Dictionary<string, FileMeta> destFiles, string destRoot, ScanStatus status, CompareResult result, string blockedMessage)
        {
            foreach (string dir in sourceDirs)
            {
                if (status.Cancel)
                {
                    result.Cancelled = true;
                    return;
                }
                if (destDirs.Contains(dir)) continue;
                if (destFiles.ContainsKey(dir))
                {
                    result.DirCreateFailed++;
                    if (result.FirstProblem == null)
                        result.FirstProblem = blockedMessage + dir;
                    continue;
                }
                try
                {
                    Native.EnsureDirectory(Native.ToExtended(Join(destRoot, dir)));
                    result.DirsCreated++;
                }
                catch (Exception ex)
                {
                    result.DirCreateFailed++;
                    if (result.FirstProblem == null) result.FirstProblem = ex.Message;
                }
            }
        }

        static bool TimesDiffer(long a, long b)
        {
            long delta = a > b ? a - b : b - a;
            // FAT stores modified times in 2 second steps. Closer than that counts as the same time.
            return delta > TwoSeconds;
        }

        static bool ParentIsFile(string rel, Dictionary<string, FileMeta> files)
        {
            int cut = rel.LastIndexOf('\\');
            while (cut > 0)
            {
                string parent = rel.Substring(0, cut);
                if (files.ContainsKey(parent)) return true;
                cut = parent.LastIndexOf('\\');
            }
            return false;
        }

        static string Join(string root, string rel)
        {
            if (root.EndsWith("\\")) return root + rel;
            return root + "\\" + rel;
        }

        static void ScanTree(string root, HashSet<string> dirs, bool countAsA, ScanStatus status, Action<string, FileMeta> onFile)
        {
            var stack = new Stack<string>();
            stack.Push("");
            while (stack.Count > 0)
            {
                if (status.Cancel) return;
                string relDir = stack.Pop();
                string full = relDir.Length == 0 ? root : Join(root, relDir);
                string spec = Native.ToExtended(full).TrimEnd('\\') + "\\*";
                FindData data;
                SafeFindHandle find = Native.FindFirst(spec, out data);
                if (find == null || find.IsInvalid)
                {
                    int err = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                    if (find != null) find.Dispose();
                    if (relDir.Length == 0 && (err == 2 || err == 3))
                        throw new DirectoryNotFoundException("The folder does not exist: " + root);
                    NoteProblem(status, err);
                    continue;
                }
                try
                {
                    for (;;)
                    {
                        if (status.Cancel) return;
                        string name = data.FileName;
                        if (!string.IsNullOrEmpty(name) && name != "." && name != "..")
                        {
                            bool isDir = (data.Attributes & 0x10) != 0;
                            bool reparse = (data.Attributes & 0x400) != 0;
                            string rel = relDir.Length == 0 ? name : relDir + "\\" + name;
                            if (reparse)
                            {
                                status.Links++;
                            }
                            else if (isDir)
                            {
                                dirs.Add(rel);
                                stack.Push(rel);
                            }
                            else
                            {
                                FileMeta meta = new FileMeta();
                                meta.Size = ((long)data.SizeHigh << 32) | data.SizeLow;
                                meta.WriteTime = data.Written.ToLong();
                                if (countAsA) status.FilesA++;
                                else status.FilesB++;
                                onFile(rel, meta);
                            }
                        }
                        if (!Native.FindNext(find, out data))
                        {
                            int err = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                            if (err != 0 && err != 18)
                                NoteProblem(status, err);
                            break;
                        }
                    }
                }
                finally
                {
                    find.Dispose();
                }
            }
        }

        static void NoteProblem(ScanStatus status, int err)
        {
            status.Unreadable++;
            if (status.FirstProblem == null && err != 0)
                status.FirstProblem = new System.ComponentModel.Win32Exception(err).Message;
        }
    }

    internal enum CopyOutcome
    {
        Success,
        Failed,
        Cancelled
    }

    internal sealed class CopySession
    {
        readonly object gate = new object();
        readonly ConcurrentQueue<int> doneTicks = new ConcurrentQueue<int>();
        List<FileJob> order = new List<FileJob>();
        List<FileJob> waiting = new List<FileJob>();
        readonly List<FileJob> active = new List<FileJob>();
        readonly List<FileJob> failed = new List<FileJob>();
        int waitStart;
        int running;
        int desired = 1;
        int peak;
        int completedFiles;
        int totalJobs;
        int shown;
        int[] bit = new int[1];
        long totalBytes;
        long copiedBytes;
        volatile bool cancel;
        volatile bool started;

        public volatile bool IsFinished;
        public volatile bool WasCancelled;
        public int TestPauseMs;

        public int PeakActive
        {
            get { lock (gate) return peak; }
        }

        public int WaitingCount
        {
            get { lock (gate) return Math.Max(0, waiting.Count - waitStart); }
        }

        public int Count
        {
            get { lock (gate) return DisplayCount(); }
        }

        public void SetJobs(List<FileJob> jobs)
        {
            long bytes = 0;
            var queue = new List<FileJob>();
            var broken = new List<FileJob>();
            int total = 0;
            if (jobs != null)
            {
                for (int i = 0; i < jobs.Count; i++)
                {
                    FileJob job = jobs[i];
                    total++;
                    if (job.State == FileJob.Failed)
                    {
                        broken.Add(job);
                    }
                    else
                    {
                        job.State = FileJob.Waiting;
                        queue.Add(job);
                        bytes += job.Size;
                    }
                }
            }
            lock (gate)
            {
                waiting = queue;
                failed.Clear();
                failed.AddRange(broken);
                active.Clear();
                order = jobs == null ? new List<FileJob>() : new List<FileJob>(jobs);
                BuildIndex();
                waitStart = 0;
                totalJobs = total;
                completedFiles = 0;
                peak = 0;
                running = 0;
            }
            Interlocked.Exchange(ref totalBytes, bytes);
            Interlocked.Exchange(ref copiedBytes, 0);
        }

        public void Start(int parallelism)
        {
            if (parallelism < 1) parallelism = 1;
            if (parallelism > 32) parallelism = 32;
            lock (gate)
            {
                desired = parallelism;
                started = true;
                if (waitStart >= waiting.Count)
                {
                    IsFinished = true;
                    return;
                }
            }
            Pump();
        }

        public void SetParallelism(int parallelism)
        {
            if (parallelism < 1) parallelism = 1;
            if (parallelism > 32) parallelism = 32;
            bool pump = false;
            lock (gate)
            {
                desired = parallelism;
                pump = started && !IsFinished && !cancel;
            }
            if (pump) Pump();
        }

        public void Cancel()
        {
            WasCancelled = true;
            cancel = true;
        }

        public void ReadTotals(out long total, out long copied, out int completed, out int remaining, out int failedCount, out int jobs)
        {
            total = Interlocked.Read(ref totalBytes);
            copied = Interlocked.Read(ref copiedBytes);
            lock (gate)
            {
                completed = completedFiles;
                failedCount = failed.Count;
                remaining = DisplayCount();
                jobs = totalJobs;
            }
        }

        public int DrainTicks(List<int> dest)
        {
            int n = 0;
            int tick;
            while (doneTicks.TryDequeue(out tick))
            {
                dest.Add(tick);
                n++;
            }
            return n;
        }

        public int Snapshot(int start, FileJob[] buffer)
        {
            lock (gate)
            {
                if (start < 0) start = 0;
                for (int i = 0; i < buffer.Length; i++)
                {
                    int index = start + i;
                    buffer[i] = index >= 0 && index < shown ? order[FindShown(index)] : null;
                }
                return shown;
            }
        }

        public FileJob Get(int index)
        {
            lock (gate) return GetUnlocked(index);
        }

        public int IndexOf(FileJob job)
        {
            if (job == null) return -1;
            lock (gate) return IndexOfUnlocked(job);
        }

        internal void ApplyVisibility()
        {
            lock (gate)
            {
                BuildIndex();
                for (int i = 0; i < order.Count; i++)
                {
                    if (order[i].State == FileJob.Done)
                        HideUnlocked(order[i]);
                }
            }
        }

        int DisplayCount()
        {
            return shown;
        }

        FileJob GetUnlocked(int index)
        {
            if (index < 0 || index >= shown) return null;
            return order[FindShown(index)];
        }

        int IndexOfUnlocked(FileJob job)
        {
            if (job == null) return -1;
            int slot = job.Slot;
            if (slot < 0 || slot >= order.Count) return -1;
            if (!object.ReferenceEquals(order[slot], job)) return -1;
            if (SumTo(slot + 1) == SumTo(slot)) return -1;
            return SumTo(slot);
        }

        void BuildIndex()
        {
            int n = order.Count;
            bit = new int[n + 1];
            shown = 0;
            for (int i = 0; i < n; i++)
            {
                order[i].Slot = i;
                AddBit(i + 1, 1);
                shown++;
            }
        }

        void HideUnlocked(FileJob job)
        {
            if (job == null) return;
            int slot = job.Slot;
            if (slot < 0 || slot >= order.Count) return;
            if (!object.ReferenceEquals(order[slot], job)) return;
            if (SumTo(slot + 1) == SumTo(slot)) return;
            AddBit(slot + 1, -1);
            shown--;
        }

        void AddBit(int index, int delta)
        {
            int n = bit.Length - 1;
            while (index <= n)
            {
                bit[index] += delta;
                index += index & -index;
            }
        }

        int SumTo(int index)
        {
            int sum = 0;
            while (index > 0)
            {
                sum += bit[index];
                index -= index & -index;
            }
            return sum;
        }

        int FindShown(int index)
        {
            int target = index + 1;
            int n = bit.Length - 1;
            int step = 1;
            while (step <= n / 2) step <<= 1;
            int at = 0;
            for (; step != 0; step >>= 1)
            {
                int next = at + step;
                if (next <= n && bit[next] < target)
                {
                    at = next;
                    target -= bit[next];
                }
            }
            return at;
        }

        void Pump()
        {
            var start = new List<FileJob>();
            lock (gate)
            {
                if (cancel || IsFinished) return;
                while (running < desired && waitStart < waiting.Count)
                {
                    FileJob job = waiting[waitStart];
                    waitStart++;
                    job.State = FileJob.Copying;
                    active.Add(job);
                    running++;
                    if (running > peak) peak = running;
                    start.Add(job);
                }
            }
            for (int i = 0; i < start.Count; i++)
                ThreadPool.QueueUserWorkItem(CopyCallback, start[i]);
        }

        void CopyCallback(object state)
        {
            FileJob job = (FileJob)state;
            CopyOutcome outcome;
            try
            {
                outcome = CopyOne(job);
            }
            catch (Exception ex)
            {
                job.Error = ex.Message;
                outcome = CopyOutcome.Failed;
            }
            OnJobDone(job, outcome);
        }

        void OnJobDone(FileJob job, CopyOutcome outcome)
        {
            bool pump = false;
            lock (gate)
            {
                active.Remove(job);
                running--;
                if (outcome == CopyOutcome.Success)
                {
                    job.State = FileJob.Done;
                    HideUnlocked(job);
                    completedFiles++;
                    doneTicks.Enqueue(Environment.TickCount);
                }
                else if (outcome == CopyOutcome.Cancelled)
                {
                    job.State = FileJob.Waiting;
                    job.Error = null;
                    waiting.Insert(waitStart, job);
                }
                else
                {
                    job.State = FileJob.Failed;
                    if (string.IsNullOrEmpty(job.Error))
                        job.Error = "Could not copy this file.";
                    failed.Add(job);
                }

                if (running == 0 && (cancel || waitStart >= waiting.Count))
                    IsFinished = true;
                else
                    pump = !cancel && !IsFinished && waitStart < waiting.Count && running < desired;
            }
            if (pump) Pump();
        }

        CopyOutcome CopyOne(FileJob job)
        {
            if (cancel) return CopyOutcome.Cancelled;
            string temp = job.DestPath + ".fdc-partial";
            bool committed = false;
            OpenedRead source = null;
            FileStream output = null;
            try
            {
                Native.EnsureDirectory(Native.Parent(temp));
                if (cancel) return CopyOutcome.Cancelled;
                source = Native.OpenForRead(job.SourcePath);
                if (source.Size >= 0 && source.Size != job.Size)
                {
                    Interlocked.Add(ref totalBytes, source.Size - job.Size);
                    job.Size = source.Size;
                }
                Native.DeleteQuiet(temp);
                output = Native.OpenForWrite(temp);
                byte[] buffer = new byte[256 * 1024];
                long written = 0;
                while (true)
                {
                    if (cancel) return CopyOutcome.Cancelled;
                    int n = source.Stream.Read(buffer, 0, buffer.Length);
                    if (n <= 0) break;
                    output.Write(buffer, 0, n);
                    written += n;
                    Interlocked.Exchange(ref job.Copied, written);
                    Interlocked.Add(ref copiedBytes, n);
                    int pause = TestPauseMs;
                    if (pause > 0) Thread.Sleep(pause);
                }
                if (cancel) return CopyOutcome.Cancelled;
                if (!Native.ApplyTimes(output.SafeFileHandle, source.Created, source.Accessed, source.Written))
                    throw new IOException("Could not keep the modified time of " + job.RelativePath + ".");
                output.Flush();
                output.Dispose();
                output = null;
                source.Dispose();
                source = null;
                Native.MoveReplace(temp, job.DestPath);
                committed = true;
                try { Native.CopyAttributes(job.SourcePath, job.DestPath); }
                catch { }
                return CopyOutcome.Success;
            }
            catch (Exception ex)
            {
                if (string.IsNullOrEmpty(job.Error))
                    job.Error = ex.Message;
                return cancel ? CopyOutcome.Cancelled : CopyOutcome.Failed;
            }
            finally
            {
                try { if (output != null) output.Dispose(); }
                catch { }
                try { if (source != null) source.Dispose(); }
                catch { }
                if (!committed)
                {
                    long partial = Interlocked.Exchange(ref job.Copied, 0);
                    if (partial != 0)
                        Interlocked.Add(ref copiedBytes, -partial);
                    Native.DeleteQuiet(temp);
                }
            }
        }
    }

    internal static class CopyPlan
    {
        public const int PathOrder = 0;
        public const int LargeWithSmall = 1;
        public const int SmallestFirst = 2;
        public const int LargestFirst = 3;

        public static void Arrange(List<FileJob> jobs, int order, int width)
        {
            if (jobs == null || jobs.Count < 2) return;
            if (order == LargeWithSmall)
                Interleave(jobs, width);
            else if (order == SmallestFirst)
                SortBySize(jobs, true);
            else if (order == LargestFirst)
                SortBySize(jobs, false);
            else
                SortByPath(jobs);
        }

        public static void SortByPath(List<FileJob> jobs)
        {
            jobs.Sort(delegate(FileJob x, FileJob y)
            {
                return string.Compare(x.RelativePath, y.RelativePath, StringComparison.OrdinalIgnoreCase);
            });
        }

        static void SortBySize(List<FileJob> jobs, bool smallestFirst)
        {
            jobs.Sort(delegate(FileJob x, FileJob y)
            {
                int size = smallestFirst ? x.Size.CompareTo(y.Size) : y.Size.CompareTo(x.Size);
                if (size != 0) return size;
                return string.Compare(x.RelativePath, y.RelativePath, StringComparison.OrdinalIgnoreCase);
            });
        }

        static void Interleave(List<FileJob> jobs, int width)
        {
            if (width < 2) width = 2;
            var bySize = new List<FileJob>(jobs);
            bySize.Sort(delegate(FileJob x, FileJob y)
            {
                int size = y.Size.CompareTo(x.Size);
                if (size != 0) return size;
                return string.Compare(x.RelativePath, y.RelativePath, StringComparison.OrdinalIgnoreCase);
            });
            jobs.Clear();
            int large = 0;
            int small = bySize.Count - 1;
            while (large <= small)
            {
                jobs.Add(bySize[large]);
                large++;
                int extras = width - 1;
                for (int n = 0; n < extras && small >= large; n++)
                {
                    jobs.Add(bySize[small]);
                    small--;
                }
            }
        }
    }
}
