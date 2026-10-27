using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using ComStream = System.Runtime.InteropServices.ComTypes.IStream;

namespace FolderDiffCopy
{
    // One side of a copy. A normal folder has Path. A folder on a phone has PhoneItem,
    // the shell parsing name Explorer uses for it, and Text, the name shown in the box.
    internal sealed class FolderSpot
    {
        public string Path;
        public string PhoneItem;
        public string Text;
        public string Error;
        public uint GitCookie;
        public IShellItem Live;

        public bool IsPhone
        {
            get { return PhoneItem != null; }
        }

        public static FolderSpot ForPath(string path)
        {
            FolderSpot spot = new FolderSpot();
            spot.Path = path;
            spot.Text = path;
            return spot;
        }

        public static FolderSpot ForPhone(string item, string text)
        {
            return ForPhone(item, text, 0);
        }

        public static FolderSpot ForPhone(string item, string text, uint cookie)
        {
            FolderSpot spot = new FolderSpot();
            spot.PhoneItem = item;
            spot.Text = string.IsNullOrEmpty(text) ? item : text;
            spot.GitCookie = cookie;
            return spot;
        }

        public static FolderSpot Problem(string message)
        {
            FolderSpot spot = new FolderSpot();
            spot.Error = message;
            return spot;
        }
    }

    // A phone folder tree for one run. Parsing names are kept as strings so each copy
    // thread can open its own shell items.
    internal sealed class PhoneTree
    {
        public string Root;
        public string RootText;
        public uint RootCookie;
        public IShellItem Live;
        public readonly Dictionary<string, string> Dirs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string> Files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> Leftovers = new List<string>();
        public readonly object Gate = new object();

        public string DirItem(string rel)
        {
            if (string.IsNullOrEmpty(rel)) return Root;
            lock (Gate)
            {
                string item;
                return Dirs.TryGetValue(rel, out item) ? item : null;
            }
        }

        public string FileItem(string rel)
        {
            lock (Gate)
            {
                string item;
                return Files.TryGetValue(rel, out item) ? item : null;
            }
        }

        readonly Dictionary<string, IShellItem> liveItems = new Dictionary<string, IShellItem>(StringComparer.OrdinalIgnoreCase);

        public void SetLive(string rel, IShellItem item)
        {
            if (item == null) return;
            lock (Gate) liveItems[rel ?? ""] = item;
        }

        public IShellItem GetLive(string rel)
        {
            lock (Gate)
            {
                IShellItem item;
                return liveItems.TryGetValue(rel ?? "", out item) ? item : null;
            }
        }

        public bool Owns(IShellItem item)
        {
            if (item == null) return false;
            if (object.ReferenceEquals(item, Live)) return true;
            lock (Gate)
            {
                foreach (IShellItem held in liveItems.Values)
                    if (object.ReferenceEquals(held, item)) return true;
            }
            return false;
        }

        public void SetFile(string rel, string item)
        {
            lock (Gate)
            {
                if (item == null) Files.Remove(rel);
                else Files[rel] = item;
            }
        }

        public void SetDir(string rel, string item)
        {
            lock (Gate) Dirs[rel] = item;
        }
    }

    internal struct PhoneEntry
    {
        public long Size;
        public long WriteTime;
        public string Item;
        public IShellItem Live;
    }

    internal static class PhoneErrors
    {
        public const string CannotReach = "Windows can see the phone, but that folder could not be opened. Unplug the phone, plug it back in, pick the folder again, then press Preview.";
        public const string Unplugged = "The phone was unplugged or stopped responding. Plug it back in, unlock it, and run the copy again. Unfinished files are cleaned up on the next run.";
        public const string Locked = "The phone refused access. Unlock the phone, make sure its USB mode is File transfer, and try again.";
        public const string Full = "The phone is out of space.";

        public static string Describe(Exception ex)
        {
            if (ex == null) return "The phone copy failed.";
            if (ex is PhoneException) return ex.Message;
            int hr = Marshal.GetHRForException(ex);
            switch (unchecked((uint)hr))
            {
                case 0x8007048F: // ERROR_DEVICE_NOT_CONNECTED
                case 0x80070015: // ERROR_NOT_READY
                case 0x8007001F: // ERROR_GEN_FAILURE
                case 0x80070079: // ERROR_SEM_TIMEOUT
                case 0x800701B1: // ERROR_NO_SUCH_DEVICE
                case 0x80070037: // ERROR_DEV_NOT_EXIST
                case 0x802A0002: // E_WPD_DEVICE_NOT_OPEN
                case 0x802A0006: // E_WPD_DEVICE_IS_HUNG
                case 0x800706BA: // RPC server unavailable
                case 0x800706BE: // RPC call failed
                case 0x80010108: // RPC_E_DISCONNECTED
                    return Unplugged;
                case 0x80070005: // E_ACCESSDENIED
                case 0x80070020: // ERROR_SHARING_VIOLATION
                    return Locked;
                case 0x80070070: // ERROR_DISK_FULL
                case 0x80070027: // ERROR_HANDLE_DISK_FULL
                    return Full;
            }
            if (ex is IOException || ex is UnauthorizedAccessException) return ex.Message;
            return "Phone error: " + ex.Message;
        }
    }

    internal sealed class PhoneException : Exception
    {
        public PhoneException(string message) : base(message) { }
    }

    // Shell access to phones. Windows shows phones (MTP / Windows Portable Devices) in
    // Explorer through the shell namespace, so the app reads and writes them through the
    // same IShellItem and IFileOperation interfaces Explorer uses. No extra DLLs.
    internal static class PhoneShell
    {
        public const string PartSuffix = ".fdc-part";
        const string WpdInterface = "{6ac27878-a6fa-4155-ba85-f98f491d4f33}";
        const string PortableDevicesFolder = "::{35786D3C-B075-49B9-88DD-029876E11C01}";

        const uint SigdnDesktopAbsoluteParsing = 0x80028000;
        const uint SigdnDesktopAbsoluteEditing = 0x8004C000;
        const uint SigdnNormalDisplay = 0;
        const uint SfgaoFolder = 0x20000000;
        const uint SfgaoStream = 0x00400000;
        const uint SfgaoFileSystem = 0x40000000;
        const int ShcontfFolders = 0x20;
        const int ShcontfNonFolders = 0x40;
        const int ShcontfIncludeHidden = 0x80;
        const int ShcontfStorage = 0x800;
        const string ThisPcFolder = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
        const uint FofNoUi = 0x4 | 0x10 | 0x200 | 0x400 | 0x2000;
        const uint FofxEarlyFailure = 0x00100000;
        const uint FileAttributeDirectory = 0x10;
        const int SFalse = 1;

        static Guid BhidEnumItems = new Guid("94f60519-2850-4924-aa5a-d15e84868039");
        static Guid BhidSfObject = new Guid("3981e224-f559-11d3-8e3a-00c04f6837d5");
        static Guid BhidStream = new Guid("1cebb3ab-7c10-499a-a417-92ca16c4cb83");
        static Guid IidStream = new Guid("0000000c-0000-0000-C000-000000000046");
        static Guid IidEnumShellItems = new Guid("70629033-e363-4a28-a567-0db78006e6d7");
        static Guid IidShellFolder = new Guid("000214E6-0000-0000-C000-000000000046");
        static PropertyKey KeySize = new PropertyKey(new Guid("B725F130-47EF-101A-A5F1-02608C9EEBAC"), 12);
        static PropertyKey KeyModified = new PropertyKey(new Guid("B725F130-47EF-101A-A5F1-02608C9EEBAC"), 14);
        static PropertyKey KeyFileName = new PropertyKey(new Guid("41CF5AE0-F75A-4806-BD87-59C7D9248EB9"), 100);

