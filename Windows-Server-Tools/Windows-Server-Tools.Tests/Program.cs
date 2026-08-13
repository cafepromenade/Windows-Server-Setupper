using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows_Server_Tools;

namespace Windows_Server_Tools.Tests
{
    internal static class Program
    {
        private static readonly List<string> Failures = new List<string>();
        private static int _checks;

        private static int Main(string[] args)
        {
            if (args != null && args.Length == 1 && args[0] == "--hold-inherited-pipes")
            {
                Console.Out.WriteLine("descendant-ready");
                Console.Error.WriteLine("descendant-error-ready");
                Thread.Sleep(TimeSpan.FromSeconds(30));
                return 0;
            }

            if (args != null && args.Length == 1 && args[0] == "--spawn-pipe-descendant")
            {
                Thread.Sleep(200);
                string executable = System.Reflection.Assembly.GetExecutingAssembly().Location;
                using (Process.Start(new ProcessStartInfo(executable, "--hold-inherited-pipes")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                }))
                {
                }
                return 0;
            }

            try
            {
                Run().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Failures.Add("test harness crashed: " + ex);
            }

            if (Failures.Count == 0)
            {
                Console.WriteLine("PASS: " + _checks + " recovery checks");
                return 0;
            }

            foreach (string failure in Failures)
            {
                Console.Error.WriteLine("FAIL: " + failure);
            }

            Console.Error.WriteLine("FAILED: " + Failures.Count + " of " + _checks + " recovery checks");
            return 1;
        }

        private static async Task Run()
        {
            await RetriesOnlyExplicitlyIdempotentWork();
            await PreservesDeterministicErrorAndFriendlyMessage();
            await OrdersForwardDependenciesAndContinuesIndependentWork();
            await DistinguishesMissingDependenciesAndCycles();
            await RestartResumesAndExplicitUserRetryStartsNewGeneration();
            await StaleUserRetryTokenCannotResetNewerFailure();
            await IndeterminateTimeoutRequiresExplicitReconciliation();
            await GenericTimeoutRequiresExplicitReconciliation();
            await IndeterminateOutcomeBlocksLaterIndependentMutations();
            await InterruptedRunningStateRequiresExplicitReconciliation();
            await InterruptedAutomaticRetryHonorsCurrentPolicyAndBudget();
            await CorruptStateBlocksEveryReplay();
            await SnapshotV3RejectsTruncationTamperAndLegacyState();
            await CorruptStateRepairPreservesEvidenceAndRequiresCurrentToken();
            await ReviewedRetryPreparationIsAtomicAndIdempotent();
            await NewerCorruptStateCannotBeOverriddenByStaleValidBackup();
            await CorruptPrimaryCannotBeOverriddenByFutureDatedCandidate();
            await ValidPrimaryRemainsAuthoritativeOverFutureDatedBackup();
            await ValidPrimaryQuarantinesInvalidTransientResidue();
            await RejectsMalformedCheckpointSemantics();
            await PersistenceFailureBlocksBeforeAction();
            await SuccessPersistenceFailureIsIndeterminate();
            await ConcurrentSamePathBatchesAreSerializedWithoutLostRecords();
            await CompetingLeaseFailsQuicklyThenRetrySucceeds();
            LeasePathIsStableAcrossSessions();
            await RecoversAValidInterruptedTemporarySnapshot();
            await RedactsPersistedAndLoggedErrorSummaries();
            RedactsQuotedAuthorizationConnectionAndMultilineSecrets();
            await RunsExternalProcessesWithBoundedHonestOutcomes();
            await StreamsAndClearsStandardInput();
            await CommandScriptsFailFast();
            await CommandScriptsIgnorePoisonedComSpec();
            await CompletionStateClearsOnlyWhenRequested();
            ProtectedWorkflowStateRejectsUnsafePaths();
            ClassifiesRecoverableAndFatalExceptions();
            ParsesCommandNamesSafely();
            DestructiveAuthorizationRequiresBothKeysFullRangeAndOneClaim();
            ChecksUiRecoverySourceContracts();
            ChecksWpfDependencyContracts();
            ChecksWpfLogoContracts();
            await ChecksAutomaticUpdateContracts();
            ChecksCustomLogoContracts();
        }

        private static async Task RetriesOnlyExplicitlyIdempotentWork()
        {
            int safeAttempts = 0;
            OperationResult safe = await RecoveryRunner.RunAsync(
                "idempotent network step",
                () =>
                {
                    safeAttempts++;
                    if (safeAttempts == 1)
                    {
                        throw new WebException("temporary refusal", WebExceptionStatus.ConnectFailure);
                    }

                    return Task.CompletedTask;
                },
                maxAttempts: 3,
                retryDelay: TimeSpan.Zero,
                retrySafety: RetrySafety.Idempotent);

            int unsafeAttempts = 0;
            OperationResult unsafeResult = await RecoveryRunner.RunAsync(
                "unsafe network step",
                () =>
                {
                    unsafeAttempts++;
                    throw new IOException("outcome may be ambiguous");
                },
                maxAttempts: 3,
                retryDelay: TimeSpan.Zero);

            Check(safe.Succeeded && safe.Attempts == 2 && safeAttempts == 2,
                "explicitly idempotent transient work should retry and recover");
            Check(!unsafeResult.Succeeded && unsafeResult.Attempts == 1 && unsafeAttempts == 1,
                "unsafe work should remain single-attempt even when a larger budget is supplied");
        }

        private static async Task PreservesDeterministicErrorAndFriendlyMessage()
        {
            var expected = new InvalidOperationException("invalid configuration: port 44");
            OperationResult result = await RecoveryRunner.RunAsync(
                "deterministic step",
                () => { throw expected; },
                maxAttempts: 4,
                retryDelay: TimeSpan.Zero,
                retrySafety: RetrySafety.Idempotent);

            Check(ReferenceEquals(result.Error, expected), "the exact deterministic exception should be preserved");
            Check(RecoveryRunner.FriendlyMessage(result.Error) == "invalid configuration: port 44",
                "the friendly message should retain an actionable deterministic error");
            Check(result.Attempts == 1, "a deterministic failure should not consume transient retries");
        }

        private static async Task OrdersForwardDependenciesAndContinuesIndependentWork()
        {
            var executed = new List<string>();
            OperationBatchResult result = await RecoveryRunner.RunAllAsync(new[]
            {
                new RecoverableOperation("third", () => Record(executed, "third"), dependencies: new[] { "second" }),
                new RecoverableOperation("failed", () =>
                {
                    executed.Add("failed");
                    throw new InvalidOperationException("expected failure");
                }),
                new RecoverableOperation("independent", () => Record(executed, "independent")),
                new RecoverableOperation("second", () => Record(executed, "second"), dependencies: new[] { "first" }),
                new RecoverableOperation("first", () => Record(executed, "first")),
                new RecoverableOperation("blocked", () => Record(executed, "blocked"), dependencies: new[] { "failed" })
            });

            Check(executed.IndexOf("first") < executed.IndexOf("second")
                && executed.IndexOf("second") < executed.IndexOf("third"),
                "forward-declared dependencies should execute in topological order");
            Check(executed.Contains("independent"), "independent work should continue after a sibling failure");
            Check(!executed.Contains("blocked") && result.Results.Single(item => item.Name == "blocked").Blocked,
                "a failed dependency should block only its dependent operation");
        }

        private static async Task DistinguishesMissingDependenciesAndCycles()
        {
            OperationBatchResult result = await RecoveryRunner.RunAllAsync(new[]
            {
                new RecoverableOperation("missing", () => Task.CompletedTask, dependencies: new[] { "not-declared" }),
                new RecoverableOperation("cycle-a", () => Task.CompletedTask, dependencies: new[] { "cycle-b" }),
                new RecoverableOperation("cycle-b", () => Task.CompletedTask, dependencies: new[] { "cycle-a" }),
                new RecoverableOperation("healthy", () => Task.CompletedTask)
            });

            Check(result.Results.Single(item => item.Name == "missing").Error is MissingOperationDependencyException,
                "a missing dependency should have a distinct diagnostic");
            Check(result.Results.Single(item => item.Name == "cycle-a").Error is OperationDependencyCycleException
                && result.Results.Single(item => item.Name == "cycle-b").Error is OperationDependencyCycleException,
                "dependency cycles should have a distinct diagnostic");
            Check(result.Results.Single(item => item.Name == "healthy").Succeeded,
                "dependency preflight failures should not stop unrelated work");
        }

        private static async Task RestartResumesAndExplicitUserRetryStartsNewGeneration()
        {
            string directory = NewTemporaryDirectory();
            string stateFile = Path.Combine(directory, "resume.steps");
            int completedRuns = 0;
            int failedRuns = 0;

            await RecoveryRunner.RunAllAsync(new[]
            {
                new RecoverableOperation("completed", () => { completedRuns++; return Task.CompletedTask; }),
                new RecoverableOperation("failed", () =>
                {
                    failedRuns++;
                    throw new InvalidOperationException("review required");
                }, maxAttempts: 3)
            }, stateFile);

            OperationBatchResult exhausted = await RecoveryRunner.RunAllAsync(new[]
            {
                new RecoverableOperation("completed", () => { completedRuns++; return Task.CompletedTask; }),
                new RecoverableOperation("failed", () => { failedRuns++; return Task.CompletedTask; }, maxAttempts: 3)
            }, stateFile);

            Check(completedRuns == 1 && exhausted.Results.Single(item => item.Name == "completed").Resumed,
                "restart should preserve completed work");
            Check(failedRuns == 1 && exhausted.Results.Single(item => item.Name == "failed").Blocked,
                "restart should not silently replenish an exhausted automatic retry budget");
            Check(RecoveryRunner.ResetForUserRetry(stateFile, "failed", expectedGeneration: 0, expectedAttempt: 1),
                "an explicit user retry should durably reset the operation budget");

            OperationBatchResult retried = await RecoveryRunner.RunAllAsync(new[]
            {
                new RecoverableOperation("completed", () => { completedRuns++; return Task.CompletedTask; }),
                new RecoverableOperation("failed", () => { failedRuns++; return Task.CompletedTask; }, maxAttempts: 3)
            }, stateFile);
            OperationResult retriedOperation = retried.Results.Single(item => item.Name == "failed");
            Check(retriedOperation.Succeeded && retriedOperation.UserRetryGeneration == 1 && failedRuns == 2,
                "an explicit user retry should run in a new generation without replaying completed work");
        }

