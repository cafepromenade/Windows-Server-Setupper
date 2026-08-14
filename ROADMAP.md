# Roadmap

## Current milestone: prove and publish the combined Windows release

The current source describes two complementary Windows applications and their delivery routes:

- the resilient WPF server-setup application with recovery-oriented state handling;
- the mostly pre-filled, staged Exchange Auto Installer;
- Cheap LFS metadata verification and explicit ISO hydration for Exchange media, with no standard Git LFS route;
- optional managed OpenCode repair with an explicit, off-by-default YOLO mode constrained to the application's repair catalog;
- unsigned WPF/Inno Setup and Exchange/Squirrel.Windows artifact routes; and
- a documentation-site build route that creates `docs-site/dist` for Sites and `docs-site/pages-dist` for GitHub Pages.

These are source and documentation records, not final-release evidence. No final combined release, installer asset, GitHub Actions result, GitHub Pages deployment, installation result, or capture is claimed here.

## Before final publication

1. Select and integrate the exact final source revision without rewriting existing history.
2. Run the supported build and installer routes from that revision and record the actual unsigned WPF/Inno Setup and Exchange/Squirrel.Windows outputs.
3. Publish the exact source revision through the configured Windows release route, then read back the resulting non-draft release, target revision, artifacts, hashes, and downloadability.
4. Build the documentation site through `docs-site/build.bat /ci` and publish the matching `pages-dist` export only through a configured GitHub Pages publisher. Record the final URL only after publication and read-back verification.
5. Keep the Exchange ISO outside release assets and Git history. The runtime media flow must validate the Cheap LFS part inventory and final ISO before use.

## Safety and evidence boundaries

- Both artifact families are intentionally unsigned. Unknown-publisher and SmartScreen warnings are expected; no release record may claim a signing result.
- The Exchange installer must not pre-fill credentials. Its OpenCode repair path remains managed and bounded; YOLO mode must remain opt-in and confined to fixed repair actions.
- The GitHub Actions release workflow is designed to build and publish rather than run tests or lint. Any local test, lint, review, audit, runtime, or UI-capture evidence must identify its exact source revision and must not be inferred from this roadmap.
- An earlier recovery-only WPF release and any historical local checks remain historical records, not proof of the pending combined final release.

See [release documentation](./docs/release/README.md) for the delivery contract and [HANDOFF.md](./HANDOFF.md) for the next owner actions.
