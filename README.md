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

## Run

Start `Folder Difference Copy.exe` on Windows 7 or later with [.NET Framework 4.7 or later](https://dotnet.microsoft.com/download/dotnet-framework/net48). Windows 10 version 1709 and later, and Windows 11, already include it. Windows 7 needs .NET Framework 4.7 or later installed first.

Self-test (exits 0 on success):

```bat
"Folder Difference Copy.exe" --selftest
```

## Build

There is no .NET SDK requirement. `build.bat` uses the framework compiler:

```bat
build.bat
copy FolderDiffCopy.exe dist\"Folder Difference Copy.exe"
```

Sources: `Program.cs`, `Native.cs`, `Engine.cs`, `FolderDialog.cs`, `Win7Ux.cs`, `FileList.cs`, `MainForm.cs`, plus `app.manifest` and `app.ico`.

Compiler: `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe` (C# 5 / .NET 4).

## License

[MIT](LICENSE).

## Attributions

| Project | License | Use |
| --- | --- | --- |
| [.NET Framework 4.7 or later](https://dotnet.microsoft.com/download/dotnet-framework/net48) | Microsoft .NET Framework EULA | Runtime. Not redistributed; the machine already has it. |
| WinForms, System.IO | Microsoft | UI and file copy |
| Windows Common Controls 6, Explorer `IFileDialog` | Microsoft | Visual styles and folder picker |

This program does not redistribute the .NET Framework or Windows.
