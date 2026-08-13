using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Net;
using System.Security.Cryptography;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Windows_Server_Tools
{
    public enum RetrySafety
    {
        SingleAttempt,
        Idempotent
    }

    public enum IndeterminateReconciliationOutcome
    {
        ConfirmedSucceeded,
        ConfirmedNotAppliedAndStopped
    }

    public enum ReviewedOperationState
    {
        Failed,
        Running,
        Indeterminate
    }

    public sealed class ReviewedOperationPreparation
    {
        public ReviewedOperationPreparation(
            string name,
            ReviewedOperationState expectedState,
            int expectedGeneration,
            int expectedAttempt,
            IndeterminateReconciliationOutcome? reconciliationOutcome = null)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("An operation name is required.", nameof(name));
            }

            if (!Enum.IsDefined(typeof(ReviewedOperationState), expectedState))
            {
                throw new ArgumentOutOfRangeException(nameof(expectedState));
            }

            if (expectedGeneration < 0 || expectedGeneration == int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(expectedGeneration));
            }

            if (expectedAttempt < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(expectedAttempt));
            }

            if (reconciliationOutcome.HasValue
                && !Enum.IsDefined(typeof(IndeterminateReconciliationOutcome), reconciliationOutcome.Value))
            {
                throw new ArgumentOutOfRangeException(nameof(reconciliationOutcome));
            }

            if (expectedState == ReviewedOperationState.Failed && reconciliationOutcome.HasValue)
            {
                throw new ArgumentException("Ordinary failures do not accept an indeterminate reconciliation outcome.", nameof(reconciliationOutcome));
            }

            if (expectedState != ReviewedOperationState.Failed && !reconciliationOutcome.HasValue)
            {
                throw new ArgumentException("Running and indeterminate operations require an explicit reconciliation outcome.", nameof(reconciliationOutcome));
            }

            Name = name.Trim();
            ExpectedState = expectedState;
            ExpectedGeneration = expectedGeneration;
            ExpectedAttempt = expectedAttempt;
            ReconciliationOutcome = reconciliationOutcome;
        }

        public string Name { get; }
        public ReviewedOperationState ExpectedState { get; }
        public int ExpectedGeneration { get; }
        public int ExpectedAttempt { get; }
        public IndeterminateReconciliationOutcome? ReconciliationOutcome { get; }
    }

    public static class CommandLineRequestParser
    {
        public static string GetCommandName(string[] args)
        {
            return args == null || args.Length <= 1
                ? string.Empty
                : (args[1] ?? string.Empty).Trim().ToLowerInvariant();
        }

    }

    public static class ProtectedWorkflowState
    {
        private const int MaximumTextBytes = 1024 * 1024;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private static readonly object SyncRoot = new object();

        public static string RootDirectory
        {
            get
            {
                string commonData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                if (string.IsNullOrWhiteSpace(commonData))
                {
                    throw new InvalidOperationException("The shared application-data directory is unavailable.");
                }

                return Path.Combine(commonData, "Windows Server Tools");
            }
        }

        public static string GetPath(params string[] safeSegments)
        {
            if (safeSegments == null)
            {
                throw new ArgumentNullException(nameof(safeSegments));
            }

            lock (SyncRoot)
            {
                EnsureDirectory(RootDirectory);
                string path = RootDirectory;
                foreach (string segment in safeSegments)
                {
                    ValidateSegment(segment);
                    path = Path.Combine(path, segment);
                }

                string fullPath = Path.GetFullPath(path);
                EnsureContained(fullPath);
                RejectReparsePoints(fullPath, includeFinal: true);
                return fullPath;
            }
        }

        public static string ReadAllText(string path)
        {
            lock (SyncRoot)
            {
                string fullPath = ValidateProtectedPath(path);
                RejectReparsePoints(fullPath, includeFinal: true);
                var info = new FileInfo(fullPath);
                if (!info.Exists)
                {
                    throw new FileNotFoundException("The protected state file was not found.", fullPath);
                }

                if (info.Length < 0 || info.Length > MaximumTextBytes)
                {
                    throw new InvalidDataException("The protected state file exceeds its size limit.");
                }

                byte[] payload;
                using (var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan))
                {
                    payload = new byte[checked((int)stream.Length)];
                    int offset = 0;
                    while (offset < payload.Length)
                    {
                        int read = stream.Read(payload, offset, payload.Length - offset);
                        if (read == 0)
                        {
                            throw new EndOfStreamException("The protected state file ended unexpectedly.");
                        }

                        offset += read;
                    }
                }

                RejectReparsePoints(fullPath, includeFinal: true);
                return StrictUtf8.GetString(payload);
            }
        }

        public static void WriteAllTextAtomic(string path, string value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            byte[] payload = StrictUtf8.GetBytes(value);
            if (payload.Length > MaximumTextBytes)
            {
                throw new InvalidDataException("The protected state text exceeds its size limit.");
            }

            lock (SyncRoot)
            {
                string fullPath = ValidateProtectedPath(path);
                string directory = Path.GetDirectoryName(fullPath);
                EnsureProtectedParents(directory);
                RejectReparsePoints(fullPath, includeFinal: true);
                string temporaryPath = fullPath + ".tmp." + Guid.NewGuid().ToString("N");
                try
                {
                    using (var stream = new FileStream(
                        temporaryPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        4096,
                        FileOptions.WriteThrough))
                    {
                        stream.Write(payload, 0, payload.Length);
                        stream.Flush(true);
                    }

                    File.SetAccessControl(temporaryPath, CreateFileSecurity());
                    RejectReparsePoints(fullPath, includeFinal: true);
                    if (File.Exists(fullPath))
                    {
                        File.Replace(temporaryPath, fullPath, null, true);
                    }
                    else
                    {
                        File.Move(temporaryPath, fullPath);
                    }

                    RejectReparsePoints(fullPath, includeFinal: true);
                    File.SetAccessControl(fullPath, CreateFileSecurity());
                }
                finally
                {
                    try
                    {
                        if (File.Exists(temporaryPath))
                        {
                            File.Delete(temporaryPath);
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static string ValidateProtectedPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A protected state path is required.", nameof(path));
            }

            EnsureDirectory(RootDirectory);
            string fullPath = Path.GetFullPath(path);
            EnsureContained(fullPath);
            if (string.Equals(fullPath, RootDirectory, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("A protected state file path is required.", nameof(path));
            }

            return fullPath;
        }

        private static void EnsureContained(string path)
        {
            string root = Path.GetFullPath(RootDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string prefix = root + Path.DirectorySeparatorChar;
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException("The protected state path must remain inside the shared state directory.");
            }
        }

        private static void ValidateSegment(string segment)
        {
            if (string.IsNullOrWhiteSpace(segment)
                || segment.Length > 128
                || segment == "."
                || segment == ".."
                || segment.EndsWith(" ", StringComparison.Ordinal)
                || segment.EndsWith(".", StringComparison.Ordinal)
                || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || !string.Equals(Path.GetFileName(segment), segment, StringComparison.Ordinal))
            {
                throw new ArgumentException("Protected state path segments must be simple file or directory names.", nameof(segment));
            }

            string stem = segment.Split('.')[0];
            string[] reserved = { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
            if (reserved.Contains(stem, StringComparer.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Protected state path segments cannot use reserved device names.", nameof(segment));
            }
        }

        private static void EnsureProtectedParents(string directory)
        {
            EnsureContained(Path.GetFullPath(directory));
            string root = Path.GetFullPath(RootDirectory);
            EnsureDirectory(root);
            string relative = directory.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string current = root;
            foreach (string segment in relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
            {
                ValidateSegment(segment);
                current = Path.Combine(current, segment);
                EnsureDirectory(current);
            }
        }

        private static void EnsureDirectory(string directory)
        {
            if (Directory.Exists(directory))
            {
                RejectSingleReparsePoint(directory);
                Directory.SetAccessControl(directory, CreateDirectorySecurity());
            }
            else
            {
                Directory.CreateDirectory(directory, CreateDirectorySecurity());
            }

            RejectSingleReparsePoint(directory);
        }

        private static DirectorySecurity CreateDirectorySecurity()
        {
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            AddFullControlRules(security, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit);
            return security;
        }

        private static FileSecurity CreateFileSecurity()
        {
            var security = new FileSecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            AddFullControlRules(security, InheritanceFlags.None);
            return security;
        }

        private static void AddFullControlRules(FileSystemSecurity security, InheritanceFlags inheritanceFlags)
        {
            SecurityIdentifier system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            SecurityIdentifier administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            security.AddAccessRule(new FileSystemAccessRule(
                system,
                FileSystemRights.FullControl,
                inheritanceFlags,
                PropagationFlags.None,
                AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(
                administrators,
                FileSystemRights.FullControl,
                inheritanceFlags,
                PropagationFlags.None,
                AccessControlType.Allow));
        }

        private static void RejectReparsePoints(string path, bool includeFinal)
        {
            string fullPath = Path.GetFullPath(path);
            string root = Path.GetFullPath(RootDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            EnsureContained(fullPath);
            RejectSingleReparsePoint(root);
            string relative = fullPath.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string current = root;
            string[] segments = relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            for (int index = 0; index < segments.Length; index++)
            {
                current = Path.Combine(current, segments[index]);
                if (index == segments.Length - 1 && !includeFinal)
                {
                    break;
                }

                RejectSingleReparsePoint(current);
            }
        }

        private static void RejectSingleReparsePoint(string path)
        {
            if ((File.Exists(path) || Directory.Exists(path))
                && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("Protected state paths cannot contain reparse points.");
            }
        }
    }

    public sealed class RecoverableOperation
    {
        public RecoverableOperation(
            string name,
            Func<Task> action,
            int maxAttempts = 1,
            IEnumerable<string> dependencies = null,
            RetrySafety retrySafety = RetrySafety.SingleAttempt)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("An operation name is required.", nameof(name));
            }

            Name = name.Trim();
            Action = action ?? throw new ArgumentNullException(nameof(action));
            MaxAttempts = Math.Max(1, maxAttempts);
            RetrySafety = retrySafety;
            Dependencies = (dependencies ?? Enumerable.Empty<string>())
                .Where(dependency => !string.IsNullOrWhiteSpace(dependency))
                .Select(dependency => dependency.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public string Name { get; }
        public Func<Task> Action { get; }
        public int MaxAttempts { get; }
        public IReadOnlyList<string> Dependencies { get; }
        public RetrySafety RetrySafety { get; }
    }

    public sealed class OperationResult
    {
        internal OperationResult(
            string name,
            bool succeeded,
            int attempts,
            Exception error,
            bool resumed = false,
            bool blocked = false,
            bool indeterminate = false,
            int userRetryGeneration = 0,
            string recoveryState = "",
            string corruptionEvidenceToken = "")
        {
            Name = name;
            Succeeded = succeeded;
            Attempts = attempts;
            Error = error;
            Resumed = resumed;
            Blocked = blocked;
            Indeterminate = indeterminate;
            UserRetryGeneration = userRetryGeneration;
            RecoveryState = recoveryState ?? string.Empty;
            CorruptionEvidenceToken = corruptionEvidenceToken ?? string.Empty;
        }

        public string Name { get; }
        public bool Succeeded { get; }
        public int Attempts { get; }
        public Exception Error { get; }
        public bool Resumed { get; }
        public bool Blocked { get; }
        public bool Indeterminate { get; }
        public int UserRetryGeneration { get; }
        public string RecoveryState { get; }
        public string CorruptionEvidenceToken { get; }
    }

    public sealed class OperationBatchResult
    {
        internal OperationBatchResult(IReadOnlyList<OperationResult> results)
        {
            Results = results;
        }

        public IReadOnlyList<OperationResult> Results { get; }
        public bool Succeeded => Results.All(result => result.Succeeded);
        public IReadOnlyList<OperationResult> Failures => Results.Where(result => !result.Succeeded).ToList();
    }

    public sealed class ExternalProcessResult
    {
        internal ExternalProcessResult(
            string operationName,
            int exitCode,
            string standardOutput,
            string standardError,
            TimeSpan duration)
        {
            OperationName = operationName;
            ExitCode = exitCode;
            StandardOutput = standardOutput ?? string.Empty;
            StandardError = standardError ?? string.Empty;
            Duration = duration;
        }

        public string OperationName { get; }
        public int ExitCode { get; }
        public string StandardOutput { get; }
        public string StandardError { get; }
        public TimeSpan Duration { get; }
    }

    public class ExternalProcessException : Exception
    {
        public ExternalProcessException(string operationName, int exitCode)
            : this(operationName, exitCode, string.Empty, string.Empty, false, true, false, null)
        {
        }

        internal ExternalProcessException(
            string operationName,
            int exitCode,
            string standardOutput,
            string standardError,
            bool timedOut,
            bool terminationConfirmed,
            Exception innerException)
            : this(operationName, exitCode, standardOutput, standardError, timedOut, terminationConfirmed, false, innerException)
        {
        }

        internal ExternalProcessException(
            string operationName,
            int exitCode,
            string standardOutput,
            string standardError,
            bool timedOut,
            bool terminationConfirmed,
            bool indeterminate,
            Exception innerException)
            : base(BuildMessage(operationName, exitCode, standardError, timedOut, terminationConfirmed), innerException)
        {
            OperationName = operationName;
            ExitCode = exitCode;
            StandardOutput = DiagnosticRedactor.RedactAndBound(standardOutput, 4096);
            StandardError = DiagnosticRedactor.RedactAndBound(standardError, 4096);
            TimedOut = timedOut;
            TerminationConfirmed = terminationConfirmed;
            Indeterminate = indeterminate || (timedOut && !terminationConfirmed);
        }

        public string OperationName { get; }
        public int ExitCode { get; }
        public string StandardOutput { get; }
        public string StandardError { get; }
        public bool TimedOut { get; }
        public bool TerminationConfirmed { get; }
        public bool Indeterminate { get; }

        private static string BuildMessage(
            string operationName,
            int exitCode,
            string standardError,
            bool timedOut,
            bool terminationConfirmed)
        {
            string safeName = string.IsNullOrWhiteSpace(operationName) ? "The external process" : operationName.Trim();
            if (timedOut)
            {
                return terminationConfirmed
                    ? safeName + " timed out and its process tree was terminated."
                    : safeName + " timed out, but termination of its process tree could not be confirmed. Reconcile the external state before retrying.";
            }

            string summary = DiagnosticRedactor.RedactAndBound(standardError, 512);
            return string.IsNullOrWhiteSpace(summary)
                ? safeName + " exited with code " + exitCode + "."
                : safeName + " exited with code " + exitCode + ": " + summary;
        }
    }

    public sealed class OperationDependencyException : Exception
    {
        public OperationDependencyException(string operationName, IEnumerable<string> dependencies)
            : base(operationName + " is waiting for: " + string.Join(", ", dependencies) + ".")
        {
        }
    }

    public sealed class MissingOperationDependencyException : Exception
    {
        public MissingOperationDependencyException(string operationName, IEnumerable<string> dependencies)
            : base(operationName + " declares missing dependencies: " + string.Join(", ", dependencies) + ".")
        {
        }
    }

    public sealed class OperationDependencyCycleException : Exception
    {
        public OperationDependencyCycleException(string operationName)
            : base(operationName + " is part of, or depends on, a dependency cycle.")
        {
        }
    }

    public sealed class OperationStatePersistenceException : Exception
    {
        public OperationStatePersistenceException(
            string operationName,
            bool actionCompleted,
            bool actionStarted = false)
            : base(actionCompleted
                ? operationName + " completed, but its recovery state could not be saved. Reconcile the actual server state before retrying."
                : actionStarted
                    ? operationName + " ended, but its recovery outcome could not be saved. Reconcile the actual server state before retrying."
                    : operationName + " was not started because its recovery state could not be saved.")
        {
            ActionCompleted = actionCompleted;
            ActionStarted = actionStarted || actionCompleted;
        }

        public bool ActionCompleted { get; }
        public bool ActionStarted { get; }
    }

    public sealed class CorruptOperationStateException : Exception
    {
        public CorruptOperationStateException(string operationName)
            : base("Recovery state for " + operationName + " is corrupt or unreadable. The operation was not replayed because its prior outcome is unknown.")
        {
        }
    }

    public sealed class OperationReconciliationRequiredException : Exception
    {
        public OperationReconciliationRequiredException(string operationName, string priorState)
            : base(BuildMessage(operationName, priorState))
        {
            PriorState = priorState ?? string.Empty;
        }

        public string PriorState { get; }

        private static string BuildMessage(string operationName, string priorState)
        {
            string safeName = string.IsNullOrWhiteSpace(operationName) ? "The operation" : operationName.Trim();
            return string.Equals(priorState, "running", StringComparison.Ordinal)
                ? safeName + " was running when recovery state was last saved. Its prior outcome is unknown; verify that the prior execution has stopped, then reconcile it before retrying."
                : safeName + " has an indeterminate prior outcome. Verify that the prior execution has stopped, then reconcile it before retrying.";
        }
    }

    public sealed class PersistedOperationFailureException : Exception
    {
        public PersistedOperationFailureException(string operationName, string summary)
            : base(string.IsNullOrWhiteSpace(summary)
                ? operationName + " previously failed. Use an explicit user retry after reviewing the failure."
                : operationName + " previously failed: " + summary)
        {
        }
    }

    public sealed class OperationBatchLeaseException : Exception
    {
        public OperationBatchLeaseException(string checkpointFile)
            : base("Another recovery batch is still using this state file: " + checkpointFile)
        {
        }
    }

    public static class RecoveryRunner
    {
        private static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromMilliseconds(600);

        public static Task<OperationResult> RunAsync(
            string name,
            Func<Task> action,
            int maxAttempts = 1,
            TimeSpan? retryDelay = null,
            RetrySafety retrySafety = RetrySafety.SingleAttempt)
        {
            return RunAsyncCore(name, action, maxAttempts, retryDelay, retrySafety, 0, 0, null);
        }

        public static Task<OperationBatchResult> RunAllAsync(IEnumerable<RecoverableOperation> operations)
        {
            return RunAllAsync(operations, null);
        }

        public static async Task<OperationBatchResult> RunAllAsync(
            IEnumerable<RecoverableOperation> operations,
            string checkpointFile)
        {
            if (operations == null)
            {
                throw new ArgumentNullException(nameof(operations));
            }

            List<RecoverableOperation> operationList = operations.Where(operation => operation != null).ToList();
            EnsureUniqueOperationNames(operationList);

            if (string.IsNullOrWhiteSpace(checkpointFile))
            {
                return await RunBatchCore(operationList, null).ConfigureAwait(true);
            }

            using (BatchFileLease lease = BatchFileLease.Acquire(checkpointFile, TimeSpan.FromSeconds(1)))
            {
                if (lease == null)
                {
                    throw new OperationBatchLeaseException(checkpointFile);
                }

                var store = new OperationCheckpointStore(checkpointFile);
                return await RunBatchCore(operationList, store).ConfigureAwait(true);
            }
        }

        public static bool ResetForUserRetry(
            string checkpointFile,
            string operationName,
            int expectedGeneration,
            int expectedAttempt)
        {
            if (string.IsNullOrWhiteSpace(checkpointFile))
            {
                throw new ArgumentException("A recovery state file is required.", nameof(checkpointFile));
            }

            if (string.IsNullOrWhiteSpace(operationName))
            {
                throw new ArgumentException("An operation name is required.", nameof(operationName));
            }

            if (expectedGeneration < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(expectedGeneration));
            }

            if (expectedAttempt < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(expectedAttempt));
            }

            using (BatchFileLease lease = BatchFileLease.Acquire(checkpointFile, TimeSpan.FromSeconds(1)))
            {
                if (lease == null)
                {
                    return false;
                }

                return new OperationCheckpointStore(checkpointFile).ResetForUserRetry(
                    operationName.Trim(),
                    expectedGeneration,
                    expectedAttempt);
            }
        }

        public static bool ReconcileIndeterminate(
            string checkpointFile,
            string operationName,
            int expectedGeneration,
            int expectedAttempt,
            IndeterminateReconciliationOutcome outcome)
        {
            if (string.IsNullOrWhiteSpace(checkpointFile))
            {
                throw new ArgumentException("A recovery state file is required.", nameof(checkpointFile));
            }

            if (string.IsNullOrWhiteSpace(operationName))
            {
                throw new ArgumentException("An operation name is required.", nameof(operationName));
            }

            if (expectedGeneration < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(expectedGeneration));
            }

            if (expectedAttempt < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(expectedAttempt));
            }

            if (!Enum.IsDefined(typeof(IndeterminateReconciliationOutcome), outcome))
            {
                throw new ArgumentOutOfRangeException(nameof(outcome));
            }

            using (BatchFileLease lease = BatchFileLease.Acquire(checkpointFile, TimeSpan.FromSeconds(1)))
            {
                if (lease == null)
                {
                    return false;
                }

                return new OperationCheckpointStore(checkpointFile).ReconcileIndeterminate(
                    operationName.Trim(),
                    expectedGeneration,
                    expectedAttempt,
                    outcome);
            }
        }

        public static bool PrepareReviewedRetry(
            string checkpointFile,
            string requestId,
            IEnumerable<ReviewedOperationPreparation> operations)
        {
            if (string.IsNullOrWhiteSpace(checkpointFile))
            {
                throw new ArgumentException("A recovery state file is required.", nameof(checkpointFile));
            }

            if (string.IsNullOrWhiteSpace(requestId) || requestId.Trim().Length > 128)
            {
                throw new ArgumentException("A bounded reviewed-retry request identifier is required.", nameof(requestId));
            }

            if (operations == null)
            {
                throw new ArgumentNullException(nameof(operations));
            }

            List<ReviewedOperationPreparation> operationList = operations.ToList();
            if (operationList.Count == 0
                || operationList.Count > 1024
                || operationList.Any(item => item == null)
                || operationList.Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != operationList.Count)
            {
                throw new ArgumentException("Reviewed operations must be non-empty and uniquely named.", nameof(operations));
            }

            using (BatchFileLease lease = BatchFileLease.Acquire(checkpointFile, TimeSpan.FromSeconds(1)))
            {
                if (lease == null)
                {
                    return false;
                }

                return new OperationCheckpointStore(checkpointFile).PrepareReviewedRetry(
                    requestId.Trim(),
                    operationList);
            }
        }

        public static bool PrepareReviewedRetry(
            string checkpointFile,
            string requestId,
            ReviewedOperationPreparation operation)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            return PrepareReviewedRetry(checkpointFile, requestId, new[] { operation });
        }

        public static bool RepairCorruptCheckpoint(
            string checkpointFile,
            string expectedCorruptionEvidenceToken)
        {
            if (string.IsNullOrWhiteSpace(checkpointFile))
            {
                throw new ArgumentException("A recovery state file is required.", nameof(checkpointFile));
            }

            if (string.IsNullOrWhiteSpace(expectedCorruptionEvidenceToken))
            {
                throw new ArgumentException("The current corruption evidence token is required.", nameof(expectedCorruptionEvidenceToken));
            }

            using (BatchFileLease lease = BatchFileLease.Acquire(checkpointFile, TimeSpan.FromSeconds(1)))
            {
                if (lease == null)
                {
                    return false;
                }

                return new OperationCheckpointStore(checkpointFile).RepairCorruptState(
                    expectedCorruptionEvidenceToken.Trim());
            }
        }

        public static bool ClearCheckpoint(string checkpointFile)
        {
            if (string.IsNullOrWhiteSpace(checkpointFile))
            {
                return true;
            }

            using (BatchFileLease lease = BatchFileLease.Acquire(checkpointFile, TimeSpan.FromSeconds(1)))
            {
                if (lease == null)
                {
                    ErrorLog.Write("Clear completed recovery state", new OperationBatchLeaseException(checkpointFile));
                    return false;
                }

                return new OperationCheckpointStore(checkpointFile).ClearCompleted();
            }
        }

        public static bool IsFatal(Exception exception)
        {
            Exception unwrapped = Unwrap(exception);
            return unwrapped is OutOfMemoryException
                || unwrapped is StackOverflowException
                || unwrapped is AccessViolationException
                || unwrapped is AppDomainUnloadedException;
        }

        public static bool IsTransient(Exception exception)
        {
            Exception unwrapped = Unwrap(exception);
            if (unwrapped is ExternalProcessException processException)
            {
                if (processException.TimedOut && processException.TerminationConfirmed)
                {
                    return true;
                }

                if (processException.ExitCode == -1 && processException.TerminationConfirmed)
                {
                    Exception startFailure = Unwrap(processException.InnerException);
                    if (startFailure is Win32Exception startWin32)
                    {
                        return startWin32.NativeErrorCode == 170
                            || startWin32.NativeErrorCode == 54
                            || startWin32.NativeErrorCode == 1237
                            || startWin32.NativeErrorCode == 32;
                    }
                }

                return false;
            }

            if (unwrapped is TimeoutException || unwrapped is IOException)
            {
                return true;
            }

            if (unwrapped is WebException webException)
            {
                return webException.Status != WebExceptionStatus.ProtocolError
                    && webException.Status != WebExceptionStatus.TrustFailure
                    && webException.Status != WebExceptionStatus.SecureChannelFailure;
            }

            if (unwrapped is Win32Exception win32Exception)
            {
                return win32Exception.NativeErrorCode == 170
                    || win32Exception.NativeErrorCode == 54
                    || win32Exception.NativeErrorCode == 1237
                    || win32Exception.NativeErrorCode == 32;
            }

            return false;
        }

        public static bool CanContinueAfterDispatcherException(Exception exception)
        {
            Exception unwrapped = Unwrap(exception);
            return unwrapped is WebException
                || unwrapped is IOException
                || unwrapped is TimeoutException
                || unwrapped is UnauthorizedAccessException
                || unwrapped is Win32Exception
                || unwrapped is ExternalProcessException
                || unwrapped is OperationDependencyException
                || unwrapped is MissingOperationDependencyException
                || unwrapped is OperationDependencyCycleException
                || unwrapped is OperationStatePersistenceException
                || unwrapped is CorruptOperationStateException
                || unwrapped is OperationBatchLeaseException;
        }

        public static string FriendlyMessage(Exception exception)
        {
            Exception unwrapped = Unwrap(exception);
            if (unwrapped == null)
            {
                return "The operation did not complete.";
            }

            if (unwrapped is WebException)
            {
                return "The network request did not complete. Check the connection, then retry.";
            }

            if (unwrapped is UnauthorizedAccessException)
            {
                return "Access was refused. Confirm that the app is running with the permissions required for this server task, then retry.";
            }

            if (unwrapped is ExternalProcessException processException)
            {
                if (processException.Indeterminate)
                {
                    return "A required command timed out, and its process tree could not be confirmed stopped. Reconcile the server state before retrying.";
                }

                return processException.TimedOut
                    ? "A required command timed out and was stopped. Review the error log, then retry."
                    : "A required command exited with code " + processException.ExitCode + ". Review the error log, correct the reported condition, then retry.";
            }

            return string.IsNullOrWhiteSpace(unwrapped.Message)
                ? "The operation did not complete. Review the error log, then retry."
                : unwrapped.Message;
        }

        private static async Task<OperationBatchResult> RunBatchCore(
            IReadOnlyList<RecoverableOperation> operations,
            OperationCheckpointStore checkpointStore)
        {
            if (checkpointStore?.IsCorrupt == true)
            {
                return new OperationBatchResult(operations
                    .Select(operation => new OperationResult(
                        operation.Name,
                        false,
                        0,
                        new CorruptOperationStateException(operation.Name),
                        blocked: true,
                        indeterminate: true,
                        recoveryState: "corrupt",
                        corruptionEvidenceToken: checkpointStore.CorruptionEvidenceToken))
                    .ToList());
            }

            var resultsByName = new Dictionary<string, OperationResult>(StringComparer.OrdinalIgnoreCase);
            foreach (RecoverableOperation operation in operations)
            {
                string state = checkpointStore?.GetState(operation.Name);
                if (string.Equals(state, "succeeded", StringComparison.Ordinal))
                {
                    resultsByName[operation.Name] = new OperationResult(
                        operation.Name,
                        true,
                        checkpointStore.GetAttempts(operation.Name),
                        null,
                        resumed: true,
                        userRetryGeneration: checkpointStore.GetGeneration(operation.Name),
                        recoveryState: state);
                }
                else if (string.Equals(state, "running", StringComparison.Ordinal)
                    || string.Equals(state, "indeterminate", StringComparison.Ordinal))
                {
                    resultsByName[operation.Name] = new OperationResult(
                        operation.Name,
                        false,
                        checkpointStore.GetAttempts(operation.Name),
                        new OperationReconciliationRequiredException(operation.Name, state),
                        blocked: true,
                        indeterminate: true,
                        userRetryGeneration: checkpointStore.GetGeneration(operation.Name),
                        recoveryState: state);
                }
                else if (string.Equals(state, "failed", StringComparison.Ordinal))
                {
                    resultsByName[operation.Name] = new OperationResult(
                        operation.Name,
                        false,
                        checkpointStore.GetAttempts(operation.Name),
                        new PersistedOperationFailureException(
                            operation.Name,
                            checkpointStore.GetErrorSummary(operation.Name)),
                        blocked: true,
                        userRetryGeneration: checkpointStore.GetGeneration(operation.Name),
                        recoveryState: state);
                }
            }

            IReadOnlyList<RecoverableOperation> ordered = TopologicallyOrder(operations, checkpointStore, resultsByName);

            foreach (RecoverableOperation operation in ordered)
            {
                if (resultsByName.ContainsKey(operation.Name))
                {
                    continue;
                }

                string persistedState = checkpointStore?.GetState(operation.Name);
                if (string.Equals(persistedState, "running", StringComparison.Ordinal)
                    || string.Equals(persistedState, "indeterminate", StringComparison.Ordinal))
                {
                    resultsByName[operation.Name] = new OperationResult(
                        operation.Name,
                        false,
                        checkpointStore.GetAttempts(operation.Name),
                        new OperationReconciliationRequiredException(operation.Name, persistedState),
                        blocked: true,
                        indeterminate: true,
                        userRetryGeneration: checkpointStore.GetGeneration(operation.Name));
                    continue;
                }

                if (string.Equals(persistedState, "retrying", StringComparison.Ordinal)
                    && (operation.RetrySafety != RetrySafety.Idempotent
                        || checkpointStore.GetAttempts(operation.Name) >= operation.MaxAttempts))
                {
                    int retryAttempts = checkpointStore.GetAttempts(operation.Name);
                    int retryGeneration = checkpointStore.GetGeneration(operation.Name);
                    Exception interruptedRetry = new InvalidOperationException(
                        operation.Name + " had a pending automatic retry, but its current retry policy or budget no longer permits automatic execution. Review the failure, then use an explicit user retry.");
                    if (!checkpointStore.Record(
                        operation.Name,
                        "failed",
                        retryAttempts,
                        retryGeneration,
                        interruptedRetry))
                    {
                        interruptedRetry = new OperationStatePersistenceException(operation.Name, false);
                    }

                    resultsByName[operation.Name] = new OperationResult(
                        operation.Name,
                        false,
                        retryAttempts,
                        interruptedRetry,
                        blocked: true,
                        userRetryGeneration: retryGeneration);
                    continue;
                }

                if (string.Equals(persistedState, "failed", StringComparison.Ordinal))
                {
                    resultsByName[operation.Name] = new OperationResult(
                        operation.Name,
                        false,
                        checkpointStore.GetAttempts(operation.Name),
                        new PersistedOperationFailureException(
                            operation.Name,
                            checkpointStore.GetErrorSummary(operation.Name)),
                        blocked: true,
                        userRetryGeneration: checkpointStore.GetGeneration(operation.Name));
                    continue;
                }

                if (checkpointStore?.IsSucceeded(operation.Name) == true)
                {
                    resultsByName[operation.Name] = new OperationResult(
                        operation.Name,
                        true,
                        checkpointStore.GetAttempts(operation.Name),
                        null,
                        resumed: true,
                        userRetryGeneration: checkpointStore.GetGeneration(operation.Name));
                    continue;
                }

                IReadOnlyList<string> unsatisfied = operation.Dependencies
                    .Where(dependency =>
                    {
                        if (resultsByName.TryGetValue(dependency, out OperationResult dependencyResult))
                        {
                            return !dependencyResult.Succeeded;
                        }

                        return checkpointStore?.IsSucceeded(dependency) != true;
                    })
                    .ToList();

                if (unsatisfied.Count > 0)
                {
                    var error = new OperationDependencyException(operation.Name, unsatisfied);
                    checkpointStore?.Record(operation.Name, "blocked", 0, checkpointStore.GetGeneration(operation.Name), error);
                    resultsByName[operation.Name] = new OperationResult(
                        operation.Name,
                        false,
                        0,
                        error,
                        blocked: true,
                        userRetryGeneration: checkpointStore?.GetGeneration(operation.Name) ?? 0);
                    continue;
                }

                int generation = checkpointStore?.GetGeneration(operation.Name) ?? 0;
                OperationResult result = await RunAsyncCore(
                    operation.Name,
                    operation.Action,
                    operation.MaxAttempts,
                    null,
                    operation.RetrySafety,
                    checkpointStore?.GetAttempts(operation.Name) ?? 0,
                    generation,
                    (state, attempt, error) => checkpointStore?.Record(
                        operation.Name,
                        state,
                        attempt,
                        generation,
                        error) ?? true).ConfigureAwait(true);
                resultsByName[operation.Name] = result;
            }

            return new OperationBatchResult(operations
                .Select(operation => resultsByName[operation.Name])
                .ToList());
        }

        private static async Task<OperationResult> RunAsyncCore(
            string name,
            Func<Task> action,
            int maxAttempts,
            TimeSpan? retryDelay,
            RetrySafety retrySafety,
            int initialAttempts,
            int generation,
            Func<string, int, Exception, bool> stateChanged)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("An operation name is required.", nameof(name));
            }

            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            int boundedAttempts = Math.Max(1, maxAttempts);
            TimeSpan delay = retryDelay ?? DefaultRetryDelay;
            if (initialAttempts >= boundedAttempts)
            {
                var exhausted = new InvalidOperationException(
                    name + " exhausted its " + boundedAttempts + "-attempt automatic retry budget. Use an explicit user retry after reviewing the last failure.");
                return new OperationResult(name, false, initialAttempts, exhausted, blocked: true, userRetryGeneration: generation);
            }

            Exception lastError = null;
            for (int attempt = initialAttempts + 1; attempt <= boundedAttempts; attempt++)
            {
                if (stateChanged != null && !stateChanged("running", attempt, null))
                {
                    return new OperationResult(
                        name,
                        false,
                        attempt - 1,
                        new OperationStatePersistenceException(name, false),
                        blocked: true,
                        userRetryGeneration: generation);
                }

                try
                {
                    await action().ConfigureAwait(true);
                }
                catch (Exception ex) when (!IsFatal(ex))
                {
                    lastError = Unwrap(ex);
                    ErrorLog.Write(name + " (attempt " + attempt + " of " + boundedAttempts + ")", lastError);
                    bool outcomeIndeterminate = IsIndeterminateOutcome(lastError);
                    bool retry = !outcomeIndeterminate
                        && retrySafety == RetrySafety.Idempotent
                        && attempt < boundedAttempts
                        && IsTransient(lastError);

                    if (!retry)
                    {
                        string terminalState = outcomeIndeterminate ? "indeterminate" : "failed";
                        if (stateChanged != null && !stateChanged(terminalState, attempt, lastError))
                        {
                            return new OperationResult(
                                name,
                                false,
                                attempt,
                                new OperationStatePersistenceException(name, false, actionStarted: true),
                                blocked: true,
                                indeterminate: true,
                                userRetryGeneration: generation);
                        }

                        return new OperationResult(
                            name,
                            false,
                            attempt,
                            lastError,
                            blocked: outcomeIndeterminate,
                            indeterminate: outcomeIndeterminate,
                            userRetryGeneration: generation);
                    }

                    if (stateChanged != null && !stateChanged("retrying", attempt, lastError))
                    {
                        return new OperationResult(
                            name,
                            false,
                            attempt,
                            new OperationStatePersistenceException(name, false, actionStarted: true),
                            blocked: true,
                            indeterminate: true,
                            userRetryGeneration: generation);
                    }

                    double multiplier = Math.Pow(2, attempt - 1);
                    await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * multiplier, 5000)))
                        .ConfigureAwait(true);
                    continue;
                }

                if (stateChanged != null && !stateChanged("succeeded", attempt, null))
                {
                    return new OperationResult(
                        name,
                        false,
                        attempt,
                        new OperationStatePersistenceException(name, true),
                        blocked: true,
                        indeterminate: true,
                        userRetryGeneration: generation);
                }

                return new OperationResult(name, true, attempt, null, userRetryGeneration: generation);
            }

            return new OperationResult(name, false, boundedAttempts, lastError, userRetryGeneration: generation);
        }

        private static void EnsureUniqueOperationNames(IEnumerable<RecoverableOperation> operations)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (RecoverableOperation operation in operations)
            {
                if (!names.Add(operation.Name))
                {
                    throw new ArgumentException("Operation names must be unique. Duplicate: " + operation.Name, nameof(operations));
                }
            }
        }

        private static IReadOnlyList<RecoverableOperation> TopologicallyOrder(
            IReadOnlyList<RecoverableOperation> operations,
            OperationCheckpointStore checkpointStore,
            IDictionary<string, OperationResult> preflightResults)
        {
            var byName = operations.ToDictionary(operation => operation.Name, StringComparer.OrdinalIgnoreCase);
            var index = operations.Select((operation, position) => new { operation.Name, position })
                .ToDictionary(item => item.Name, item => item.position, StringComparer.OrdinalIgnoreCase);

            foreach (RecoverableOperation operation in operations)
            {
                List<string> missing = operation.Dependencies
                    .Where(dependency => !byName.ContainsKey(dependency) && checkpointStore?.IsSucceeded(dependency) != true)
                    .ToList();
                if (missing.Count > 0 && !preflightResults.ContainsKey(operation.Name))
                {
                    preflightResults[operation.Name] = new OperationResult(
                        operation.Name,
                        false,
                        0,
                        new MissingOperationDependencyException(operation.Name, missing),
                        blocked: true,
                        userRetryGeneration: checkpointStore?.GetGeneration(operation.Name) ?? 0);
                }
            }

            var indegree = operations.ToDictionary(operation => operation.Name, _ => 0, StringComparer.OrdinalIgnoreCase);
            var dependents = operations.ToDictionary(
                operation => operation.Name,
                _ => new List<string>(),
                StringComparer.OrdinalIgnoreCase);

            foreach (RecoverableOperation operation in operations)
            {
                foreach (string dependency in operation.Dependencies.Where(byName.ContainsKey))
                {
                    indegree[operation.Name]++;
                    dependents[dependency].Add(operation.Name);
                }
            }

            var ready = new List<string>(indegree.Where(pair => pair.Value == 0).Select(pair => pair.Key));
            ready.Sort((left, right) => index[left].CompareTo(index[right]));
            var ordered = new List<RecoverableOperation>();
            while (ready.Count > 0)
            {
                string current = ready[0];
                ready.RemoveAt(0);
                ordered.Add(byName[current]);
                foreach (string dependent in dependents[current])
                {
                    indegree[dependent]--;
                    if (indegree[dependent] == 0)
                    {
                        ready.Add(dependent);
                        ready.Sort((left, right) => index[left].CompareTo(index[right]));
                    }
                }
            }

            foreach (RecoverableOperation operation in operations.Where(operation => indegree[operation.Name] > 0))
            {
                if (!preflightResults.ContainsKey(operation.Name))
                {
                    preflightResults[operation.Name] = new OperationResult(
                        operation.Name,
                        false,
                        0,
                        new OperationDependencyCycleException(operation.Name),
                        blocked: true,
                        userRetryGeneration: checkpointStore?.GetGeneration(operation.Name) ?? 0);
                }
            }

            return ordered;
        }

        private static Exception Unwrap(Exception exception)
        {
            if (exception is AggregateException aggregateException)
            {
                AggregateException flattened = aggregateException.Flatten();
                if (flattened.InnerExceptions.Count == 1)
                {
                    return Unwrap(flattened.InnerExceptions[0]);
                }
            }

            return exception;
        }

        private static bool IsIndeterminateOutcome(Exception exception)
        {
            Exception unwrapped = Unwrap(exception);
            if (unwrapped is ExternalProcessException processException)
            {
                return processException.Indeterminate;
            }

            return unwrapped is TimeoutException
                || unwrapped is OperationCanceledException
                || (unwrapped is OperationStatePersistenceException persistenceException
                    && persistenceException.ActionStarted);
        }
    }

    public static class ExternalProcessRunner
    {
        private const int OutputLimit = 64 * 1024;
        private static readonly TimeSpan DefaultCommandTimeout = TimeSpan.FromMinutes(30);

        public static Task<ExternalProcessResult> RunAsync(
            string operationName,
            ProcessStartInfo startInfo,
            TimeSpan timeout)
        {
            return RunAsyncCore(operationName, startInfo, timeout, null, false);
        }

        public static async Task<ExternalProcessResult> RunAsync(
            string operationName,
            ProcessStartInfo startInfo,
            TimeSpan timeout,
            char[] standardInput)
        {
            if (standardInput == null)
            {
                throw new ArgumentNullException(nameof(standardInput));
            }

            try
            {
                return await RunAsyncCore(operationName, startInfo, timeout, standardInput, true).ConfigureAwait(false);
            }
            finally
            {
                Array.Clear(standardInput, 0, standardInput.Length);
            }
        }

        private static async Task<ExternalProcessResult> RunAsyncCore(
            string operationName,
            ProcessStartInfo startInfo,
            TimeSpan timeout,
            char[] standardInput,
            bool suppressCapturedOutput)
        {
            if (string.IsNullOrWhiteSpace(operationName))
            {
                throw new ArgumentException("An operation name is required.", nameof(operationName));
            }

            if (startInfo == null)
            {
                throw new ArgumentNullException(nameof(startInfo));
            }

            if (string.IsNullOrWhiteSpace(startInfo.FileName))
            {
                throw new ArgumentException("A process file name is required.", nameof(startInfo));
            }

            if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout));
            }

            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.RedirectStandardInput = standardInput != null;
            startInfo.CreateNoWindow = true;

            var stopwatch = Stopwatch.StartNew();
            using (var job = ProcessJob.Create())
            using (var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true })
            {
                var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                process.Exited += (sender, args) => exited.TrySetResult(true);
                try
                {
                    if (!process.Start())
                    {
                        throw new InvalidOperationException("Process.Start returned false.");
                    }
                }
                catch (Exception ex) when (!RecoveryRunner.IsFatal(ex))
                {
                    throw new ExternalProcessException(operationName, -1, string.Empty, ex.Message, false, true, ex);
                }

                try
                {
                    job.Assign(process);
                }
                catch (Exception ex) when (!RecoveryRunner.IsFatal(ex))
                {
                    TryTerminateUncontainedProcess(process);
                    throw new ExternalProcessException(
                        operationName,
                        -1,
                        string.Empty,
                        suppressCapturedOutput ? string.Empty : ex.Message,
                        false,
                        false,
                        true,
                        ex);
                }

                Task<string> standardOutput = DrainBoundedAsync(process.StandardOutput, OutputLimit);
                Task<string> standardError = DrainBoundedAsync(process.StandardError, OutputLimit);
                Task standardInputWrite = standardInput == null
                    ? Task.CompletedTask
                    : WriteAndCloseStandardInputAsync(process, standardInput);
                if (process.HasExited)
                {
                    exited.TrySetResult(true);
                }

                Task jobEmpty = WaitForJobEmptyAsync(job, timeout);
                Task completeOperation = Task.WhenAll(
                    exited.Task,
                    standardInputWrite,
                    standardOutput,
                    standardError,
                    jobEmpty);
                Task completed = await Task.WhenAny(completeOperation, Task.Delay(timeout)).ConfigureAwait(false);
                if (completed != completeOperation)
                {
                    bool terminationConfirmed = job.TerminateAndConfirmEmpty(TimeSpan.FromSeconds(10));
                    string timedOutOutput = suppressCapturedOutput ? string.Empty : CompletedOutput(standardOutput);
                    string timedOutError = suppressCapturedOutput ? string.Empty : CompletedOutput(standardError);
                    throw new ExternalProcessException(
                        operationName,
                        -1,
                        timedOutOutput,
                        timedOutError,
                        true,
                        terminationConfirmed,
                        null);
                }

                try
                {
                    await completeOperation.ConfigureAwait(false);
                }
                catch (Exception ex) when (!RecoveryRunner.IsFatal(ex))
                {
                    bool terminationConfirmed = job.TerminateAndConfirmEmpty(TimeSpan.FromSeconds(10));
                    string inputErrorOutput = suppressCapturedOutput ? string.Empty : CompletedOutput(standardOutput);
                    string inputError = suppressCapturedOutput ? string.Empty : CompletedOutput(standardError);
                    throw new ExternalProcessException(
                        operationName,
                        process.HasExited ? process.ExitCode : -1,
                        inputErrorOutput,
                        inputError,
                        false,
                        terminationConfirmed,
                        ex);
                }

                string output = await standardOutput.ConfigureAwait(false);
                string error = await standardError.ConfigureAwait(false);
                if (suppressCapturedOutput)
                {
                    output = string.Empty;
                    error = string.Empty;
                }

                stopwatch.Stop();
                if (process.ExitCode != 0)
                {
                    throw new ExternalProcessException(operationName, process.ExitCode, output, error, false, true, null);
                }

                return new ExternalProcessResult(
                    operationName,
                    process.ExitCode,
                    DiagnosticRedactor.RedactAndBound(output, OutputLimit),
                    DiagnosticRedactor.RedactAndBound(error, OutputLimit),
                    stopwatch.Elapsed);
            }
        }

        public static async Task<ExternalProcessResult> RunCommandScriptAsync(
            string operationName,
            string script,
            TimeSpan? timeout = null)
        {
            if (string.IsNullOrWhiteSpace(script))
            {
                throw new ArgumentException("A command script is required.", nameof(script));
            }

            char[] scriptInput = BuildFailFastScript(script).ToCharArray();
            try
            {
                var startInfo = new ProcessStartInfo(
                    GetTrustedCommandProcessor(),
                    "/d /q");
                return await RunAsyncCore(
                    operationName,
                    startInfo,
                    timeout ?? DefaultCommandTimeout,
                    scriptInput,
                    false).ConfigureAwait(false);
            }
            finally
            {
                Array.Clear(scriptInput, 0, scriptInput.Length);
            }
        }

        private static string BuildFailFastScript(string script)
        {
            string normalized = script.Replace("\r\n", "\n").Replace('\r', '\n');
            string[] lines = normalized.Split('\n');
            var output = new StringBuilder();
            output.AppendLine("@echo off");
            output.AppendLine("setlocal EnableExtensions DisableDelayedExpansion");
            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("::", StringComparison.Ordinal) || line.StartsWith("rem ", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (line.StartsWith(":", StringComparison.Ordinal)
                    || line.EndsWith("^", StringComparison.Ordinal)
                    || line == "(" || line == ")")
                {
                    throw new ArgumentException("The command script contains unsupported multi-line batch control syntax.", nameof(script));
                }

                if (line.Equals("@echo off", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                output.AppendLine("call " + rawLine);
                output.AppendLine("if errorlevel 1 exit /b %errorlevel%");
            }

            output.AppendLine("exit /b 0");
            return output.ToString();
        }

        private static string GetTrustedCommandProcessor()
        {
            string systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (string.IsNullOrWhiteSpace(systemRoot) || !Path.IsPathRooted(systemRoot))
            {
                throw new InvalidOperationException("The Windows directory could not be resolved safely.");
            }

            string normalizedRoot = Path.GetFullPath(systemRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string commandProcessor = Path.GetFullPath(Path.Combine(normalizedRoot, "System32", "cmd.exe"));
            string requiredPrefix = normalizedRoot + Path.DirectorySeparatorChar;
            if (!commandProcessor.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase)
                || !File.Exists(commandProcessor))
            {
                throw new FileNotFoundException("The trusted Windows command processor was not found.", commandProcessor);
            }

            return commandProcessor;
        }

        private static async Task WriteAndCloseStandardInputAsync(Process process, char[] standardInput)
        {
            try
            {
                await process.StandardInput.WriteAsync(standardInput, 0, standardInput.Length).ConfigureAwait(false);
                await process.StandardInput.FlushAsync().ConfigureAwait(false);
            }
            finally
            {
                process.StandardInput.Close();
            }
        }

        private static async Task<string> DrainBoundedAsync(StreamReader reader, int limit)
        {
            var output = new StringBuilder(Math.Min(limit, 4096));
            var buffer = new char[4096];
            int read;
            while ((read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
            {
                int available = limit - output.Length;
                if (available > 0)
                {
                    output.Append(buffer, 0, Math.Min(available, read));
                }
            }

            if (output.Length >= limit)
            {
                output.Append("\n[output truncated]");
            }

            return output.ToString();
        }

        private static string CompletedOutput(Task<string> output)
        {
            return output.Status == TaskStatus.RanToCompletion
                ? output.Result
                : "[output stream did not close before the deadline]";
        }

        private static async Task WaitForJobEmptyAsync(ProcessJob job, TimeSpan timeout)
        {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < timeout)
            {
                if (job.IsEmpty())
                {
                    return;
                }

                await Task.Delay(25).ConfigureAwait(false);
            }

            throw new TimeoutException("The external process tree did not exit before the deadline.");
        }

        private static void TryTerminateUncontainedProcess(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(5000);
                }
            }
            catch (Exception ex) when (!RecoveryRunner.IsFatal(ex))
            {
                ErrorLog.Write("Terminate uncontained external process", ex);
            }
        }

        private sealed class ProcessJob : IDisposable
        {
            private const uint JobObjectLimitKillOnJobClose = 0x00002000;
            private const int JobObjectBasicAccountingInformation = 1;
            private const int JobObjectExtendedLimitInformation = 9;
            private readonly SafeJobHandle _handle;

            private ProcessJob(SafeJobHandle handle)
            {
                _handle = handle;
            }

            public static ProcessJob Create()
            {
                SafeJobHandle handle = CreateJobObject(IntPtr.Zero, null);
                if (handle == null || handle.IsInvalid)
                {
                    int error = Marshal.GetLastWin32Error();
                    handle?.Dispose();
                    throw new Win32Exception(error, "A containment job could not be created for the external process.");
                }

                var extended = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                extended.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnJobClose;
                int length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
                if (!SetInformationJobObject(handle, JobObjectExtendedLimitInformation, ref extended, (uint)length))
                {
                    int error = Marshal.GetLastWin32Error();
                    handle.Dispose();
                    throw new Win32Exception(error, "Kill-on-close containment could not be enabled for the external process.");
                }

                return new ProcessJob(handle);
            }

            public void Assign(Process process)
            {
                if (!AssignProcessToJobObject(_handle, process.Handle))
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "The external process could not be assigned to its containment job.");
                }
            }

            public bool IsEmpty()
            {
                var accounting = new JOBOBJECT_BASIC_ACCOUNTING_INFORMATION();
                int length = Marshal.SizeOf(typeof(JOBOBJECT_BASIC_ACCOUNTING_INFORMATION));
                if (!QueryInformationJobObject(
                    _handle,
                    JobObjectBasicAccountingInformation,
                    ref accounting,
                    (uint)length,
                    IntPtr.Zero))
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "The external process containment state could not be verified.");
                }

                return accounting.ActiveProcesses == 0;
            }

            public bool TerminateAndConfirmEmpty(TimeSpan confirmationTimeout)
            {
                try
                {
                    if (!TerminateJobObject(_handle, 1))
                    {
                        ErrorLog.Write(
                            "Terminate timed-out external process tree",
                            new Win32Exception(Marshal.GetLastWin32Error()));
                        return false;
                    }

                    var stopwatch = Stopwatch.StartNew();
                    while (stopwatch.Elapsed < confirmationTimeout)
                    {
                        if (IsEmpty())
                        {
                            return true;
                        }

                        Thread.Sleep(25);
                    }
                }
                catch (Exception ex) when (!RecoveryRunner.IsFatal(ex))
                {
                    ErrorLog.Write("Confirm external process tree termination", ex);
                }

                return false;
            }

            public void Dispose()
            {
                _handle.Dispose();
            }

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            private static extern SafeJobHandle CreateJobObject(IntPtr jobAttributes, string name);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool SetInformationJobObject(
                SafeJobHandle job,
                int informationClass,
                ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION information,
                uint informationLength);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool AssignProcessToJobObject(SafeJobHandle job, IntPtr process);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool TerminateJobObject(SafeJobHandle job, uint exitCode);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool QueryInformationJobObject(
                SafeJobHandle job,
                int informationClass,
                ref JOBOBJECT_BASIC_ACCOUNTING_INFORMATION information,
                uint informationLength,
                IntPtr returnLength);

            private sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
            {
                private SafeJobHandle() : base(true)
                {
                }

                protected override bool ReleaseHandle()
                {
                    return CloseHandle(handle);
                }

                [DllImport("kernel32.dll", SetLastError = true)]
                [return: MarshalAs(UnmanagedType.Bool)]
                private static extern bool CloseHandle(IntPtr handle);
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct JOBOBJECT_BASIC_ACCOUNTING_INFORMATION
            {
                public long TotalUserTime;
                public long TotalKernelTime;
                public long ThisPeriodTotalUserTime;
                public long ThisPeriodTotalKernelTime;
                public uint TotalPageFaultCount;
                public uint TotalProcesses;
                public uint ActiveProcesses;
                public uint TotalTerminatedProcesses;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                public long PerProcessUserTimeLimit;
                public long PerJobUserTimeLimit;
                public uint LimitFlags;
                public UIntPtr MinimumWorkingSetSize;
                public UIntPtr MaximumWorkingSetSize;
                public uint ActiveProcessLimit;
                public UIntPtr Affinity;
                public uint PriorityClass;
                public uint SchedulingClass;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct IO_COUNTERS
            {
                public ulong ReadOperationCount;
                public ulong WriteOperationCount;
                public ulong OtherOperationCount;
                public ulong ReadTransferCount;
                public ulong WriteTransferCount;
                public ulong OtherTransferCount;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
                public IO_COUNTERS IoInfo;
                public UIntPtr ProcessMemoryLimit;
                public UIntPtr JobMemoryLimit;
                public UIntPtr PeakProcessMemoryUsed;
                public UIntPtr PeakJobMemoryUsed;
            }
        }
    }

    internal sealed class OperationCheckpointStore
    {
        private const string Header = "windows-server-tools-recovery-v3";
        private const string LegacyHeader = "windows-server-tools-recovery-v2";
        private const string CorruptionMarkerHeader = "windows-server-tools-corrupt-v1";
        private const int MaxFileBytes = 1024 * 1024;
        private const int MaxRecords = 1024;
        private const int MaxLineLength = 16384;
        private const int MaxNameLength = 256;
        private const int MaxErrorTypeLength = 256;
        private const int MaxErrorSummaryLength = 2048;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private readonly object _sync = new object();
        private readonly string _path;
        private Dictionary<string, OperationCheckpointRecord> _records;
        private string _lastPreparedRequestId;
        private string _lastPreparedRequestDigest;

        public OperationCheckpointStore(string path)
        {
            _path = Path.GetFullPath(path ?? throw new ArgumentNullException(nameof(path)));
            CheckpointLoadResult load = LoadBest(_path, true);
            _records = load.Records;
            IsCorrupt = load.IsCorrupt;
            CorruptionEvidenceToken = load.CorruptionEvidenceToken;
            _lastPreparedRequestId = load.LastPreparedRequestId ?? string.Empty;
            _lastPreparedRequestDigest = load.LastPreparedRequestDigest ?? string.Empty;
            if (load.FoundValidSnapshot
                && !string.Equals(load.SourcePath, _path, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    Save(_records);
                }
                catch (Exception ex) when (!RecoveryRunner.IsFatal(ex))
                {
                    ErrorLog.Write("Restore interrupted recovery state", ex);
                }
            }
        }

        public bool IsCorrupt { get; private set; }
        public string CorruptionEvidenceToken { get; private set; }

        public bool IsSucceeded(string operationName)
        {
            lock (_sync)
            {
                return _records.TryGetValue(operationName, out OperationCheckpointRecord record)
                    && string.Equals(record.State, "succeeded", StringComparison.Ordinal);
            }
        }

        public int GetAttempts(string operationName)
        {
            lock (_sync)
            {
                return _records.TryGetValue(operationName, out OperationCheckpointRecord record) ? record.Attempts : 0;
            }
        }

        public int GetGeneration(string operationName)
        {
            lock (_sync)
            {
                return _records.TryGetValue(operationName, out OperationCheckpointRecord record) ? record.Generation : 0;
            }
        }

        public string GetState(string operationName)
        {
            lock (_sync)
            {
                return _records.TryGetValue(operationName, out OperationCheckpointRecord record)
                    ? record.State
                    : string.Empty;
            }
        }

        public string GetErrorSummary(string operationName)
        {
            lock (_sync)
            {
                return _records.TryGetValue(operationName, out OperationCheckpointRecord record)
                    ? record.ErrorSummary
                    : string.Empty;
            }
        }

        public bool Record(string operationName, string state, int attempts, int generation, Exception error)
        {
            lock (_sync)
            {
                Dictionary<string, OperationCheckpointRecord> before = Clone(_records);
                try
                {
                    CheckpointLoadResult latest = LoadBest(_path, false);
                    if (latest.IsCorrupt)
                    {
                        return false;
                    }

                    if (latest.FoundValidSnapshot)
                    {
                        _records = latest.Records;
                        _lastPreparedRequestId = latest.LastPreparedRequestId ?? string.Empty;
                        _lastPreparedRequestDigest = latest.LastPreparedRequestDigest ?? string.Empty;
                    }

                    var record = new OperationCheckpointRecord
                    {
                        Name = operationName,
                        State = state,
                        Attempts = attempts,
                        Generation = generation,
                        UpdatedAt = DateTimeOffset.UtcNow,
                        ErrorType = error?.GetType().FullName ?? string.Empty,
                        ErrorSummary = DiagnosticRedactor.Summarize(error, MaxErrorSummaryLength)
                    };
                    ValidateRecord(record);
                    _records[operationName] = record;
                    Save(_records, string.Empty, string.Empty);
                    _lastPreparedRequestId = string.Empty;
                    _lastPreparedRequestDigest = string.Empty;
                    return true;
                }
                catch (Exception ex) when (!RecoveryRunner.IsFatal(ex))
                {
                    _records = before;
                    ErrorLog.Write("Persist recovery state", ex);
                    return false;
                }
            }
        }

        public bool ResetForUserRetry(
            string operationName,
            int expectedGeneration,
            int expectedAttempt)
        {
            lock (_sync)
            {
                if (IsCorrupt)
                {
                    return false;
                }

                if (!_records.TryGetValue(operationName, out OperationCheckpointRecord record)
                    || !string.Equals(record.State, "failed", StringComparison.Ordinal)
                    || record.Generation != expectedGeneration
                    || record.Attempts != expectedAttempt)
                {
                    return false;
                }

                int nextGeneration = record.Generation + 1;
                return Record(operationName, "pending", 0, nextGeneration, null);
            }
        }

        public bool ReconcileIndeterminate(
            string operationName,
            int expectedGeneration,
            int expectedAttempt,
            IndeterminateReconciliationOutcome outcome)
        {
            lock (_sync)
            {
                if (IsCorrupt
                    || !_records.TryGetValue(operationName, out OperationCheckpointRecord record)
                    || (!string.Equals(record.State, "running", StringComparison.Ordinal)
                        && !string.Equals(record.State, "indeterminate", StringComparison.Ordinal))
                    || record.Generation != expectedGeneration
                    || record.Attempts != expectedAttempt)
                {
                    return false;
                }

                switch (outcome)
                {
                    case IndeterminateReconciliationOutcome.ConfirmedSucceeded:
                        return Record(
                            operationName,
                            "succeeded",
                            record.Attempts,
                            record.Generation,
                            null);
                    case IndeterminateReconciliationOutcome.ConfirmedNotAppliedAndStopped:
                        return Record(
                            operationName,
                            "pending",
                            0,
                            record.Generation + 1,
                            null);
                    default:
                        return false;
                }
            }
        }

        public bool PrepareReviewedRetry(
            string requestId,
            IReadOnlyList<ReviewedOperationPreparation> operations)
        {
            lock (_sync)
            {
                if (IsCorrupt || operations == null || operations.Count == 0)
                {
                    return false;
                }

                string requestDigest = ComputePreparationDigest(requestId, operations);
                if (string.Equals(_lastPreparedRequestId, requestId, StringComparison.Ordinal))
                {
                    return string.Equals(_lastPreparedRequestDigest, requestDigest, StringComparison.OrdinalIgnoreCase)
                        && ArePreparedStatesPresent(operations);
                }

                Dictionary<string, OperationCheckpointRecord> prepared = Clone(_records);
                foreach (ReviewedOperationPreparation operation in operations)
                {
                    if (!prepared.TryGetValue(operation.Name, out OperationCheckpointRecord record)
                        || !string.Equals(record.State, StateName(operation.ExpectedState), StringComparison.Ordinal)
                        || record.Generation != operation.ExpectedGeneration
                        || record.Attempts != operation.ExpectedAttempt)
                    {
                        return false;
                    }
                }

                foreach (ReviewedOperationPreparation operation in operations)
                {
                    OperationCheckpointRecord record = prepared[operation.Name];
                    if (operation.ExpectedState == ReviewedOperationState.Failed
                        || operation.ReconciliationOutcome == IndeterminateReconciliationOutcome.ConfirmedNotAppliedAndStopped)
                    {
                        record.State = "pending";
                        record.Attempts = 0;
                        record.Generation++;
                    }
                    else if (operation.ReconciliationOutcome == IndeterminateReconciliationOutcome.ConfirmedSucceeded)
                    {
                        record.State = "succeeded";
                    }
                    else
                    {
                        return false;
                    }

                    record.UpdatedAt = DateTimeOffset.UtcNow;
                    record.ErrorType = string.Empty;
                    record.ErrorSummary = string.Empty;
                    ValidateRecord(record);
                }

                try
                {
                    Save(prepared, requestId, requestDigest);
                    _records = prepared;
                    _lastPreparedRequestId = requestId;
                    _lastPreparedRequestDigest = requestDigest;
                    return true;
                }
                catch (Exception ex) when (!RecoveryRunner.IsFatal(ex))
                {
                    ErrorLog.Write("Prepare reviewed recovery retry", ex);
                    return false;
                }
            }
        }

        public bool RepairCorruptState(string expectedCorruptionEvidenceToken)
        {
            lock (_sync)
            {
                string markerPath = CorruptionMarkerPath(_path);
                string currentToken = ReadCorruptionEvidenceToken(markerPath);
                if (!IsCorrupt
                    || string.IsNullOrWhiteSpace(currentToken)
                    || !FixedTimeEquals(currentToken, expectedCorruptionEvidenceToken))
                {
                    return false;
                }

                try
                {
                    List<string> evidencePaths = CorruptionEvidencePaths(_path)
                        .Where(File.Exists)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    string archiveDirectory = Path.Combine(
                        Path.GetDirectoryName(_path),
                        Path.GetFileName(_path)
                            + ".recovery-archive."
                            + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture)
                            + "."
                            + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(archiveDirectory);
                    var manifest = new StringBuilder("windows-server-tools-recovery-archive-v1\n");
                    for (int archiveIndex = 0; archiveIndex < evidencePaths.Count; archiveIndex++)
                    {
                        string evidencePath = evidencePaths[archiveIndex];
                        byte[] sourceBytes = File.ReadAllBytes(evidencePath);
                        string sourceDigest = ComputeSha256Hex(sourceBytes);
                        string archivePath = Path.Combine(
                            archiveDirectory,
                            archiveIndex.ToString("D4", CultureInfo.InvariantCulture) + ".evidence");
                        using (var archiveStream = new FileStream(
                            archivePath,
                            FileMode.CreateNew,
                            FileAccess.Write,
                            FileShare.None,
                            4096,
                            FileOptions.WriteThrough))
                        {
                            archiveStream.Write(sourceBytes, 0, sourceBytes.Length);
                            archiveStream.Flush(true);
                        }

                        byte[] archivedBytes = File.ReadAllBytes(archivePath);
                        if (!FixedTimeEquals(sourceDigest, ComputeSha256Hex(archivedBytes)))
                        {
                            throw new IOException("A recovery evidence archive could not be verified.");
                        }

                        manifest.Append(archiveIndex.ToString("D4", CultureInfo.InvariantCulture)).Append('|')
                            .Append(Encode(Path.GetFileName(evidencePath))).Append('|')
                            .Append(sourceBytes.Length.ToString(CultureInfo.InvariantCulture)).Append('|')
                            .Append(sourceDigest).Append('\n');
                    }

                    WriteNewAndFlush(
                        Path.Combine(archiveDirectory, "manifest.txt"),
                        StrictUtf8.GetBytes(manifest.ToString()));
                    if (!FixedTimeEquals(currentToken, ReadCorruptionEvidenceToken(markerPath)))
                    {
                        throw new IOException("The corrupt recovery evidence changed while it was being archived.");
                    }

                    var empty = new Dictionary<string, OperationCheckpointRecord>(StringComparer.OrdinalIgnoreCase);
                    Save(empty, string.Empty, string.Empty);
                    CheckpointSnapshot verified = Parse(_path);
                    if (verified.Records.Count != 0)
                    {
                        throw new InvalidDataException("The repaired recovery state was not empty.");
                    }

                    foreach (string liveCandidate in CandidatePaths(_path)
                        .Where(candidate => !string.Equals(candidate, _path, StringComparison.OrdinalIgnoreCase))
                        .Where(File.Exists)
                        .Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        File.Delete(liveCandidate);
                    }

                    File.Delete(markerPath);
                    _records = empty;
                    _lastPreparedRequestId = string.Empty;
                    _lastPreparedRequestDigest = string.Empty;
                    IsCorrupt = false;
                    CorruptionEvidenceToken = string.Empty;
                    return true;
                }
                catch (Exception ex) when (!RecoveryRunner.IsFatal(ex))
                {
                    ErrorLog.Write("Repair corrupt recovery state", ex);
                    return false;
                }
            }
        }

        private static void WriteNewAndFlush(string path, byte[] payload)
        {
            using (var stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.WriteThrough))
            {
                stream.Write(payload, 0, payload.Length);
                stream.Flush(true);
            }
        }

        public bool ClearCompleted()
        {
            lock (_sync)
            {
                if (IsCorrupt || _records.Values.Any(record => !string.Equals(record.State, "succeeded", StringComparison.Ordinal)))
                {
                    return false;
                }

                List<string> candidates = CandidatePaths(_path)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                foreach (string candidate in candidates.Where(candidate =>
                    !string.Equals(candidate, _path, StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        if (File.Exists(candidate))
                        {
                            File.Delete(candidate);
                        }
                    }
                    catch (Exception ex) when (!RecoveryRunner.IsFatal(ex))
                    {
                        ErrorLog.Write("Clear completed recovery state", ex);
                        return false;
                    }
                }

                try
                {
                    if (File.Exists(_path))
                    {
                        File.Delete(_path);
                    }
                }
                catch (Exception ex) when (!RecoveryRunner.IsFatal(ex))
                {
                    ErrorLog.Write("Clear completed recovery state", ex);
                    return false;
                }

                _records.Clear();
                return true;
            }
        }

        private static CheckpointLoadResult LoadBest(string path, bool quarantineInvalid)
        {
            string markerPath = CorruptionMarkerPath(path);
            if (File.Exists(markerPath))
            {
                return CheckpointLoadResult.Corrupt(ReadCorruptionEvidenceToken(markerPath));
            }

            List<string> candidates = CandidatePaths(path).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (candidates.Count == 0)
            {
                return CheckpointLoadResult.Empty();
            }

            var valid = new List<CheckpointSnapshot>();
            var invalid = new List<string>();
            foreach (string candidate in candidates)
            {
                try
                {
                    valid.Add(Parse(candidate));
                }
                catch (Exception ex) when (!RecoveryRunner.IsFatal(ex))
                {
                    invalid.Add(candidate);
                    ErrorLog.Write("Read recovery state", ex);
                }
            }

            if (invalid.Count > 0)
            {
                string evidenceToken = PersistCorruptionMarker(path);
                return CheckpointLoadResult.Corrupt(
                    string.IsNullOrWhiteSpace(evidenceToken)
                        ? evidenceToken
                        : ReadCorruptionEvidenceToken(CorruptionMarkerPath(path)));
            }

            CheckpointSnapshot primary = valid.FirstOrDefault(snapshot =>
                string.Equals(snapshot.SourcePath, path, StringComparison.OrdinalIgnoreCase));
            if (primary != null)
            {
                return CheckpointLoadResult.Valid(
                    Clone(primary.Records),
                    primary.SourcePath,
                    primary.LastPreparedRequestId,
                    primary.LastPreparedRequestDigest);
            }

            if (valid.Count != 1)
            {
                return CheckpointLoadResult.Corrupt(PersistCorruptionMarker(path));
            }

            CheckpointSnapshot soleCandidate = valid[0];
            return CheckpointLoadResult.Valid(
                Clone(soleCandidate.Records),
                soleCandidate.SourcePath,
                soleCandidate.LastPreparedRequestId,
                soleCandidate.LastPreparedRequestDigest);
        }

        private static CheckpointSnapshot Parse(string path)
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length <= 0 || info.Length > MaxFileBytes)
            {
                throw new InvalidDataException("The recovery state file has an invalid size.");
            }

            string text;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan))
            using (var reader = new StreamReader(stream, StrictUtf8, false, 4096, false))
            {
                text = reader.ReadToEnd();
            }

            if (text.IndexOf('\r') >= 0 || (text.Length > 0 && text[0] == '\uFEFF'))
            {
                throw new InvalidDataException("The recovery state is not in canonical UTF-8 line format.");
            }

            string normalized = text;
            if (!normalized.EndsWith("\n", StringComparison.Ordinal))
            {
                throw new InvalidDataException("The recovery state is missing its final commit record.");
            }

            string[] splitLines = normalized.Split('\n');
            if (splitLines.Length < 4 || splitLines[splitLines.Length - 1].Length != 0)
            {
                throw new InvalidDataException("The recovery state snapshot is incomplete.");
            }

            string[] lines = splitLines.Take(splitLines.Length - 1).ToArray();
            if (string.Equals(lines[0], LegacyHeader, StringComparison.Ordinal)
                || !string.Equals(lines[0], Header, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The recovery state has an unsupported or unprotected format.");
            }

            if (lines.Any(line => line.Length > MaxLineLength))
            {
                throw new InvalidDataException("The recovery state contains an oversized record.");
            }

            string[] metadata = lines[1].Split('|');
            if (metadata.Length != 5 || !string.Equals(metadata[0], "snapshot", StringComparison.Ordinal))
            {
                throw new InvalidDataException("The recovery state snapshot metadata is incomplete.");
            }

            if (!DateTimeOffset.TryParseExact(
                metadata[1],
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTimeOffset writtenAt)
                || !Guid.TryParseExact(metadata[2], "N", out Guid ignoredSnapshotId))
            {
                throw new InvalidDataException("The recovery state snapshot metadata is invalid.");
            }

            string lastPreparedRequestId = Decode(metadata[3]);
            string lastPreparedRequestDigest = metadata[4];
            if (!string.Equals(Encode(lastPreparedRequestId), metadata[3], StringComparison.Ordinal)
                || lastPreparedRequestId.Length > 128
                || (lastPreparedRequestId.Length == 0) != (lastPreparedRequestDigest.Length == 0)
                || (lastPreparedRequestDigest.Length > 0 && !IsSha256(lastPreparedRequestDigest)))
            {
                throw new InvalidDataException("The recovery state reviewed-request metadata is invalid.");
            }

            string[] commit = lines[lines.Length - 1].Split('|');
            if (commit.Length != 3
                || !string.Equals(commit[0], "commit", StringComparison.Ordinal)
                || !int.TryParse(commit[1], NumberStyles.None, CultureInfo.InvariantCulture, out int declaredRecordCount)
                || declaredRecordCount < 0
                || declaredRecordCount > MaxRecords
                || !IsSha256(commit[2]))
            {
                throw new InvalidDataException("The recovery state commit record is invalid.");
            }

            string[] recordLines = lines.Skip(2).Take(lines.Length - 3).ToArray();
            if (recordLines.Length != declaredRecordCount || recordLines.Any(line => line.Length == 0))
            {
                throw new InvalidDataException("The recovery state record count does not match its commit record.");
            }

            var canonicalPayload = new StringBuilder();
            canonicalPayload.Append(Header).Append('\n').Append(lines[1]).Append('\n');
            foreach (string recordLine in recordLines)
            {
                canonicalPayload.Append(recordLine).Append('\n');
            }

            string actualDigest = ComputeSha256Hex(StrictUtf8.GetBytes(canonicalPayload.ToString()));
            if (!FixedTimeEquals(actualDigest, commit[2]))
            {
                throw new InvalidDataException("The recovery state payload digest does not match its commit record.");
            }

            var records = new Dictionary<string, OperationCheckpointRecord>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in recordLines)
            {
                string[] fields = line.Split('|');
                if (fields.Length != 7
                    || !int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out int attempts)
                    || !int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out int generation)
                    || !DateTimeOffset.TryParseExact(
                        fields[3],
                        "O",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out DateTimeOffset updatedAt))
                {
                    throw new InvalidDataException("The recovery state contains an invalid record.");
                }

                var record = new OperationCheckpointRecord
                {
                    State = fields[0],
                    Attempts = attempts,
                    Generation = generation,
                    UpdatedAt = updatedAt,
                    Name = Decode(fields[4]),
                    ErrorType = Decode(fields[5]),
                    ErrorSummary = Decode(fields[6])
                };
                if (!string.Equals(Encode(record.Name), fields[4], StringComparison.Ordinal)
                    || !string.Equals(Encode(record.ErrorType), fields[5], StringComparison.Ordinal)
                    || !string.Equals(Encode(record.ErrorSummary), fields[6], StringComparison.Ordinal))
                {
                    throw new InvalidDataException("The recovery state contains non-canonical encoded text.");
                }

                ValidateRecord(record);
                if (records.ContainsKey(record.Name))
                {
                    throw new InvalidDataException("The recovery state contains duplicate operation names.");
                }

                records.Add(record.Name, record);
            }

            string[] canonicalOrder = records.Values
                .OrderBy(record => record.Name, StringComparer.OrdinalIgnoreCase)
                .Select(SerializeRecord)
                .ToArray();
            if (!recordLines.SequenceEqual(canonicalOrder, StringComparer.Ordinal))
            {
                throw new InvalidDataException("The recovery state records are not in canonical order.");
            }

            return new CheckpointSnapshot
            {
                Records = records,
                WrittenAt = writtenAt,
                LastWriteTimeUtc = info.LastWriteTimeUtc,
                SourcePath = path,
                LastPreparedRequestId = lastPreparedRequestId,
                LastPreparedRequestDigest = lastPreparedRequestDigest
            };
        }

        private void Save(Dictionary<string, OperationCheckpointRecord> records)
        {
            Save(records, _lastPreparedRequestId, _lastPreparedRequestDigest);
        }

        private void Save(
            Dictionary<string, OperationCheckpointRecord> records,
            string lastPreparedRequestId,
            string lastPreparedRequestDigest)
        {
            string directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            foreach (OperationCheckpointRecord record in records.Values)
            {
                ValidateRecord(record);
            }

            lastPreparedRequestId = lastPreparedRequestId ?? string.Empty;
            lastPreparedRequestDigest = lastPreparedRequestDigest ?? string.Empty;
            if (lastPreparedRequestId.Length > 128
                || (lastPreparedRequestId.Length == 0) != (lastPreparedRequestDigest.Length == 0)
                || (lastPreparedRequestDigest.Length > 0 && !IsSha256(lastPreparedRequestDigest)))
            {
                throw new InvalidDataException("The reviewed-request metadata is invalid.");
            }

            string temporaryPath = _path + ".tmp." + Guid.NewGuid().ToString("N");
            string backupPath = _path + ".bak." + Guid.NewGuid().ToString("N");
            byte[] payload = Serialize(records, lastPreparedRequestId, lastPreparedRequestDigest);
            if (payload.Length > MaxFileBytes)
            {
                throw new InvalidDataException("The recovery state exceeds its size limit.");
            }

            try
            {
                using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.WriteThrough))
                {
                    stream.Write(payload, 0, payload.Length);
                    stream.Flush(true);
                }

                Parse(temporaryPath);
                if (File.Exists(_path))
                {
                    File.Replace(temporaryPath, _path, backupPath, true);
                }
                else
                {
                    File.Move(temporaryPath, _path);
                }

                Parse(_path);
                if (File.Exists(backupPath))
                {
                    try
                    {
                        File.Delete(backupPath);
                    }
                    catch (Exception ex) when (!RecoveryRunner.IsFatal(ex))
                    {
                        ErrorLog.Write("Remove previous recovery state backup", ex);
                    }
                }

                _lastPreparedRequestId = lastPreparedRequestId;
                _lastPreparedRequestDigest = lastPreparedRequestDigest;
            }
            finally
            {
                TryDelete(temporaryPath);
            }
        }

        private static byte[] Serialize(
            Dictionary<string, OperationCheckpointRecord> records,
            string lastPreparedRequestId,
            string lastPreparedRequestDigest)
        {
            var metadata = new StringBuilder();
            metadata.Append("snapshot|")
                .Append(DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture))
                .Append('|')
                .Append(Guid.NewGuid().ToString("N"))
                .Append('|')
                .Append(Encode(lastPreparedRequestId))
                .Append('|')
                .Append(lastPreparedRequestDigest);

            List<string> recordLines = records.Values
                .OrderBy(record => record.Name, StringComparer.OrdinalIgnoreCase)
                .Select(SerializeRecord)
                .ToList();
            var canonicalPayload = new StringBuilder();
            canonicalPayload.Append(Header).Append('\n').Append(metadata).Append('\n');
            foreach (string recordLine in recordLines)
            {
                canonicalPayload.Append(recordLine).Append('\n');
            }

            string digest = ComputeSha256Hex(StrictUtf8.GetBytes(canonicalPayload.ToString()));
            var snapshot = new StringBuilder();
            snapshot.Append(canonicalPayload)
                .Append("commit|")
                .Append(recordLines.Count.ToString(CultureInfo.InvariantCulture))
                .Append('|')
                .Append(digest)
                .Append('\n');
            return StrictUtf8.GetBytes(snapshot.ToString());
        }

        private static string SerializeRecord(OperationCheckpointRecord record)
        {
            return new StringBuilder()
                .Append(record.State).Append('|')
                .Append(record.Attempts.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(record.Generation.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(record.UpdatedAt.ToString("O", CultureInfo.InvariantCulture)).Append('|')
                .Append(Encode(record.Name)).Append('|')
                .Append(Encode(record.ErrorType)).Append('|')
                .Append(Encode(record.ErrorSummary))
                .ToString();
        }

        private static void ValidateRecord(OperationCheckpointRecord record)
        {
            string[] states = { "pending", "running", "retrying", "failed", "succeeded", "blocked", "indeterminate" };
            if (record == null
                || !states.Contains(record.State, StringComparer.Ordinal)
                || string.IsNullOrWhiteSpace(record.Name)
                || record.Name.Length > MaxNameLength
                || record.Attempts < 0
                || record.Generation < 0
                || record.ErrorType == null
                || record.ErrorType.Length > MaxErrorTypeLength
                || record.ErrorSummary == null
                || record.ErrorSummary.Length > MaxErrorSummaryLength
                || ((record.State == "running" || record.State == "retrying" || record.State == "failed" || record.State == "succeeded" || record.State == "indeterminate") && record.Attempts < 1)
                || ((record.State == "pending" || record.State == "blocked") && record.Attempts != 0))
            {
                throw new InvalidDataException("The recovery state contains an invalid operation record.");
            }
        }

        private static IEnumerable<string> CandidatePaths(string path)
        {
            yield return path;
            yield return path + ".tmp";
            yield return path + ".bak";
            string directory = Path.GetDirectoryName(path);
            string fileName = Path.GetFileName(path);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                foreach (string candidate in Directory.GetFiles(directory, fileName + ".tmp.*"))
                {
                    yield return candidate;
                }

                foreach (string candidate in Directory.GetFiles(directory, fileName + ".bak.*"))
                {
                    yield return candidate;
                }
            }
        }

        private static IEnumerable<string> CorruptionEvidencePaths(string path)
        {
            foreach (string candidate in CandidatePaths(path))
            {
                yield return candidate;
            }

            yield return CorruptionMarkerPath(path);
            string directory = Path.GetDirectoryName(path);
            string fileName = Path.GetFileName(path);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                foreach (string candidate in Directory.GetFiles(directory, fileName + ".corrupt-*"))
                {
                    yield return candidate;
                }
            }
        }

        private static Dictionary<string, OperationCheckpointRecord> Clone(
            IDictionary<string, OperationCheckpointRecord> records)
        {
            return records.ToDictionary(
                pair => pair.Key,
                pair => new OperationCheckpointRecord
                {
                    Name = pair.Value.Name,
                    State = pair.Value.State,
                    Attempts = pair.Value.Attempts,
                    Generation = pair.Value.Generation,
                    UpdatedAt = pair.Value.UpdatedAt,
                    ErrorType = pair.Value.ErrorType,
                    ErrorSummary = pair.Value.ErrorSummary
                },
                StringComparer.OrdinalIgnoreCase);
        }

        private bool ArePreparedStatesPresent(IReadOnlyList<ReviewedOperationPreparation> operations)
        {
            foreach (ReviewedOperationPreparation operation in operations)
            {
                if (!_records.TryGetValue(operation.Name, out OperationCheckpointRecord record))
                {
                    return false;
                }

                bool shouldBeSucceeded = operation.ReconciliationOutcome
                    == IndeterminateReconciliationOutcome.ConfirmedSucceeded;
                string expectedState = shouldBeSucceeded ? "succeeded" : "pending";
                int expectedGeneration = shouldBeSucceeded
                    ? operation.ExpectedGeneration
                    : operation.ExpectedGeneration + 1;
                int expectedAttempts = shouldBeSucceeded ? operation.ExpectedAttempt : 0;
                if (!string.Equals(record.State, expectedState, StringComparison.Ordinal)
                    || record.Generation != expectedGeneration
                    || record.Attempts != expectedAttempts)
                {
                    return false;
                }
            }

            return true;
        }

        private static string StateName(ReviewedOperationState state)
        {
            switch (state)
            {
                case ReviewedOperationState.Failed:
                    return "failed";
                case ReviewedOperationState.Running:
                    return "running";
                case ReviewedOperationState.Indeterminate:
                    return "indeterminate";
                default:
                    throw new ArgumentOutOfRangeException(nameof(state));
            }
        }

        private static string ComputePreparationDigest(
            string requestId,
            IEnumerable<ReviewedOperationPreparation> operations)
        {
            var canonical = new StringBuilder();
            canonical.Append(Encode(requestId)).Append('\n');
            foreach (ReviewedOperationPreparation operation in operations.OrderBy(
                item => item.Name,
                StringComparer.OrdinalIgnoreCase))
            {
                canonical.Append(Encode(operation.Name)).Append('|')
                    .Append(((int)operation.ExpectedState).ToString(CultureInfo.InvariantCulture)).Append('|')
                    .Append(operation.ExpectedGeneration.ToString(CultureInfo.InvariantCulture)).Append('|')
                    .Append(operation.ExpectedAttempt.ToString(CultureInfo.InvariantCulture)).Append('|')
                    .Append(operation.ReconciliationOutcome.HasValue
                        ? ((int)operation.ReconciliationOutcome.Value).ToString(CultureInfo.InvariantCulture)
                        : string.Empty)
                    .Append('\n');
            }

            return ComputeSha256Hex(StrictUtf8.GetBytes(canonical.ToString()));
        }

        private static string ComputeSha256Hex(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty);
            }
        }

        private static bool IsSha256(string value)
        {
            return value != null
                && value.Length == 64
                && value.All(character =>
                    (character >= '0' && character <= '9')
                    || (character >= 'a' && character <= 'f')
                    || (character >= 'A' && character <= 'F'));
        }

        private static bool FixedTimeEquals(string left, string right)
        {
            byte[] leftBytes = StrictUtf8.GetBytes(left ?? string.Empty);
            byte[] rightBytes = StrictUtf8.GetBytes(right ?? string.Empty);
            int different = leftBytes.Length ^ rightBytes.Length;
            int count = Math.Max(leftBytes.Length, rightBytes.Length);
            for (int index = 0; index < count; index++)
            {
                byte leftByte = index < leftBytes.Length ? leftBytes[index] : (byte)0;
                byte rightByte = index < rightBytes.Length ? rightBytes[index] : (byte)0;
                different |= leftByte ^ rightByte;
            }

            return different == 0;
        }

        private static string Encode(string value)
        {
            return Convert.ToBase64String(StrictUtf8.GetBytes(value ?? string.Empty));
        }

        private static string Decode(string value)
        {
            return StrictUtf8.GetString(Convert.FromBase64String(value));
        }

        private static void Quarantine(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Move(path, path + ".corrupt-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N"));
                }
            }
            catch (Exception ex) when (!RecoveryRunner.IsFatal(ex))
            {
                ErrorLog.Write("Quarantine invalid recovery state", ex);
            }
        }

        private static string CorruptionMarkerPath(string path)
        {
            return path + ".corrupt";
        }

        private static string PersistCorruptionMarker(string path)
        {
            string markerPath = CorruptionMarkerPath(path);
            try
            {
                if (File.Exists(markerPath))
                {
                    return ReadCorruptionEvidenceToken(markerPath);
                }

                string directory = Path.GetDirectoryName(markerPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using (var stream = new FileStream(
                    markerPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.Read,
                    4096,
                    FileOptions.WriteThrough))
                {
                    string evidenceToken = Guid.NewGuid().ToString("N");
                    byte[] payload = StrictUtf8.GetBytes(
                        CorruptionMarkerHeader
                        + "|"
                        + evidenceToken
                        + "|"
                        + DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
                        + "\n");
                    stream.Write(payload, 0, payload.Length);
                    stream.Flush(true);
                }

                return ReadCorruptionEvidenceToken(markerPath);
            }
            catch (IOException) when (File.Exists(markerPath))
            {
                return ReadCorruptionEvidenceToken(markerPath);
            }
            catch (Exception ex) when (!RecoveryRunner.IsFatal(ex))
            {
                ErrorLog.Write("Persist corrupt recovery state marker", ex);
                return string.Empty;
            }
        }

        private static string ReadCorruptionEvidenceToken(string markerPath)
        {
            try
            {
                var info = new FileInfo(markerPath);
                if (!info.Exists)
                {
                    return string.Empty;
                }

                if (info.Length <= 0 || info.Length > 4096)
                {
                    return "file-" + ComputeCorruptionManifestDigest(
                        markerPath.Substring(0, markerPath.Length - ".corrupt".Length),
                        File.ReadAllBytes(markerPath));
                }

                byte[] payload = File.ReadAllBytes(markerPath);
                string text = StrictUtf8.GetString(payload).Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n');
                string[] fields = text.Split('|');
                if (fields.Length == 3
                    && string.Equals(fields[0], CorruptionMarkerHeader, StringComparison.Ordinal)
                    && Guid.TryParseExact(fields[1], "N", out Guid ignoredEvidenceId)
                    && DateTimeOffset.TryParseExact(
                        fields[2],
                        "O",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out DateTimeOffset ignoredWrittenAt))
                {
                    return fields[1]
                        + "."
                        + ComputeCorruptionManifestDigest(
                            markerPath.Substring(0, markerPath.Length - ".corrupt".Length),
                            payload);
                }

                return "sha256-" + ComputeCorruptionManifestDigest(
                    markerPath.Substring(0, markerPath.Length - ".corrupt".Length),
                    payload);
            }
            catch (Exception ex) when (!RecoveryRunner.IsFatal(ex))
            {
                ErrorLog.Write("Read corrupt recovery state marker", ex);
                return string.Empty;
            }
        }

        private static string ComputeCorruptionManifestDigest(string statePath, byte[] markerPayload)
        {
            var manifest = new StringBuilder("windows-server-tools-corrupt-evidence-v1\n");
            manifest.Append("marker|")
                .Append((markerPayload ?? Array.Empty<byte>()).Length.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(ComputeSha256Hex(markerPayload ?? Array.Empty<byte>())).Append('\n');
            foreach (string candidate in CorruptionEvidencePaths(statePath)
                .Where(File.Exists)
                .Where(candidate => !string.Equals(candidate, CorruptionMarkerPath(statePath), StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(candidate => Path.GetFileName(candidate), StringComparer.OrdinalIgnoreCase))
            {
                byte[] bytes = File.ReadAllBytes(candidate);
                manifest.Append(Encode(Path.GetFileName(candidate))).Append('|')
                    .Append(bytes.Length.ToString(CultureInfo.InvariantCulture)).Append('|')
                    .Append(ComputeSha256Hex(bytes)).Append('\n');
            }

            return ComputeSha256Hex(StrictUtf8.GetBytes(manifest.ToString()));
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        private sealed class CheckpointSnapshot
        {
            public Dictionary<string, OperationCheckpointRecord> Records { get; set; }
            public DateTimeOffset WrittenAt { get; set; }
            public DateTime LastWriteTimeUtc { get; set; }
            public string SourcePath { get; set; }
            public string LastPreparedRequestId { get; set; }
            public string LastPreparedRequestDigest { get; set; }
        }

        private sealed class CheckpointLoadResult
        {
            public Dictionary<string, OperationCheckpointRecord> Records { get; private set; }
            public bool IsCorrupt { get; private set; }
            public bool FoundValidSnapshot { get; private set; }
            public string SourcePath { get; private set; }
            public string CorruptionEvidenceToken { get; private set; }
            public string LastPreparedRequestId { get; private set; }
            public string LastPreparedRequestDigest { get; private set; }

            public static CheckpointLoadResult Empty()
            {
                return new CheckpointLoadResult
                {
                    Records = new Dictionary<string, OperationCheckpointRecord>(StringComparer.OrdinalIgnoreCase)
                };
            }

            public static CheckpointLoadResult Corrupt(string corruptionEvidenceToken)
            {
                return new CheckpointLoadResult
                {
                    Records = new Dictionary<string, OperationCheckpointRecord>(StringComparer.OrdinalIgnoreCase),
                    IsCorrupt = true,
                    CorruptionEvidenceToken = corruptionEvidenceToken ?? string.Empty
                };
            }

            public static CheckpointLoadResult Valid(
                Dictionary<string, OperationCheckpointRecord> records,
                string sourcePath,
                string lastPreparedRequestId,
                string lastPreparedRequestDigest)
            {
                return new CheckpointLoadResult
                {
                    Records = records,
                    FoundValidSnapshot = true,
                    SourcePath = sourcePath,
                    LastPreparedRequestId = lastPreparedRequestId ?? string.Empty,
                    LastPreparedRequestDigest = lastPreparedRequestDigest ?? string.Empty
                };
            }
        }
    }

    internal sealed class OperationCheckpointRecord
    {
        public string Name { get; set; }
        public string State { get; set; }
        public int Attempts { get; set; }
        public int Generation { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public string ErrorType { get; set; }
        public string ErrorSummary { get; set; }
    }

    internal sealed class BatchFileLease : IDisposable
    {
        private readonly Thread _ownerThread;
        private readonly ManualResetEvent _acquired;
        private readonly ManualResetEvent _release;
        private bool _released;

        private BatchFileLease(Thread ownerThread, ManualResetEvent acquired, ManualResetEvent release)
        {
            _ownerThread = ownerThread;
            _acquired = acquired;
            _release = release;
        }

        public static BatchFileLease Acquire(string path, TimeSpan timeout)
        {
            string mutexName = GetMutexNameForTest(path);
            MutexSecurity mutexSecurity = CreateMutexSecurity();
            var acquiredSignal = new ManualResetEvent(false);
            var releaseSignal = new ManualResetEvent(false);
            bool acquired = false;
            Exception failure = null;
            var ownerThread = new Thread(() =>
            {
                bool reported = false;
                try
                {
                    bool createdNew;
                    using (var mutex = new Mutex(false, mutexName, out createdNew, mutexSecurity))
                    {
                        bool ownsMutex = false;
                        try
                        {
                            try
                            {
                                ownsMutex = mutex.WaitOne(timeout);
                            }
                            catch (AbandonedMutexException)
                            {
                                ownsMutex = true;
                            }

                            acquired = ownsMutex;
                            acquiredSignal.Set();
                            reported = true;
                            if (ownsMutex)
                            {
                                releaseSignal.WaitOne();
                            }
                        }
                        finally
                        {
                            if (ownsMutex)
                            {
                                mutex.ReleaseMutex();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
                finally
                {
                    if (!reported)
                    {
                        acquiredSignal.Set();
                    }
                }
            })
            {
                IsBackground = true,
                Name = "Recovery state lease"
            };
            ownerThread.Start();
            bool ownerReported = acquiredSignal.WaitOne(timeout + TimeSpan.FromSeconds(1));
            if (!ownerReported)
            {
                releaseSignal.Set();
                DisposeSignalsWhenOwnerStops(ownerThread, acquiredSignal, releaseSignal);
                return null;
            }

            if (failure != null)
            {
                releaseSignal.Set();
                DisposeSignalsWhenOwnerStops(ownerThread, acquiredSignal, releaseSignal);
                ErrorLog.Write("Acquire recovery state lease", failure);
                return null;
            }

            if (!acquired)
            {
                releaseSignal.Set();
                DisposeSignalsWhenOwnerStops(ownerThread, acquiredSignal, releaseSignal);
                return null;
            }

            return new BatchFileLease(ownerThread, acquiredSignal, releaseSignal);
        }

        private static void DisposeSignalsWhenOwnerStops(
            Thread ownerThread,
            ManualResetEvent acquiredSignal,
            ManualResetEvent releaseSignal)
        {
            if (ownerThread.Join(TimeSpan.FromSeconds(5)))
            {
                acquiredSignal.Dispose();
                releaseSignal.Dispose();
                return;
            }

            var disposer = new Thread(() =>
            {
                ownerThread.Join();
                acquiredSignal.Dispose();
                releaseSignal.Dispose();
            })
            {
                IsBackground = true,
                Name = "Recovery lease cleanup"
            };
            disposer.Start();
        }

        internal static string GetMutexNameForTest(string path)
        {
            string normalized = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
            using (SHA256 sha = SHA256.Create())
            {
                string hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(normalized))).Replace("-", string.Empty);
                return "Global\\WindowsServerToolsRecovery_" + hash;
            }
        }

        private static MutexSecurity CreateMutexSecurity()
        {
            var security = new MutexSecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            SecurityIdentifier administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            security.AddAccessRule(new MutexAccessRule(
                administrators,
                MutexRights.FullControl,
                AccessControlType.Allow));
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            if (identity?.User == null)
            {
                throw new InvalidOperationException("The current user could not be identified for the recovery batch lease.");
            }

            security.AddAccessRule(new MutexAccessRule(
                identity.User,
                MutexRights.FullControl,
                AccessControlType.Allow));
            return security;
        }

        public void Dispose()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            _release.Set();
            DisposeSignalsWhenOwnerStops(_ownerThread, _acquired, _release);
        }
    }

    internal static class DiagnosticRedactor
    {
        private static readonly Regex SecretAssignment = new Regex(
            "(?is)\\b(password|passwd|pwd|token|secret|api[_-]?key)\\s*([:=])\\s*(?:\"[^\"]*\"|'[^']*'|[^\\s,;\\r\\n]+)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(250));
        private static readonly Regex Authorization = new Regex(
            @"(?im)\b(Authorization\s*:\s*)(?:Basic|Bearer)\s+[^\s,;\r\n]+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(250));
        private static readonly Regex UrlCredentials = new Regex(
            @"(?i)(https?://)[^/@\s:]+:[^/@\s]+@",
            RegexOptions.Compiled | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(250));

        public static string Summarize(Exception exception, int limit)
        {
            if (exception == null)
            {
                return string.Empty;
            }

            return RedactAndBound(exception.Message, limit);
        }

        public static string RedactAndBound(string value, int limit)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            string safe = SecretAssignment.Replace(value, "$1$2<redacted>");
            safe = Authorization.Replace(safe, "$1<redacted>");
            safe = Regex.Replace(
                safe,
                @"(?i)\bBearer\s+[^\s,;\r\n]+",
                "Bearer <redacted>",
                RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(250));
            safe = UrlCredentials.Replace(safe, "$1<redacted>@");
            safe = ReplacePath(safe, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "%USERPROFILE%");
            safe = ReplacePath(safe, Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), "%TEMP%");
            safe = safe.Replace('\0', ' ').Replace("\r", " ").Replace("\n", " ").Trim();
            if (safe.Length > limit)
            {
                safe = safe.Substring(0, Math.Max(0, limit - 14)) + "...[truncated]";
            }

            return safe;
        }

        private static string ReplacePath(string value, string path, string replacement)
        {
            return string.IsNullOrWhiteSpace(path)
                ? value
                : Regex.Replace(value, Regex.Escape(path), replacement, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
    }

    public static class ErrorLog
    {
        private static readonly object SyncRoot = new object();
        private const long MaxLogBytes = 1024 * 1024;
        private const int RetainedLogFiles = 3;

        public static string LogDirectory
        {
            get
            {
                string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrWhiteSpace(localData))
                {
                    localData = Path.GetTempPath();
                }

                return Path.Combine(localData, "Windows-Server-Tools", "Logs");
            }
        }

        public static string CurrentLogFile => Path.Combine(LogDirectory, "recovery.log");

        public static void Write(string context, Exception exception)
        {
            try
            {
                lock (SyncRoot)
                {
                    Directory.CreateDirectory(LogDirectory);
                    RotateIfNeeded();
                    string entry = string.Join(
                        Environment.NewLine,
                        "------------------------------------------------------------",
                        DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                        DiagnosticRedactor.RedactAndBound(string.IsNullOrWhiteSpace(context) ? "Unexpected application error" : context, 512),
                        exception == null
                            ? "No exception details were available."
                            : exception.GetType().FullName + ": " + DiagnosticRedactor.Summarize(exception, 4096),
                        string.Empty);
                    File.AppendAllText(CurrentLogFile, entry, new UTF8Encoding(false));
                }
            }
            catch
            {
                // Diagnostics must never become a second failure that closes the app.
            }
        }

        private static void RotateIfNeeded()
        {
            var current = new FileInfo(CurrentLogFile);
            if (!current.Exists || current.Length < MaxLogBytes)
            {
                return;
            }

            for (int index = RetainedLogFiles - 1; index >= 1; index--)
            {
                string source = index == 1
                    ? CurrentLogFile
                    : CurrentLogFile + "." + (index - 1).ToString(CultureInfo.InvariantCulture);
                string destination = CurrentLogFile + "." + index.ToString(CultureInfo.InvariantCulture);
                if (!File.Exists(source))
                {
                    continue;
                }

                if (File.Exists(destination))
                {
                    File.Delete(destination);
                }

                File.Move(source, destination);
            }
        }
    }
}
