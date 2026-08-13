# Error recovery and resumable operations

Windows Server Tools treats a server-configuration workflow as a set of named operations rather than one all-or-nothing click. Each operation records its state before and after execution. Completed independent work is preserved, dependent work waits for its prerequisites, and an operation with an uncertain outcome is never replayed automatically.

## Durable recovery state

Recovery checkpoints use the `windows-server-tools-recovery-v3` format. The application stores protected workflow data below:

```text
%ProgramData%\Windows Server Tools
```

Recovery checkpoints are normally under the `Recovery` child directory, while durable completion and configuration records are under `State`.

Version 3 checkpoints contain:

- snapshot metadata with an exact UTC timestamp, unique snapshot identifier, and the last reviewed-request identity/digest;
- canonical, name-sorted operation records with state, attempt count, user-retry generation, update time, error type, and a bounded redacted error summary;
- a final commit record containing the record count and SHA-256 digest of the complete canonical payload.

The parser accepts strict UTF-8 without a byte-order mark, LF line endings, a final newline, no duplicate operation names, no unknown state names, and only canonical Base64 fields. Files are limited to 1 MiB, 1,024 records, and bounded line and field lengths.

The protected state root and files reject reparse points and path traversal. Their discretionary access control lists allow full control only to the local Administrators group and `SYSTEM`. Atomic writes use a write-through temporary file, parse the temporary snapshot before replacement, replace or move it into place, parse the committed file again, and remove temporary material when possible.

Recovery-state mutation is serialized through a protected exclusive lock file so only one batch can own the checkpoint at a time. The lock path is checked for exact location, owner, access rules, reparse state, and unsafe pre-existing objects. Acquisition is bounded; a second process does not wait indefinitely or merge state without owning the coordination boundary.

## States and restart behavior

The durable state machine uses these states:

| State | Meaning on restart |
| --- | --- |
| `pending` | The operation has not completed and may run when its dependencies permit it. |
| `running` | The previous process stopped before recording a final outcome. The result is uncertain and needs review. |
| `retrying` | An automatic retry was scheduled. It resumes only if the operation is declared idempotent and retry budget remains. |
| `failed` | The failure is retained and requires an explicit user-reviewed retry. |
| `succeeded` | The operation is preserved and skipped. |
| `blocked` | A dependency failed, is missing, or participates in a dependency cycle. The operation waits instead of being reported as successful. |
| `indeterminate` | The action may have taken effect, but its final outcome or process-tree termination could not be proven. It needs reconciliation. |

Operations are topologically ordered by declared dependencies. A failed operation does not prevent unrelated operations from continuing, but any dependent operation reports exactly which prerequisites are still waiting.

An old `windows-server-tools-recovery-v2` checkpoint is deliberately not trusted or migrated. It is treated as unsupported recovery evidence so that an operation is not replayed from state that lacks the version 3 integrity record.

## Automatic retry and idempotency

Every operation has an explicit maximum attempt count and retry-safety classification:

- `SingleAttempt` operations receive no automatic replay;
- `Idempotent` operations may retry within their bounded attempt budget;
- transient failures retry with exponential delay beginning at 600 ms;
- permanent, exhausted, or uncertain failures stop automatic execution and surface an explicit recovery action.

An attempt is recorded as `running` before its action begins. A success is not reported until `succeeded` has been written. If persistence fails after an action starts or completes, the result becomes uncertain rather than being reported as success.

The user's Retry action begins a new generation. It compares the displayed expected state, generation, and attempt with the current checkpoint before changing anything, so a stale recovery card cannot reset newer state.

## Atomic reviewed retry and uncertain outcomes

The recovery card prepares all selected state changes in one transaction. `PrepareReviewedRetry` first validates every expected operation record, then applies all transitions to a cloned snapshot and writes that snapshot atomically. A bounded request identifier and SHA-256 preparation digest make a repeated request idempotent only when it describes the same transitions and those transitions are already present.

An indeterminate action presents two distinct choices:

1. **It completed — continue** records `ConfirmedSucceeded`. The action is marked succeeded and is not replayed; remaining incomplete work may continue.
2. **Stopped without completing — retry** records `ConfirmedNotAppliedAndStopped`. This choice is valid only after confirming that the process is no longer running and that the action did not complete or apply. It resets only the reviewed action to `pending` in a new generation.

