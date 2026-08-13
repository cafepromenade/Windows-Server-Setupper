# Windows Server Setupper brand assets

This directory contains the original application mark shared by the Windows desktop tools.

## Files

- `windows-server-setupper-logo-master.png` is the committed 1024 x 1024 RGBA raster master. It uses solid navy `#071A3D` and cyan `#00D8E8` fills on a transparent background.
- `windows-server-setupper.ico` is the Windows icon container derived from the master. It includes 16, 20, 24, 32, 40, 48, 64, 128, and 256 pixel square entries.

## Design and usage

The shield-shaped server rack, integrated envelope, and circular recovery arc form one original, text-free mark. The shape is padded and intentionally simple enough to remain identifiable at small application-icon sizes.

Use the PNG for application chrome and documentation that supports transparent raster images. Use the ICO for executable, installer, shortcut, and Windows shell metadata. Logo presentation must not change package identifiers, installation paths, update feeds, data directories, or other stable application identity.

The image-generation source was normalized locally into a deterministic two-color raster, passed through chroma-key background removal, and validated for transparent corners, bounded coverage, file signatures, color fringing, and icon-directory round trips. No generated intermediate is required at runtime.
