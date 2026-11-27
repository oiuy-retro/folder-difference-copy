# Known issues

Open copies live on the [Issues](https://github.com/oiuy-retro/folder-difference-copy/issues) tab.

## Same-sized files an hour apart

[Issue #6](https://github.com/oiuy-retro/folder-difference-copy/issues/6). Shared folders can shift a modified time by one hour while the size stays the same. Copying those files can shift the clock again. On this branch a same-sized file within 2 seconds of one hour is left alone. A different size, or a gap more than 2 seconds past an hour, is still copied.