When several outcomes are uncertain, the application reviews them one at a time. It does not apply one answer to every uncertain action.

The recovery UI carries the exact evidence or reconciliation token shown to the user into the requested state transition. Reconciliation tokens are protected for the local machine with DPAPI before durable storage. A stale card therefore cannot silently reconcile evidence that changed after it was displayed.

## Corrupt-state recovery

Checkpoint loading is fail-closed. The primary file is authoritative when it is valid. If any discovered primary, temporary, or backup candidate is invalid, or if there is no unambiguous valid candidate, the loader creates a corruption marker and blocks all workflow replay.

The marker carries a unique evidence identifier plus a digest over the current marker and all discovered recovery candidates. The UI offers an explicit repair action; repair is never automatic.

Repair performs the following sequence:

1. compare the current evidence token with the token shown to the user;
2. enumerate every recovery candidate and marker under explicit count and size bounds;
3. hash, copy, re-hash, and verify each item with bounded streaming into a uniquely named archive directory, then write a manifest;
4. confirm the live evidence token did not change during archival;
5. create and parse a new, empty version 3 checkpoint;
6. remove non-primary live candidates and the marker only after the empty checkpoint is verified.

This keeps the old bytes available for diagnosis before recovery knowledge is reset. After repair, the workflow starts from an empty checkpoint only because the user explicitly authorized that reset.

## External-process result semantics

External commands run with shell execution disabled, standard output and standard error redirected, and an explicit timeout. Output is drained concurrently and bounded before it is exposed in diagnostics. A nonzero exit code is a failure even when the process printed no error text.

Each process is created suspended through `CreateProcessW` with an explicit inherited-handle allowlist. It is assigned to a Windows Job Object configured with kill-on-close behavior before its primary thread is resumed. If creation, handle restriction, Job Object assignment, or resumption cannot be proven safe, the application fails closed and attempts confirmed termination instead of allowing an uncontained process to continue. Completion waits for both the initial process and the Job Object's active process count to reach zero, so a launcher process cannot report success while a child continues in the background.

On timeout, the application terminates the Job Object and spends up to 10 seconds confirming that the process tree is empty:

- confirmed termination produces a normal timed-out failure that may be retried only under the operation's retry policy;
- unconfirmed termination produces an indeterminate result that cannot be replayed until the user reconciles it.

If Job Object assignment or termination confirmation fails, the application reports an indeterminate result rather than continuing without containment. A timeout while waiting for the Job Object to empty, an unproven process exit, or an output-drain fault also remains indeterminate unless both exit and termination are proven.

Command scripts are streamed through the trusted `%SystemRoot%\System32\cmd.exe` process by standard input instead of being written to a user-writable temporary command file. The generated wrapper rejects unsupported multiline control syntax, checks `errorlevel` after every command, and exits at the first failing line.

Sensitive process input uses a character buffer that is cleared in a `finally` block. Output capture is suppressed for that path so credentials cannot reappear through ordinary process-result diagnostics.

## Recovery card and accessibility

Routine failures stay inside the application as a non-blocking recovery card. Each pending request retains its own operation key, retry delegate, optional completed-action delegate, focus origin, and sensitive-resource lifetime.

The card provides:

- a queue position and Previous/Next controls when more than one action needs attention;
- an action-specific Retry label rather than a generic retry button;
- separate completed and stopped-without-completing choices for uncertain operations;
- **Open error log**, **Dismiss**, and a persistent **Review pending actions** control;
- an assertive live region for changed recovery details;
- programmatic names and help text that explain why an action is disabled or already running;
- focus return to the control that started the action, with a safe fallback when that control is unavailable;
- wrapping and scrollable content for narrow windows and long error text.

Retry controls are disabled while their exact operation is already running. The card changes to a running state and announces it, then restores the recovery choice if the action fails again. A failed retry remains in the queue; it is removed only when its delegate returns a proven success result.

Successful retry and reconciliation actions return focus to a valid recovery or originating control. Refreshing the Simpsons action while it runs preserves the running state, and the initial-setup status copy is cleared when it no longer describes an active operation.

## Server-operation coordination and trusted continuations

