# Known issues

Open copies live on the [Issues](https://github.com/oiuy-retro/folder-difference-copy/issues) tab.

## Preview and Start after picking a phone folder

[#3](https://github.com/oiuy-retro/folder-difference-copy/issues/3). On `issue-3-phone-preview-start`. Browse can pick a folder on a plugged-in phone. Preview lists files; phone→PC copy uses live items. PC→phone writes `*.fdc-part` then renames with the live shell item so a real name on the phone is always a complete file. If the phone refuses the rename, the rest of the run writes the real name. Leftover `.fdc-part` files are deleted on Start.
