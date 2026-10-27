# Known issues

Open copies live on the [Issues](https://github.com/oiuy-retro/folder-difference-copy/issues) tab.

## Preview and Start after picking a phone folder

[#3](https://github.com/oiuy-retro/folder-difference-copy/issues/3). On `issue-3-phone-preview-start` and `feature/android-mtp`. Browse can pick a folder on a plugged-in phone. Preview lists files; phone→PC copy uses live items. PC→phone writes `*.fdc-part` then renames with the live shell item so a real name on the phone is always a complete file. If the phone refuses the rename, the rest of the run writes the real name. A file already under the real name only counts as the new copy when it clearly is one (new, or now the source's size and a different file than before), so an old file is never reported as copied. A rename that reports an error but did finish counts as done, and a rename that lands on another name such as `name (1)` is removed and the file is written directly. Leftover `.fdc-part` files are deleted on Start. Preview lists the picked folder on the window thread with Shell.Application, the same listing that already showed the phone's files. The dialog's shell item is kept. GetFolder is called as a method when walking down from This PC. A folder Browse already opened is not reported as closed when a later name lookup fails.
