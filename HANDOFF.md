# Handoff: combined Windows release contract

## Status

The repository contains a local release-contract candidate that unifies the WPF and Exchange Auto Installer build/package routes. The workflow, root orchestration, dependency inventory, line counter, release publisher, safe output collector, and negative release-asset fixtures are implemented in the candidate.

This handoff does not claim that the candidate has been pushed, run by GitHub Actions, published as a combined release, installed, launched, or captured. Those states require their own evidence after final integration.

## Implemented release behavior

- `build.bat /s` requires exact committed source, clears only validated generated output, bootstraps the WPF and Node toolchains, restores locked dependencies, builds both runnable applications, records SHA-256 provenance, and restores only known MSBuild-generated tracked byproducts after starting from a clean checkout.
- `build-installer.bat /s` refuses tracked or staged source changes, builds the WPF installer, packages Exchange in a temporary isolated source copy, assigns a task-owned release version without changing tracked manifests, and reports both installer families.
- Squirrel verification requires exactly one setup executable, one `RELEASES` index, one full package, optional delta packages, one unpacked application executable, and `resources/app.asar`.
- Every `RELEASES` entry must name a present package whose byte count and SHA-1 match the index. Release evidence adds SHA-256 for every install/update artifact.
- Both setup executables and the unpacked Exchange application must have valid PE structure and no certificate table. Signer/certificate environment inputs are cleared, certificate auto-discovery is disabled, and the package log is rejected if it records a signer invocation.
- The workflow creates one unique tag/release per run and attempt, verifies the exact target, final non-draft state, asset count, nonzero files, hashes, and downloadability, and records server publication timing from the Actions job `started_at` value.
- The release links a verified public dim-sum photo without copying it. Catalog unavailability is a non-blocking decoration failure.
- Safe failure evidence is uploaded with `if: always()`, bounded retention, and nonmasking collection/upload steps.

## Local contract evidence

| Check | Current result |
| --- | --- |
| PowerShell parser over release scripts | Passed |
| Dependency inventory JSON parse | Passed |
| Positive release workflow contract | Passed |
| Deliberately broken release contract | `10/10` mutations turned red |
| Missing/corrupt Squirrel asset fixtures | `11/11` fixtures turned red |
| Git whitespace/error scan | Passed |
| Workflow structural lint | Passed with shell-content integration disabled on Windows |
| Root build and installer scripts | Pending final committed-candidate execution |
| GitHub Actions / release | Not run or claimed |

The workflow structural lint uses `actionlint -shellcheck=` on Windows because the installed shellcheck integration can deadlock on this platform. The workflow contains PowerShell and batch release steps; no shell test command was omitted from a quality gate because GitHub Actions intentionally runs no tests or lint.

## Negative fixtures

`scripts/test-release-assets.ps1` proves the package verifier rejects:

1. missing Setup executable;
2. missing `RELEASES`;
3. missing full package;
4. missing unpacked app executable;
5. missing `app.asar`;
6. malformed `RELEASES` syntax;
7. a missing index target;
8. an index byte-count mismatch;
9. an index SHA-1 mismatch;
10. a source-provenance mismatch;
11. a corrupt setup PE file.

`scripts/validate-release-contract.ps1 -SelfTest` proves ten independent release regressions turn red, including missing triggers, wrong runner, missing root installer route, a prohibited test command, missing always-on failure collection, a mutable action tag, missing release-download proof, missing Exchange package verification, removal of default-icon rejection, and removal of the manifest/icon-container verifier.

## External blockers and ownership boundaries

- A separately owned Cheap LFS lane is correcting stale Exchange ISO pointer inventory metadata. This lane does not hydrate, copy, upload, or attach the ISO.
- The WPF and Exchange application owners must supply and verify original application logos and packaged Windows icons. This release-contract lane does not alter app source or package manifests.
- The dedicated website lane owns GitHub Pages and repository homepage publication. The repository homepage field was empty during this audit; it was not changed here.
- Project issues #1 and #2 remain open. Their latest public permission comments are stale relative to the current CLI's live administrative repository access, but this lane does not edit issues.
- The open global-memory issue #10 concerns the shared Status Hub and is unrelated to this repository release contract.

## Next owner actions

1. Commit the final candidate and run both root scripts from that exact commit.
2. Correct any real build/package failure in the scripts rather than bypassing them.
3. Integrate the candidate into the default branch without rewriting or dropping commits.
4. Push once, monitor the exact workflow run, and verify the release and downloadable assets.
5. Update this handoff with the final commit, run URL, release URL, artifact names, hashes, and external blockers.

No code signing is permitted. A missing signing certificate is expected; any signer invocation is a release blocker.
