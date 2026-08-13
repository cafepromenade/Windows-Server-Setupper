# Windows Server Setupper documentation site

This vinext project builds the Material 3 landing page, offline documentation browser, local-only companion tools, and explicit completeness inventory for Windows Server Setupper.

## Build both hosting outputs

On Windows, run:

```batch
build.bat
```

The script restores the locked dependencies, produces the Cloudflare Worker-compatible Sites output at `dist`, exports a static GitHub Pages artifact at `pages-dist`, and runs the focused contract checks. The default GitHub Pages base path is `/Windows-Server-Setupper/`. To verify a different repository name, pass its single-segment base path:

```batch
build.bat /different-repository-name
```

The static export command alone is:

```powershell
npm run export:pages -- --base-path /Windows-Server-Setupper
```

It must run after `npm run build`. The export writes `index.html`, `404.html`, `.nojekyll`, the client assets, the shipped logo, service worker, and a machine-readable `pages-build.json` contract. The Pages publisher must upload exactly `docs-site/pages-dist` and serve it at the matching base path. Sites publication continues to package `docs-site/dist`; neither output replaces the other.

## Verification

```powershell
npm run validate:inventory
npm run build
npm run export:pages -- --base-path /Windows-Server-Setupper
node --test tests/*.test.mjs
```

The hand-written inventory is `content/completeness-inventory.json`. Its negative regression removes every required row and every proof field in memory and requires validation to turn red.

## Publication state

The final release download, GitHub Pages URL, and Sites URL remain absent until their exact versions are published and verified. The landing page currently identifies one older verified installer as previous release evidence; it never presents that asset as the pending final release.

## Development

Node.js 22.13.0 or newer is required. Run `npm ci` followed by `npm run dev` for local development. No D1 database or R2 object storage binding is declared; all visitor preferences are explicitly local to the browser profile.