        public static bool IsPhoneItem(string parsingName)
        {
            if (string.IsNullOrEmpty(parsingName)) return false;
            if (parsingName.IndexOf(WpdInterface, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return StartsWithGuid(parsingName, PortableDevicesFolder);
        }

        // The phone itself (its children are storages such as "Internal shared storage")
        // cannot hold files, so only folders inside a storage are allowed.
        public static bool IsPhoneDevice(string parsingName)
        {
            if (string.IsNullOrEmpty(parsingName)) return false;
            int at = parsingName.IndexOf(WpdInterface, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
                return EqualsGuid(parsingName, PortableDevicesFolder);
            string rest = parsingName.Substring(at + WpdInterface.Length).Trim('\\');
            return rest.Length == 0;
        }

        // A filesystem display name from the shell is only a disk folder when that
        // directory actually exists. Phones often report a path that cannot be opened.
        public static bool UseAsDiskFolder(string fileSystemPath)
        {
            return !string.IsNullOrEmpty(fileSystemPath) && Paths.Exists(fileSystemPath);
        }

        public static bool IsThisPcOrPortableDevices(string parsingName)
        {
            return EqualsGuid(parsingName, ThisPcFolder) || EqualsGuid(parsingName, PortableDevicesFolder);
        }

        static string StripShellPrefix(string parsingName)
        {
            if (string.IsNullOrEmpty(parsingName)) return "";
            string t = parsingName.Trim();
            if (t.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                t = t.Substring(6);
            return t.TrimEnd('\\');
        }

        static bool EqualsGuid(string parsingName, string guid)
        {
            return StripShellPrefix(parsingName).Equals(guid, StringComparison.OrdinalIgnoreCase);
        }

        static bool StartsWithGuid(string parsingName, string guid)
        {
            string t = StripShellPrefix(parsingName);
            if (t.Equals(guid, StringComparison.OrdinalIgnoreCase)) return true;
            return t.StartsWith(guid + "\\", StringComparison.OrdinalIgnoreCase);
        }

        // Phone folders picked in the window must be used on that window's thread.
        public static SynchronizationContext Ui;

        public static void RunSta(ThreadStart work)
        {
            if (Ui != null)
            {
                if (object.ReferenceEquals(Ui, SynchronizationContext.Current))
                {
                    work();
                    return;
                }
                Exception failed = null;
                Ui.Send(delegate
                {
                    try { work(); }
                    catch (Exception ex) { failed = ex; }
                }, null);
                if (failed != null) throw failed;
                return;
            }
            PhoneSta.Run(work);
        }

        public static void StartSta(ThreadStart work)
        {
            if (Ui != null)
            {
                Ui.Post(delegate { work(); }, null);
                return;
            }
            PhoneSta.Post(work);
        }

        public static uint Register(object item)
        {
            if (item == null) return 0;
            try
            {
                Guid iid = typeof(IShellItem).GUID;
                uint cookie;
                GitTable().RegisterInterfaceInGlobal(item, ref iid, out cookie);
                return cookie;
            }
            catch
            {
                return 0;
            }
        }

        public static void Revoke(FolderSpot spot)
        {
            if (spot == null) return;
            if (spot.GitCookie != 0)
            {
                try { GitTable().RevokeInterfaceFromGlobal(spot.GitCookie); }
                catch { }
                spot.GitCookie = 0;
            }
            if (spot.Live != null)
            {
                Release(spot.Live);
                spot.Live = null;
            }
        }

        public static IShellItem FromCookie(uint cookie)
        {
            if (cookie == 0) return null;
            try
            {
                Guid iid = typeof(IShellItem).GUID;
                object obj;
                GitTable().GetInterfaceFromGlobal(cookie, ref iid, out obj);
                IShellItem item = AsShellItem(obj);
                if (item != null) return item;
                Guid unk = new Guid("00000000-0000-0000-C000-000000000046");
                GitTable().GetInterfaceFromGlobal(cookie, ref unk, out obj);
                return AsShellItem(obj);
            }
            catch
            {
                return null;
            }
        }

        public static IShellItem AsShellItem(object obj)
        {
            if (obj == null) return null;
            IShellItem direct = obj as IShellItem;
            if (direct != null) return direct;
            IntPtr unk = IntPtr.Zero;
            try
            {
                unk = Marshal.GetIUnknownForObject(obj);
                if (unk == IntPtr.Zero) return null;
                return (IShellItem)Marshal.GetTypedObjectForIUnknown(unk, typeof(IShellItem));
            }
            catch
            {
                return null;
            }
            finally
            {
                if (unk != IntPtr.Zero) Marshal.Release(unk);
            }
        }

        static IGlobalInterfaceTable gitTable;

        static IGlobalInterfaceTable GitTable()
        {
            if (gitTable == null)
                gitTable = (IGlobalInterfaceTable)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00000323-0000-0000-C000-000000000046")));
            return gitTable;
        }

        public static IShellItem Open(string parsingName)
        {
            if (string.IsNullOrEmpty(parsingName)) return null;
            Guid iid = typeof(IShellItem).GUID;
            IShellItem item;
            int hr = SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref iid, out item);
            if (hr >= 0 && item != null) return item;
            return OpenParsed(parsingName);
        }

        static IShellItem OpenParsed(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            IntPtr pidl = IntPtr.Zero;
            int hr = SHParseDisplayName(text, IntPtr.Zero, out pidl, 0, IntPtr.Zero);
            if (hr < 0 || pidl == IntPtr.Zero) return null;
            try
            {
                Guid iid = typeof(IShellItem).GUID;
                IShellItem item;
                hr = SHCreateItemFromIDList(pidl, ref iid, out item);
                return hr >= 0 ? item : null;
            }
            finally
            {
                Marshal.FreeCoTaskMem(pidl);
            }
        }

        public static IShellItem OpenOrThrow(string parsingName)
        {
            return OpenOrThrow(parsingName, 0, null);
        }

        public static IShellItem OpenOrThrow(string parsingName, uint cookie)
        {
            return OpenOrThrow(parsingName, cookie, null);
        }

        public static IShellItem OpenOrThrow(string parsingName, uint cookie, string displayText)
        {
            return OpenOrThrow(parsingName, cookie, displayText, null);
        }

        public static IShellItem OpenOrThrow(string parsingName, uint cookie, string displayText, IShellItem live)
        {
            if (live != null) return live;
            IShellItem item = FromCookie(cookie);
            if (item == null) item = Open(parsingName);
            if (item == null) item = Open(displayText);
            if (item == null) throw new PhoneException(PhoneErrors.CannotReach);
            return item;
        }

        public static void Release(object item)
        {
            if (item == null) return;
            try { Marshal.ReleaseComObject(item); }
            catch { }
        }

        public static string ParsingName(IShellItem item)
        {
            string name;
            item.GetDisplayName(SigdnDesktopAbsoluteParsing, out name);
            return name;
        }

        public static string EditingName(IShellItem item)
        {
            try
            {
                string name;
                item.GetDisplayName(SigdnDesktopAbsoluteEditing, out name);
                return name;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static bool IsFolder(IShellItem item)
        {
            uint attributes;
            item.GetAttributes(SfgaoFolder, out attributes);
            return (attributes & SfgaoFolder) != 0;
        }

        // The real file name, extension included, even when Explorer hides extensions.
        public static string FileName(IShellItem item)
        {
            IShellItem2 item2 = item as IShellItem2;
            if (item2 != null)
            {
                IntPtr text;
                if (item2.GetString(ref KeyFileName, out text) == 0 && text != IntPtr.Zero)
                {
                    try
                    {
                        string name = Marshal.PtrToStringUni(text);
                        if (!string.IsNullOrEmpty(name)) return name;
                    }
                    finally
                    {
                        Marshal.FreeCoTaskMem(text);
                    }
                }
            }
            string display;
            item.GetDisplayName(SigdnNormalDisplay, out display);
            return display;
        }

        public static void ReadMeta(IShellItem item, out long size, out long written)
        {
            size = -1;
            written = 0;
            IShellItem2 item2 = item as IShellItem2;
            if (item2 == null) return;
            ulong bytes;
            if (item2.GetUInt64(ref KeySize, out bytes) == 0) size = (long)bytes;
            long time;
            if (item2.GetFileTime(ref KeyModified, out time) == 0) written = time;
        }

        public static List<IShellItem> Children(IShellItem folder)
        {
            List<IShellItem> viaFolder = null;
            Exception folderError = null;
            try { viaFolder = ChildrenByShellFolder(folder); }
            catch (Exception ex) { folderError = ex; }
            if (viaFolder != null && viaFolder.Count > 0) return viaFolder;

            List<IShellItem> viaItems = null;
            Exception itemsError = null;
            try { viaItems = ChildrenByEnumItems(folder); }
            catch (Exception ex) { itemsError = ex; }
            if (viaItems != null && viaItems.Count > 0)
            {
                ReleaseList(viaFolder);
                return viaItems;
            }

            List<IShellItem> viaApp = null;
            Exception appError = null;
            try { viaApp = ChildrenByShellApp(folder); }
            catch (Exception ex) { appError = ex; }
            if (viaApp != null && viaApp.Count > 0)
            {
                ReleaseList(viaFolder);
                ReleaseList(viaItems);
                return viaApp;
            }
            if (viaFolder != null) return viaFolder;
            if (viaItems != null) return viaItems;
            if (viaApp != null) return viaApp;
            if (folderError != null) throw folderError;
            if (itemsError != null) throw itemsError;
            if (appError != null) throw appError;
            return new List<IShellItem>();
        }

        static List<IShellItem> ChildrenByShellApp(IShellItem folder)
        {
            var list = new List<IShellItem>();
            object ns = ShellNameSpace(ParsingName(folder));
            if (ns == null) ns = ShellNameSpace(EditingName(folder));
            if (ns == null) return list;
            object items = ShellGet(ns, "Items");
            if (items == null) return list;
            int count = Convert.ToInt32(ShellGet(items, "Count"));
            for (int i = 0; i < count; i++)
            {
                object fi = ShellInvoke(items, "Item", i);
                if (fi == null) continue;
                string name = ShellGet(fi, "Name") as string;
                IShellItem child = null;
                if (!string.IsNullOrEmpty(name)) child = RelativeChild(folder, name);
                if (child != null) list.Add(child);
            }
            return list;
        }

        public static IShellItem RelativeChild(IShellItem parent, string name)
        {
            if (parent == null || string.IsNullOrEmpty(name)) return null;
            IShellItem item = FromRelativeName(parent, name);
            if (item != null) return item;
            return FromFolderParse(parent, name);
        }

        static IShellItem FromRelativeName(IShellItem parent, string name)
        {
            try
            {
                Guid iid = typeof(IShellItem).GUID;
                IShellItem item;
                int hr = SHCreateItemFromRelativeName(parent, name, IntPtr.Zero, ref iid, out item);
                if (hr >= 0 && item != null) return item;
            }
            catch
            {
            }
            return null;
        }

        static IShellItem FromFolderParse(IShellItem parent, string name)
        {
            object raw = null;
            IntPtr parentPidl = IntPtr.Zero;
            IntPtr childPidl = IntPtr.Zero;
            IntPtr abs = IntPtr.Zero;
            try
            {
                parent.BindToHandler(IntPtr.Zero, ref BhidSfObject, ref IidShellFolder, out raw);
                IShellFolder folder = (IShellFolder)raw;
                uint eaten = 0;
                uint attribs = 0;
                folder.ParseDisplayName(GetDesktopWindow(), IntPtr.Zero, name, ref eaten, out childPidl, ref attribs);
                if (childPidl == IntPtr.Zero) return null;
                int hrPidl = SHGetIDListFromObject(parent, out parentPidl);
                if (hrPidl < 0 || parentPidl == IntPtr.Zero) return null;
                abs = ILCombine(parentPidl, childPidl);
                if (abs == IntPtr.Zero) return null;
                Guid iid = typeof(IShellItem).GUID;
                IShellItem item;
                int hr = SHCreateItemFromIDList(abs, ref iid, out item);
                return hr >= 0 ? item : null;
            }
            catch
            {
                return null;
            }
            finally
            {
                if (childPidl != IntPtr.Zero) Marshal.FreeCoTaskMem(childPidl);
                if (parentPidl != IntPtr.Zero) Marshal.FreeCoTaskMem(parentPidl);
                if (abs != IntPtr.Zero) Marshal.FreeCoTaskMem(abs);
                Release(raw);
            }
        }

        static object ShellNameSpace(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            Type type = Type.GetTypeFromProgID("Shell.Application");
            if (type == null) return null;
            object shell = Activator.CreateInstance(type);
            try
            {
                return type.InvokeMember("NameSpace", BindingFlags.InvokeMethod, null, shell, new object[] { path });
            }
            catch
            {
                return null;
            }
        }

        static object ShellGet(object target, string name)
        {
            return target.GetType().InvokeMember(name, BindingFlags.GetProperty, null, target, null);
        }

        static object ShellInvoke(object target, string name, params object[] args)
        {
            return target.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, target, args);
        }

        static void ReleaseList(List<IShellItem> list)
        {
            if (list == null) return;
            foreach (IShellItem item in list) Release(item);
        }

        static List<IShellItem> ChildrenByEnumItems(IShellItem folder)
        {
            var list = new List<IShellItem>();
            object found;
            folder.BindToHandler(IntPtr.Zero, ref BhidEnumItems, ref IidEnumShellItems, out found);
            IEnumShellItems items = (IEnumShellItems)found;
            try
            {
                for (;;)
                {
                    IShellItem child;
                    uint fetched;
                    int hr = items.Next(1, out child, out fetched);
                    if (hr < 0) Marshal.ThrowExceptionForHR(hr);
                    if (hr == SFalse || fetched == 0 || child == null) break;
                    list.Add(child);
                }
            }
            finally
            {
                Release(items);
            }
            return list;
        }

        // Phones need SHCONTF_STORAGE. BHID_EnumItems often returns no children on MTP.
        static List<IShellItem> ChildrenByShellFolder(IShellItem folder)
        {
            var list = new List<IShellItem>();
            object raw;
            folder.BindToHandler(IntPtr.Zero, ref BhidSfObject, ref IidShellFolder, out raw);
            IShellFolder shellFolder = (IShellFolder)raw;
            IEnumIDList enumerator = null;
            IntPtr parentPidl = IntPtr.Zero;
            try
            {
                int flags = ShcontfFolders | ShcontfNonFolders | ShcontfIncludeHidden | ShcontfStorage;
                int hrEnum = shellFolder.EnumObjects(GetDesktopWindow(), flags, out enumerator);
                if (hrEnum < 0) Marshal.ThrowExceptionForHR(hrEnum);
                if (enumerator == null) return list;
                int hrPidl = SHGetIDListFromObject(folder, out parentPidl);
                if (hrPidl < 0 || parentPidl == IntPtr.Zero) Marshal.ThrowExceptionForHR(hrPidl);
                Guid iid = typeof(IShellItem).GUID;
                for (;;)
                {
                    IntPtr childPidl;
                    uint fetched;
                    int hr = enumerator.Next(1, out childPidl, out fetched);
                    if (hr < 0) Marshal.ThrowExceptionForHR(hr);
                    if (hr == SFalse || fetched == 0 || childPidl == IntPtr.Zero) break;
                    IntPtr abs = ILCombine(parentPidl, childPidl);
                    Marshal.FreeCoTaskMem(childPidl);
                    if (abs == IntPtr.Zero) continue;
                    try
                    {
                        IShellItem child;
                        int hrItem = SHCreateItemFromIDList(abs, ref iid, out child);
                        if (hrItem == 0 && child != null) list.Add(child);
                    }
                    finally
                    {
                        Marshal.FreeCoTaskMem(abs);
                    }
                }
            }
            finally
            {
                if (parentPidl != IntPtr.Zero) Marshal.FreeCoTaskMem(parentPidl);
                Release(enumerator);
                Release(shellFolder);
            }
            return list;
        }

        public static IShellItem FindChild(IShellItem folder, string name)
        {
            IShellItem relative = RelativeChild(folder, name);
            if (relative != null) return relative;
            List<IShellItem> children = Children(folder);
            IShellItem match = null;
            foreach (IShellItem child in children)
            {
                if (match == null && string.Equals(FileName(child), name, StringComparison.OrdinalIgnoreCase))
                    match = child;
                else
                    Release(child);
            }
            return match;
        }

        public static ComStream OpenStream(IShellItem item)
        {
            object found;
            item.BindToHandler(IntPtr.Zero, ref BhidStream, ref IidStream, out found);
            return (ComStream)found;
        }

        // Scans a phone folder tree. Unfinished '.fdc-part' files are listed as leftovers
        // and never treated as real files.
        public static void Scan(PhoneTree tree, Dictionary<string, PhoneEntry> files, HashSet<string> dirs, bool countAsA, ScanStatus status)
        {
            IShellItem root = tree.Live;
            bool own = false;
            if (root == null)
            {
                try
                {
                    root = OpenOrThrow(tree.Root, tree.RootCookie, tree.RootText, null);
                    own = true;
                }
                catch (Exception)
                {
                    root = null;
                }
            }
            if (root != null)
            {
                try
                {
                    ScanFolder(root, "", tree, files, dirs, countAsA, status);
                }
                finally
                {
                    if (own) Release(root);
                }
                if (files.Count > 0 || dirs.Count > 0 || status.Cancel) return;
            }
            ScanByDisplayName(tree, files, dirs, countAsA, status);
        }

        static void ScanByDisplayName(PhoneTree tree, Dictionary<string, PhoneEntry> files, HashSet<string> dirs, bool countAsA, ScanStatus status)
        {
            object ns = ShellNameSpace(tree.RootText);
            if (ns == null) ns = ShellNameSpace(tree.Root);
            if (ns == null) throw new PhoneException(PhoneErrors.CannotReach);
            IShellItem parent = tree.Live;
            if (parent == null) parent = Open(tree.Root);
            if (parent == null) parent = Open(tree.RootText);
            ScanShellFolder(ns, "", parent, tree, files, dirs, countAsA, status);
        }

        static void ScanShellFolder(object ns, string relDir, IShellItem parent, PhoneTree tree, Dictionary<string, PhoneEntry> files, HashSet<string> dirs, bool countAsA, ScanStatus status)
        {
            object items = ShellGet(ns, "Items");
            if (items == null) return;
            int count = Convert.ToInt32(ShellGet(items, "Count"));
            for (int i = 0; i < count; i++)
            {
                if (status.Cancel) return;
                object fi = ShellInvoke(items, "Item", i);
                if (fi == null) continue;
                string name = ShellGet(fi, "Name") as string;
                if (string.IsNullOrEmpty(name)) continue;
                string rel = relDir.Length == 0 ? name : relDir + "\\" + name;
                string path = ShellGet(fi, "Path") as string;
                bool isFolder = false;
                try { isFolder = Convert.ToBoolean(ShellGet(fi, "IsFolder")); }
                catch { isFolder = false; }
                IShellItem live = null;
                if (parent != null) live = RelativeChild(parent, name);
                if (isFolder)
                {
                    if (dirs.Contains(rel))
                    {
                        if (live != null && !tree.Owns(live)) Release(live);
                        continue;
                    }
                    dirs.Add(rel);
                    if (!string.IsNullOrEmpty(path)) tree.SetDir(rel, path);
                    if (live != null) tree.SetLive(rel, live);
                    object child = null;
                    if (!string.IsNullOrEmpty(path)) child = ShellNameSpace(path);
                    if (child == null && live != null) child = ShellNameSpace(ParsingName(live));
                    if (child != null) ScanShellFolder(child, rel, live, tree, files, dirs, countAsA, status);
                    continue;
                }
                if (name.EndsWith(PartSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    tree.Leftovers.Add(rel);
                    if (live != null) tree.SetLive(rel, live);
                    continue;
                }
                if (files.ContainsKey(rel))
                {
                    if (live != null && !tree.Owns(live)) Release(live);
                    continue;
                }
                PhoneEntry entry = new PhoneEntry();
                try { entry.Size = Convert.ToInt64(ShellGet(fi, "Size")); }
                catch { entry.Size = 0; }
                if (entry.Size < 0) entry.Size = 0;
                entry.Item = path;
                entry.Live = live;
                files[rel] = entry;
                if (live != null) tree.SetLive(rel, live);
                if (!string.IsNullOrEmpty(path)) tree.SetFile(rel, path);
                if (countAsA) status.FilesA++;
                else status.FilesB++;
            }
        }

        static void ScanFolder(IShellItem folder, string relDir, PhoneTree tree, Dictionary<string, PhoneEntry> files, HashSet<string> dirs, bool countAsA, ScanStatus status)
        {
            List<IShellItem> children;
            try
            {
                children = Children(folder);
            }
            catch (Exception ex)
            {
                if (relDir.Length == 0) throw new PhoneException(PhoneErrors.Describe(ex));
                status.Unreadable++;
                if (status.FirstProblem == null) status.FirstProblem = PhoneErrors.Describe(ex);
                return;
            }
            var folders = new List<KeyValuePair<string, IShellItem>>();
            foreach (IShellItem child in children)
            {
                if (status.Cancel) return;
                string name = FileName(child);
                if (string.IsNullOrEmpty(name))
                {
                    Release(child);
                    continue;
                }
                string rel = relDir.Length == 0 ? name : relDir + "\\" + name;
                string item = ParsingName(child);
                if (IsFolder(child))
                {
                    if (dirs.Contains(rel))
                    {
                        Release(child);
                        continue;
                    }
                    dirs.Add(rel);
                    tree.SetDir(rel, item);
                    tree.SetLive(rel, child);
                    folders.Add(new KeyValuePair<string, IShellItem>(rel, child));
                    continue;
                }
                if (name.EndsWith(PartSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    tree.Leftovers.Add(rel);
                    tree.SetLive(rel, child);
                    continue;
                }
                if (files.ContainsKey(rel))
                {
                    Release(child);
                    continue;
                }
                PhoneEntry entry = new PhoneEntry();
                ReadMeta(child, out entry.Size, out entry.WriteTime);
                if (entry.Size < 0) entry.Size = 0;
                entry.Item = item;
                entry.Live = child;
                files[rel] = entry;
                tree.SetFile(rel, item);
                tree.SetLive(rel, child);
                if (countAsA) status.FilesA++;
                else status.FilesB++;
            }
            foreach (KeyValuePair<string, IShellItem> pair in folders)
            {
                if (status.Cancel) return;
                ScanFolder(pair.Value, pair.Key, tree, files, dirs, countAsA, status);
            }
        }

        public static IShellItem FolderChild(IShellItem parent, string name, string rel)
        {
            IShellItem existing = FindChild(parent, name);
            if (existing == null) return null;
            if (!IsFolder(existing))
            {
                Release(existing);
                throw new PhoneException("The phone has a file where a folder is needed: " + rel);
            }
            return existing;
        }

        // Opens a folder in the phone tree, creating it and any missing parents.
        public static IShellItem OpenDir(PhoneTree tree, string rel)
        {
            IShellItem live = tree.GetLive(rel);
            if (live != null) return live;
            if (string.IsNullOrEmpty(rel))
            {
                if (tree.Live != null) return tree.Live;
                return OpenOrThrow(tree.Root, tree.RootCookie, tree.RootText, tree.Live);
            }
            string known = tree.DirItem(rel);
            if (known != null)
            {
                IShellItem opened = Open(known);
                if (opened != null)
                {
                    tree.SetLive(rel, opened);
                    return opened;
                }
            }
            int cut = rel.LastIndexOf('\\');
            string parentRel = cut < 0 ? "" : rel.Substring(0, cut);
            string name = cut < 0 ? rel : rel.Substring(cut + 1);
            IShellItem parent = OpenDir(tree, parentRel);
            try
            {
                IShellItem existing = FolderChild(parent, name, rel);
                if (existing != null)
                {
                    tree.SetDir(rel, ParsingName(existing));
                    tree.SetLive(rel, existing);
                    return existing;
                }
                string created = NewFolder(parent, name);
                IShellItem made = created == null ? null : Open(created);
                if (made == null) made = FolderChild(parent, name, rel);
                if (made == null) throw new PhoneException("The phone did not create the folder " + rel + ".");
                tree.SetDir(rel, ParsingName(made));
                tree.SetLive(rel, made);
                return made;
            }
            finally
            {
                if (!tree.Owns(parent)) Release(parent);
            }
        }

        public static IShellItem OpenFile(FileJob job)
        {
            if (job.PhoneLive != null) return job.PhoneLive;
            if (job.PhoneFrom != null)
            {
                IShellItem live = job.PhoneFrom.GetLive(job.RelativePath);
                if (live != null) return live;
                live = Walk(job.PhoneFrom, job.RelativePath);
                if (live != null)
                {
                    job.PhoneLive = live;
                    return live;
                }
            }
            IShellItem opened = Open(job.PhoneSource);
            if (opened != null) return opened;
            throw new PhoneException(PhoneErrors.CannotReach);
        }

        static IShellItem Walk(PhoneTree tree, string rel)
        {
            IShellItem current = tree.Live;
            if (current == null) current = tree.GetLive("");
            if (current == null || string.IsNullOrEmpty(rel)) return current;
            string[] parts = rel.Split('\\');
            string soFar = "";
            for (int i = 0; i < parts.Length; i++)
            {
                soFar = i == 0 ? parts[0] : soFar + "\\" + parts[i];
                IShellItem next = tree.GetLive(soFar);
                if (next == null) next = RelativeChild(current, parts[i]);
                if (next == null) next = FindChild(current, parts[i]);
                if (next == null) return null;
                tree.SetLive(soFar, next);
                current = next;
            }
            return current;
        }

        // Copies a file into a folder under newName. Returns the new item's parsing name.
        public static string CopyToFolder(IShellItem source, IShellItem folder, string newName, Action<uint, uint> progress, Func<bool> cancelled)
        {
            OperationSink sink = new OperationSink(progress, cancelled);
            IFileOperation op = NewOperation();
            uint cookie = 0;
            try
            {
                op.Advise(sink, out cookie);
                op.CopyItem(source, folder, newName, null);
                Perform(op, sink);
                if (!string.IsNullOrEmpty(sink.NewItem)) return sink.NewItem;
                IShellItem found = FindChild(folder, newName);
                if (found == null) return null;
                try { return ParsingName(found); }
                finally { Release(found); }
            }
            finally
            {
                Finish(op, cookie);
            }
        }

        // Copies a file into a phone folder under newName. Returns the new item's parsing name.
        public static string CopyInto(IShellItem source, IShellItem folder, string newName, Action<uint, uint> progress, Func<bool> cancelled)
        {
            string made = CopyToFolder(source, folder, newName, progress, cancelled);
            IShellItem found = FindChild(folder, newName);
            if (found != null)
            {
                try
                {
                    string kept = ParsingName(found);
                    if (!string.IsNullOrEmpty(kept)) return kept;
                }
                finally
                {
                    Release(found);
                }
            }
            if (string.IsNullOrEmpty(made))
                throw new PhoneException("The phone did not keep the copied file.");
            return made;
        }

        public static string FindOnDisk(string folder, string name)
        {
            if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(name)) return null;
            string exact = Path.Combine(folder, name);
            if (Native.FileExists(Native.ToExtended(exact))) return exact;
            try
            {
                string[] files = Directory.GetFiles(folder);
                if (files.Length == 1) return files[0];
                for (int i = 0; i < files.Length; i++)
                {
                    if (string.Equals(Path.GetFileName(files[i]), name, StringComparison.OrdinalIgnoreCase))
                        return files[i];
                }
            }
            catch
            {
            }
            return null;
        }

        public static void CopyHereToFolder(IShellItem source, string destFolder, string destName, long expectedSize, Func<bool> cancelled)
        {
            if (source == null) throw new PhoneException(PhoneErrors.CannotReach);
            IShellItem parent = null;
            try
            {
                try { source.GetParent(out parent); }
                catch { parent = null; }
                string parentPath = null;
                if (parent != null)
                {
                    parentPath = ParsingName(parent);
                    if (string.IsNullOrEmpty(parentPath)) parentPath = EditingName(parent);
                }
                object srcNs = ShellNameSpace(parentPath);
                object payload = null;
                string srcName = FileName(source);
                if (srcNs != null && !string.IsNullOrEmpty(srcName))
                    payload = ShellInvoke(srcNs, "ParseName", srcName);
                if (payload == null)
                {
                    string parsing = ParsingName(source);
                    if (!string.IsNullOrEmpty(parsing)) payload = parsing;
                }
                if (payload == null) throw new PhoneException(PhoneErrors.CannotReach);
                object destNs = ShellNameSpace(destFolder);
                if (destNs == null) throw new IOException("Could not open " + destFolder + ".");
                ShellInvoke(destNs, "CopyHere", payload, 4 | 16 | 512 | 1024);
                WaitForIncoming(destFolder, destName, srcName, expectedSize, cancelled);
            }
            finally
            {
                Release(parent);
            }
        }

        static void WaitForIncoming(string folder, string destName, string srcName, long expectedSize, Func<bool> cancelled)
        {
            int stable = 0;
            int missing = 0;
            long last = -1;
            while (true)
            {
                if (cancelled != null && cancelled()) throw new OperationCanceledException();
                string found = FindOnDisk(folder, destName);
                if (found == null && !string.IsNullOrEmpty(srcName)
                    && !string.Equals(srcName, destName, StringComparison.OrdinalIgnoreCase))
                    found = FindOnDisk(folder, srcName);
                if (found != null)
                {
                    missing = 0;
                    long len = 0;
                    try { len = new FileInfo(found).Length; }
                    catch { len = 0; }
                    if (expectedSize > 0 && len >= expectedSize) return;
                    if (len == last)
                    {
                        stable++;
                        if (expectedSize > 0)
                        {
                            if (stable >= 300 && len > 0)
                                throw new PhoneException(PhoneErrors.Unplugged);
                        }
                        else if (stable >= 20)
                            return;
                    }
                    else
                    {
                        stable = 0;
                        last = len;
                    }
                }
                else
                {
                    missing++;
                    if (missing > 6000)
                        throw new PhoneException("The phone copy did not finish.");
                }
                Thread.Sleep(100);
            }
        }

        public static string RenameItem(IShellItem opened, string newName)
        {
            OperationSink sink = new OperationSink(null, null);
            IFileOperation op = NewOperation();
            uint cookie = 0;
            try
            {
                op.Advise(sink, out cookie);
                op.RenameItem(opened, newName, null);
                Perform(op, sink);
                return sink.NewItem;
            }
            finally
            {
                Finish(op, cookie);
            }
        }

        public static string Rename(string item, string newName)
        {
            IShellItem opened = Open(item);
            if (opened == null) throw new PhoneException(PhoneErrors.CannotReach);
            try
            {
                return RenameItem(opened, newName);
            }
            finally
            {
                Release(opened);
            }
        }

        // True when the item exists and its real file name is exactly name.
        public static bool HasName(string item, string name)
        {
            IShellItem opened = Open(item);
            if (opened == null) return false;
            try
            {
                return string.Equals(FileName(opened), name, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                Release(opened);
            }
        }

        public static void DeleteItem(IShellItem opened)
        {
            if (opened == null) return;
            OperationSink sink = new OperationSink(null, null);
            IFileOperation op = NewOperation();
            uint cookie = 0;
            try
            {
                op.Advise(sink, out cookie);
                op.DeleteItem(opened, null);
                Perform(op, sink);
            }
            finally
            {
                Finish(op, cookie);
            }
        }

        public static void Delete(string item)
        {
            IShellItem opened = Open(item);
            if (opened == null) return;
            try
            {
                DeleteItem(opened);
            }
            finally
            {
                Release(opened);
            }
        }

        public static void DeleteQuiet(string item)
        {
            try { Delete(item); }
            catch { }
        }

        public static void DeleteInTree(PhoneTree tree, string rel)
        {
            if (tree == null || string.IsNullOrEmpty(rel)) return;
            IShellItem live = tree.GetLive(rel);
            if (live == null) live = Walk(tree, rel);
            if (live != null)
            {
                DeleteItem(live);
                return;
            }
            Delete(rel);
        }

        public static void DeleteNamedQuiet(IShellItem folder, string name)
        {
            if (folder == null) return;
            try
            {
                IShellItem found = FindChild(folder, name);
                if (found == null) return;
                try { DeleteItem(found); }
                finally { Release(found); }
            }
            catch
            {
            }
        }

        public static string NewFolder(IShellItem parent, string name)
        {
            OperationSink sink = new OperationSink(null, null);
            IFileOperation op = NewOperation();
            uint cookie = 0;
            try
            {
                op.Advise(sink, out cookie);
                op.NewItem(parent, FileAttributeDirectory, name, null, null);
                Perform(op, sink);
                return sink.NewItem;
            }
            finally
            {
                Finish(op, cookie);
            }
        }

        static IFileOperation NewOperation()
        {
            Type type = Type.GetTypeFromCLSID(new Guid("3ad05575-8857-4850-9277-11b85bdb8e09"));
            IFileOperation op = (IFileOperation)Activator.CreateInstance(type);
            op.SetOperationFlags(FofNoUi | FofxEarlyFailure);
            return op;
        }

        static void Perform(IFileOperation op, OperationSink sink)
        {
            op.PerformOperations();
            if (sink.Failure < 0) Marshal.ThrowExceptionForHR(sink.Failure);
            bool aborted;
            op.GetAnyOperationsAborted(out aborted);
            if (aborted)
            {
                if (sink.WasCancelled) throw new OperationCanceledException();
                throw new PhoneException(PhoneErrors.Unplugged);
            }
        }

        static void Finish(IFileOperation op, uint cookie)
        {
            if (op == null) return;
            if (cookie != 0)
            {
                try { op.Unadvise(cookie); }
                catch { }
            }
            Release(op);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        static extern int SHCreateItemFromParsingName(
            string path,
            IntPtr bind,
            ref Guid iid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        static extern int SHCreateItemFromRelativeName(
            [MarshalAs(UnmanagedType.Interface)] IShellItem parent,
            string name,
            IntPtr bind,
            ref Guid iid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        static extern int SHParseDisplayName(
            string name,
            IntPtr bind,
            out IntPtr pidl,
            uint attributesIn,
            IntPtr attributesOut);

        [DllImport("shell32.dll", PreserveSig = true)]
        static extern int SHGetIDListFromObject(
            [MarshalAs(UnmanagedType.IUnknown)] object punk,
            out IntPtr pidl);

        [DllImport("shell32.dll")]
        static extern IntPtr ILCombine(IntPtr pidl1, IntPtr pidl2);

        [DllImport("shell32.dll", PreserveSig = true)]
        static extern int SHCreateItemFromIDList(
            IntPtr pidl,
            ref Guid iid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

        [DllImport("user32.dll")]
        static extern IntPtr GetDesktopWindow();
    }

    sealed class StaJob
    {
        public ThreadStart Work;
        public Exception Error;
        public ManualResetEvent Done;
    }

    // One STA thread for every phone shell call in this process.
    static class PhoneSta
    {
        static readonly object Gate = new object();
        static readonly Queue<StaJob> Queue = new Queue<StaJob>();
        static readonly AutoResetEvent Signal = new AutoResetEvent(false);
        static Thread thread;

        static void Ensure()
        {
            lock (Gate)
            {
                if (thread != null) return;
                thread = new Thread(Loop);
                thread.IsBackground = true;
                thread.SetApartmentState(ApartmentState.STA);
                thread.Name = "PhoneSTA";
                thread.Start();
            }
        }

        static void Loop()
        {
            while (true)
            {
                StaJob job = null;
                lock (Gate)
                {
                    if (Queue.Count > 0) job = Queue.Dequeue();
                }
                if (job == null)
                {
                    Signal.WaitOne();
                    continue;
                }
                try { job.Work(); }
                catch (Exception ex) { job.Error = ex; }
                if (job.Done != null) job.Done.Set();
            }
        }

        static void Enqueue(StaJob job)
        {
            Ensure();
            lock (Gate) Queue.Enqueue(job);
            Signal.Set();
        }

        public static void Run(ThreadStart work)
        {
            ManualResetEvent done = new ManualResetEvent(false);
            StaJob job = new StaJob();
            job.Work = work;
            job.Done = done;
            Enqueue(job);
            done.WaitOne();
            done.Close();
            if (job.Error != null) throw job.Error;
        }

        public static void Post(ThreadStart work)
        {
            StaJob job = new StaJob();
            job.Work = work;
            Enqueue(job);
        }
    }

    internal struct PlannedCopy
    {
        public string Rel;
        public bool ToA;
        public long Size;
    }

    // Compares when one folder is on a phone. Phones report modified times loosely
    // (rounded, shifted by time zones, or set to the copy time), so a file counts as
    // unchanged when the size matches. Times only pick a direction in Copy both ways,
    // and only when they are more than an hour apart.
    internal static class PhoneDiff
    {
        public const long TimeTolerance = 36000000000L;

        public static CompareResult Compare(FolderSpot a, FolderSpot b, ScanStatus status, bool writeFolders, bool bothWays)
        {
            CompareResult result = null;
            PhoneShell.RunSta(delegate { result = CompareOnThread(a, b, status, writeFolders, bothWays); });
            return result;
        }

        static CompareResult CompareOnThread(FolderSpot a, FolderSpot b, ScanStatus status, bool writeFolders, bool bothWays)
        {
            if (status == null) status = new ScanStatus();
            if (a == null || b == null)
                throw new InvalidOperationException("Choose Folder A and Folder B.");
            if (a.IsPhone && b.IsPhone)
                throw new InvalidOperationException("Only one folder can be on a phone. Pick a folder on this PC for the other side.");

            CompareResult result = new CompareResult();
            var filesA = new Dictionary<string, PhoneEntry>(StringComparer.OrdinalIgnoreCase);
            var filesB = new Dictionary<string, PhoneEntry>(StringComparer.OrdinalIgnoreCase);
            var dirsA = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dirsB = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            PhoneTree treeA = a.IsPhone ? NewTree(a) : null;
            PhoneTree treeB = b.IsPhone ? NewTree(b) : null;
            string rootA = a.IsPhone ? null : Paths.Normalize(a.Path);
            string rootB = b.IsPhone ? null : Paths.Normalize(b.Path);

            if (rootA != null && (rootA.Length == 0 || !Paths.Exists(rootA)))
                throw new InvalidOperationException("Folder A does not exist.");
            if (rootB != null && rootB.Length == 0)
                throw new InvalidOperationException("Choose Folder A and Folder B.");
            if (rootB != null && writeFolders && !Paths.Exists(rootB))
                throw new InvalidOperationException("Folder B does not exist.");

            if (treeB != null) PhoneShell.Scan(treeB, filesB, dirsB, false, status);
            else if (Paths.Exists(rootB)) ScanPc(rootB, filesB, dirsB, false, status);
            if (status.Cancel)
            {
                result.Cancelled = true;
                return result;
            }
            if (treeA != null) PhoneShell.Scan(treeA, filesA, dirsA, true, status);
            else ScanPc(rootA, filesA, dirsA, true, status);
            if (status.Cancel)
            {
                result.Cancelled = true;
                return result;
            }

            int unchanged;
            List<PlannedCopy> plan = Plan(filesA, filesB, bothWays, out unchanged);
            result.Unchanged = unchanged;
            foreach (PlannedCopy copy in plan)
            {
                if (copy.ToA)
                    result.Jobs.Add(MakeJob(copy, b, rootB, treeB, filesB, a, rootA, treeA, dirsA, filesA, bothWays));
                else
                    result.Jobs.Add(MakeJob(copy, a, rootA, treeA, filesA, b, rootB, treeB, dirsB, filesB, bothWays));
            }
            result.Jobs.Sort(delegate(FileJob x, FileJob y)
            {
                return string.Compare(x.RelativePath, y.RelativePath, StringComparison.OrdinalIgnoreCase);
            });

            if (writeFolders)
            {
                if (treeB != null) RemoveLeftovers(treeB, status, result);
                if (bothWays && treeA != null) RemoveLeftovers(treeA, status, result);
                if (result.Cancelled) return result;
                CreateMissingDirs(dirsA, dirsB, filesB, rootB, treeB, status, result, "Folder B has a file where Folder A has a folder: ");
                if (result.Cancelled) return result;
                if (bothWays)
                {
                    CreateMissingDirs(dirsB, dirsA, filesA, rootA, treeA, status, result, "Folder A has a file where Folder B has a folder: ");
                    if (result.Cancelled) return result;
                }
            }

            result.LinksSkipped = status.Links;
            result.Unreadable = status.Unreadable;
            if (result.FirstProblem == null) result.FirstProblem = status.FirstProblem;
            return result;
        }

        static PhoneTree NewTree(FolderSpot spot)
        {
            PhoneTree tree = new PhoneTree();
            tree.Root = spot.PhoneItem;
            tree.RootText = spot.Text;
            tree.RootCookie = spot.GitCookie;
            tree.Live = spot.Live;
            return tree;
        }

        static void ScanPc(string root, Dictionary<string, PhoneEntry> files, HashSet<string> dirs, bool countAsA, ScanStatus status)
        {
            Diff.ScanTree(root, dirs, countAsA, status, delegate(string rel, FileMeta meta)
            {
                if (files.ContainsKey(rel)) return;
                PhoneEntry entry = new PhoneEntry();
                entry.Size = meta.Size;
                entry.WriteTime = meta.WriteTime;
                files[rel] = entry;
            });
        }

        // Decides what to copy. Same size means unchanged. In Copy both ways a file whose
        // size differs goes from the side that is clearly newer; A wins when it is unclear.
        internal static List<PlannedCopy> Plan(Dictionary<string, PhoneEntry> filesA, Dictionary<string, PhoneEntry> filesB, bool bothWays, out int unchanged)
        {
            var plan = new List<PlannedCopy>();
            unchanged = 0;
            foreach (KeyValuePair<string, PhoneEntry> pair in filesA)
            {
                PhoneEntry other;
                bool exists = filesB.TryGetValue(pair.Key, out other);
                if (exists && other.Size == pair.Value.Size)
                {
                    unchanged++;
                    continue;
                }
                PlannedCopy copy = new PlannedCopy();
                copy.Rel = pair.Key;
                if (bothWays && exists && ClearlyNewer(other.WriteTime, pair.Value.WriteTime))
                {
                    copy.ToA = true;
                    copy.Size = other.Size;
                }
                else
                {
                    copy.Size = pair.Value.Size;
                }
                plan.Add(copy);
            }
            if (bothWays)
            {
                foreach (KeyValuePair<string, PhoneEntry> pair in filesB)
                {
                    if (filesA.ContainsKey(pair.Key)) continue;
                    PlannedCopy copy = new PlannedCopy();
                    copy.Rel = pair.Key;
                    copy.ToA = true;
                    copy.Size = pair.Value.Size;
                    plan.Add(copy);
                }
            }
            return plan;
        }

        static bool ClearlyNewer(long time, long than)
        {
            if (time == 0 || than == 0) return false;
            return time - than > TimeTolerance;
        }

        static FileJob MakeJob(PlannedCopy copy, FolderSpot from, string fromRoot, PhoneTree fromTree, Dictionary<string, PhoneEntry> fromFiles,
            FolderSpot to, string toRoot, PhoneTree toTree, HashSet<string> destDirs, Dictionary<string, PhoneEntry> destFiles, bool labelWay)
        {
            FileJob job = new FileJob();
            job.RelativePath = copy.Rel;
            job.Size = copy.Size;
            job.State = FileJob.Waiting;
            if (labelWay) job.Way = copy.ToA ? 2 : 1;
            if (fromTree != null)
            {
                PhoneEntry entry = fromFiles[copy.Rel];
                job.PhoneSource = entry.Item;
                job.PhoneLive = entry.Live;
                job.PhoneFrom = fromTree;
                job.PhoneTime = entry.WriteTime;
                job.SourcePath = from.Text + "\\" + copy.Rel;
            }
            else
            {
                job.SourcePath = Native.ToExtended(Diff.Join(fromRoot, copy.Rel));
            }
            if (toTree != null)
            {
                job.PhoneDest = toTree;
                job.DestPath = to.Text + "\\" + copy.Rel;
            }
            else
            {
                job.DestPath = Native.ToExtended(Diff.Join(toRoot, copy.Rel));
            }
            if (destDirs.Contains(copy.Rel) || ParentIsFile(copy.Rel, destFiles))
            {
                job.State = FileJob.Failed;
                string destName = copy.ToA ? "Folder A" : "Folder B";
                job.Error = destDirs.Contains(copy.Rel)
                    ? destName + " has a folder with this name."
                    : destName + " has a file in the way of this path.";
            }
            return job;
        }

        static bool ParentIsFile(string rel, Dictionary<string, PhoneEntry> files)
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

        // Deletes '.fdc-part' files left on the phone when an earlier copy was cut off.
        static void RemoveLeftovers(PhoneTree tree, ScanStatus status, CompareResult result)
        {
            foreach (string rel in tree.Leftovers)
            {
                if (status.Cancel)
                {
                    result.Cancelled = true;
                    return;
                }
                try
                {
                    PhoneShell.DeleteInTree(tree, rel);
                }
                catch (Exception ex)
                {
                    if (result.FirstProblem == null)
                        result.FirstProblem = "Could not remove an unfinished file on the phone. " + PhoneErrors.Describe(ex);
                }
            }
            tree.Leftovers.Clear();
        }

        static void CreateMissingDirs(HashSet<string> sourceDirs, HashSet<string> destDirs, Dictionary<string, PhoneEntry> destFiles, string destRoot, PhoneTree destTree, ScanStatus status, CompareResult result, string blockedMessage)
        {
            var missing = new List<string>();
            foreach (string dir in sourceDirs)
            {
                if (destDirs.Contains(dir)) continue;
                if (destFiles.ContainsKey(dir))
                {
                    result.DirCreateFailed++;
                    if (result.FirstProblem == null)
                        result.FirstProblem = blockedMessage + dir;
                    continue;
                }
                missing.Add(dir);
            }
            missing.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string dir in missing)
            {
                if (status.Cancel)
                {
                    result.Cancelled = true;
                    return;
                }
                try
                {
                    if (destTree != null)
                    {
                        IShellItem made = PhoneShell.OpenDir(destTree, dir);
                        if (!destTree.Owns(made)) PhoneShell.Release(made);
                    }
                    else
                        Native.EnsureDirectory(Native.ToExtended(Diff.Join(destRoot, dir)));
                    result.DirsCreated++;
                }
                catch (Exception ex)
                {
                    result.DirCreateFailed++;
                    if (result.FirstProblem == null)
                        result.FirstProblem = destTree != null ? PhoneErrors.Describe(ex) : ex.Message;
                }
            }
        }
    }

    // Copying when a phone is on one side. Phones handle one file at a time, so the
    // session runs these one by one, each on its own single-threaded apartment thread.
    internal sealed partial class CopySession
    {
        public volatile bool OneAtATime;

        void Launch(FileJob job)
        {
            if (job.OnPhone)
                PhoneShell.StartSta(delegate { CopyCallback(job); });
            else
                ThreadPool.QueueUserWorkItem(CopyCallback, job);
        }

        CopyOutcome CopyPhone(FileJob job)
        {
            if (cancel) return CopyOutcome.Cancelled;
            if (job.PhoneSource != null) return CopyFromPhone(job);
            return CopyToPhone(job);
        }

        // Phone to PC: copy into a temp folder with Explorer's file operation (MTP
        // usually has no usable stream), then rename into place.
        CopyOutcome CopyFromPhone(FileJob job)
        {
            string destPretty = Native.Pretty(job.DestPath);
            string parentPretty = Native.Pretty(Native.Parent(job.DestPath));
            string destName = Path.GetFileName(destPretty);
            string tempDir = Path.Combine(parentPretty, destName + ".fdc-dir");
            bool committed = false;
            IShellItem item = null;
            IShellItem destFolder = null;
            try
            {
                Native.EnsureDirectory(Native.ToExtended(parentPretty));
                if (cancel) return CopyOutcome.Cancelled;
                item = PhoneShell.OpenFile(job);
                long size;
                long time;
                PhoneShell.ReadMeta(item, out size, out time);
                if (size >= 0 && size != job.Size)
                {
                    Interlocked.Add(ref totalBytes, size - job.Size);
                    job.Size = size;
                }
                if (time != 0) job.PhoneTime = time;
                DeleteTreeQuiet(tempDir);
                Native.EnsureDirectory(Native.ToExtended(tempDir));
                destFolder = PhoneShell.Open(tempDir);
                if (destFolder == null)
                    throw new IOException("Could not open " + tempDir + ".");
                Action<uint, uint> progress = delegate(uint total, uint soFar) { Report(job, total, soFar); };
                Func<bool> stopping = delegate { return cancel; };
                Exception copyError = null;
                try
                {
                    PhoneShell.CopyToFolder(item, destFolder, destName, progress, stopping);
                }
                catch (Exception ex)
                {
                    copyError = ex;
                }
                PhoneShell.Release(destFolder);
                destFolder = null;
                if (cancel) return CopyOutcome.Cancelled;
                string found = PhoneShell.FindOnDisk(tempDir, destName);
                if (found == null)
                {
                    DeleteTreeQuiet(tempDir);
                    Native.EnsureDirectory(Native.ToExtended(tempDir));
                    try
                    {
                        PhoneShell.CopyHereToFolder(item, tempDir, destName, job.Size, stopping);
                    }
                    catch (Exception ex)
                    {
                        if (copyError == null) copyError = ex;
                    }
                    if (cancel) return CopyOutcome.Cancelled;
                    found = PhoneShell.FindOnDisk(tempDir, destName);
                }
                if (found == null)
                {
                    if (copyError != null) throw copyError;
                    throw new PhoneException("The phone file did not arrive on this PC.");
                }
                if (job.PhoneTime != 0)
                {
                    try { File.SetLastWriteTimeUtc(found, DateTime.FromFileTimeUtc(job.PhoneTime)); }
                    catch { }
                }
                Native.MoveReplace(Native.ToExtended(found), job.DestPath);
                committed = true;
                Complete(job);
                return CopyOutcome.Success;
            }
            catch (Exception ex)
            {
                if (string.IsNullOrEmpty(job.Error))
                    job.Error = PhoneErrors.Describe(ex);
                return cancel ? CopyOutcome.Cancelled : CopyOutcome.Failed;
            }
            finally
            {
                PhoneShell.Release(destFolder);
                if (item != job.PhoneLive && (job.PhoneFrom == null || !job.PhoneFrom.Owns(item)))
                    PhoneShell.Release(item);
                DeleteTreeQuiet(tempDir);
                if (!committed)
                {
                    long partial = Interlocked.Exchange(ref job.Copied, 0);
                    if (partial != 0)
                        Interlocked.Add(ref copiedBytes, -partial);
                }
            }
        }

        static void DeleteTreeQuiet(string dir)
        {
            try
            {
                if (!string.IsNullOrEmpty(dir) && Native.DirExists(Native.ToExtended(dir)))
                    Directory.Delete(dir, true);
            }
            catch
            {
            }
        }

        // PC to phone: write under the real name. Phones do not rename '.fdc-part'
        // files, so that extra step left unfinished names on the device.
        CopyOutcome CopyToPhone(FileJob job)
        {
            PhoneTree tree = job.PhoneDest;
            string rel = job.RelativePath;
            int cut = rel.LastIndexOf('\\');
            string parentRel = cut < 0 ? "" : rel.Substring(0, cut);
            string name = cut < 0 ? rel : rel.Substring(cut + 1);
            string partName = name + PhoneShell.PartSuffix;
            IShellItem folder = null;
            IShellItem source = null;
            IShellItem kept = null;
            bool committed = false;
            bool started = false;
            try
            {
                using (OpenedRead probe = Native.OpenForRead(job.SourcePath))
                {
                    if (probe.Size >= 0 && probe.Size != job.Size)
                    {
                        Interlocked.Add(ref totalBytes, probe.Size - job.Size);
                        job.Size = probe.Size;
                    }
                }
                if (cancel) return CopyOutcome.Cancelled;
                folder = PhoneShell.OpenDir(tree, parentRel);
                source = PhoneShell.Open(Native.Pretty(job.SourcePath));
                if (source == null)
                    throw new IOException("Could not open " + Native.Pretty(job.SourcePath) + ".");
                Action<uint, uint> progress = delegate(uint total, uint soFar) { Report(job, total, soFar); };
                Func<bool> stopping = delegate { return cancel; };

                RemoveOld(tree, rel);
                PhoneShell.DeleteNamedQuiet(folder, partName);

                started = true;
                string made = PhoneShell.CopyToFolder(source, folder, name, progress, stopping);
                if (cancel) return CopyOutcome.Cancelled;
                kept = PhoneShell.FindChild(folder, name);
                if (kept == null)
                    throw new PhoneException("The phone did not keep the copied file.");
                if (string.IsNullOrEmpty(made)) made = PhoneShell.ParsingName(kept);
                tree.SetFile(rel, made);
                tree.SetLive(rel, kept);
                kept = null;
                committed = true;
                Complete(job);
                return CopyOutcome.Success;
            }
            catch (Exception ex)
            {
                if (string.IsNullOrEmpty(job.Error) && !(ex is OperationCanceledException))
                    job.Error = PhoneErrors.Describe(ex);
                return cancel ? CopyOutcome.Cancelled : CopyOutcome.Failed;
            }
            finally
            {
                if (!committed)
                {
                    long partial = Interlocked.Exchange(ref job.Copied, 0);
                    if (partial != 0)
                        Interlocked.Add(ref copiedBytes, -partial);
                    if (started)
                        PhoneShell.DeleteNamedQuiet(folder, name);
                }
                PhoneShell.Release(kept);
                PhoneShell.Release(source);
                PhoneShell.Release(folder);
            }
        }

        static void RemoveOld(PhoneTree tree, string rel)
        {
            try { PhoneShell.DeleteInTree(tree, rel); }
            catch { }
            tree.SetFile(rel, null);
        }

        void Report(FileJob job, uint total, uint soFar)
        {
            if (total == 0) return;
            long now = (long)(job.Size * ((double)soFar / total));
            if (now > job.Size) now = job.Size;
            long before = Interlocked.Exchange(ref job.Copied, now);
            if (now != before) Interlocked.Add(ref copiedBytes, now - before);
        }

        void Complete(FileJob job)
        {
            long before = Interlocked.Exchange(ref job.Copied, job.Size);
            if (job.Size != before) Interlocked.Add(ref copiedBytes, job.Size - before);
        }

        void ResetProgress(FileJob job)
        {
            long before = Interlocked.Exchange(ref job.Copied, 0);
            if (before != 0) Interlocked.Add(ref copiedBytes, -before);
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct PropertyKey
    {
        public Guid FormatId;
        public uint PropertyId;

        public PropertyKey(Guid formatId, uint propertyId)
        {
            FormatId = formatId;
            PropertyId = propertyId;
        }
    }

    [ComImport]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItem
    {
        void BindToHandler(IntPtr bind, ref Guid bhid, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object handler);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint sigdn, [MarshalAs(UnmanagedType.LPWStr)] out string name);
        void GetAttributes(uint mask, out uint attributes);
        void Compare(IShellItem other, uint hint, out int order);
    }

    [ComImport]
    [Guid("7e9fb0d3-919f-4307-ab2e-9b1860310c93")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItem2
    {
        void BindToHandler(IntPtr bind, ref Guid bhid, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object handler);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint sigdn, [MarshalAs(UnmanagedType.LPWStr)] out string name);
        void GetAttributes(uint mask, out uint attributes);
        void Compare(IShellItem other, uint hint, out int order);
        [PreserveSig] int GetPropertyStore(int flags, ref Guid iid, out IntPtr store);
        [PreserveSig] int GetPropertyStoreWithCreateObject(int flags, IntPtr createObject, ref Guid iid, out IntPtr store);
        [PreserveSig] int GetPropertyStoreForKeys(IntPtr keys, uint count, int flags, ref Guid iid, out IntPtr store);
        [PreserveSig] int GetPropertyDescriptionList(ref PropertyKey keyType, ref Guid iid, out IntPtr list);
        [PreserveSig] int Update(IntPtr bind);
        [PreserveSig] int GetProperty(ref PropertyKey key, IntPtr value);
        [PreserveSig] int GetCLSID(ref PropertyKey key, out Guid clsid);
        [PreserveSig] int GetFileTime(ref PropertyKey key, out long fileTime);
        [PreserveSig] int GetInt32(ref PropertyKey key, out int value);
        [PreserveSig] int GetString(ref PropertyKey key, out IntPtr value);
        [PreserveSig] int GetUInt32(ref PropertyKey key, out uint value);
        [PreserveSig] int GetUInt64(ref PropertyKey key, out ulong value);
        [PreserveSig] int GetBool(ref PropertyKey key, out int value);
    }

    [ComImport]
    [Guid("70629033-e363-4a28-a567-0db78006e6d7")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IEnumShellItems
    {
        [PreserveSig] int Next(uint count, [MarshalAs(UnmanagedType.Interface)] out IShellItem item, out uint fetched);
        [PreserveSig] int Skip(uint count);
        [PreserveSig] int Reset();
        [PreserveSig] int Clone(out IEnumShellItems copy);
    }

    [ComImport]
    [Guid("000214E6-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellFolder
    {
        void ParseDisplayName(IntPtr hwnd, IntPtr bind, [MarshalAs(UnmanagedType.LPWStr)] string name, ref uint eaten, out IntPtr pidl, ref uint attributes);
        [PreserveSig] int EnumObjects(IntPtr hwnd, int flags, out IEnumIDList list);
        void BindToObject(IntPtr pidl, IntPtr bind, ref Guid iid, out IntPtr ppv);
        void BindToStorage(IntPtr pidl, IntPtr bind, ref Guid iid, out IntPtr ppv);
        [PreserveSig] int CompareIDs(IntPtr lparam, IntPtr pidl1, IntPtr pidl2);
        void CreateViewObject(IntPtr hwndOwner, ref Guid iid, out IntPtr ppv);
        void GetAttributesOf(uint count, IntPtr pidl, ref uint attributes);
        void GetUIObjectOf(IntPtr hwndOwner, uint count, IntPtr pidl, ref Guid iid, IntPtr reserved, out IntPtr ppv);
        void GetDisplayNameOf(IntPtr pidl, uint flags, IntPtr name);
        void SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string name, uint flags, out IntPtr pidlOut);
    }

    [ComImport]
    [Guid("000214F2-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IEnumIDList
    {
        [PreserveSig] int Next(uint count, out IntPtr pidl, out uint fetched);
        [PreserveSig] int Skip(uint count);
        [PreserveSig] int Reset();
        [PreserveSig] int Clone(out IEnumIDList copy);
    }

    [ComImport]
    [Guid("00000146-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IGlobalInterfaceTable
    {
        void RegisterInterfaceInGlobal([MarshalAs(UnmanagedType.IUnknown)] object punk, ref Guid riid, out uint cookie);
        void RevokeInterfaceFromGlobal(uint cookie);
        void GetInterfaceFromGlobal(uint cookie, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
    }

    [ComImport]
    [Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IFileOperation
    {
        void Advise(IFileOperationProgressSink sink, out uint cookie);
        void Unadvise(uint cookie);
        void SetOperationFlags(uint flags);
        void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
        void SetProgressDialog(IntPtr dialog);
        void SetProperties(IntPtr properties);
        void SetOwnerWindow(IntPtr owner);
        void ApplyPropertiesToItem(IShellItem item);
        void ApplyPropertiesToItems([MarshalAs(UnmanagedType.IUnknown)] object items);
        void RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string newName, IFileOperationProgressSink sink);
        void RenameItems([MarshalAs(UnmanagedType.IUnknown)] object items, [MarshalAs(UnmanagedType.LPWStr)] string newName);
        void MoveItem(IShellItem item, IShellItem destFolder, [MarshalAs(UnmanagedType.LPWStr)] string newName, IFileOperationProgressSink sink);
        void MoveItems([MarshalAs(UnmanagedType.IUnknown)] object items, IShellItem destFolder);
        void CopyItem(IShellItem item, IShellItem destFolder, [MarshalAs(UnmanagedType.LPWStr)] string copyName, IFileOperationProgressSink sink);
        void CopyItems([MarshalAs(UnmanagedType.IUnknown)] object items, IShellItem destFolder);
        void DeleteItem(IShellItem item, IFileOperationProgressSink sink);
        void DeleteItems([MarshalAs(UnmanagedType.IUnknown)] object items);
        void NewItem(IShellItem destFolder, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string templateName, IFileOperationProgressSink sink);
        void PerformOperations();
        void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }

    [ComImport]
    [Guid("04b0f1a7-9490-44bc-96e1-4296a31252e2")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IFileOperationProgressSink
    {
        [PreserveSig] int StartOperations();
        [PreserveSig] int FinishOperations(int result);
        [PreserveSig] int PreRenameItem(uint flags, IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string newName);
        [PreserveSig] int PostRenameItem(uint flags, IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string newName, int result, IShellItem created);
        [PreserveSig] int PreMoveItem(uint flags, IShellItem item, IShellItem destFolder, [MarshalAs(UnmanagedType.LPWStr)] string newName);
        [PreserveSig] int PostMoveItem(uint flags, IShellItem item, IShellItem destFolder, [MarshalAs(UnmanagedType.LPWStr)] string newName, int result, IShellItem created);
        [PreserveSig] int PreCopyItem(uint flags, IShellItem item, IShellItem destFolder, [MarshalAs(UnmanagedType.LPWStr)] string newName);
        [PreserveSig] int PostCopyItem(uint flags, IShellItem item, IShellItem destFolder, [MarshalAs(UnmanagedType.LPWStr)] string newName, int result, IShellItem created);
        [PreserveSig] int PreDeleteItem(uint flags, IShellItem item);
        [PreserveSig] int PostDeleteItem(uint flags, IShellItem item, int result, IShellItem created);
        [PreserveSig] int PreNewItem(uint flags, IShellItem destFolder, [MarshalAs(UnmanagedType.LPWStr)] string newName);
        [PreserveSig] int PostNewItem(uint flags, IShellItem destFolder, [MarshalAs(UnmanagedType.LPWStr)] string newName, [MarshalAs(UnmanagedType.LPWStr)] string templateName, uint attributes, int result, IShellItem created);
        [PreserveSig] int UpdateProgress(uint workTotal, uint workSoFar);
        [PreserveSig] int ResetTimer();
        [PreserveSig] int PauseTimer();
        [PreserveSig] int ResumeTimer();
    }

    // Receives progress and results from IFileOperation.
    [ComVisible(true)]
    internal sealed class OperationSink : IFileOperationProgressSink
    {
        const int Abort = unchecked((int)0x80004004);
        readonly Action<uint, uint> progress;
        readonly Func<bool> cancelled;
        public int Failure;
        public string NewItem;
        public bool WasCancelled;

        public OperationSink(Action<uint, uint> progress, Func<bool> cancelled)
        {
            this.progress = progress;
            this.cancelled = cancelled;
        }

        void Note(int result, IShellItem created)
        {
            if (result < 0)
            {
                if (Failure >= 0) Failure = result;
                return;
            }
            if (created == null) return;
            try { NewItem = PhoneShell.ParsingName(created); }
            catch { }
        }

        bool Stop()
        {
            if (cancelled != null && cancelled())
            {
                WasCancelled = true;
                return true;
            }
            return false;
        }

        public int StartOperations() { return 0; }
        public int FinishOperations(int result) { if (result < 0 && Failure >= 0) Failure = result; return 0; }
        public int PreRenameItem(uint flags, IShellItem item, string newName) { return 0; }
        public int PostRenameItem(uint flags, IShellItem item, string newName, int result, IShellItem created) { Note(result, created); return 0; }
        public int PreMoveItem(uint flags, IShellItem item, IShellItem destFolder, string newName) { return 0; }
        public int PostMoveItem(uint flags, IShellItem item, IShellItem destFolder, string newName, int result, IShellItem created) { Note(result, created); return 0; }
        public int PreCopyItem(uint flags, IShellItem item, IShellItem destFolder, string newName) { return Stop() ? Abort : 0; }
        public int PostCopyItem(uint flags, IShellItem item, IShellItem destFolder, string newName, int result, IShellItem created) { Note(result, created); return 0; }
        public int PreDeleteItem(uint flags, IShellItem item) { return 0; }
        public int PostDeleteItem(uint flags, IShellItem item, int result, IShellItem created) { if (result < 0 && Failure >= 0) Failure = result; return 0; }
        public int PreNewItem(uint flags, IShellItem destFolder, string newName) { return 0; }
        public int PostNewItem(uint flags, IShellItem destFolder, string newName, string templateName, uint attributes, int result, IShellItem created) { Note(result, created); return 0; }

        public int UpdateProgress(uint workTotal, uint workSoFar)
        {
            if (Stop()) return Abort;
            if (progress != null) progress(workTotal, workSoFar);
            return 0;
        }

        public int ResetTimer() { return 0; }
        public int PauseTimer() { return 0; }
        public int ResumeTimer() { return 0; }
    }
}
