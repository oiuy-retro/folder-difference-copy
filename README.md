# Folder Difference Copy

Windows 7–style WinForms app that copies missing or changed files from folder A to folder B. Optional two-way copy.

A built copy is [`Folder.Difference.Copy.exe`](https://github.com/oiuy-retro/folder-difference-copy/releases/latest/download/Folder.Difference.Copy.exe) on the [latest release](https://github.com/oiuy-retro/folder-difference-copy/releases/latest). It is a single file. The installed [.NET Framework 4](https://dotnet.microsoft.com/download/dotnet-framework/net40) is required (no extra `.config` next to the exe).
```
sha256: 27a3769df8722d4106d2f68d5d6437ffe4787fd81f7d94622c4eb2a7f32234b8
```

## What it does

- One-way (default): copy files that are missing in B, and replace a same-named file when size or modified time differs. Files that exist only in B stay.
- **Copy both ways:** missing files go A → B and B → A. Same-named files that differ use the newer write time; A wins a tie. Empty folders are created on both sides.
- Refuses folders that contain each other.
- Parallelization 1–32 (default 1). Each file is one copy stream. Finished rows leave the remaining list immediately.
- Drive profiles (hard drive vs SSD) set folder order or large-beside-small interleaving.
- Start-at waits until a date and time. Stop cancels the wait. A time that has already passed is refused.
- Long paths (`\\?\`) are enabled.
- Cancel removes leftover `.fdc-partial` files.

The window uses Segoe UI, Aero-style chrome, glossy progress, and the Explorer folder picker. Profiles live in the app; the preset control is labeled Profile.

## Run

Start `Folder Difference Copy.exe` on Windows 7 or later with [.NET Framework 4.0+](https://dotnet.microsoft.com/download/dotnet-framework/net40).

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
| [.NET Framework 4](https://dotnet.microsoft.com/download/dotnet-framework/net40) | Microsoft .NET Framework EULA | Runtime. Not redistributed; the machine already has it. |
| WinForms, System.IO | Microsoft | UI and file copy |
| Windows Common Controls 6, Explorer `IFileDialog` | Microsoft | Visual styles and folder picker |

This program does not redistribute the .NET Framework or Windows.
