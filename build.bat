@echo off
setlocal
set CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set OUT=%~dp0FolderDiffCopy.exe
"%CSC%" /nologo /target:winexe /optimize+ /warn:4 /platform:anycpu /debug- /out:"%OUT%" /win32manifest:"%~dp0app.manifest" /win32icon:"%~dp0app.ico" /reference:System.dll,System.Core.dll,System.Drawing.dll,System.Windows.Forms.dll "%~dp0Program.cs" "%~dp0Native.cs" "%~dp0Engine.cs" "%~dp0Phone.cs" "%~dp0FolderDialog.cs" "%~dp0Win7Ux.cs" "%~dp0FileList.cs" "%~dp0MainForm.cs"
if errorlevel 1 exit /b 1
echo Built %OUT%
