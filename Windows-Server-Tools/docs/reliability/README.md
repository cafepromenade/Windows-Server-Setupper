# Reliability

This category documents how Windows Server Tools preserves completed work, reports failures, resumes unfinished operations without silently treating an error as success, and stops legacy callers when secure guided input is unavailable.

## Articles

- [Error recovery and resumable operations](./error-recovery.md) — durable state, bounded retries, process containment, user-reviewed reconciliation, process-wide operation coordination, trusted continuations, accessible recovery controls, diagnostics, limitations, and historical evidence.

## Current evidence boundary

The implementation described here is being prepared for an expedited release. Its intended contract is durable resumable recovery, truthful uncertain outcomes, protected recovery state, and coordination that allows only one server-changing operation at a time.

The following completed evidence is historical and belongs to an earlier source state:

- the focused recovery executable reports `PASS: 146 recovery checks`;
- a fresh scratch copy of the primary WPF project compiled successfully against .NET Framework 4.7.2 reference assemblies at that baseline;
- the scratch source files used for that build matched the repository source by SHA-256 at the time of the build.

Source edits continued after that baseline. The expedited release path intentionally skips tests, review passes, and UI captures after the current edits. The historical checks, source-hash comparison, and WPF build therefore do not verify the release candidate.

This documentation does not claim a current test result, review verdict, UI capture, integrated commit, installer, tagged release, installation result, or deployment. Bounded build, packaging, integration, and release milestones must be recorded with their own evidence when they occur.
