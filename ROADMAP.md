# Roadmap

## Current milestone: resilient error recovery

The recovery hardening is being prepared for an expedited release. The current implementation is intended to keep completed work, resume unfinished work, report uncertain outcomes honestly, protect recovery state, and prevent overlapping server-changing operations.

The latest completed local evidence is historical and belongs to an earlier source state:

- `PASS: 146 recovery checks` from the focused .NET Framework 4.7.2 executable;
- a successful fresh scratch compile of the primary WPF application against .NET Framework 4.7.2 reference assemblies at that baseline;
- matching SHA-256 values between the scratch build inputs and repository source at the time of that build.

Source edits continued after that baseline. The expedited release path intentionally skips tests, review passes, and UI captures after those edits, so the historical results must not be presented as verification of the release candidate.

Implemented locally:

- version 3 durable per-operation state with atomic integrity records and protected per-machine storage;
- bounded, idempotency-aware automatic retries and explicit user retry generations;
- dependency-aware continuation that preserves completed independent steps;
- explicit two-outcome reconciliation for uncertain work;
- evidence-preserving, user-authorized recovery from corrupt checkpoints;
- Windows Job Object containment, timeout termination verification, and nonzero-exit handling;
- an accessible queued recovery card with persistent review, precise action labels, and focus restoration;
- process-wide single-operation coordination across initial setup, Active Directory, Simpsons, Chocolatey, Windows features, IIS, and storage actions, with non-modal busy feedback;
- explicit trusted System32 utility resolution and owner/access-rule/reparse validation for staged reboot-continuation paths;
- cleanup-only recovery paths that do not replay completed server changes;
- pinned and protected Chocolatey staging with package, installer, and installed-version verification;
- fail-closed legacy Exchange/SCCM call chains that propagate stopped results and do not fall back to embedded credentials when secure guided input is unavailable.

Detailed behavior and verification commands are in [Error recovery and resumable operations](./Windows-Server-Tools/docs/reliability/error-recovery.md).

## Expedited integration and release

- Commit the scoped implementation and directly related documentation with an exact source revision.
- Reconcile the local default branch with the current remote default branch without discarding either history.
- Build and package the exact committed candidate through the repository's supported release path.
- Record the integrated revision, remote containment proof, release tag, and downloadable artifact details in repository issue #1.
- Confirm that no unrelated working-copy changes were included.
- State in release records that no tests, review passes, or UI captures were run after the current edits.

## Deferred verification after the expedited release

- Exercise the recovery flows on a disposable Windows Server environment, including reboot continuation, role installation, network failure, timeout, indeterminate reconciliation, and cleanup-only retry.
- Re-run the focused recovery executable and a clean primary WPF build against the released revision.
- Perform an independent source review and capture the recovery UI from the released build.
- Compare the released artifact digest with a reproducible local package when that evidence is available.
- Do not describe the reachable legacy Exchange/SCCM paths as deployable until secure guided credential input and pinned replacement artifacts exist; their current contract is to fail closed without advancing the caller.

## Later reliability work

- Extend real-server integration coverage to every supported server role and topology.
- Add packaged-artifact accessibility and narrow-window interaction evidence for every recovery state.
- Document operator playbooks for role-specific partial completion and rollback boundaries.

No integrated commit, installer, tagged release, post-edit test result, review verdict, or current UI capture is claimed by this roadmap entry.
