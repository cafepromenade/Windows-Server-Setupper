# Roadmap

## Current milestone: resilient error recovery

The recovery hardening is implemented in the local working copy and is undergoing final review and integration. The latest completed local baseline reports:

- `PASS: 146 recovery checks` from the focused .NET Framework 4.7.2 executable;
- a successful fresh scratch compile of the primary WPF application against .NET Framework 4.7.2 reference assemblies at that baseline;
- matching SHA-256 values between the scratch build inputs and repository source at the time of that build.

Source review continued after that baseline. The integrating owner must rerun the checks, source-hash comparison, and WPF compile after the final source freeze; the final integrated count may be higher.

Implemented locally:

- version 3 durable per-operation state with atomic integrity records and protected per-machine storage;
- bounded, idempotency-aware automatic retries and explicit user retry generations;
- dependency-aware continuation that preserves completed independent steps;
- explicit two-outcome reconciliation for uncertain work;
- evidence-preserving, user-authorized recovery from corrupt checkpoints;
- Windows Job Object containment, timeout termination verification, and nonzero-exit handling;
- an accessible queued recovery card with persistent review, precise action labels, and focus restoration;
- cleanup-only recovery paths that do not replay completed server changes;
- pinned and protected Chocolatey staging with package, installer, and installed-version verification;
- disabled legacy server-role launch controls until credential-safe artifacts are rebuilt, published, and pinned.

Detailed behavior and verification commands are in [Error recovery and resumable operations](./Windows-Server-Tools/docs/reliability/error-recovery.md).

## Before integration is complete

- Commit the reviewed implementation and documentation with an exact source revision.
- Reconcile the local default branch with the current remote default branch without discarding either history.
- Re-run the focused recovery checks and fresh WPF compile on the integrated tree.
- Record the integrated revision and remote containment proof in repository issue #1.
- Confirm that no unrelated working-copy changes were included.

## Before a release

- Build the supported primary application artifact through the repository's release path.
- Exercise the recovery flows on a disposable Windows Server environment, including reboot continuation, role installation, network failure, timeout, indeterminate reconciliation, and cleanup-only retry.
- Publish and verify a real installer before adding an installer link to the README.
- Record the exact release revision, artifact digest, and remote workflow/release evidence.
- Keep the legacy Exchange/SCCM launch paths disabled until replacement artifacts are rebuilt without embedded credentials, published from a reviewed source revision, and pinned by immutable identity and digest.

## Later reliability work

- Extend real-server integration coverage to every supported server role and topology.
- Add packaged-artifact accessibility and narrow-window interaction evidence for every recovery state.
- Document operator playbooks for role-specific partial completion and rollback boundaries.

No commit, remote workflow, installer, or release is claimed by this roadmap entry.