        private static async Task IndeterminateTimeoutRequiresExplicitReconciliation()
        {
            string directory = NewTemporaryDirectory();
            string stateFile = Path.Combine(directory, "indeterminate.steps");
            int runs = 0;

            OperationBatchResult first = await RecoveryRunner.RunAllAsync(new[]
            {
                new RecoverableOperation(
                    "unconfirmed-timeout",
                    () =>
                    {
                        runs++;
                        throw new ExternalProcessException(
                            "unconfirmed-timeout",
                            -1,
                            string.Empty,
                            string.Empty,
                            timedOut: true,
                            terminationConfirmed: false,
                            innerException: null);
                    },
                    maxAttempts: 3,
                    retrySafety: RetrySafety.Idempotent)
            }, stateFile);

            OperationResult firstResult = first.Results.Single();
            Check(runs == 1 && firstResult.Blocked && firstResult.Indeterminate,
                "an unconfirmed timeout should be durably marked indeterminate");
            Check(File.ReadAllLines(stateFile).Skip(2).Single(line => line.StartsWith("indeterminate|", StringComparison.Ordinal)).StartsWith("indeterminate|", StringComparison.Ordinal),
                "the checkpoint should distinguish an indeterminate outcome from an ordinary failure");
            Check(!RecoveryRunner.ResetForUserRetry(
                    stateFile,
                    "unconfirmed-timeout",
                    expectedGeneration: 0,
                    expectedAttempt: 1),
                "an ordinary user retry reset must not clear an indeterminate outcome");

            OperationBatchResult restart = await RecoveryRunner.RunAllAsync(new[]
            {
                new RecoverableOperation(
                    "unconfirmed-timeout",
                    () => { runs++; return Task.CompletedTask; },
                    maxAttempts: 3,
                    retrySafety: RetrySafety.Idempotent)
            }, stateFile);
            Check(runs == 1 && restart.Results.Single().Blocked && restart.Results.Single().Indeterminate,
                "restart should not replay indeterminate work even when it is idempotent and has retry budget left");

            Check(!RecoveryRunner.ReconcileIndeterminate(
                    stateFile,
                    "unconfirmed-timeout",
                    expectedGeneration: 0,
                    expectedAttempt: 2,
                    outcome: IndeterminateReconciliationOutcome.ConfirmedNotAppliedAndStopped),
                "reconciliation should reject a stale attempt identity");
            Check(RecoveryRunner.ReconcileIndeterminate(
                    stateFile,
                    "unconfirmed-timeout",
                    expectedGeneration: 0,
                    expectedAttempt: 1,
                    outcome: IndeterminateReconciliationOutcome.ConfirmedNotAppliedAndStopped),
                "an explicit not-applied-and-stopped reconciliation should start a new user retry generation");

            OperationBatchResult reconciled = await RecoveryRunner.RunAllAsync(new[]
            {
                new RecoverableOperation(
                    "unconfirmed-timeout",
                    () => { runs++; return Task.CompletedTask; },
                    maxAttempts: 3,
                    retrySafety: RetrySafety.Idempotent)
            }, stateFile);
            Check(reconciled.Succeeded && runs == 2 && reconciled.Results.Single().UserRetryGeneration == 1,
                "only explicitly reconciled indeterminate work should execute again");
        }

        private static async Task StaleUserRetryTokenCannotResetNewerFailure()
        {
            string directory = NewTemporaryDirectory();
            string stateFile = Path.Combine(directory, "retry-cas.steps");
            int runs = 0;
            Func<Task> fail = () =>
            {
                runs++;
                throw new InvalidOperationException("review required");
            };

            await RecoveryRunner.RunAllAsync(new[]
            {
                new RecoverableOperation("failed", fail, maxAttempts: 3)
            }, stateFile);
            Check(RecoveryRunner.ResetForUserRetry(
                    stateFile,
                    "failed",
                    expectedGeneration: 0,
                    expectedAttempt: 1),
                "the current failed generation should accept its matching explicit retry token");
            await RecoveryRunner.RunAllAsync(new[]
            {
                new RecoverableOperation("failed", fail, maxAttempts: 3)
            }, stateFile);

            Check(runs == 2 && !RecoveryRunner.ResetForUserRetry(
                    stateFile,
                    "failed",
                    expectedGeneration: 0,
                    expectedAttempt: 1),
                "a stale UI retry token must not reset a newer failed generation");
            Check(RecoveryRunner.ResetForUserRetry(
                    stateFile,
                    "failed",
                    expectedGeneration: 1,
                    expectedAttempt: 1),
                "the current generation and attempt should remain explicitly retryable");
        }

        private static async Task GenericTimeoutRequiresExplicitReconciliation()
        {
            string directory = NewTemporaryDirectory();
            string stateFile = Path.Combine(directory, "generic-timeout.steps");
            int runs = 0;
            OperationBatchResult first = await RecoveryRunner.RunAllAsync(new[]
            {
                new RecoverableOperation(
                    "generic-timeout",
                    () =>
                    {
                        runs++;
                        throw new TimeoutException("outcome unknown");
                    },
                    maxAttempts: 3,
                    retrySafety: RetrySafety.Idempotent)
            }, stateFile);

            Check(runs == 1 && first.Results.Single().Blocked && first.Results.Single().Indeterminate,
                "a generic timeout after an action starts should not be automatically retried");
            Check(!RecoveryRunner.ResetForUserRetry(
                    stateFile,
                    "generic-timeout",
                    expectedGeneration: 0,
                    expectedAttempt: 1),
                "ordinary user retry must not clear a generic indeterminate timeout");

            OperationBatchResult restart = await RecoveryRunner.RunAllAsync(new[]
            {
                new RecoverableOperation(
                    "generic-timeout",
                    () => { runs++; return Task.CompletedTask; },
                    maxAttempts: 3,
                    retrySafety: RetrySafety.Idempotent)
            }, stateFile);
            Check(runs == 1 && restart.Results.Single().Indeterminate,
                "restart should preserve a generic timeout as reconciliation-only work");
        }

        private static async Task IndeterminateOutcomeBlocksLaterIndependentMutations()
        {
            string directory = NewTemporaryDirectory();
            string stateFile = Path.Combine(directory, "indeterminate-barrier.steps");
            int laterRuns = 0;
            OperationBatchResult result = await RecoveryRunner.RunAllAsync(new[]
            {
                new RecoverableOperation("uncertain mutation", () =>
                {
                    throw new ExternalProcessException(
                        "uncertain mutation",
                        -1,
                        string.Empty,
                        string.Empty,
                        timedOut: true,
                        terminationConfirmed: false,
                        indeterminate: true,
                        innerException: null);
                }),
                new RecoverableOperation("later independent mutation", () =>
                {
                    laterRuns++;
                    return Task.CompletedTask;
                })
            }, stateFile);

            Check(result.Results[0].Indeterminate
                && result.Results[1].Blocked
                && laterRuns == 0,
                "an indeterminate mutation should block every later mutation until explicit reconciliation");
        }

        private static async Task InterruptedRunningStateRequiresExplicitReconciliation()
        {
            string directory = NewTemporaryDirectory();
            string stateFile = Path.Combine(directory, "running.steps");
            WriteRawCheckpoint(stateFile, RawRecord("running", 1, 0, "interrupted"));
            int runs = 0;

            OperationBatchResult result = await RecoveryRunner.RunAllAsync(new[]
            {
                new RecoverableOperation(
                    "interrupted",
                    () => { runs++; return Task.CompletedTask; },
                    maxAttempts: 3,
                    retrySafety: RetrySafety.Idempotent)
            }, stateFile);

            OperationResult interrupted = result.Results.Single();
            Check(runs == 0 && interrupted.Blocked && interrupted.Indeterminate
                && interrupted.Error is OperationReconciliationRequiredException,
                "a running record left by a crash should be treated as indeterminate on restart");
            Check(!RecoveryRunner.ResetForUserRetry(
                    stateFile,
                    "interrupted",
                    expectedGeneration: 0,
                    expectedAttempt: 1),
                "a running record should not be reset through the ordinary retry API");

            Check(RecoveryRunner.ReconcileIndeterminate(
                    stateFile,
                    "interrupted",
                    expectedGeneration: 0,
                    expectedAttempt: 1,
                    outcome: IndeterminateReconciliationOutcome.ConfirmedSucceeded),
                "explicit reconciliation should be able to record that interrupted work actually succeeded");
            OperationBatchResult reconciled = await RecoveryRunner.RunAllAsync(new[]
            {
                new RecoverableOperation("interrupted", () => { runs++; return Task.CompletedTask; })
            }, stateFile);
            Check(reconciled.Succeeded && reconciled.Results.Single().Resumed && runs == 0,
                "confirmed-success reconciliation should resume without replaying the action");
        }

        private static async Task InterruptedAutomaticRetryHonorsCurrentPolicyAndBudget()
        {
            string directory = NewTemporaryDirectory();
            string unsafeState = Path.Combine(directory, "retrying-unsafe.steps");
            WriteRawCheckpoint(unsafeState, RawRecord("retrying", 1, 0, "changed-policy"));
            int unsafeRuns = 0;
            OperationBatchResult changedPolicy = await RecoveryRunner.RunAllAsync(new[]
            {
                new RecoverableOperation(
                    "changed-policy",
                    () => { unsafeRuns++; return Task.CompletedTask; },
                    maxAttempts: 3,
                    retrySafety: RetrySafety.SingleAttempt)
            }, unsafeState);
            Check(unsafeRuns == 0 && changedPolicy.Results.Single().Blocked
                && !changedPolicy.Results.Single().Indeterminate,
                "an interrupted automatic retry must not replay after its operation becomes single-attempt");
            Check(RecoveryRunner.ResetForUserRetry(
                    unsafeState,
                    "changed-policy",
                    expectedGeneration: 0,
                    expectedAttempt: 1),
                "a safely interrupted retry with changed policy should transition to an ordinary explicit retry");

            string exhaustedState = Path.Combine(directory, "retrying-exhausted.steps");
            WriteRawCheckpoint(exhaustedState, RawRecord("retrying", 2, 0, "reduced-budget"));
            int exhaustedRuns = 0;
            OperationBatchResult reducedBudget = await RecoveryRunner.RunAllAsync(new[]
            {
                new RecoverableOperation(
                    "reduced-budget",
                    () => { exhaustedRuns++; return Task.CompletedTask; },
                    maxAttempts: 2,
                    retrySafety: RetrySafety.Idempotent)
            }, exhaustedState);
            Check(exhaustedRuns == 0 && reducedBudget.Results.Single().Blocked
                && !reducedBudget.Results.Single().Indeterminate,
                "an interrupted automatic retry at the current budget must require an explicit user retry");
            Check(RecoveryRunner.ResetForUserRetry(
                    exhaustedState,
                    "reduced-budget",
                    expectedGeneration: 0,
                    expectedAttempt: 2),
                "an exhausted interrupted retry should remain recoverable through an explicit new generation");
        }

        private static async Task CorruptStateBlocksEveryReplay()
        {
            string directory = NewTemporaryDirectory();
            string stateFile = Path.Combine(directory, "corrupt.steps");
            await RecoveryRunner.RunAllAsync(new[]
            {
                new RecoverableOperation("old", () => Task.CompletedTask)
            }, stateFile);
            File.AppendAllText(stateFile, "malformed|tail");
            int unsafeRuns = 0;
            int safeRuns = 0;

            OperationBatchResult result = await RecoveryRunner.RunAllAsync(new[]
            {
                new RecoverableOperation("unsafe", () => { unsafeRuns++; return Task.CompletedTask; }),
                new RecoverableOperation(
                    "safe",
                    () => { safeRuns++; return Task.CompletedTask; },
                    retrySafety: RetrySafety.Idempotent)
            }, stateFile);

            OperationResult unsafeResult = result.Results.Single(item => item.Name == "unsafe");
            Check(unsafeRuns == 0 && unsafeResult.Blocked && unsafeResult.Indeterminate
                && unsafeResult.Error is CorruptOperationStateException,
                "a valid prefix with a malformed tail should never be trusted for unsafe replay");
            OperationResult safeResult = result.Results.Single(item => item.Name == "safe");
            Check(safeRuns == 0 && safeResult.Blocked && safeResult.Indeterminate
                && safeResult.Error is CorruptOperationStateException,
                "corrupt recovery state should fail closed even for explicitly idempotent work");
            Check(File.Exists(stateFile) && File.Exists(stateFile + ".corrupt"),
                "invalid recovery state and its durable marker should remain available for diagnosis");
            OperationBatchResult secondRestart = await RecoveryRunner.RunAllAsync(new[]
            {
                new RecoverableOperation("unsafe", () => { unsafeRuns++; return Task.CompletedTask; }),
                new RecoverableOperation(
                    "safe",
                    () => { safeRuns++; return Task.CompletedTask; },
                    retrySafety: RetrySafety.Idempotent)
            }, stateFile);
            Check(unsafeRuns == 0 && safeRuns == 0
                && secondRestart.Results.All(item => item.Blocked && item.Indeterminate),
                "a durable corruption marker should keep every later restart fail closed");
            Check(!RecoveryRunner.ClearCheckpoint(stateFile),
                "the ordinary completion cleanup API must not erase corrupt recovery evidence");
        }