A process-wide coordinator permits only one server-changing operation at a time across initial setup, Active Directory, Simpsons, Chocolatey, Windows feature installation, IIS configuration, and storage configuration. Competing controls provide non-modal busy text that names the active operation and become available again when it stops; the app does not queue an overlapping server mutation behind the user's back.

Utilities used by these paths resolve through explicit files below the trusted System32 directory, including `csvde.exe` and `control.exe`, rather than through ambient command lookup. Reboot-continuation directories and files must resolve to their exact expected locations and pass object-type, owner, access-rule, and reparse-point validation. Those checks run both before and after replacement so a changed continuation path fails closed instead of being scheduled.

## Completion and cleanup-only recovery

Completion markers are written only after every required server action succeeds. Checkpoint deletion is also guarded: `ClearCheckpoint` refuses to remove corrupt state or any record that is not `succeeded`.

If all server actions completed but deleting the completed checkpoint fails, the UI creates a cleanup-only recovery action. Retrying that action removes only the completed recovery state; it does not repeat server changes. The directory/user/share workflow follows the same rule when removing its scheduled continuation and completed checkpoint.

## Chocolatey installation boundary

Chocolatey installation uses a fixed package and fixed expected version:

| Item | Value |
| --- | --- |
| Package | Chocolatey `2.7.3` release package |
| Package URL | `https://github.com/chocolatey/choco/releases/download/2.7.3/chocolatey.2.7.3.nupkg` |
| Package SHA-256 | `40778CC59245B3EB6EA5147AEEF5BEA5D577419E5ABCE22A224189740DC16DB5` |
| Installer SHA-256 | `C46903CFED1D74620630D0653CE057B3079AF5789AFEB1A5F884298A8693B4EC` |
| Installed executable SHA-256 | `4A1C6CF52929DD0348F5C91CE2A69A7D35A06A4C143957F42D855756DA4AF510` |
| Required installed version | `2.7.3` |

The package is downloaded into a unique, ownership-marked directory below:

```text
%ProgramData%\WindowsServerToolsSecureStaging
```

The staging root and attempt directory are protected for Administrators and `SYSTEM`, must not be reparse points, and are revalidated before execution. The download requires an HTTPS final address, allows at most five redirects, has a ten-minute total deadline, and is limited to 64 MiB. Extraction rejects traversal and symbolic-link entries and is bounded to 4,096 entries and 512 MiB expanded data.

Every existing per-machine path component is checked for its object type, owner, access rules, and reparse state before use. Both the package and extracted installer are hashed, and verified read handles remain open while the installer starts. The installed executable is rechecked, opened with a retained read handle, hashed against its pinned digest, and kept protected from replacement while its version is queried. On failure, bounded evidence remains in the protected attempt directory. Successful installation removes only the attempt directory whose ownership marker matches the current attempt.

The server-role launch controls that lead to the legacy Exchange/SCCM delivery chain—including **Promote to Omega Server**, **SCCM Only**, and **Install Side Server**—are reachable. Their call chains propagate `false` when a required step stops, so a caller does not continue into later setup. Embedded Active Directory, SQL, and mail-relay credentials have been removed from these routes; without secure guided credential input, the affected operation fails closed instead of advancing.

## Diagnostics

The recovery log is stored at:

```text
%LOCALAPPDATA%\Windows-Server-Tools\Logs\recovery.log
```

Diagnostic summaries redact URI user information, Basic/Bearer authorization values, password/token/secret assignments, the current-user profile path, and the temporary-directory path. Individual summaries and captured output are bounded. The log rotates at 1 MiB and keeps three generations in total: the current file plus two rotated files. Logging errors are swallowed so a diagnostic write cannot become a second application failure.

The recovery card's **Open error log** action opens this file when available. For protected Chocolatey staging failures, `failure.txt` records the phase and expected/observed hashes without recording credentials.

## Failure modes and recovery actions

