# Navigation, search, and the command palette
Category: Using the site
Suggested: accessibility-and-responsive-use,material-appearance-and-logo,offline-documentation

## Dockable tabs

The browser-style tab strip can dock on the left, right, top, or bottom and persists that choice on this device. Product, Explore, and Configure groups remain distinct. Pinned tabs keep a visible marker, and phone layouts collapse labels to a horizontal icon strip.

## Four discovery searches

The site exposes current-strip search, per-group tab search, group-name search, and master tab search. Each field owns its own anchored regular expression builder and keeps plain text as the default.

## Regular expression safety

Patterns use the browser's ECMAScript engine, are bounded to 160 characters, and evaluate only against a bounded sample. The builder supports literals, character classes, anchors, groups, alternation, quantifiers, and flags without transmitting the pattern.

## Command palette

Press Ctrl+Shift+F to open the command palette. Results open the exact destination or focus the matching upload control. Escape closes the palette and other transient overlays.
