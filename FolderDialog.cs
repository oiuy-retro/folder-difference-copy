using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FolderDiffCopy
{
    internal static class FolderDialog
    {
        const uint FosNoChangeDir = 0x8;
        const uint FosPickFolders = 0x20;
        const uint FosAllNonStorageItems = 0x80;
        const uint FosPathMustExist = 0x800;
        const uint SigdnFileSystemPath = 0x80058000;
        const uint SigdnDesktopAbsoluteParsing = 0x80028000;
        const uint SigdnDesktopAbsoluteEditing = 0x8004C000;
        const uint SfgaoFolder = 0x20000000;
        const string NotAFolder = "That place is not a folder on a drive or a phone. Pick a regular folder, or a folder on a connected phone.";
        const string PhoneItself = "Pick a folder inside the phone's storage, such as Internal shared storage, not the phone itself.";
        const int Cancelled = unchecked((int)0x800704C7);

        public static void Probe()
        {
            IFileOpenDialog dialog = (IFileOpenDialog)new FileOpenDialog();
            try
            {
                uint options;
                dialog.GetOptions(out options);
                dialog.SetOptions(options | FosPickFolders | FosAllNonStorageItems | FosPathMustExist | FosNoChangeDir);
                dialog.SetTitle("Folder Difference Copy");
            }
            finally
            {
                Marshal.ReleaseComObject(dialog);
            }
        }

        // Returns null when cancelled. The result is a normal folder (Path), a folder on a
        // phone (PhoneItem), or an Error to show when the pick cannot be used.
        public static FolderSpot Pick(IWin32Window owner, string title, string startPath, string startPhoneItem)
        {
            try
            {
                return PickExplorer(owner, title, startPath, startPhoneItem);
            }
            catch (Exception)
            {
                string path = PickLegacy(owner, title, startPath);
                return path == null ? null : FolderSpot.ForPath(path);
            }
        }

        // Turns typed text such as "This PC\Pixel 8\Internal shared storage\Music" into a
        // phone folder. Returns null when the text is not a phone folder.
        public static FolderSpot ResolvePhone(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            Guid iid = typeof(IShellItem).GUID;
            IShellItem item;
            int hr = SHCreateItemFromParsingName(text, IntPtr.Zero, ref iid, out item);
            if (hr < 0 || item == null) return null;
            try
            {
                FolderSpot spot = Classify(item);
                if (spot == null || !spot.IsPhone) return null;
                return spot;
            }
            finally
            {
                Marshal.ReleaseComObject(item);
            }
        }

        static FolderSpot Classify(IShellItem item)
        {
            string path = null;
            try { item.GetDisplayName(SigdnFileSystemPath, out path); }
            catch (Exception) { path = null; }
            if (!string.IsNullOrEmpty(path)) return FolderSpot.ForPath(path);

            string parsing = null;
            try { item.GetDisplayName(SigdnDesktopAbsoluteParsing, out parsing); }
            catch (Exception) { parsing = null; }
            uint attributes = 0;
            try { item.GetAttributes(SfgaoFolder, out attributes); }
            catch (Exception) { attributes = 0; }
            if (!PhoneShell.IsPhoneItem(parsing) || (attributes & SfgaoFolder) == 0)
                return FolderSpot.Problem(NotAFolder);
            if (PhoneShell.IsPhoneDevice(parsing))
                return FolderSpot.Problem(PhoneItself);

            string text = null;
            try { item.GetDisplayName(SigdnDesktopAbsoluteEditing, out text); }
            catch (Exception) { text = null; }
            return FolderSpot.ForPhone(parsing, text);
        }

        static FolderSpot PickExplorer(IWin32Window owner, string title, string startPath, string startPhoneItem)
        {
            IFileOpenDialog dialog = null;
            try
            {
                dialog = (IFileOpenDialog)new FileOpenDialog();
                uint options;
                dialog.GetOptions(out options);
                dialog.SetOptions(options | FosPickFolders | FosAllNonStorageItems | FosPathMustExist | FosNoChangeDir);
                if (!string.IsNullOrEmpty(title))
                    dialog.SetTitle(title);
                string startItem = null;
                if (!string.IsNullOrEmpty(startPhoneItem))
                    startItem = startPhoneItem;
                else if (!string.IsNullOrEmpty(startPath) && Paths.Exists(startPath))
                    startItem = startPath;
                if (startItem != null)
                {
                    Guid iid = typeof(IShellItem).GUID;
                    IShellItem start;
                    int hrItem = SHCreateItemFromParsingName(startItem, IntPtr.Zero, ref iid, out start);
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
                    return Classify(result);
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
