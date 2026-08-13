# Roadmap

## Current milestone: combined Windows release contract

The repository now defines one Windows-only delivery route for both the WPF application and the Exchange Auto Installer:

- `build.bat` bootstraps and builds both runnable applications from exact committed source;
- `build-installer.bat` produces the WPF Inno Setup installer and complete Exchange Squirrel.Windows setup/update set;
- `.github/workflows/windows-release.yml` runs on every branch push and manual dispatch, then publishes one unique non-draft release only after build, packaging, evidence assembly, and asset validation succeed;
- `scripts/release-dependencies.json` is the hand-written job/bootstrap inventory;
- `scripts/count-lines.ps1` produces release-pinned category and surviving-line attribution tables;
- safe output collection runs even after an earlier step fails, without uploading source, dependencies, caches, credentials, or the Exchange ISO.

The workflow performs no tests, lint, type checking, static analysis, accessibility checks, or screenshots. Local checks remain required work for each change, but their verdict is not a GitHub Actions release gate.

## Release evidence still required

- Run both root scripts from the final committed candidate and record their exact output.
- Verify both installers, the Squirrel.Windows update set, and every digest from the final candidate.
- Push the integrated default branch and observe a terminal GitHub Actions result.
- Read back the unique non-draft release, target commit, notes, timings, and every downloadable asset.
- Resolve the separately owned Cheap LFS pointer metadata correction without hydrating or attaching the ISO.
- Confirm original application logos and packaged multi-resolution Windows icons in the app/package owners' lanes.
- Capture the real packaged user interfaces through the approved hidden-desktop route; no current capture claim is made by this release-contract lane.

## Reliability and Exchange follow-up

- Exercise the recovery flows on a disposable Windows Server environment, including reboot continuation, role installation, network failure, timeout, indeterminate reconciliation, and cleanup-only retry.
- Verify the Exchange install plan against supported Windows Server and Exchange media in an isolated environment.
- Keep the large media flow resumable, cancellable, bounded, and cryptographically verified from Cheap LFS part metadata through the final ISO.
- Keep all credential and secret material out of source, process arguments, build logs, release evidence, and Git history.

No combined release, remote workflow result, installation result, or deployment is claimed by this roadmap until its external evidence exists.