| What the user sees | Meaning | Safe next action |
| --- | --- | --- |
| A normal failed action with Retry | The last attempt has a definite failure result. | Correct the reported condition, review the log, then Retry. |
| An uncertain action with two choices | The app cannot prove whether the action applied or whether its process tree stopped. | Inspect the real server state, then choose exactly one truthful outcome. |
| Corrupt recovery state | No candidate checkpoint can be trusted unambiguously. | Preserve external evidence if required, then use the explicit repair action to archive the app's evidence and start with empty recovery state. |
| Waiting for another operation | A declared dependency did not succeed. | Resolve the named prerequisite; the dependent action will not run early. |
| Cleanup incomplete | All server actions completed, but checkpoint/task cleanup failed. | Retry cleanup; no completed server action will be replayed. |
| Another recovery batch is active | Another process owns the bounded recovery-state coordination boundary. | Let the owning batch finish, then retry. Do not delete the checkpoint. |

## Limitations and security considerations

- This is a Windows-only administrative application; the recovery engine cannot make an inherently destructive server action reversible.
- Idempotency is declared per operation. Automatic retry is safe only to the extent that the underlying server command honors that contract.
- Human reconciliation is intentionally required when an external result is uncertain. Guessing the outcome can either repeat a completed action or skip an incomplete one.
- Explicit corrupt-state repair archives evidence and then resets the application's recovery knowledge. It does not prove whether external server actions had already occurred.
- Version 2 checkpoints are unsupported by design and require evidence-preserving repair rather than silent migration.
- The legacy Exchange/SCCM controls are reachable, but the routes are not deployable without secure guided credential input and pinned replacement artifacts. The current behavior fails closed rather than restoring embedded credentials or advancing after a stopped result.
- The local verification below does not replace testing on a disposable Windows Server environment with the intended roles, network topology, reboot behavior, and administrative policy.
- No current installer, release, remote workflow, deployment, or code-signing result is asserted by this article.

## Historical local verification (not current)

The focused executable links the production `Recovery.cs` into a small .NET Framework 4.7.2 test project. The following commands were run against an earlier source state:

```powershell
$referenceAssemblies = Join-Path $env:TEMP 'windows-server-tools-recovery-build\reference-assemblies\build\.NETFramework\v4.7.2'

dotnet msbuild '.\Windows-Server-Tools\Windows-Server-Tools.Tests\Windows-Server-Tools.Tests.csproj' `
  /t:Build `
  /p:Configuration=Release `
  /p:FrameworkPathOverride="$referenceAssemblies" `
  /nologo `
  /v:minimal

& '.\Windows-Server-Tools\Windows-Server-Tools.Tests\bin\Release\Windows-Server-Tools.Tests.exe'
```

Result:

```text
PASS: 146 recovery checks
```

This is a historical baseline, not a result for the current edits. Source changes continued afterward. The expedited release path intentionally skips tests, review passes, and UI captures after those edits, so this count must not be attributed to the release candidate.

The checks cover the version 3 checkpoint contract, restart behavior, idempotent and single-attempt retry policy, explicit retry generations, atomic reviewed reconciliation, corrupt-state archival, concurrency leases, dependency ordering, Job Object process semantics, timeout outcomes, diagnostic redaction, completion cleanup, source wiring, and recovery-card accessibility contracts.

The host did not have the .NET Framework 4.7.2 developer targeting pack installed. A session-owned scratch copy of the primary WPF source was therefore built with the reference assemblies above and a scratch `BuildProbe.csproj`. The exact build command was:

```powershell
$referenceAssemblies = Join-Path $env:TEMP 'windows-server-tools-recovery-build\reference-assemblies\build\.NETFramework\v4.7.2'
$scratchProject = Join-Path $env:TEMP 'windows-server-tools-recovery-wpf-20260812-2110\Windows-Server-Tools\BuildProbe.csproj'

dotnet msbuild $scratchProject `
  /t:Rebuild `
  /p:Configuration=Release `
  /p:FrameworkPathOverride="$referenceAssemblies" `
  /nologo `
  /v:minimal
```

That historical scratch build succeeded and produced `Windows-Server-Tools.exe`. SHA-256 comparison confirmed that the copied `Recovery.cs`, WPF XAML/code-behind, `Functions.cs`, and `App.xaml.cs` matched the repository files at the time of that build. They do not match a proven current candidate by this article. A later standard verification pass should repeat the focused checks, source-hash comparison, build, independent review, and packaged UI capture against the exact released revision. The scratch project and downloaded reference assemblies are test-environment material outside the repository; they are evidence for that historical compile only, not a shipped build path.

See the [reliability index](./README.md) for the current evidence boundary.