        private static async Task SnapshotV3RejectsTruncationTamperAndLegacyState()
        {
            string roundTripDirectory = NewTemporaryDirectory();
            string roundTripState = Path.Combine(roundTripDirectory, "v3-roundtrip.steps");
            await RecoveryRunner.RunAllAsync(new[]
            {
                new RecoverableOperation("roundtrip", () => Task.CompletedTask)
            }, roundTripState);
            string[] roundTripLines = File.ReadAllLines(roundTripState);
            Check(roundTripLines.First() == "windows-server-tools-recovery-v3"
                && roundTripLines.Last().StartsWith("commit|1|", StringComparison.Ordinal),
                "a v3 recovery state should include a record count and payload digest commit record");

            string truncatedDirectory = NewTemporaryDirectory();
            string truncatedState = Path.Combine(truncatedDirectory, "truncated.steps");
            WriteRawCheckpoint(truncatedState, RawRecord("running", 1, 0, "unsafe"));
            string[] truncatedLines = File.ReadAllLines(truncatedState);
            File.WriteAllText(
                truncatedState,
                string.Join("\n", truncatedLines.Take(truncatedLines.Length - 1)) + "\n",
                new UTF8Encoding(false));
            int truncatedRuns = 0;
            OperationBatchResult truncated = await RecoveryRunner.RunAllAsync(new[]
            {
                new RecoverableOperation("unsafe", () => { truncatedRuns++; return Task.CompletedTask; })
            }, truncatedState);
            Check(truncatedRuns == 0 && truncated.Results.Single().Error is CorruptOperationStateException,
                "a syntactically valid prefix missing its commit record must fail closed");

            string tamperedDirectory = NewTemporaryDirectory();
            string tamperedState = Path.Combine(tamperedDirectory, "tampered.steps");
            WriteRawCheckpoint(tamperedState, RawRecord("running", 1, 0, "unsafe"));
            string tamperedText = File.ReadAllText(tamperedState);
            File.WriteAllText(
                tamperedState,
                tamperedText.Replace("snapshot|", "snapshot|1"),
                new UTF8Encoding(false));
            int tamperedRuns = 0;
            OperationBatchResult tampered = await RecoveryRunner.RunAllAsync(new[]
            {
                new RecoverableOperation("unsafe", () => { tamperedRuns++; return Task.CompletedTask; })
            }, tamperedState);
            Check(tamperedRuns == 0 && tampered.Results.Single().Error is CorruptOperationStateException,
                "a recovery snapshot whose protected metadata was changed must fail closed");

            string legacyDirectory = NewTemporaryDirectory();
            string legacyState = Path.Combine(legacyDirectory, "legacy.steps");
            WriteLegacyV2Checkpoint(legacyState, RawRecord("pending", 0, 0, "unsafe"));
            int legacyRuns = 0;
            OperationBatchResult legacy = await RecoveryRunner.RunAllAsync(new[]
            {
                new RecoverableOperation("unsafe", () => { legacyRuns++; return Task.CompletedTask; })
            }, legacyState);
            Check(legacyRuns == 0 && legacy.Results.Single().Error is CorruptOperationStateException,
                "an unprotected v2 recovery snapshot must be treated as unknown and never replayed");
        }

        private static async Task CorruptStateRepairPreservesEvidenceAndRequiresCurrentToken()
        {
            string directory = NewTemporaryDirectory();
            string stateFile = Path.Combine(directory, "repair.steps");
            WriteRawCheckpoint(stateFile, RawRecord("running", 1, 0, "unsafe"));
            File.AppendAllText(stateFile, "unexpected-tail", new UTF8Encoding(false));
            int runs = 0;
            OperationBatchResult blocked = await RecoveryRunner.RunAllAsync(new[]
            {
                new RecoverableOperation("unsafe", () => { runs++; return Task.CompletedTask; })
            }, stateFile);
            string evidenceToken = blocked.Results.Single().CorruptionEvidenceToken;
            Check(runs == 0 && evidenceToken.Length > 64,
                "a corrupt operation result should expose a non-secret evidence identity for explicit review");
            Check(!RecoveryRunner.RepairCorruptCheckpoint(stateFile, evidenceToken + "stale"),
                "corrupt-state repair must reject a stale evidence token without changing state");

            string extraCandidate = stateFile + ".tmp." + Guid.NewGuid().ToString("N");
            WriteRawCheckpoint(extraCandidate, RawRecord("pending", 0, 0, "unsafe"));
            Check(!RecoveryRunner.RepairCorruptCheckpoint(stateFile, evidenceToken),
                "corrupt-state repair must reject a token after the reviewed candidate evidence changes");

            OperationBatchResult refreshed = await RecoveryRunner.RunAllAsync(new[]
            {
                new RecoverableOperation("unsafe", () => { runs++; return Task.CompletedTask; })
            }, stateFile);
            string currentToken = refreshed.Results.Single().CorruptionEvidenceToken;
            Check(RecoveryRunner.RepairCorruptCheckpoint(stateFile, currentToken),
                "explicit repair should archive current corruption evidence and install an empty verified snapshot");
            string[] archives = Directory.GetDirectories(directory, "repair.steps.recovery-archive.*");
            Check(archives.Length == 1
                && File.Exists(Path.Combine(archives[0], "manifest.txt"))
                && Directory.GetFiles(archives[0], "*.evidence").Length >= 3,
                "corrupt-state repair should preserve every live candidate and marker in a unique diagnostic archive");
            Check(!RecoveryRunner.RepairCorruptCheckpoint(stateFile, currentToken),
                "an already healthy recovery state must refuse the old repair request");

            OperationBatchResult rerun = await RecoveryRunner.RunAllAsync(new[]
            {
                new RecoverableOperation("unsafe", () => { runs++; return Task.CompletedTask; })
            }, stateFile);
            Check(rerun.Succeeded && runs == 1,
                "work should run again only after explicit evidence-preserving repair makes the state healthy");
        }

        private static async Task ReviewedRetryPreparationIsAtomicAndIdempotent()
        {
            string directory = NewTemporaryDirectory();
            string stateFile = Path.Combine(directory, "reviewed-retry.steps");
            int firstRuns = 0;
            int secondRuns = 0;
            OperationBatchResult failed = await RecoveryRunner.RunAllAsync(new[]
            {
                new RecoverableOperation("first", () => { firstRuns++; throw new InvalidOperationException("first failed"); }),
                new RecoverableOperation("second", () => { secondRuns++; throw new InvalidOperationException("second failed"); })
            }, stateFile);
            Check(failed.Results.Count == 2 && failed.Results.All(result => !result.Succeeded),
                "the reviewed-retry fixture should begin with two deterministic failures");

            var validFirst = new ReviewedOperationPreparation("first", ReviewedOperationState.Failed, 0, 1);
            var staleSecond = new ReviewedOperationPreparation("second", ReviewedOperationState.Failed, 1, 1);
            byte[] beforeRejectedRequest = File.ReadAllBytes(stateFile);
            Check(!RecoveryRunner.PrepareReviewedRetry(
                    stateFile,
                    "partial-request",
                    new[] { validFirst, staleSecond })
                && beforeRejectedRequest.SequenceEqual(File.ReadAllBytes(stateFile)),
                "one stale reviewed item must reject the whole request without a partial state transition");

            var validSecond = new ReviewedOperationPreparation("second", ReviewedOperationState.Failed, 0, 1);
            const string requestId = "reviewed-request-1";
            Check(RecoveryRunner.PrepareReviewedRetry(stateFile, requestId, new[] { validSecond, validFirst }),
                "one reviewed request should atomically prepare every matching ordinary failure");
            byte[] preparedBytes = File.ReadAllBytes(stateFile);
            DateTime preparedWriteTime = File.GetLastWriteTimeUtc(stateFile);
            Check(RecoveryRunner.PrepareReviewedRetry(stateFile, requestId, new[] { validFirst, validSecond })
                && preparedBytes.SequenceEqual(File.ReadAllBytes(stateFile))
                && preparedWriteTime == File.GetLastWriteTimeUtc(stateFile),
                "an exact reviewed request repeat should succeed idempotently without rewriting durable state");
            Check(!RecoveryRunner.PrepareReviewedRetry(
                    stateFile,
                    requestId,
                    new[] { new ReviewedOperationPreparation("first", ReviewedOperationState.Failed, 0, 1) }),
                "reusing a reviewed request identifier with a different request set must be rejected");

            OperationBatchResult retried = await RecoveryRunner.RunAllAsync(new[]
            {
                new RecoverableOperation("first", () => { firstRuns++; return Task.CompletedTask; }),
                new RecoverableOperation("second", () => { secondRuns++; return Task.CompletedTask; })
            }, stateFile);
            Check(retried.Succeeded && firstRuns == 2 && secondRuns == 2,
                "an atomically prepared ordinary failure set should execute in its new generation");
            Check(!RecoveryRunner.PrepareReviewedRetry(stateFile, requestId, new[] { validFirst, validSecond }),
                "later execution must clear idempotency metadata so an old request cannot report success");
        }

        private static async Task NewerCorruptStateCannotBeOverriddenByStaleValidBackup()
        {
            string directory = NewTemporaryDirectory();
            string stateFile = Path.Combine(directory, "mixed-corruption.steps");
            string staleBackup = stateFile + ".bak." + Guid.NewGuid().ToString("N");
            WriteRawCheckpoint(staleBackup, RawRecord("pending", 0, 0, "unsafe"));
            Thread.Sleep(25);
            File.WriteAllText(stateFile, "newer corrupt running-state evidence");
            int runs = 0;

            OperationBatchResult result = await RecoveryRunner.RunAllAsync(new[]
            {
                new RecoverableOperation(
                    "unsafe",
                    () => { runs++; return Task.CompletedTask; },
                    retrySafety: RetrySafety.Idempotent)
            }, stateFile);

            Check(runs == 0 && result.Results.Single().Blocked && result.Results.Single().Indeterminate,
                "a stale valid backup must not override newer corrupt state and authorize replay");
        }

        private static async Task CorruptPrimaryCannotBeOverriddenByFutureDatedCandidate()
        {
            string directory = NewTemporaryDirectory();
            string stateFile = Path.Combine(directory, "future-candidate.steps");
            File.WriteAllText(stateFile, "corrupt authoritative primary");
            string candidate = stateFile + ".tmp." + Guid.NewGuid().ToString("N");
            WriteRawCheckpoint(candidate, RawRecord("pending", 0, 0, "unsafe"));
            RewriteSnapshotTimestamp(candidate, "9999-12-31T23:59:59.9999999+00:00");
            int runs = 0;

            OperationBatchResult result = await RecoveryRunner.RunAllAsync(new[]
            {
                new RecoverableOperation(
                    "unsafe",
                    () => { runs++; return Task.CompletedTask; },
                    retrySafety: RetrySafety.Idempotent)
            }, stateFile);
            Check(runs == 0 && result.Results.Single().Blocked && result.Results.Single().Indeterminate,
                "future-dated candidate metadata must not override a corrupt authoritative primary");
        }

        private static async Task ValidPrimaryRemainsAuthoritativeOverFutureDatedBackup()
        {
            string directory = NewTemporaryDirectory();
            string stateFile = Path.Combine(directory, "authoritative-primary.steps");
            WriteRawCheckpoint(stateFile, RawRecord("running", 1, 0, "unsafe"));
            string backup = stateFile + ".bak." + Guid.NewGuid().ToString("N");
            WriteRawCheckpoint(backup, RawRecord("pending", 0, 0, "unsafe"));
            RewriteSnapshotTimestamp(backup, "9999-12-31T23:59:59.9999999+00:00");
            int runs = 0;

            OperationBatchResult result = await RecoveryRunner.RunAllAsync(new[]
            {
                new RecoverableOperation(
                    "unsafe",
                    () => { runs++; return Task.CompletedTask; },
                    retrySafety: RetrySafety.Idempotent)
            }, stateFile);

            Check(runs == 0 && result.Results.Single().Blocked && result.Results.Single().Indeterminate,
                "a valid canonical primary should stay authoritative over a future-dated stale backup");
        }

