# Material appearance and local logo customization
Category: Personalization
Suggested: navigation-search-and-command-palette,accessibility-and-responsive-use,privacy-local-data-and-history

## Material 3

The site uses role-based colors, expressive type, rounded shapes, tonal surfaces, bounded elevation, reduced-motion support, and responsive density. Light, dark, and high-contrast modes share the same information architecture.

## Appearance editor

Tab context menus and Shift+right-click expose an anchored appearance editor. The current static implementation edits accent, panel width, and representative shape controls. Platform-only typography and effects remain documented as unavailable rather than silently disappearing.

## Custom mark

The shipped mark is a 1024 by 1024 transparent PNG from source commit a06419b7f387927cff647d945e7bf51e471879d4 with SHA-256 8e6333f433bc875a5829bfe7ad13e89630f7cbfbd7725a38be998593f769d03c. The custom mark picker accepts genuine PNG, JPEG, and WebP bytes up to 1 MiB, limits decoded dimensions and pixel count, and keeps the previous mark if validation fails. The image stays in browser storage and changes presentation only; it never changes package identity, update feed, or storage keys.
