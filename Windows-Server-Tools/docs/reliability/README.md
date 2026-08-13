# Reliability

This category documents how Windows Server Tools preserves completed work, reports failures, and resumes unfinished operations without silently treating an error as success.

## Articles

- [Error recovery and resumable operations](./error-recovery.md) — durable state, bounded retries, process containment, user-reviewed reconciliation, accessible recovery controls, diagnostics, limitations, and verification.

## Current evidence boundary

The implementation described here is present in the local working copy and has the following completed baseline evidence:

- the focused recovery executable reports `PASS: 146 recovery checks`;
- a fresh scratch copy of the primary WPF project compiled successfully against .NET Framework 4.7.2 reference assemblies at that baseline;
- the scratch source files used for that build matched the repository source by SHA-256 at the time of the build.

Source review continued after that baseline. The focused checks, source-hash comparison, and WPF build must be rerun after the final source freeze; the final integrated count may increase as additional hardening checks are added.

This documentation does not claim a commit, default-branch integration, remote workflow result, installer, release, or deployment. Those milestones remain pending and must be recorded with their own evidence.
