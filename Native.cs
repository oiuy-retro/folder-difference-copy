using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace FolderDiffCopy
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct FileTime
    {
        public uint Low;
        public uint High;

        public long ToLong()
        {
            return ((long)High << 32) | Low;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct FindData
    {
        public uint Attributes;
        public FileTime Created;
        public FileTime Accessed;
        public FileTime Written;
        public uint SizeHigh;
        public uint SizeLow;
        public uint Reserved0;
        public uint Reserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string FileName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
        public string Alternate;
    }

    internal sealed class SafeFindHandle : SafeHandle
    {
        public SafeFindHandle() : base(new IntPtr(-1), true) { }

        public override bool IsInvalid
        {
            get { return handle == IntPtr.Zero || handle == new IntPtr(-1); }
        }

        protected override bool ReleaseHandle()
        {
            return FindClose(handle);
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool FindClose(IntPtr handle);
    }

    internal sealed class OpenedRead : IDisposable
    {
        public FileStream Stream;
        public FileTime Created;
        public FileTime Accessed;
        public FileTime Written;
        public long Size;

        public void Dispose()
        {
            if (Stream != null)
            {
                Stream.Dispose();
                Stream = null;
            }
        }
    }

    internal static class Native
    {
        const uint GenericRead = 0x80000000;
        const uint GenericWrite = 0x40000000;
        const uint ShareAll = 1 | 2 | 4;
        const uint ShareRead = 1;
        const uint OpenExisting = 3;
        const uint CreateAlways = 2;
        const uint Sequential = 0x08000000;
        const uint AttrNormal = 0x80;
        const uint InvalidAttributes = 0xFFFFFFFF;
        const uint FileAttributeDirectory = 0x10;
        const uint FileAttributeReadonly = 0x1;
        const uint AttrCopyMask = 0x1 | 0x2 | 0x4 | 0x20 | 0x100 | 0x2000;
        const int MoveFileReplaceExisting = 0x1;
        const int MoveFileWriteThrough = 0x8;
        const int ErrorAlreadyExists = 183;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern SafeFindHandle FindFirstFile(string fileName, out FindData data);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool FindNextFile(SafeFindHandle handle, out FindData data);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern SafeFileHandle CreateFile(
            string fileName,
            uint access,
            uint share,
            IntPtr security,
            uint disposition,
            uint flags,
            IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetFileTime(SafeFileHandle file, out FileTime created, out FileTime accessed, out FileTime written);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetFileTime(SafeFileHandle file, ref FileTime created, ref FileTime accessed, ref FileTime written);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetFileSizeEx(SafeFileHandle file, out long size);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern uint GetFileAttributes(string path);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool SetFileAttributes(string path, uint attributes);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool CreateDirectory(string path, IntPtr security);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool DeleteFile(string path);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool MoveFileEx(string existing, string destination, int flags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, uint length, uint flags);

        const uint ReadAttributesAccess = 0x80;
        const uint BackupSemantics = 0x02000000;
        const uint FileNameNormalized = 0x0;

        // The real location of a folder on disk, with junctions, symbolic links, subst drives,
        // and mapped network drives followed, as a plain path such as C:\Music or
        // \\server\share\Music. A folder that does not exist yet is resolved through its
        // nearest existing parent. Returns null when Windows cannot tell.
        public static string RealPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string current = Pretty(path);
            if (current.Length > 3) current = current.TrimEnd('\\');
            string tail = "";
            for (int guard = 0; guard < 4096; guard++)
            {
                string resolved = FinalPathOfDirectory(ToExtended(current));
                if (resolved != null)
                {
                    resolved = Pretty(resolved);
                    if (tail.Length == 0) return resolved;
                    return resolved.TrimEnd('\\') + "\\" + tail;
                }
                string trimmed = current.TrimEnd('\\');
                int cut = trimmed.LastIndexOf('\\');
                if (cut <= 1) return null;
                string name = trimmed.Substring(cut + 1);
                if (name.Length == 0) return null;
                tail = tail.Length == 0 ? name : name + "\\" + tail;
                current = trimmed.Substring(0, cut);
                if (current.Length == 2 && current[1] == ':') current += "\\";
            }
            return null;
        }

        static string FinalPathOfDirectory(string extendedPath)
        {
            if (!DirExists(extendedPath)) return null;
            SafeFileHandle handle = CreateFile(extendedPath, ReadAttributesAccess, ShareAll, IntPtr.Zero, OpenExisting, BackupSemantics, IntPtr.Zero);
            try
            {
                if (handle.IsInvalid) return null;
                var buffer = new StringBuilder(1024);
                uint length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, FileNameNormalized);
                if (length >= buffer.Capacity)
                {
                    buffer = new StringBuilder((int)length + 1);
                    length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, FileNameNormalized);
                }
                if (length == 0 || length >= buffer.Capacity) return null;
                return buffer.ToString(0, (int)length);
            }
            catch (EntryPointNotFoundException)
            {
                return null;
            }
            finally
            {
                handle.Dispose();
            }
        }

        public static string ToExtended(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return fullPath;
            if (fullPath.StartsWith(@"\\?\")) return fullPath;
            if (fullPath.StartsWith(@"\\")) return @"\\?\UNC\" + fullPath.Substring(2);
            return @"\\?\" + fullPath;
        }

        public static string Pretty(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            if (path.StartsWith(@"\\?\UNC\")) return @"\\" + path.Substring(8);
            if (path.StartsWith(@"\\?\")) return path.Substring(4);
            return path;
        }

        public static bool DirExists(string extendedPath)
        {
            uint attr = GetFileAttributes(extendedPath);
            if (attr == InvalidAttributes) return false;
            return (attr & FileAttributeDirectory) != 0;
        }

        public static bool FileExists(string extendedPath)
        {
            uint attr = GetFileAttributes(extendedPath);
            if (attr == InvalidAttributes) return false;
            return (attr & FileAttributeDirectory) == 0;
        }

        public static bool IsRoot(string extendedPath)
        {
            if (string.IsNullOrEmpty(extendedPath)) return false;
            string path = extendedPath.TrimEnd('\\');
            if (path.Length == 6 && path.StartsWith(@"\\?\") && path[5] == ':') return true;
            if (path.StartsWith(@"\\?\UNC\"))
            {
                string rest = path.Substring(8);
                int slash = rest.IndexOf('\\');
                if (slash > 0 && rest.IndexOf('\\', slash + 1) < 0) return true;
            }
            return false;
        }

        public static void EnsureDirectory(string extendedPath)
        {
            if (string.IsNullOrEmpty(extendedPath) || IsRoot(extendedPath)) return;
            string path = extendedPath.TrimEnd('\\');
            if (DirExists(path)) return;

            string prefix;
            string rest;
            int startParts;
            if (path.StartsWith(@"\\?\UNC\"))
            {
                prefix = @"\\?\UNC\";
                rest = path.Substring(prefix.Length);
                startParts = 2;
            }
            else if (path.StartsWith(@"\\?\"))
            {
                prefix = @"\\?\";
                rest = path.Substring(prefix.Length);
                startParts = 1;
            }
            else
            {
                prefix = "";
                rest = path;
                startParts = 1;
            }

            string[] parts = rest.Split('\\');
            var current = new StringBuilder();
            current.Append(prefix);
            if (startParts == 2)
            {
                if (parts.Length < 2) return;
                current.Append(parts[0]).Append('\\').Append(parts[1]);
            }
            else
            {
                if (parts.Length == 0 || parts[0].Length == 0) return;
                current.Append(parts[0]);
            }

            for (int i = startParts; i < parts.Length; i++)
            {
                if (parts[i].Length == 0) continue;
                current.Append('\\').Append(parts[i]);
                string dir = current.ToString();
                if (DirExists(dir)) continue;
                if (!CreateDirectory(dir, IntPtr.Zero) && !DirExists(dir))
                    throw Io("Could not create folder " + Pretty(dir));
            }
        }

        public static string Parent(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            string trimmed = path.TrimEnd('\\');
            int i = trimmed.LastIndexOf('\\');
            if (i <= 0) return trimmed;
            return trimmed.Substring(0, i);
        }

        public static SafeFindHandle FindFirst(string spec, out FindData data)
        {
            return FindFirstFile(spec, out data);
        }

        public static bool FindNext(SafeFindHandle handle, out FindData data)
        {
            return FindNextFile(handle, out data);
        }

        public static OpenedRead OpenForRead(string extendedPath)
        {
            SafeFileHandle handle = CreateFile(extendedPath, GenericRead, ShareAll, IntPtr.Zero, OpenExisting, Sequential, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                int err = Marshal.GetLastWin32Error();
                handle.Dispose();
                throw Io("Could not open " + Pretty(extendedPath), err);
            }

            FileTime created, accessed, written;
            if (!GetFileTime(handle, out created, out accessed, out written))
            {
                int err = Marshal.GetLastWin32Error();
                handle.Dispose();
                throw Io("Could not read the time of " + Pretty(extendedPath), err);
            }

            long size;
            if (!GetFileSizeEx(handle, out size)) size = -1;

            FileStream stream;
            try
            {
                stream = new FileStream(handle, FileAccess.Read, 262144, false);
            }
            catch
            {
                handle.Dispose();
                throw;
            }

            OpenedRead opened = new OpenedRead();
            opened.Stream = stream;
            opened.Created = created;
            opened.Accessed = accessed;
            opened.Written = written;
            opened.Size = size;
            return opened;
        }

        public static FileStream OpenForWrite(string extendedPath)
        {
            SafeFileHandle handle = CreateFile(
                extendedPath,
                GenericWrite,
                ShareRead,
                IntPtr.Zero,
                CreateAlways,
                AttrNormal | Sequential,
                IntPtr.Zero);
            if (handle.IsInvalid)
            {
                int err = Marshal.GetLastWin32Error();
                handle.Dispose();
                throw Io("Could not create " + Pretty(extendedPath), err);
            }
            try
            {
                return new FileStream(handle, FileAccess.Write, 262144, false);
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }

        public static bool ApplyTimes(SafeFileHandle handle, FileTime created, FileTime accessed, FileTime written)
        {
            return SetFileTime(handle, ref created, ref accessed, ref written);
        }

        public static void ClearReadOnly(string extendedPath)
        {
            uint attr = GetFileAttributes(extendedPath);
            if (attr == InvalidAttributes) return;
            if ((attr & FileAttributeReadonly) == 0) return;
            SetFileAttributes(extendedPath, attr & ~FileAttributeReadonly);
        }

        public static void CopyAttributes(string source, string dest)
        {
            uint attr = GetFileAttributes(source);
            if (attr == InvalidAttributes) return;
            uint keep = attr & AttrCopyMask;
            if (keep == 0) keep = AttrNormal;
            SetFileAttributes(dest, keep);
        }

        public static void MoveReplace(string from, string to)
        {
            ClearReadOnly(to);
            if (!MoveFileEx(from, to, MoveFileReplaceExisting | MoveFileWriteThrough))
                throw Io("Could not replace " + Pretty(to));
        }

        public static void DeleteQuiet(string extendedPath)
        {
            if (!FileExists(extendedPath)) return;
            ClearReadOnly(extendedPath);
            DeleteFile(extendedPath);
        }

        public static long Fnv(string extendedPath)
        {
            long hash = unchecked((long)1469598103934665603L);
            using (OpenedRead opened = OpenForRead(extendedPath))
            {
                byte[] buffer = new byte[8192];
                int n;
                while ((n = opened.Stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    for (int i = 0; i < n; i++)
                    {
                        hash ^= buffer[i];
                        hash *= 1099511628211L;
                    }
                }
            }
            return hash;
        }

        public static void WriteAllBytes(string extendedPath, byte[] data)
        {
            EnsureDirectory(Parent(extendedPath));
            using (FileStream stream = OpenForWrite(extendedPath))
            {
                stream.Write(data, 0, data.Length);
                stream.Flush();
            }
        }

        static IOException Io(string message)
        {
            int err = Marshal.GetLastWin32Error();
            return Io(message, err);
        }

        static IOException Io(string message, int err)
        {
            string detail = err == 0 ? "" : " " + new Win32Exception(err).Message;
            return new IOException(message + "." + detail);
        }
    }
}
