using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FolderDiffCopy
{
    internal static class FolderDialog
    {
        const uint FosNoChangeDir = 0x8;
        const uint FosPickFolders = 0x20;
        const uint FosForceFileSystem = 0x40;
        const uint FosPathMustExist = 0x800;
        const uint SigdnFileSystemPath = 0x80058000;
        const int Cancelled = unchecked((int)0x800704C7);

        public static void Probe()
        {
            IFileOpenDialog dialog = (IFileOpenDialog)new FileOpenDialog();
            try
            {
                uint options;
                dialog.GetOptions(out options);
                dialog.SetOptions(options | FosPickFolders | FosForceFileSystem | FosPathMustExist | FosNoChangeDir);
                dialog.SetTitle("Folder Difference Copy");
            }
            finally
            {
                Marshal.ReleaseComObject(dialog);
            }
        }

        public static string Pick(IWin32Window owner, string title, string startPath)
        {
            try
            {
                return PickExplorer(owner, title, startPath);
            }
            catch (Exception)
            {
                return PickLegacy(owner, title, startPath);
            }
        }

        static string PickExplorer(IWin32Window owner, string title, string startPath)
        {
            IFileOpenDialog dialog = null;
            try
            {
                dialog = (IFileOpenDialog)new FileOpenDialog();
                uint options;
                dialog.GetOptions(out options);
                dialog.SetOptions(options | FosPickFolders | FosForceFileSystem | FosPathMustExist | FosNoChangeDir);
                if (!string.IsNullOrEmpty(title))
                    dialog.SetTitle(title);
                if (!string.IsNullOrEmpty(startPath) && Paths.Exists(startPath))
                {
                    Guid iid = typeof(IShellItem).GUID;
                    IShellItem start;
                    int hrItem = SHCreateItemFromParsingName(startPath, IntPtr.Zero, ref iid, out start);
                    if (hrItem == 0 && start != null)
                    {
                        try { dialog.SetFolder(start); }
                        finally { Marshal.ReleaseComObject(start); }
                    }
                }

                IntPtr hwnd = owner == null ? IntPtr.Zero : owner.Handle;
                int hr = dialog.Show(hwnd);
                if (hr == Cancelled) return null;
                if (hr < 0) Marshal.ThrowExceptionForHR(hr);

                IShellItem result;
                dialog.GetResult(out result);
                try
                {
                    string path;
                    result.GetDisplayName(SigdnFileSystemPath, out path);
                    return path;
                }
                finally
                {
                    Marshal.ReleaseComObject(result);
                }
            }
            finally
            {
                if (dialog != null) Marshal.ReleaseComObject(dialog);
            }
        }

        static string PickLegacy(IWin32Window owner, string title, string startPath)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = string.IsNullOrEmpty(title) ? "Select a folder" : title;
                dialog.ShowNewFolderButton = true;
                if (!string.IsNullOrEmpty(startPath) && Paths.Exists(startPath))
                    dialog.SelectedPath = startPath;
                if (dialog.ShowDialog(owner) != DialogResult.OK) return null;
                return dialog.SelectedPath;
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        static extern int SHCreateItemFromParsingName(
            string path,
            IntPtr bind,
            ref Guid iid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

        [ComImport]
        [Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        class FileOpenDialog
        {
        }

        [ComImport]
        [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItem
        {
            void BindToHandler(IntPtr bind, ref Guid bhid, ref Guid iid, out IntPtr ppv);
            void GetParent(out IShellItem parent);
            void GetDisplayName(uint sigdn, [MarshalAs(UnmanagedType.LPWStr)] out string name);
            void GetAttributes(uint mask, out uint attributes);
            void Compare(IShellItem other, uint hint, out int order);
        }

        [ComImport]
        [Guid("d57c7288-d4ad-4768-be02-9d969532d960")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IFileOpenDialog
        {
            [PreserveSig]
            int Show(IntPtr parent);
            void SetFileTypes(uint count, IntPtr filters);
            void SetFileTypeIndex(uint index);
            void GetFileTypeIndex(out uint index);
            void Advise(IntPtr events, out uint cookie);
            void Unadvise(uint cookie);
            void SetOptions(uint options);
            void GetOptions(out uint options);
            void SetDefaultFolder(IShellItem item);
            void SetFolder(IShellItem item);
            void GetFolder(out IShellItem item);
            void GetCurrentSelection(out IShellItem item);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void GetResult(out IShellItem item);
            void AddPlace(IShellItem item, uint fdap);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr filter);
            void GetResults(out IntPtr items);
            void GetSelectedItems(out IntPtr items);
        }
    }
}
