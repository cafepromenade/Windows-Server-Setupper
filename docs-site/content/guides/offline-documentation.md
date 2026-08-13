# Offline documentation and publication
Category: Documentation
Suggested: navigation-search-and-command-palette,accessibility-and-responsive-use,product-overview

## Bundled articles

Every Markdown article in this directory is read at build time and emitted into a local TypeScript bundle. The documentation browser searches article titles and bodies without fetching a remote document.

## Offline cache

A small service worker caches the built page and same-origin static requests after they are visited. It does not intercept non-GET requests or cache cross-origin resources.

## Publication boundary

The source includes Sites hosting metadata with no platform database or object-storage binding. No hosted URL is claimed until a validated version is published. GitHub Pages is not enabled in the repository at the time of this build and remains an external publication step.
