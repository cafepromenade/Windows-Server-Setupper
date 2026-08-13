# Local application-logo customization

## Behavior

The main WPF window exposes the active logo, two shipped presets, and a local custom-image picker. The active selection appears in the main-window chrome, its live preview, and the commonly installed server-features window. Changing presentation never changes the application executable, installer, package identity, update feed, or data directory.

The shipped presets are the **Server and mail mark**, backed by the committed 1024-pixel PNG master, and the **Compact application icon**, backed by the committed nine-resolution ICO. The custom route accepts a user-selected PNG, JPEG, or BMP whose actual byte signature matches its decoder. It does not trust the filename extension.

## Bounds and conversion

The source must satisfy every bound before it becomes active:

- 1 byte through 5 MiB encoded size;
- exactly one frame;
- no dimension greater than 4096 pixels;
- no more than 16 megapixels decoded;
- a PNG, JPEG, or BMP signature that the platform decoder can parse.

The decoder uses an in-memory, load-complete platform bitmap and produces only the two raster sizes consumed by the WPF chrome: 256 by 256 and 48 by 48 PNG. Each derivative is decoded again and its dimensions and PNG signature are verified before activation.

**Contain** preserves the complete source inside the square. **Fill** covers the square and uses the keyboard-accessible horizontal and vertical focal-point sliders to choose the crop. Background is either transparent or an exact `#AARRGGBB` value. These choices never rewrite the original selected file.

## Privacy and persistence

Processing is local and has no network route. The selected file path is neither persisted nor logged. The validated source bytes, settings, and derivatives live only below the protected machine application-data branding directory. Settings record the preset, rendering choices, and derivative SHA-256; they do not record the source path.

**Reset to shipped logo** deletes the private source and every derivative, then immediately restores the server-and-mail preset.

## Failure behavior

Malformed, unsupported, oversized, multi-frame, over-dimension, over-pixel, or invalid-background input is rejected without activating it. A derivative whose bytes or SHA-256 later change fails closed to the shipped mark. Conversion failure keeps the prior displayed logo. Status is reported inline without a blocking informational dialog.

The decoder is bounded by input, dimension, pixel, frame, and output-count limits. It uses the platform image decoder in-process; it is not a security sandbox for hostile files. This is a documented remaining boundary for a future separately isolated decoder host.

## Verification

`Windows-Server-Tools.Tests` covers valid local PNG input, signature mismatch, the byte limit, contain and fill rendering, focal/background persistence, exact 48-pixel cache decoding, private state without paths or URLs, restart persistence, cache corruption fallback, both shipped presets, reset purging, both-window presentation wiring, and absence of an HTTP route.

Suggested articles: [Automatic updates](../reliability/automatic-updates.md), [WPF completeness](../completeness/wpf-universal-feature-inventory.md), and the committed brand-asset README.