        private static async Task ValidPrimaryQuarantinesInvalidTransientResidue()
        {
            string directory = NewTemporaryDirectory();
            string stateFile = Path.Combine(directory, "valid-primary-residue.steps");
            int runs = 0;
            RecoverableOperation operation = new RecoverableOperation(
                "completed mutation",
                () => { runs++; return Task.CompletedTask; });
            OperationBatchResult first = await RecoveryRunner.RunAllAsync(new[] { operation }, stateFile);
            string residue = stateFile + ".tmp." + Guid.NewGuid().ToString("N");
            File.WriteAllText(residue, "truncated crash-left bytes");

            OperationBatchResult second = await RecoveryRunner.RunAllAsync(new[] { operation }, stateFile);
            string[] quarantined = Directory.GetFiles(
                directory,
                Path.GetFileName(stateFile) + ".discarded-residue-*");
            OperationBatchResult third = await RecoveryRunner.RunAllAsync(new[] { operation }, stateFile);

            Check(first.Succeeded
                && second.Succeeded
                && second.Results.Single().Resumed
                && third.Succeeded
                && runs == 1,
                "a valid canonical primary should resume without replay despite invalid transient residue");
            Check(!File.Exists(stateFile + ".corrupt")
                && !File.Exists(residue)
                && quarantined.Length == 1,
                "invalid transient residue beside a valid primary should be quarantined outside candidate discovery without a corruption marker");
        }

        private static async Task RejectsMalformedCheckpointSemantics()
        {
            string[] invalidRecords =
            {
                RawRecord("unknown", 1, 0, "bad-state"),
                RawRecord("running", -1, 0, "negative-attempts"),
                RawRecord("pending", 1, 0, "pending-with-attempt"),
                RawRecord("blocked", 1, 0, "blocked-with-attempt"),
                RawRecord("succeeded", 1, -1, "negative-generation"),
                RawRecord("succeeded", 1, 0, "duplicate") + Environment.NewLine + RawRecord("failed", 1, 0, "duplicate")
            };

            foreach (string invalid in invalidRecords)
            {
                string directory = NewTemporaryDirectory();
                string stateFile = Path.Combine(directory, "strict.steps");
                WriteRawCheckpoint(stateFile, invalid);
                int runs = 0;
                OperationBatchResult result = await RecoveryRunner.RunAllAsync(new[]
                {
                    new RecoverableOperation("unsafe", () => { runs++; return Task.CompletedTask; })
                }, stateFile);
                Check(runs == 0 && result.Results[0].Error is CorruptOperationStateException,
                    "strict parsing should reject malformed record semantics: " + invalid.Split('|')[0]);
            }
        }

        private static async Task PersistenceFailureBlocksBeforeAction()
        {
            string directory = NewTemporaryDirectory();
            string fileWhereDirectoryIsExpected = Path.Combine(directory, "not-a-directory");
            File.WriteAllText(fileWhereDirectoryIsExpected, "occupied");
            string stateFile = Path.Combine(fileWhereDirectoryIsExpected, "state.steps");
            int runs = 0;

            OperationBatchResult result = await RecoveryRunner.RunAllAsync(new[]
            {
                new RecoverableOperation("must-persist-first", () => { runs++; return Task.CompletedTask; })
            }, stateFile);

            Check(runs == 0, "an action should not start until its running state is durable");
            Check(result.Results[0].Blocked && result.Results[0].Error is OperationStatePersistenceException,
                "a pre-action persistence failure should be visible and blocked");
        }

        private static async Task SuccessPersistenceFailureIsIndeterminate()
        {
            string directory = NewTemporaryDirectory();
            string stateFile = Path.Combine(directory, "state.steps");
            int runs = 0;
            OperationBatchResult result = await RecoveryRunner.RunAllAsync(new[]
            {
                new RecoverableOperation("completed-but-unrecorded", () =>
                {
                    runs++;
                    File.Delete(stateFile);
                    Directory.Delete(directory);
                    File.WriteAllText(directory, "blocks state directory recreation");
                    return Task.CompletedTask;
                })
            }, stateFile);

            OperationResult operation = result.Results[0];
            Check(runs == 1 && !operation.Succeeded && operation.Blocked && operation.Indeterminate,
                "completed work with an unpersisted success marker should be indeterminate");
            Check(operation.Error is OperationStatePersistenceException
                && ((OperationStatePersistenceException)operation.Error).ActionCompleted,
                "the persistence diagnostic should say that the action completed");
            File.Delete(directory);
            Check(!string.IsNullOrWhiteSpace(operation.ReconciliationToken)
                && RecoveryRunner.PrepareReviewedRetry(
                    stateFile,
                    "missing-record-reconciliation",
                    new ReviewedOperationPreparation(
                        operation.Name,
                        ReviewedOperationState.Indeterminate,
                        operation.UserRetryGeneration,
                        operation.Attempts,
                        IndeterminateReconciliationOutcome.ConfirmedSucceeded,
                        operation.ReconciliationToken)),
                "a protected attempt token should reconcile completed work after its checkpoint record is lost");
        }

        private static async Task ConcurrentSamePathBatchesAreSerializedWithoutLostRecords()
        {
            string directory = NewTemporaryDirectory();
            string stateFile = Path.Combine(directory, "shared.steps");
            int active = 0;
            int maxActive = 0;
            Func<string, Func<Task>> action = name => async () =>
            {
                int now = Interlocked.Increment(ref active);
                int observed;
                while ((observed = maxActive) < now && Interlocked.CompareExchange(ref maxActive, now, observed) != observed)
                {
                }

                await Task.Delay(80);
                Interlocked.Decrement(ref active);
            };

            Task<OperationBatchResult> first = RecoveryRunner.RunAllAsync(
                new[] { new RecoverableOperation("one", action("one")) }, stateFile);
            Task<OperationBatchResult> second = RecoveryRunner.RunAllAsync(
                new[] { new RecoverableOperation("two", action("two")) }, stateFile);
            await Task.WhenAll(first, second);

            Check(maxActive == 1, "same-path recovery batches should hold one whole-batch cross-process lease");
            OperationBatchResult resumed = await RecoveryRunner.RunAllAsync(new[]
            {
                new RecoverableOperation("one", () => { throw new Exception("should not replay one"); }),
                new RecoverableOperation("two", () => { throw new Exception("should not replay two"); })
            }, stateFile);
            Check(resumed.Succeeded && resumed.Results.All(item => item.Resumed),
                "serialized batches should merge state without losing either completed record");
        }

        private static void LeasePathIsStableAcrossSessions()
        {
            string directory = NewTemporaryDirectory();
            string stateFile = Path.Combine(directory, "global.steps");
            string first = BatchFileLease.GetLockPathForTest(stateFile);
            string second = BatchFileLease.GetLockPathForTest(Path.Combine(directory, ".", "global.steps"));
            Check(Path.IsPathRooted(first)
                && string.Equals(first, second, StringComparison.Ordinal),
                "the same recovery state path should map to one protected lock file across interactive sessions");
        }

        private static async Task CompetingLeaseFailsQuicklyThenRetrySucceeds()
        {
            string directory = NewTemporaryDirectory();
            string stateFile = Path.Combine(directory, "contended.steps");
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<OperationBatchResult> holder = RecoveryRunner.RunAllAsync(new[]
            {
                new RecoverableOperation("holder", async () =>
                {
                    entered.TrySetResult(true);
                    await release.Task;
                })
            }, stateFile);
            await entered.Task;

            var stopwatch = Stopwatch.StartNew();
            OperationBatchLeaseException leaseFailure = null;
            try
            {
                await RecoveryRunner.RunAllAsync(new[]
                {
                    new RecoverableOperation("later", () => Task.CompletedTask)
                }, stateFile);
            }
            catch (OperationBatchLeaseException ex)
            {
                leaseFailure = ex;
            }
            stopwatch.Stop();
            Check(leaseFailure != null && stopwatch.Elapsed < TimeSpan.FromSeconds(3),
                "a competing recovery lease should fail quickly instead of freezing the UI thread");

            release.TrySetResult(true);
            await holder;
            OperationBatchResult later = await RecoveryRunner.RunAllAsync(new[]
            {
                new RecoverableOperation("later", () => Task.CompletedTask)
            }, stateFile);
            Check(later.Succeeded,
                "retrying after the competing batch releases its lease should succeed");
        }

        private static async Task RecoversAValidInterruptedTemporarySnapshot()
        {
            string directory = NewTemporaryDirectory();
            string stateFile = Path.Combine(directory, "interrupted.steps");
            int runs = 0;
            await RecoveryRunner.RunAllAsync(new[]
            {
                new RecoverableOperation("complete", () => { runs++; return Task.CompletedTask; })
            }, stateFile);
            string temporary = stateFile + ".tmp." + Guid.NewGuid().ToString("N");
            File.Copy(stateFile, temporary);
            File.Delete(stateFile);

            OperationBatchResult result = await RecoveryRunner.RunAllAsync(new[]
            {
                new RecoverableOperation("complete", () => { runs++; return Task.CompletedTask; })
            }, stateFile);

            Check(result.Succeeded && result.Results[0].Resumed && runs == 1,
                "a valid interrupted temporary snapshot should recover a missing primary without replay");
            Check(File.ReadLines(stateFile).First() == "windows-server-tools-recovery-v3",
                "interrupted-state recovery should restore a validated primary file");
        }

        private static async Task RedactsPersistedAndLoggedErrorSummaries()
        {
            string uniqueSecret = "sensitive-" + Guid.NewGuid().ToString("N");
            string directory = NewTemporaryDirectory();
            string stateFile = Path.Combine(directory, "redacted.steps");
            await RecoveryRunner.RunAllAsync(new[]
            {
                new RecoverableOperation("redaction", () =>
                {
                    throw new InvalidOperationException("password=" + uniqueSecret + " token:" + uniqueSecret + " at " + Path.GetTempPath());
                })
            }, stateFile);

            string state = File.ReadAllText(stateFile);
            string log = File.Exists(ErrorLog.CurrentLogFile) ? File.ReadAllText(ErrorLog.CurrentLogFile) : string.Empty;
            Check(!state.Contains(uniqueSecret) && !log.Contains(uniqueSecret),
                "checkpoint and log summaries should redact password and token values");
            Check(!state.Contains(Convert.ToBase64String(Encoding.UTF8.GetBytes(uniqueSecret))),
                "checkpoint state should not hide an unredacted secret inside base64");
        }

        private static void RedactsQuotedAuthorizationConnectionAndMultilineSecrets()
        {
            string[] secrets =
            {
                "quoted multi word value",
                "basic-credential-value",
                "bearer-credential-value",
                "connection string value",
                "multiline value"
            };
            string diagnostic = "password=\"" + secrets[0] + "\"\r\n"
                + "Authorization: Basic " + secrets[1] + "\r\n"
                + "Authorization: Bearer " + secrets[2] + "\r\n"
                + "Server=localhost;Password='" + secrets[3] + "';User Id=server-admin\r\n"
                + "token:\"" + secrets[4] + "\"\r\n"
                + "safe diagnostic text";
            string redacted = DiagnosticRedactor.RedactAndBound(diagnostic, 4096);

            Check(secrets.All(secret => !redacted.Contains(secret))
                && redacted.Contains("safe diagnostic text"),
                "diagnostics should redact quoted, authorization, connection-string, and multiline secret values without deleting safe context");
        }

