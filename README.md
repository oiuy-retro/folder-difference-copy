# Folder Difference Copy

Windows 7–style WinForms app that copies missing or changed files from folder A to folder B. Optional two-way copy.

A built copy is [`Folder.Difference.Copy.exe`](https://github.com/oiuy-retro/folder-difference-copy/releases/latest/download/Folder.Difference.Copy.exe) on the [latest release](https://github.com/oiuy-retro/folder-difference-copy/releases/latest).

## What it does

- One-way, A to B (default): copies files from A that are missing in B, and replaces a same-named file in B when its size or modified time differs. Files that exist only in B stay.
- **Copy both ways:** missing files go A → B and B → A. Same-named files that differ use the newer write time; A wins a tie. Empty folders are created on both sides.
- Refuses folders that contain each other.
- Parallelization sets how many files are copied at the same time, from 1 to 32.
- Copy order can be folder order, smallest first, largest first, or large beside small, where each large file is copied alongside small ones.
- Drive profiles set the parallelization and copy order to suit the drives you're copying between, such as hard drive to solid-state drive or the same solid-state drive.
- Start at schedules the copy to begin at a chosen future date and time. Stop cancels the scheduled copy.
- Long paths (`\\?\`) are enabled.
- Cancel removes leftover `.fdc-partial` files.
- Android phones and other devices that show up in Explorer (MTP) can be Folder A or Folder B. See [Phones](#phones).

## Phones

Plug the phone in with USB, unlock it, and set its USB mode to **File transfer**. Then use **Browse...** and pick a folder inside the phone's storage, such as `This PC\Pixel 8\Internal shared storage\Music`. The phone itself, Libraries, Network, and other places that aren't real folders are refused.

- Either side can be the phone: PC to phone, phone to PC, or **Copy both ways**. Only one side can be a phone.
- Phones take one file at a time over USB, so the app copies one file at a time whenever a phone is involved. Parallelization is greyed out at 1 for the phone profiles (Hard drive to phone, Solid-state drive to phone, Phone to hard drive, Phone to solid-state drive) and whenever either folder is on a phone.
- **Comparing:** phones report modified times loosely (rounded, shifted by time zones, or set to the time of the copy), so a file counts as unchanged when its size matches. A file that changed but kept exactly the same size is not copied again. With **Copy both ways**, a file whose size differs is copied from the side whose modified time is more than an hour newer; otherwise Folder A wins.
- **Unfinished files:** files are sent to the phone as `<name>.fdc-part` and renamed to the real name when the last byte arrives, so a file with its real name is always complete. If the phone refuses renames, the file is written under its real name and deleted right away if the copy fails. At the start of each run, leftover `.fdc-part` files in the phone folder are deleted and those files are copied again.
- Phone to PC uses the same `.fdc-partial` file on the PC and gives the PC file the phone's modified time.
- If the phone is locked, not in File transfer mode, or unplugged, the copy stops with a message that says so. Plug it back in, unlock it, and run the copy again.

## Run

Start `Folder Difference Copy.exe` on Windows 7 or later with [.NET Framework 4.7 or later](https://dotnet.microsoft.com/download/dotnet-framework/net48). Windows 10 version 1709 and later, and Windows 11, already include it. Windows 7 needs .NET Framework 4.7 or later installed first.

Self-test (exits 0 on success):

```bat
"Folder Difference Copy.exe" --selftest
```

## Settings file

The app keeps its settings in `configuration.conf`, a plain text file next to the exe. If the file is missing, the app writes a commented copy on first run. Lines starting with `#` are comments, and you can edit the file in Notepad while the app is closed.

```ini
[tools]
# Folder Difference Copy doesn't use any outside programs.

[folders]
a = D:\Photos
b = E:\Backup\Photos
```

`[folders]` holds the folder each Browse button opens at. After you pick a folder, the app updates only that line and leaves the rest of the file alone. If a saved folder no longer exists, Browse opens at the nearest folder above it that still exists. If the exe's folder can't be written to, for example under Program Files, the app saves to `%APPDATA%\Folder Difference Copy\configuration.conf` instead, and values there win over the ones next to the exe. Relative paths start from the exe's folder, and `%VARS%` such as `%USERPROFILE%` are expanded.

## Build

There is no .NET SDK requirement. `build.bat` uses the framework compiler:

```bat
build.bat
copy FolderDiffCopy.exe dist\"Folder Difference Copy.exe"
```

Sources: `Program.cs`, `Native.cs`, `Engine.cs`, `Phone.cs`, `Config.cs`, `FolderDialog.cs`, `Win7Ux.cs`, `FileList.cs`, `MainForm.cs`, plus `app.manifest` and `app.ico`.

Compiler: `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe` (C# 5 / .NET 4).

## License

[MIT](LICENSE).

## Attributions

| Project | License | Use |
| --- | --- | --- |
| [.NET Framework 4.7 or later](https://dotnet.microsoft.com/download/dotnet-framework/net48) | Microsoft .NET Framework EULA | Runtime. Not redistributed; the machine already has it. |
| WinForms, System.IO | Microsoft | UI and file copy |
| Windows Common Controls 6, Explorer `IFileDialog` | Microsoft | Visual styles and folder picker |
| Explorer shell `IShellItem`, `IFileOperation` | Microsoft | Reading and writing phone folders. Part of Windows. |

This program does not redistribute the .NET Framework or Windows.
