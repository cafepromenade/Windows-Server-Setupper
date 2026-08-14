# Handoff: pending combined Windows final release

## Current state

The repository has release-record source for the resilient WPF server tools and the Exchange Auto Installer, but the combined final release is not yet asserted as built, packaged, published, installed, or captured. This handoff records the route and the outstanding evidence; it does not convert historical results into current-candidate proof.

The current release record covers:

- a WPF server-setup application with recovery-oriented state and explicit uncertain-outcome reconciliation;
- a mostly pre-filled Exchange installation plan that does not pre-fill credentials;
- the Cheap LFS Exchange media route, which validates release-part metadata before hydration and validates the final ISO before use;
- the optional managed OpenCode repair adviser, whose YOLO mode is off by default and limited to fixed Exchange repair actions; and
- the intentional unsigned boundaries for the WPF/Inno Setup and Exchange/Squirrel.Windows outputs.

## Delivery route, not delivery evidence

- `build.bat /s` is the supported runnable-application route.
- `build-installer.bat /s` is the supported unsigned installer route. It is expected to produce the WPF Inno Setup installer and the Exchange Squirrel.Windows setup/update set.
- `.github/workflows/windows-release.yml` is the configured Windows build/package/publication route. It intentionally does not run tests or lint.
- `docs-site/build.bat /ci` is the build-only documentation-site route. It emits `docs-site/dist` for Sites and `docs-site/pages-dist` for GitHub Pages, but this source revision does not prove that either output has been published.

No current-candidate command result, GitHub Actions run, GitHub Release URL, final artifact name, digest, asset download, GitHub Pages URL, installer execution, test result, lint result, review, audit, or UI capture is asserted by this handoff.

## Required final-release evidence

1. Preserve the exact source revision selected for release and record its commit identifier.
2. Run the supported build and installer routes from that exact revision; retain the actual output paths, unsigned-state evidence, and hashes.
3. Publish through the configured Windows release route and read back the unique non-draft release, its target revision, asset list, nonzero files, hashes, and downloadability.
4. Build the documentation site with the matching source and publish `pages-dist` through a configured GitHub Pages route. Record the site URL only after it is live and verified.
5. Keep Exchange ISO media out of Git history and release assets. Runtime use must complete its Cheap LFS metadata and final-ISO validation first.

## Boundaries for the next owner

- Do not treat the previous recovery-only WPF artifact or historical local checks as evidence for the pending combined release.
- Do not claim code signing: both installer families are intentionally unsigned, and a signer invocation is a release failure.
- Do not widen managed OpenCode repair into arbitrary execution. YOLO mode must remain explicit, off by default, and constrained to the application-defined repair catalog.
- Do not attach or hydrate the Exchange ISO as part of publication. The runtime media flow owns its validated download/reassembly path.

The next owner should update this file only with concrete, source-revision-specific evidence after final build, publication, and read-back steps complete.
