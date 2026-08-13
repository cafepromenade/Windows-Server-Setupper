# Handoff: error-recovery hardening

## Status

The error-recovery implementation is present in the current task working copy and is being prepared for an expedited release. A completed historical verification baseline exists, but source edits continued afterward. The expedited release path intentionally skips tests, review passes, and UI captures after the current edits.

Default-branch reconciliation, commit creation, remote publication, packaging, and tagged-release evidence remain pending at this handoff point. This handoff deliberately makes no claim that the current source is tested, reviewed, captured, committed, pushed, released, installed, or deployed.

## Implemented behavior

- Durable `windows-server-tools-recovery-v3` checkpoints preserve completed work and distinguish pending, running, retrying, failed, succeeded, blocked, and indeterminate states.
- Checkpoints live in protected per-machine application data, use bounded canonical records plus a SHA-256 commit record, validate owner/access rules/reparse state and pre-existing objects, and allow only one recovery-state mutation batch through a protected exclusive lock file.
- Automatic retries are both bounded and limited to operations declared idempotent. Explicit retry uses expected state/generation/attempt comparisons so stale UI actions cannot reset newer state.
- User-reviewed reconciliation is atomic, uses machine-protected evidence tokens, and supports two separate uncertain outcomes: confirmed succeeded, or confirmed stopped without completing/applying.
- Corrupt state blocks replay. Its explicit repair path enumerates, hashes, copies, and verifies evidence with bounded streaming before creating an empty checkpoint.
- External processes are created suspended with an explicit inherited-handle allowlist, assigned to a kill-on-close Job Object, and resumed only after containment succeeds. Assignment or termination failures fail closed; timeout, job-empty, or output-drain faults become indeterminate unless exit and termination are proven.
- The WPF recovery card retains multiple pending actions, precise Retry/completed choices, a persistent review route, accessible live announcements, explanatory disabled states, and focus restoration after retry and reconciliation actions.
- A process-wide coordinator allows only one server-changing operation across initial setup, Active Directory, Simpsons, Chocolatey, Windows features, IIS, and storage. Competing actions show non-modal busy feedback and return when the active operation stops.
- System utilities resolve through explicit trusted System32 paths. Reboot-continuation parents and files are checked for exact location, object type, owner, access rules, and reparse state before and after replacement.
- Completion cleanup is independently retryable and does not repeat completed server actions.
- Chocolatey 2.7.3 installation validates path type, owner, access rules, and reparse state; verifies pinned package, installer, and installed-executable digests; retains verified read handles through launch; and checks the installed version.
- Legacy server-role controls that reach the Exchange/SCCM delivery chain are reachable. Their callers propagate stopped results and fail closed without advancing when secure guided credential input is unavailable; the removed embedded-credential behavior is not used.

The detailed contract is in [Error recovery and resumable operations](./Windows-Server-Tools/docs/reliability/error-recovery.md).

## Historical local evidence

| Verification | Result |
| --- | --- |
| Focused recovery executable | Historical baseline: `PASS: 146 recovery checks`; not rerun for the current edits |
| Fresh primary WPF scratch build | Historical baseline produced `Windows-Server-Tools.exe`; not a build of the current edits |
| Scratch/source identity | SHA-256 matched selected source files at the historical baseline only |
| Targeting environment | Scratch .NET Framework 4.7.2 reference assemblies because the host developer targeting pack was unavailable |
| Post-edit review and UI capture | Intentionally not run under the expedited release path |

Source edits continued after the 146-check baseline. These results must not be used as evidence for the release candidate. A later standard verification pass should rerun the checks, source-hash comparison, build, independent review, and packaged UI capture against the exact released revision.

Reproducible commands and the exact evidence boundary are recorded in the [verification section](./Windows-Server-Tools/docs/reliability/error-recovery.md#local-verification).

## Primary implementation files

```text
Windows-Server-Tools/Windows-Server-Tools/Recovery.cs
Windows-Server-Tools/Windows-Server-Tools/App.xaml.cs
Windows-Server-Tools/Windows-Server-Tools/Functions.cs
Windows-Server-Tools/Windows-Server-Tools/MainWindow.xaml
Windows-Server-Tools/Windows-Server-Tools/MainWindow.xaml.cs
Windows-Server-Tools/Windows-Server-Tools/CommonlyInstalledWindowsComponents.xaml
Windows-Server-Tools/Windows-Server-Tools/CommonlyInstalledWindowsComponents.xaml.cs
Windows-Server-Tools/Windows-Server-Tools.Tests/Program.cs
Windows-Server-Tools/Windows-Server-Tools.Tests/Windows-Server-Tools.Tests.csproj
```

The solution file and primary WPF project file also include the focused test project and recovery source. Separate legacy Exchange/SCCM source files contain scoped fail-closed caller changes in the same candidate; they have not received a post-edit review pass under the expedited release path.

## Integration checklist

- [ ] Preserve unrelated changes and include only the scoped error-recovery implementation and records.
- [ ] Commit the intended implementation and directly related records with an exact revision.
- [ ] Reconcile with the current remote default branch without rewriting or dropping commits.
- [ ] Build and package the exact committed candidate through the supported release path.
- [ ] Verify the remote default branch contains the intended revision.
- [ ] Publish one unique non-draft tagged release and verify its expected downloadable assets are present and nonempty.
- [ ] Record the exact revision, changed files, build/package route, release tag, and asset details in repository issue #1.
- [ ] State explicitly that post-edit tests, review passes, and UI captures were intentionally skipped.
- [ ] Keep issue #1 open until integration and the bounded release evidence are recorded.
- [ ] Keep legacy role callers fail-closed until secure guided credential input and immutable artifact pins exist; do not restore embedded credentials or advance after a stopped result.

## Build and installer route

- `build.bat /s` restores dependencies and builds the primary Release executable.
- `build-installer.bat /s` calls the build script, installs canonical Inno Setup through `winget` when missing, and compiles `packaging/WindowsServerTools.iss`.
- The installer output is `Windows-Server-Tools/Windows-Server-Tools/bin/Installer/WindowsServerTools-Setup-<commit>.exe`.
- The installer is intentionally unsigned. The script requires `NotSigned` and prints the output size, SHA-256 digest, and source commit before reporting success.
- These scripts produce the candidate artifact; they do not publish it or prove installation/runtime behavior.

## Operational notes

- Recovery state is under `%ProgramData%\Windows Server Tools`; logs are under `%LOCALAPPDATA%\Windows-Server-Tools\Logs`.
- An indeterminate operation is a decision point, not an ordinary Retry. Inspect the server before selecting either outcome.
- Corrupt-state repair resets application recovery knowledge only after archiving evidence; it does not establish the actual server state.
- A cleanup-only failure should never be replaced with a full workflow replay.
- The historical local checks and scratch compile do not verify the current source and do not substitute for disposable Windows Server integration testing or a packaged installer test.

## Outstanding external evidence

- Exact integrated commit: pending.
- Remote default-branch proof: pending.
- Build/package result for the integrated revision: pending.
- Installer and artifact digest: not available for the current local changes.
- Release and deployment: not claimed.
- Post-edit tests, independent review, and UI captures: intentionally not run under the expedited release path.
