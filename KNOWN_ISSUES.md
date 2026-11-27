# Known issues

Open copies live on the [Issues](https://github.com/oiuy-retro/folder-difference-copy/issues) tab.

## Same-sized files an hour apart

[Issue #6](https://github.com/oiuy-retro/folder-difference-copy/issues/6). Shared folders, a camera clock change, or daylight saving can shift a modified time by one hour while the size stays the same. Copying those files can shift the clock again. With **Daylight Savings error fix** on (the default), a same-sized file within 2 seconds of one hour is left alone. Turn the option off to copy those files. A different size, or a gap more than 2 seconds past an hour, is still copied.