        private static async Task RunsExternalProcessesWithBoundedHonestOutcomes()
        {
            ExternalProcessResult success = await ExternalProcessRunner.RunAsync(
                "successful command",
                Cmd("echo hello"),
                TimeSpan.FromSeconds(5));
            Check(success.ExitCode == 0 && success.StandardOutput.Contains("hello"),
                "the process runner should capture successful output and exit code zero");

            ExternalProcessResult warning = await ExternalProcessRunner.RunAsync(
                "warning command",
                Cmd("echo warning 1>&2 & exit /b 0"),
                TimeSpan.FromSeconds(5));
            Check(warning.ExitCode == 0 && warning.StandardError.Contains("warning"),
                "stderr output alone should not turn a zero exit code into a failure");

            ExternalProcessException nonzero = await CaptureProcessFailure(
                "failing command",
                Cmd("echo password=hidden-value 1>&2 & exit /b 37"),
                TimeSpan.FromSeconds(5));
            Check(nonzero != null && nonzero.ExitCode == 37 && nonzero.StandardError.Contains("<redacted>")
                && !nonzero.StandardError.Contains("hidden-value"),
                "nonzero exit should throw a redacted process exception with the exact exit code");

            ExternalProcessException timeout = await CaptureProcessFailure(
                "hanging command",
                Cmd("ping 127.0.0.1 -n 30 >nul"),
                TimeSpan.FromMilliseconds(200));
            Check(timeout != null && timeout.TimedOut && timeout.TerminationConfirmed && !timeout.Indeterminate,
                "a timed-out process should report confirmed tree termination before it is retryable");
            Check(RecoveryRunner.IsTransient(timeout),
                "only a timeout with confirmed tree termination should be transient");

            string fixtureExecutable = System.Reflection.Assembly.GetExecutingAssembly().Location;
            ExternalProcessException descendantTimeout = await CaptureProcessFailure(
                "descendant retaining output pipes",
                new ProcessStartInfo(fixtureExecutable, "--spawn-pipe-descendant"),
                TimeSpan.FromMilliseconds(750));
            Check(descendantTimeout != null
                && descendantTimeout.TimedOut
                && descendantTimeout.TerminationConfirmed
                && !descendantTimeout.Indeterminate,
                "one deadline should include descendant exit and inherited output-pipe drains, then confirm the entire job is empty");

            Task<ExternalProcessResult> first = ExternalProcessRunner.RunAsync(
                "parallel one", Cmd("echo one"), TimeSpan.FromSeconds(5));
            Task<ExternalProcessResult> second = ExternalProcessRunner.RunAsync(
                "parallel two", Cmd("echo two"), TimeSpan.FromSeconds(5));
            ExternalProcessResult[] parallel = await Task.WhenAll(first, second);
            Check(parallel[0].StandardOutput.Contains("one") && parallel[1].StandardOutput.Contains("two"),
                "concurrent process output should remain attached to the owning process");
        }

        private static async Task StreamsAndClearsStandardInput()
        {
            string directory = NewTemporaryDirectory();
            string receipt = Path.Combine(directory, "received.txt");
            string sensitiveValue = "sensitive-input-" + Guid.NewGuid().ToString("N");
            char[] input = (sensitiveValue + "\r\n").ToCharArray();
            ProcessStartInfo startInfo = Cmd("set /p WST_INPUT=& >\"" + receipt + "\" echo received");
            ExternalProcessResult result = await ExternalProcessRunner.RunAsync(
                "standard input command",
                startInfo,
                TimeSpan.FromSeconds(5),
                input);

            Check(File.Exists(receipt) && File.ReadAllText(receipt).Contains("received"),
                "the process runner should stream supplied characters through standard input before closing it");
            Check(!result.StandardOutput.Contains(sensitiveValue)
                && !result.StandardError.Contains(sensitiveValue)
                && !startInfo.Arguments.Contains(sensitiveValue)
                && !startInfo.EnvironmentVariables.Values.Cast<string>().Any(value => value != null && value.Contains(sensitiveValue))
                && !RecoveryLogsContain(sensitiveValue),
                "sensitive standard input should not appear in captured output, arguments, environment values, or logs");
            Check(input.All(character => character == '\0'),
                "the process runner should clear the caller-supplied standard-input buffer in a finally path");

            string nonzeroValue = "sensitive-nonzero-" + Guid.NewGuid().ToString("N");
            char[] nonzeroInput = (nonzeroValue + "\r\n").ToCharArray();
            ExternalProcessException nonzero = null;
            try
            {
                await ExternalProcessRunner.RunAsync(
                    "standard input nonzero",
                    Cmd("set /p WST_INPUT=& exit /b 17"),
                    TimeSpan.FromSeconds(5),
                    nonzeroInput);
            }
            catch (ExternalProcessException ex)
            {
                nonzero = ex;
            }
            Check(nonzero != null && nonzero.ExitCode == 17
                && nonzeroInput.All(character => character == '\0')
                && !nonzero.Message.Contains(nonzeroValue)
                && !nonzero.ToString().Contains(nonzeroValue)
                && !nonzero.StandardOutput.Contains(nonzeroValue)
                && !nonzero.StandardError.Contains(nonzeroValue)
                && !RecoveryLogsContain(nonzeroValue),
                "sensitive standard input should be cleared and absent from every nonzero-exit diagnostic");

            string timeoutValue = "sensitive-timeout-" + Guid.NewGuid().ToString("N");
            char[] timeoutInput = (timeoutValue + "\r\n").ToCharArray();
            ExternalProcessException timeout = null;
            try
            {
                await ExternalProcessRunner.RunAsync(
                    "standard input timeout",
                    Cmd("set /p WST_INPUT=& ping 127.0.0.1 -n 30 >nul"),
                    TimeSpan.FromMilliseconds(250),
                    timeoutInput);
            }
            catch (ExternalProcessException ex)
            {
                timeout = ex;
            }
            Check(timeout != null && timeout.TimedOut
                && timeoutInput.All(character => character == '\0')
                && !timeout.ToString().Contains(timeoutValue)
                && !timeout.StandardOutput.Contains(timeoutValue)
                && !timeout.StandardError.Contains(timeoutValue)
                && !RecoveryLogsContain(timeoutValue),
                "sensitive standard input should be cleared and absent from every timeout diagnostic");

            string startValue = "sensitive-start-" + Guid.NewGuid().ToString("N");
            char[] startInput = (startValue + "\r\n").ToCharArray();
            ExternalProcessException startFailure = null;
            try
            {
                await ExternalProcessRunner.RunAsync(
                    "standard input start failure",
                    new ProcessStartInfo(Path.Combine(directory, "missing-program.exe")),
                    TimeSpan.FromSeconds(1),
                    startInput);
            }
            catch (ExternalProcessException ex)
            {
                startFailure = ex;
            }
            Check(startFailure != null
                && startInput.All(character => character == '\0')
                && !startFailure.ToString().Contains(startValue)
                && !RecoveryLogsContain(startValue),
                "sensitive standard input should be cleared and absent when process start fails");
        }

        private static async Task CommandScriptsFailFast()
        {
            ExternalProcessException failure = null;
            try
            {
                await ExternalProcessRunner.RunCommandScriptAsync(
                    "fail-fast script",
                    "echo before\r\ncmd /c exit 23\r\necho after",
                    TimeSpan.FromSeconds(5));
            }
            catch (ExternalProcessException ex)
            {
                failure = ex;
            }

            Check(failure != null && failure.ExitCode == 23,
                "the command script runner should propagate the first failing child exit code");
            Check(failure != null && failure.StandardOutput.Contains("before") && !failure.StandardOutput.Contains("after"),
                "the command script runner should stop before later commands after a failure");
        }

        private static async Task CommandScriptsIgnorePoisonedComSpec()
        {
            string original = Environment.GetEnvironmentVariable("ComSpec");
            string poison = Path.Combine(NewTemporaryDirectory(), "untrusted-command-processor.exe");
            try
            {
                Environment.SetEnvironmentVariable("ComSpec", poison);
                ExternalProcessResult result = await ExternalProcessRunner.RunCommandScriptAsync(
                    "trusted command processor",
                    "echo trusted-command-ran",
                    TimeSpan.FromSeconds(5));
                Check(result.StandardOutput.Contains("trusted-command-ran"),
                    "command scripts should use the validated system command processor instead of inherited ComSpec");
            }
            finally
            {
                Environment.SetEnvironmentVariable("ComSpec", original);
            }
        }

        private static async Task CompletionStateClearsOnlyWhenRequested()
        {
            string directory = NewTemporaryDirectory();
            string stateFile = Path.Combine(directory, "clear.steps");
            await RecoveryRunner.RunAllAsync(new[]
            {
                new RecoverableOperation("complete", () => Task.CompletedTask)
            }, stateFile);

            Check(File.Exists(stateFile), "a completed batch should retain restart evidence until the caller commits completion");
            Check(RecoveryRunner.ClearCheckpoint(stateFile),
                "completion cleanup should accept a checkpoint whose records all succeeded");
            Check(!File.Exists(stateFile), "the committed completion path should clear recovery state and its candidates");

            WriteRawCheckpoint(stateFile, RawRecord("running", 1, 0, "active"));
            Check(!RecoveryRunner.ClearCheckpoint(stateFile) && File.Exists(stateFile),
                "completion cleanup must not erase running or indeterminate recovery evidence");
        }

        private static void ProtectedWorkflowStateRejectsUnsafePaths()
        {
            bool traversalRejected = false;
            try
            {
                ProtectedWorkflowState.GetPath("..", "outside.txt");
            }
            catch (ArgumentException)
            {
                traversalRejected = true;
            }
            catch (UnauthorizedAccessException)
            {
                traversalRejected = true;
            }
            Check(traversalRejected,
                "protected workflow paths should reject traversal before returning a path");

            bool rootedSegmentRejected = false;
            try
            {
                ProtectedWorkflowState.GetPath(Path.GetPathRoot(Environment.SystemDirectory), "outside.txt");
            }
            catch (ArgumentException)
            {
                rootedSegmentRejected = true;
            }
            Check(rootedSegmentRejected,
                "protected workflow paths should reject rooted path segments");

            try
            {
                string testPath = ProtectedWorkflowState.GetPath(
                    "RecoveryTests",
                    "atomic-" + Guid.NewGuid().ToString("N") + ".txt");
                ProtectedWorkflowState.WriteAllTextAtomic(testPath, "first");
                ProtectedWorkflowState.WriteAllTextAtomic(testPath, "second");
                Check(ProtectedWorkflowState.ReadAllText(testPath) == "second",
                    "protected workflow text writes should replace files atomically and read strict UTF-8");

                DirectorySecurity security = Directory.GetAccessControl(ProtectedWorkflowState.RootDirectory);
                SecurityIdentifier system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
                SecurityIdentifier administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                AuthorizationRuleCollection rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier));
                bool inherited = rules.Cast<FileSystemAccessRule>().Any(rule => rule.IsInherited);
                SecurityIdentifier[] allowed = { system, administrators };
                bool onlyAllowed = rules.Cast<FileSystemAccessRule>()
                    .Where(rule => rule.AccessControlType == AccessControlType.Allow)
                    .All(rule => allowed.Contains((SecurityIdentifier)rule.IdentityReference));
                Check(security.AreAccessRulesProtected && !inherited && onlyAllowed,
                    "the protected workflow root should use a non-inherited System-and-Administrators ACL");
            }
            catch (UnauthorizedAccessException)
            {
                Check(true,
                    "a non-administrator process should be unable to write the administrator-only protected workflow root");
            }
        }

        private static void ClassifiesRecoverableAndFatalExceptions()
        {
            Check(RecoveryRunner.CanContinueAfterDispatcherException(new WebException()),
                "a network failure should be recoverable at the dispatcher boundary");
            Check(!RecoveryRunner.CanContinueAfterDispatcherException(new NullReferenceException()),
                "an unknown programming failure should not be globally marked safe");
            Check(RecoveryRunner.IsFatal(new OutOfMemoryException()), "out-of-memory should remain fatal");
            Check(!RecoveryRunner.IsFatal(new IOException()), "ordinary I/O should remain recoverable");
            var indeterminate = new ExternalProcessException("unknown timeout", -1);
            Check(!indeterminate.Indeterminate, "an ordinary external exit exception should not be marked indeterminate");
        }

        private static void ParsesCommandNamesSafely()
        {
            Check(CommandLineRequestParser.GetCommandName(null) == string.Empty,
                "a missing command line should be treated as a normal launch");
            Check(CommandLineRequestParser.GetCommandName(new[] { "app.exe" }) == string.Empty,
                "a normal launch should not read past the executable argument");
            Check(CommandLineRequestParser.GetCommandName(new[] { "app.exe", "  RUNSETUP  " }) == "runsetup",
                "a command name should be trimmed and normalized without parsing credential-shaped arguments");
        }

        private static void DestructiveAuthorizationRequiresBothKeysFullRangeAndOneClaim()
        {
            Check(!DestructiveActionAuthorization.IsComplete(false, false, 100)
                && !DestructiveActionAuthorization.IsComplete(true, false, 100)
                && !DestructiveActionAuthorization.IsComplete(false, true, 100)
                && !DestructiveActionAuthorization.IsComplete(true, true, 99)
                && DestructiveActionAuthorization.IsComplete(true, true, 100),
                "destructive authorization should require both independent keys and the complete slider range");

            var gate = new ReviewedDestructiveActionGate();
            Check(!gate.TryAuthorize(true, true, 99)
                && gate.TryAuthorize(true, true, 100)
                && !gate.TryAuthorize(true, true, 100),
                "one reviewed destructive-action gate should authorize exactly one batch");

            var cancelled = new ReviewedDestructiveActionGate();
            cancelled.Cancel();
            Check(!cancelled.TryAuthorize(true, true, 100),
                "emergency cancellation should permanently prevent that confirmation instance from authorizing a mutation");
        }

        private static void ChecksUiRecoverySourceContracts()
        {
            string repositoryRoot = FindRepositoryRoot();
            if (repositoryRoot == null)
            {
                Check(false, "the UI source-contract test should locate the repository root");
                return;
            }

            string project = Path.Combine(repositoryRoot, "Windows-Server-Tools", "Windows-Server-Tools");
            string[] xamlFiles =
            {
                Path.Combine(project, "MainWindow.xaml"),
                Path.Combine(project, "CommonlyInstalledWindowsComponents.xaml")
            };
            string[] codeBehindFiles =
            {
                Path.Combine(project, "MainWindow.xaml.cs"),
                Path.Combine(project, "CommonlyInstalledWindowsComponents.xaml.cs")
            };

            foreach (string xamlFile in xamlFiles)
            {
                string source = File.ReadAllText(xamlFile);
                Check(source.Contains("ScrollViewer"), Path.GetFileName(xamlFile) + " should expose responsive scrolling");
                Check(source.Contains("WrapPanel"), Path.GetFileName(xamlFile) + " should wrap recovery actions at narrow widths");
                Check(source.Contains("AutomationProperties.LiveSetting"), Path.GetFileName(xamlFile) + " should mark recovery status as a live region");
                Check(source.Contains("GotKeyboardFocus="), Path.GetFileName(xamlFile) + " should expose keyboard focus handling");
            }

            foreach (string codeBehindFile in codeBehindFiles)
            {
                string source = File.ReadAllText(codeBehindFile).Replace("\r\n", "\n");
                Check(source.Contains("AutomationEvents.LiveRegionChanged"), Path.GetFileName(codeBehindFile) + " should raise live-region changes");
                Check(source.Contains("Func<Task<bool>>"), Path.GetFileName(codeBehindFile) + " retries should report a true success verdict");
                Check(source.Contains("if (succeeded)\n") || source.Contains("if (result.Succeeded)\n"),
                    Path.GetFileName(codeBehindFile) + " should clear pending retry state only on true success");
            }

            string mainWindow = File.ReadAllText(codeBehindFiles[0]);
            string secondaryWindow = File.ReadAllText(codeBehindFiles[1]);
            Check(mainWindow.Contains("hasRetry = _lastFailedAction != null")
                && mainWindow.Contains("RetryRecoveryButton.IsEnabled = hasRetry && !_isRetrying && !selectedOperationIsRunning"),
                "MainWindow.xaml.cs should keep retry enabled after an unsuccessful verdict");
            Check(secondaryWindow.Contains("RetryOperationButton.IsEnabled") && secondaryWindow.Contains("_retryAction != null"),
                "CommonlyInstalledWindowsComponents.xaml.cs should keep retry enabled after an unsuccessful verdict");

            string mainXaml = File.ReadAllText(xamlFiles[0]);
            string secondaryXaml = File.ReadAllText(xamlFiles[1]);
            int shellStart = mainWindow.IndexOf("private async Task<bool> InitializeApplicationShellAsync()", StringComparison.Ordinal);
            int shellEnd = mainWindow.IndexOf("private void ConfigureAvailableServerRoles()", shellStart, StringComparison.Ordinal);
            string startupShell = shellStart >= 0 && shellEnd > shellStart
                ? mainWindow.Substring(shellStart, shellEnd - shellStart)
                : string.Empty;
            Check(!startupShell.Contains("RunInitialServerSetupAsync")
                && !startupShell.Contains("EnsureChocolateyInstalledAsync")
                && mainWindow.Contains("_initialSetupAuthorizationGate.TryAuthorize")
                && mainWindow.Contains("await RunInitialServerSetupAsync()"),
                "normal window startup should remain read-only and the reviewed gate should be the only direct initial-setup launch route");
            Check(mainXaml.Contains("x:Name=\"InitialSetupKeyOneCheckBox\"")
                && mainXaml.Contains("x:Name=\"InitialSetupKeyTwoCheckBox\"")
                && mainXaml.Contains("x:Name=\"InitialSetupAuthorizationSlider\"")
                && mainXaml.Contains("Minimum=\"0\"")
                && mainXaml.Contains("Maximum=\"100\"")
                && mainXaml.Contains("x:Name=\"InitialSetupEmergencyExitButton\"")
                && mainXaml.Contains("AutomationProperties.LiveSetting=\"Assertive\""),
                "the native initial-setup confirmation should expose two keys, a full-range slider, emergency exit, and an assertive status region");
            Check(mainXaml.Contains("HorizontalScrollBarVisibility=\"Disabled\"")
                && mainXaml.Contains("<StackPanel Margin=\"16\" MaxWidth=\"720\">")
                && !mainXaml.Contains("<Grid MinWidth=\"729\""),
                "the main surface should reflow vertically without a fixed-width canvas or page-level horizontal scrolling");
            Check(mainWindow.Contains("FromElement(RecoveryMessageText)")
                && !mainWindow.Contains("FromElement(RecoveryNotification)"),
                "the main live-region event should target the TextBlock automation peer, never the peerless Border");
            Check(secondaryWindow.Contains("FromElement(OperationStatusText)")
                && !secondaryWindow.Contains("FromElement(OperationStatusPanel)"),
                "the feature live-region event should target the TextBlock automation peer, never the peerless Border");
            Check(mainWindow.Contains("retryLabel = \"Retry: \" + selectedRequest.Title")
                && mainWindow.Contains("AutomationProperties.SetName(RetryRecoveryButton, retryLabel)")
                && secondaryWindow.Contains(": \"Retry \" + selectedOperationName")
                && secondaryWindow.Contains("AutomationProperties.SetName(RetryOperationButton, retryLabel)"),
                "visible retry labels should name the exact selected operation whose delegate will run");
            Check(mainWindow.Contains("FocusOrigin = focusOrigin")
                && secondaryWindow.Contains("FocusOrigin = button")
                && secondaryWindow.Contains("_displayedStatusRequest?.FocusOrigin"),
                "each pending recovery request should retain its own focus origin");
            Check(mainXaml.Contains("x:Name=\"ReviewPendingRecoveryButton\"")
                && secondaryXaml.Contains("x:Name=\"ReviewPendingActionsButton\""),
                "dismissed pending work should retain a persistent keyboard-accessible review action");
            Check(mainXaml.Contains("MaxHeight=\"320\"")
                && secondaryXaml.Contains("MaxHeight=\"280\"")
                && mainXaml.Contains("<Grid.RowDefinitions>")
                && secondaryXaml.Contains("<Grid.RowDefinitions>")
                && mainXaml.Contains("<RowDefinition Height=\"*\"/>")
                && secondaryXaml.Contains("<RowDefinition Height=\"*\"/>")
                && mainXaml.Contains("<RowDefinition Height=\"Auto\"/>")
                && secondaryXaml.Contains("<RowDefinition Height=\"Auto\"/>")
                && mainXaml.Contains("VerticalScrollBarVisibility=\"Auto\"")
                && secondaryXaml.Contains("VerticalScrollBarVisibility=\"Auto\""),
                "recovery cards should bound long text while keeping their action row visible");

            string functions = File.ReadAllText(Path.Combine(project, "Functions.cs"));
            string combined = mainWindow + Environment.NewLine + functions;
            Check(mainWindow.Contains("ProtectedWorkflowState.GetPath(")
                && mainWindow.Contains("\"Coordination\"")
                && mainWindow.Contains("\"server-mutation.lease\"")
                && mainWindow.Contains("BatchFileLease.Acquire(machineLeasePath, TimeSpan.Zero)")
                && mainWindow.Contains("_machineLease.Dispose()"),
                "the process-local mutation coordinator should also own one protected machine-wide file lease for its full lifetime");
            Check(!combined.Contains("UsefulTools.Command.RunCommandHidden"),
                "production recovery paths should not call a hidden command runner that can mask exits");
            Check(!combined.Contains("Chocolatey.InstallChocolatey"),
                "production recovery paths should not call the unverified Chocolatey helper directly");
            Check(!combined.Contains("/refs/heads/main/"),
                "installer downloads should not use mutable main-branch asset URLs");
            Check(mainXaml.Contains("x:Name=\"SafeModePasswordBox\"")
                && !combined.Contains("P@ssw0rd")
                && !mainWindow.Contains("safeModeAdminPassword = args"),
                "domain promotion should use the protected UI field and never a fixed or command-line password");
            Check(functions.Contains("CopySecureStringToCharacters")
                && functions.Contains("standardInput")
                && !functions.Contains("WST_SAFE_MODE_PASSWORD_B64"),
                "domain promotion should use the cleared standard-input channel rather than a reversible environment value");
            Check(functions.Contains("chocolatey.2.7.3.nupkg")
                && functions.Contains("40778CC59245B3EB6EA5147AEEF5BEA5D577419E5ABCE22A224189740DC16DB5")
                && !combined.Contains("--ignore-checksums")
                && !functions.Contains("Invoke-Expression $installScript"),
                "Chocolatey installation should verify a pinned package and retain package checksum enforcement");
            Check(mainWindow.Contains("CreateWindowsTaskOperations(networkOperationKey)")
                && functions.Contains("dependencies: string.IsNullOrWhiteSpace(networkDependency)")
                && functions.Contains("new[] { networkDependency }"),
                "DNS and DHCP installation should declare the static-network operation as its initial-setup dependency");
            Check(!mainWindow.Contains(@"C:\Users\Administrator\Desktop\Setup.exe")
                && mainWindow.Contains("StageContinuationExecutable")
                && mainWindow.Contains("VerifyContinuationInvocation")
                && mainWindow.Contains("RemoveSimpsonsTaskAfterSuccess"),
                "the reboot continuation should stage and verify the current executable and delete its task only after success");
            Check(mainWindow.Contains("InitialSetupCompletionMarker = \"windows-server-tools-initial-setup-v2\"")
                && mainWindow.Contains("ProtectedWorkflowState.ReadAllText(completionFile).Trim()")
                && mainWindow.Contains("ProtectedWorkflowState.WriteAllTextAtomic(destination, value)")
                && !mainWindow.Contains("File.ReadAllText(completionFile)"),
                "initial setup should trust only the versioned completion marker written by the hardened path");
            Check(mainWindow.Contains("deadline.Token).ConfigureAwait(true)")
                && mainWindow.Contains("ReadAsync(")
                && mainWindow.Contains("WriteAsync(")
                && mainWindow.Contains("FlushAsync(deadline.Token)"),
                "one installer deadline should cover the request and the complete response body");
            int spreadStart = mainWindow.IndexOf("string SpreadUsersScript", StringComparison.Ordinal);
            int addMember = mainWindow.IndexOf("Add-ADGroupMember", spreadStart, StringComparison.Ordinal);
            int moveUser = mainWindow.IndexOf("Move-ADObject", spreadStart, StringComparison.Ordinal);
            Check(spreadStart >= 0
                && mainWindow.IndexOf("Import-Csv 'C:\\lol.csv'", spreadStart, StringComparison.Ordinal) >= 0
                && addMember > spreadStart
                && moveUser > addMember,
                "user distribution should reconcile stable imported identities and membership before moving each user");
            Check(secondaryWindow.Contains("if (-not $installResult.Success)")
                && secondaryWindow.Contains("$notInstalled"),
                "feature installation should reject an unsuccessful command result and verify every requested feature");
        }

        private static void ChecksWpfDependencyContracts()
        {
            string repositoryRoot = FindRepositoryRoot();
            if (repositoryRoot == null)
            {
                Check(false, "the WPF dependency-contract test should locate the repository root");
                return;
            }

            string project = Path.Combine(repositoryRoot, "Windows-Server-Tools", "Windows-Server-Tools");
            string packages = File.ReadAllText(Path.Combine(project, "packages.config"));
            string projectFile = File.ReadAllText(Path.Combine(project, "Windows-Server-Tools.csproj"));
            string appConfig = File.ReadAllText(Path.Combine(project, "App.config"));

            Check(!packages.Contains("id=\"System.Net.Http\"")
                && !packages.Contains("id=\"System.Text.RegularExpressions\""),
                "the WPF manifest should use the .NET Framework 4.7.2 HTTP and regex assemblies instead of advisory-bearing compatibility packages");
            Check(projectFile.Contains("<Reference Include=\"System.Net.Http\" />")
                && !projectFile.Contains("System.Net.Http.4.3.0")
                && !projectFile.Contains("System.Text.RegularExpressions.4.3.0"),
                "the WPF project should resolve HTTP and regex support from the declared framework target");
            Check(packages.Contains("id=\"Newtonsoft.Json\" version=\"13.0.3\"")
                && projectFile.Contains("Newtonsoft.Json.13.0.3\\lib\\net45\\Newtonsoft.Json.dll"),
                "the WPF project should own one explicit Newtonsoft.Json 13.0.3 reference instead of accepting the older bundled copy");
            Check(appConfig.Contains("oldVersion=\"0.0.0.0-13.0.0.0\"")
                && appConfig.Contains("newVersion=\"13.0.0.0\""),
                "the application binding policy should unify Newtonsoft.Json consumers on the selected 13.0 assembly");
        }

        private static void ChecksWpfLogoContracts()
        {
            string repositoryRoot = FindRepositoryRoot();
            if (repositoryRoot == null)
            {
                Check(false, "the WPF logo-contract test should locate the repository root");
                return;
            }

            string projectRoot = Path.Combine(repositoryRoot, "Windows-Server-Tools");
            string appRoot = Path.Combine(projectRoot, "Windows-Server-Tools");
            string iconPath = Path.Combine(projectRoot, "assets", "branding", "windows-server-setupper.ico");
            string masterPath = Path.Combine(projectRoot, "assets", "branding", "windows-server-setupper-logo-master.png");
            byte[] png = File.ReadAllBytes(masterPath);
            Check(png.Length > 24
                && png.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
                && ReadBigEndianInt32(png, 16) == 1024
                && ReadBigEndianInt32(png, 20) == 1024,
                "the committed application-logo master should be a real 1024 by 1024 PNG");

            using (var stream = File.OpenRead(iconPath))
            using (var reader = new BinaryReader(stream))
            {
                ushort reserved = reader.ReadUInt16();
                ushort type = reader.ReadUInt16();
                ushort count = reader.ReadUInt16();
                var dimensions = new List<int>();
                for (int index = 0; index < count; index++)
                {
                    byte width = reader.ReadByte();
                    byte height = reader.ReadByte();
                    dimensions.Add(width == 0 ? 256 : width);
                    Check(height == width,
                        "every application icon frame should be square");
                    reader.BaseStream.Position += 14;
                }

                Check(reserved == 0
                    && type == 1
                    && count == 9
                    && new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 }.All(dimensions.Contains),
                    "the committed Windows icon should contain all nine required display sizes");
            }

            string projectFile = File.ReadAllText(Path.Combine(appRoot, "Windows-Server-Tools.csproj"));
            string mainXaml = File.ReadAllText(Path.Combine(appRoot, "MainWindow.xaml"));
            string secondaryXaml = File.ReadAllText(Path.Combine(appRoot, "CommonlyInstalledWindowsComponents.xaml"));
            string installer = File.ReadAllText(Path.Combine(repositoryRoot, "packaging", "WindowsServerTools.iss"));
            Check(projectFile.Contains("<ApplicationIcon>..\\assets\\branding\\windows-server-setupper.ico</ApplicationIcon>")
                && mainXaml.Contains("Icon=\"Assets/windows-server-setupper.ico\"")
                && secondaryXaml.Contains("Icon=\"Assets/windows-server-setupper.ico\"")
                && installer.Contains("SetupIconFile=..\\Windows-Server-Tools\\assets\\branding\\windows-server-setupper.ico"),
                "the original mark should be wired into the executable, both WPF windows, and installer metadata");
        }

        private static int ReadBigEndianInt32(byte[] bytes, int offset)
        {
            return (bytes[offset] << 24)
                | (bytes[offset + 1] << 16)
                | (bytes[offset + 2] << 8)
                | bytes[offset + 3];
        }

        private static string FindRepositoryRoot()
        {
            string[] starts = { AppDomain.CurrentDomain.BaseDirectory, Environment.CurrentDirectory };
            foreach (string start in starts)
            {
                var directory = new DirectoryInfo(start);
                while (directory != null)
                {
                    if (File.Exists(Path.Combine(directory.FullName, ".git"))
                        || Directory.Exists(Path.Combine(directory.FullName, ".git")))
                    {
                        return directory.FullName;
                    }

                    directory = directory.Parent;
                }
            }

            return null;
        }

        private static ProcessStartInfo Cmd(string command)
        {
            return new ProcessStartInfo(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
                "/d /s /c \"" + command + "\"");
        }

        private static async Task<ExternalProcessException> CaptureProcessFailure(
            string name,
            ProcessStartInfo startInfo,
            TimeSpan timeout)
        {
            try
            {
                await ExternalProcessRunner.RunAsync(name, startInfo, timeout);
                return null;
            }
            catch (ExternalProcessException ex)
            {
                return ex;
            }
        }

        private static string RawRecord(string state, int attempts, int generation, string name)
        {
            return string.Join(
                "|",
                state,
                attempts.ToString(),
                generation.ToString(),
                DateTimeOffset.UtcNow.ToString("O"),
                Encode(name),
                Encode(string.Empty),
                Encode(string.Empty));
        }

        private static void WriteRawCheckpoint(string path, string records)
        {
            string metadata = "snapshot|"
                + DateTimeOffset.UtcNow.ToString("O")
                + "|"
                + Guid.NewGuid().ToString("N")
                + "||";
            string[] recordLines = (records ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            string canonical = "windows-server-tools-recovery-v3\n"
                + metadata
                + "\n"
                + (recordLines.Length == 0 ? string.Empty : string.Join("\n", recordLines) + "\n");
            string digest;
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                digest = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(canonical))).Replace("-", string.Empty);
            }

            File.WriteAllText(
                path,
                canonical
                + "commit|"
                + recordLines.Length.ToString()
                + "|"
                + digest
                + "\n",
                new UTF8Encoding(false));
        }

        private static void WriteLegacyV2Checkpoint(string path, string records)
        {
            File.WriteAllText(
                path,
                "windows-server-tools-recovery-v2\n"
                + "snapshot|" + DateTimeOffset.UtcNow.ToString("O") + "|" + Guid.NewGuid().ToString("N") + "\n"
                + records + "\n",
                new UTF8Encoding(false));
        }

        private static void RewriteSnapshotTimestamp(string path, string timestamp)
        {
            string[] lines = File.ReadAllLines(path);
            string[] metadata = lines[1].Split('|');
            metadata[1] = timestamp;
            lines[1] = string.Join("|", metadata);
            string canonical = string.Join("\n", lines.Take(lines.Length - 1)) + "\n";
            string digest;
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                digest = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(canonical))).Replace("-", string.Empty);
            }

            string[] commit = lines.Last().Split('|');
            commit[2] = digest;
            File.WriteAllText(path, canonical + string.Join("|", commit) + "\n", new UTF8Encoding(false));
        }

        private static string Encode(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        }

        private static bool RecoveryLogsContain(string value)
        {
            try
            {
                return Directory.Exists(ErrorLog.LogDirectory)
                    && Directory.GetFiles(ErrorLog.LogDirectory, "recovery*.log")
                        .Any(path => File.ReadAllText(path).Contains(value));
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static Task Record(ICollection<string> values, string value)
        {
            values.Add(value);
            return Task.CompletedTask;
        }

        private static string NewTemporaryDirectory()
        {
            string path = Path.Combine(Path.GetTempPath(), "windows-server-tools-recovery-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static async Task ChecksAutomaticUpdateContracts()
        {
            byte[] package = Encoding.UTF8.GetBytes("verified unsigned installer fixture");
            string packageHash;
            using (var sha = SHA256.Create())
            {
                packageHash = BitConverter.ToString(sha.ComputeHash(package))
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
            }

            string manifestJson = BuildUpdateManifestJson("2.0.0.0", packageHash, package.Length);
            var handler = new UpdateHttpHandler(manifestJson, package);
            string testDirectory = NewTemporaryDirectory();
            string stagingDirectory = Path.Combine(testDirectory, "Staging");
            string statePath = Path.Combine(testDirectory, "update-state.json");
            var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            var service = new UpdateService(
                client,
                new Uri("https://updates.example.test/manifest.json"),
                stagingDirectory,
                statePath,
                protectedStorage: false);

            try
            {
                UpdateCheckResult available = await service.CheckAsync(
                    new Version(1, 0, 0, 0),
                    CancellationToken.None);
                Check(available.Availability == UpdateAvailability.Available
                    && available.Manifest.ParsedVersion == new Version(2, 0, 0, 0),
                    "a newer strict HTTPS manifest should report an available update");

                UpdateCheckResult current = await service.CheckAsync(
                    new Version(2, 0, 0, 0),
                    CancellationToken.None);
                Check(current.Availability == UpdateAvailability.Current,
                    "the same manifest version should report the application current");

                int lastProgress = -1;
                string staged = await service.DownloadAndStageAsync(
                    available.Manifest,
                    new ImmediateProgress(value => lastProgress = value),
                    CancellationToken.None);
                Check(File.Exists(staged) && File.ReadAllBytes(staged).SequenceEqual(package),
                    "a package should be promoted only after its size and SHA-256 match");
                Check(lastProgress == 100,
                    "a complete package download should report 100 percent progress");

                service.SaveReadyState(new Version(1, 0, 0, 0), available.Manifest, staged);
                UpdateInstallState state = service.LoadState();
                Check(state != null
                    && state.TargetVersion == "2.0.0.0"
                    && !state.InstallerLaunched
                    && service.ValidateStagedPackage(state),
                    "verified staged state should persist without claiming the installer launched");
                service.MarkInstallerLaunched();
                Check(service.LoadState().InstallerLaunched,
                    "an explicit installer launch should be recorded for rollback diagnosis");

                File.AppendAllText(staged, "tampered");
                Check(!service.ValidateStagedPackage(service.LoadState()),
                    "a corrupt staged package should fail validation before restart");
                service.ClearStateAndStagedPackage();
                Check(!File.Exists(staged) && !File.Exists(statePath),
                    "rollback cleanup should remove corrupt staging and its state record");

                handler.ManifestJson = BuildUpdateManifestJson(
                    "2.0.0.0",
                    new string('0', 64),
                    package.Length);
                UpdateManifest badHashManifest = (await service.CheckAsync(
                    new Version(1, 0, 0, 0),
                    CancellationToken.None)).Manifest;
                bool hashRejected = false;
                try
                {
                    await service.DownloadAndStageAsync(
                        badHashManifest,
                        null,
                        CancellationToken.None);
                }
                catch (InvalidDataException)
                {
                    hashRejected = true;
                }

                Check(hashRejected && !Directory.GetFiles(stagingDirectory, "*.download-*").Any(),
                    "a hash mismatch should reject and remove the incomplete package");

                bool cancelled = false;
                using (var cancellation = new CancellationTokenSource())
                {
                    cancellation.Cancel();
                    try
                    {
                        await service.DownloadAndStageAsync(
                            badHashManifest,
                            null,
                            cancellation.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        cancelled = true;
                    }
                }

                Check(cancelled && !Directory.GetFiles(stagingDirectory, "*.download-*").Any(),
                    "cancelled downloads should retain no partial package");

                CheckThrowsInvalidManifest(
                    manifestJson.Replace(
                        "\"version\":\"2.0.0.0\"",
                        "\"version\":\"2.0.0.0\",\"version\":\"3.0.0.0\""),
                    "duplicate update manifest properties should be rejected");
                CheckThrowsInvalidManifest(
                    manifestJson.TrimEnd('}') + ",\"unexpected\":true}",
                    "unknown update manifest properties should be rejected");
                CheckThrowsInvalidManifest(
                    manifestJson.Replace("https://updates.example.test/package.exe", "http://updates.example.test/package.exe"),
                    "non-HTTPS package URLs should be rejected");

                bool redirectRejected = false;
                handler.RedirectManifest = true;
                try
                {
                    await service.CheckAsync(new Version(1, 0), CancellationToken.None);
                }
                catch (InvalidDataException)
                {
                    redirectRejected = true;
                }
                finally
                {
                    handler.RedirectManifest = false;
                }
                Check(redirectRejected, "update manifest redirects should be refused");

                bool offlineReported = false;
                handler.ThrowNetwork = true;
                try
                {
                    await service.CheckAsync(new Version(1, 0), CancellationToken.None);
                }
                catch (HttpRequestException)
                {
                    offlineReported = true;
                }
                finally
                {
                    handler.ThrowNetwork = false;
                }
                Check(offlineReported, "offline update checks should return a non-success network result");

                handler.ManifestJson = manifestJson;
                handler.RedirectPackageOnce = true;
                UpdateManifest redirectedPackageManifest = (await service.CheckAsync(
                    new Version(1, 0),
                    CancellationToken.None)).Manifest;
                string redirectedStage = await service.DownloadAndStageAsync(
                    redirectedPackageManifest,
                    null,
                    CancellationToken.None);
                Check(File.Exists(redirectedStage),
                    "a bounded HTTPS package redirect should retain manifest hash validation");
                File.Delete(redirectedStage);

                string repositoryRoot = FindRepositoryRoot();
                string project = Path.Combine(repositoryRoot, "Windows-Server-Tools", "Windows-Server-Tools");
                string updateSource = File.ReadAllText(Path.Combine(
                    project,
                    "MainWindow.Update.cs"));
                string xaml = File.ReadAllText(Path.Combine(
                    project,
                    "MainWindow.xaml"));
                string appConfig = File.ReadAllText(Path.Combine(
                    project,
                    "App.config"));
                Check(updateSource.Contains("TimeSpan.FromHours(6)")
                    && updateSource.Contains("_ = CheckForUpdatesAsync(false, null)")
                    && updateSource.Contains("CheckForUpdatesButton_Click"),
                    "the updater should provide bounded scheduled, startup, and manual checks");
                Check(updateSource.Contains("GetUpdateRestartBlockReason")
                    && updateSource.Contains("unsaved form values")
                    && updateSource.Contains("ValidateStagedPackage")
                    && updateSource.Contains("MarkInstallerLaunched"),
                    "restart should protect active and unsaved work and revalidate staged bytes");
                Check(xaml.Contains("Restart to install update")
                    && xaml.Contains("Cancel download")
                    && xaml.Contains("UpdateStatusPanel")
                    && xaml.Contains("AutomationProperties.LiveSetting=\"Polite\""),
                    "the native update surface should expose ready, cancellation, and non-blocking live states");
                Check(appConfig.Contains("https://raw.githubusercontent.com/")
                    && !appConfig.Contains("http://raw.githubusercontent.com/"),
                    "the configured update feed should be HTTPS");
            }
            finally
            {
                service.ClearStateAndStagedPackage();
                service.Dispose();
                client.Dispose();
                if (Directory.Exists(testDirectory))
                {
                    Directory.Delete(testDirectory, true);
                }
            }
        }

        private static void ChecksCustomLogoContracts()
        {
            string directory = NewTemporaryDirectory();
            var service = new LogoService(directory, protectedStorage: false);
            byte[] source = CreateLogoFixturePng(96, 48);
            try
            {
                LogoSettings custom = service.ImportCustom(
                    source,
                    "contain",
                    "transparent",
                    0.5,
                    0.5);
                Check(custom.Preset == "custom"
                    && custom.Fit == "contain"
                    && custom.DisplaySha256.Length == 64,
                    "a valid local image should produce bounded custom-logo settings and a cache identity");
                Check(File.Exists(Path.Combine(directory, "custom-logo-source.bin"))
                    && File.Exists(Path.Combine(directory, "custom-logo-256.png"))
                    && File.Exists(Path.Combine(directory, "custom-logo-48.png")),
                    "custom-logo conversion should retain a private source and both required display sizes");
                BitmapSource display = service.LoadCustomDisplay(custom);
                Check(display != null && display.PixelWidth == 48 && display.PixelHeight == 48,
                    "the consumed custom-logo cache should decode back to exactly 48 by 48 pixels");

                LogoSettings filled = service.UpdateCustomRendering("fill", "#FF112233", 0, 1);
                Check(filled.Fit == "fill"
                    && filled.Background == "#FF112233"
                    && filled.FocalX == 0
                    && filled.FocalY == 1,
                    "fit, background, and numeric focal-point choices should persist exactly");
                LogoSettings reloaded = new LogoService(directory, false).LoadSettings();
                Check(reloaded.Preset == "custom"
                    && reloaded.DisplaySha256 == filled.DisplaySha256,
                    "custom-logo selection and validated cache identity should survive restart");

                string settingsJson = File.ReadAllText(Path.Combine(directory, "logo-settings.json"));
                Check(!settingsJson.Contains(directory)
                    && !settingsJson.Contains("sourcePath")
                    && !settingsJson.Contains("http://")
                    && !settingsJson.Contains("https://"),
                    "custom-logo state should store no source path or network location");

                bool signatureRejected = false;
                try
                {
                    service.ImportCustom(
                        Encoding.UTF8.GetBytes("not an image"),
                        "contain",
                        "transparent",
                        0.5,
                        0.5);
                }
                catch (InvalidDataException)
                {
                    signatureRejected = true;
                }
                Check(signatureRejected,
                    "custom-logo type detection should reject an extension-independent signature mismatch");

                bool oversizedRejected = false;
                try
                {
                    LogoService.DecodeBoundedSource(new byte[LogoService.MaximumSourceBytes + 1]);
                }
                catch (InvalidDataException)
                {
                    oversizedRejected = true;
                }
                Check(oversizedRejected, "custom-logo input should enforce the 5 MiB byte limit");

                File.AppendAllText(Path.Combine(directory, "custom-logo-48.png"), "corrupt");
                Check(service.LoadSettings().Preset == "master",
                    "a corrupt custom-logo cache should fail closed to the shipped mark");
                service.Reset();
                Check(service.LoadSettings().Preset == "master"
                    && !Directory.GetFiles(directory, "custom-logo-*").Any(),
                    "reset should purge the private custom source and every derived cache file");

                LogoSettings icon = service.ApplyPreset("icon");
                Check(icon.Preset == "icon" && service.LoadSettings().Preset == "icon",
                    "the compact shipped application icon preset should persist without custom cache data");

                string repositoryRoot = FindRepositoryRoot();
                string project = Path.Combine(repositoryRoot, "Windows-Server-Tools", "Windows-Server-Tools");
                string mainXaml = File.ReadAllText(Path.Combine(project, "MainWindow.xaml"));
                string commonXaml = File.ReadAllText(Path.Combine(project, "CommonlyInstalledWindowsComponents.xaml"));
                string logoSource = File.ReadAllText(Path.Combine(project, "MainWindow.Logo.cs"));
                Check(mainXaml.Contains("LogoPresetComboBox")
                    && mainXaml.Contains("Choose local image")
                    && mainXaml.Contains("LogoFocalXSlider")
                    && mainXaml.Contains("LogoBackgroundTextBox")
                    && mainXaml.Contains("Reset to shipped logo"),
                    "the native logo surface should expose presets, upload, focal point, background, and reset controls");
                Check(mainXaml.Contains("x:Name=\"AppLogoImage\"")
                    && commonXaml.Contains("x:Name=\"AppLogoImage\""),
                    "both WPF windows should render the active local application logo");
                Check(logoSource.Contains("ReadBoundedLocalLogo")
                    && logoSource.Contains("The source path was not stored")
                    && !logoSource.Contains("HttpClient"),
                    "the custom-logo UI should read a bounded local file without a network route or stored path");
            }
            finally
            {
                service.Reset();
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }

        private static byte[] CreateLogoFixturePng(int width, int height)
        {
            var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            int stride = width * 4;
            byte[] pixels = new byte[stride * height];
            for (int index = 0; index < pixels.Length; index += 4)
            {
                pixels[index] = 0xe8;
                pixels[index + 1] = 0xd8;
                pixels[index + 2] = 0x00;
                pixels[index + 3] = 0xff;
            }
            bitmap.WritePixels(new System.Windows.Int32Rect(0, 0, width, height), pixels, stride, 0);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var output = new MemoryStream())
            {
                encoder.Save(output);
                return output.ToArray();
            }
        }

        private static void CheckThrowsInvalidManifest(string json, string description)
        {
            bool rejected = false;
            try
            {
                UpdateService.ParseAndValidateManifest(Encoding.UTF8.GetBytes(json));
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is ArgumentException)
            {
                rejected = true;
            }

            Check(rejected, description);
        }

        private static string BuildUpdateManifestJson(string version, string sha256, long size)
        {
            return "{"
                + "\"schemaVersion\":1,"
                + "\"version\":\"" + version + "\","
                + "\"releaseNotesUrl\":\"https://updates.example.test/notes\","
                + "\"assetUrl\":\"https://updates.example.test/package.exe\","
                + "\"sha256\":\"" + sha256 + "\","
                + "\"sizeBytes\":" + size
                + "}";
        }

        private sealed class UpdateHttpHandler : HttpMessageHandler
        {
            private readonly byte[] _package;

            public UpdateHttpHandler(string manifestJson, byte[] package)
            {
                ManifestJson = manifestJson;
                _package = package;
            }

            public string ManifestJson { get; set; }

            public bool RedirectManifest { get; set; }

            public bool RedirectPackageOnce { get; set; }

            public bool ThrowNetwork { get; set; }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ThrowNetwork)
                {
                    throw new HttpRequestException("offline fixture");
                }
                if (request.RequestUri.AbsolutePath.EndsWith("manifest.json", StringComparison.Ordinal))
                {
                    if (RedirectManifest)
                    {
                        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect));
                    }

                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(ManifestJson, Encoding.UTF8, "application/json")
                    });
                }

                if (RedirectPackageOnce
                    && !string.Equals(request.RequestUri.Host, "cdn.example.test", StringComparison.Ordinal))
                {
                    RedirectPackageOnce = false;
                    var redirect = new HttpResponseMessage(HttpStatusCode.Redirect);
                    redirect.Headers.Location = new Uri("https://cdn.example.test/package.exe");
                    return Task.FromResult(redirect);
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(_package)
                });
            }
        }

        private sealed class ImmediateProgress : IProgress<int>
        {
            private readonly Action<int> _report;

            public ImmediateProgress(Action<int> report)
            {
                _report = report;
            }

            public void Report(int value)
            {
                _report(value);
            }
        }

        private static void Check(bool condition, string description)
        {
            _checks++;
            if (!condition)
            {
                Failures.Add(description);
            }
        }
    }
}
