using Microsoft.Win32.TaskScheduler;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using Task = System.Threading.Tasks.Task;

namespace Windows_Server_Tools
{
    internal static class ServerMutationCoordinator
    {
        private static readonly object SyncRoot = new object();
        private static string _currentOperation;
        private static long _currentLeaseId;

        public static event System.Action StateChanged;

        public static string CurrentOperation
        {
            get
            {
                lock (SyncRoot)
                {
                    return _currentOperation;
                }
            }
        }

        public static IDisposable TryAcquire(string operationName)
        {
            if (string.IsNullOrWhiteSpace(operationName))
            {
                throw new ArgumentException("A server mutation name is required.", nameof(operationName));
            }

            string machineLeasePath;
            try
            {
                machineLeasePath = ProtectedWorkflowState.GetPath(
                    "Coordination",
                    "server-mutation.lease");
            }
            catch (Exception ex) when (!RecoveryRunner.IsFatal(ex))
            {
                ErrorLog.Write("Prepare machine-wide server mutation lease", ex);
                return null;
            }

            BatchFileLease machineLease = BatchFileLease.Acquire(machineLeasePath, TimeSpan.Zero);
            if (machineLease == null)
            {
                return null;
            }

            long leaseId;
            lock (SyncRoot)
            {
                if (!string.IsNullOrWhiteSpace(_currentOperation))
                {
                    machineLease.Dispose();
                    return null;
                }

                _currentOperation = operationName.Trim();
                leaseId = ++_currentLeaseId;
            }

            RaiseStateChanged();
            return new MutationLease(leaseId, machineLease);
        }

        private static void Release(long leaseId)
        {
            lock (SyncRoot)
            {
                if (leaseId != _currentLeaseId || string.IsNullOrWhiteSpace(_currentOperation))
                {
                    return;
                }

                _currentOperation = null;
            }

            RaiseStateChanged();
        }

        private static void RaiseStateChanged()
        {
            System.Action handlers = StateChanged;
            if (handlers == null)
            {
                return;
            }

            foreach (System.Action handler in handlers.GetInvocationList().Cast<System.Action>())
            {
                try
                {
                    handler();
                }
                catch (Exception ex) when (!RecoveryRunner.IsFatal(ex))
                {
                    ErrorLog.Write("Update server mutation controls", ex);
                }
            }
        }

        private sealed class MutationLease : IDisposable
        {
            private readonly long _leaseId;
            private readonly BatchFileLease _machineLease;
            private bool _disposed;

            public MutationLease(long leaseId, BatchFileLease machineLease)
            {
                _leaseId = leaseId;
                _machineLease = machineLease ?? throw new ArgumentNullException(nameof(machineLease));
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                Release(_leaseId);
                _machineLease.Dispose();
            }
        }
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private const string InitialSetupCompletionMarker = "windows-server-tools-initial-setup-v2";
        private const string SimpsonsSetupCompletionMarker = "windows-server-tools-simpsons-setup-v1";
        private const string SimpsonsTaskName = "Run Simpsons Setup";
        private Func<Task<bool>> _lastFailedAction;
        private Func<Task<bool>> _completedReconciliationAction;
        private readonly Dictionary<string, RecoveryRequest> _pendingRecoveryActions =
            new Dictionary<string, RecoveryRequest>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _operationsInFlight =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string _selectedRecoveryKey;
        private bool _isRetrying;
        private bool _startupStarted;
        private bool _initialSetupAuthorizationRunning;
        private bool _initialSetupAuthorizationCancelled;
        private IInputElement _initialSetupConfirmationFocusOrigin;
        private ReviewedDestructiveActionGate _initialSetupAuthorizationGate;
        private IInputElement _noticeFocusReturnTarget;
        private bool _focusEnteredRecoveryNotification;
        private bool _noticeOpenedFromReview;
        private string _supplementalRecoveryTitle;
        private string _supplementalRecoveryMessage;
        private readonly Dictionary<Button, MutationControlSnapshot> _mutationControlSnapshots =
            new Dictionary<Button, MutationControlSnapshot>();

        private sealed class MutationControlSnapshot
        {
            public bool IsEnabled { get; set; }

            public string HelpText { get; set; }
        }

        private sealed class RecoveryRequest
        {
            public string Key { get; set; }

            public string Title { get; set; }

            public string Message { get; set; }

            public Func<Task<bool>> Action { get; set; }

            public Func<Task<bool>> CompletedAction { get; set; }

            public IInputElement FocusOrigin { get; set; }

            public IDisposable SensitiveResource { get; set; }

            public bool RequiresReconciliation { get; set; }

            public string ReconciliationTarget { get; set; }

            public bool RepairsCorruptState { get; set; }
        }

        public MainWindow()
        {
            InitializeComponent();
            ServerMutationCoordinator.StateChanged += ServerMutationCoordinator_StateChanged;
            ApplyServerMutationControlState();
            Loaded += MainWindow_Loaded;
        }

        protected override void OnClosed(EventArgs e)
        {
            StopUpdateService();
            ServerMutationCoordinator.StateChanged -= ServerMutationCoordinator_StateChanged;
            foreach (IDisposable sensitiveResource in _pendingRecoveryActions.Values
                .Select(request => request.SensitiveResource)
                .Where(resource => resource != null)
                .Distinct())
            {
                sensitiveResource.Dispose();
            }

            base.OnClosed(e);
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (_startupStarted)
            {
                return;
            }

            _startupStarted = true;

            await InitializeApplicationShellAsync();
        }

        private async Task<bool> InitializeApplicationShellAsync()
        {
            try
            {
                ConfigureAvailableServerRoles();
                await HandleCommandLineArgs(Environment.GetCommandLineArgs());
                ResolveRecovery("application-startup");
                StartUpdateService();
                return true;
            }
            catch (Exception ex) when (!RecoveryRunner.IsFatal(ex))
            {
                ErrorLog.Write("Initialize the application", ex);
                ShowRecoveryFailure(
                    "application-startup",
                    "Startup did not finish",
                    ex,
                    async () =>
                    {
                        _startupStarted = true;
                        return await InitializeApplicationShellAsync();
                    });
                return false;
            }
        }

        private void ConfigureAvailableServerRoles()
        {
            const string unavailable = "Unavailable until a credential-safe installer is published and pinned.";
            OmegaServerPromoteButton.IsEnabled = false;
            SCCMButton.IsEnabled = false;
            SideServerButton.IsEnabled = false;
            AutomationProperties.SetHelpText(OmegaServerPromoteButton, unavailable);
            AutomationProperties.SetHelpText(SCCMButton, unavailable);
            AutomationProperties.SetHelpText(SideServerButton, unavailable);
        }

        private IEnumerable<Button> GetServerMutationControls()
        {
            yield return InstallActiveDirectoryButton;
            yield return ReviewInitialSetupButton;
            yield return SetStaticIpButton;
            yield return SimpsonsSetupButton;
            yield return InstallChocolateyButton;
            yield return InstallChocolateySoftwareButton;
            yield return OmegaServerPromoteButton;
            yield return SCCMButton;
            yield return SideServerButton;
            yield return CommonInstallStuffButton;
        }

        private void ServerMutationCoordinator_StateChanged()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new System.Action(ApplyServerMutationControlState));
                return;
            }

            ApplyServerMutationControlState();
        }

        private void ApplyServerMutationControlState()
        {
            string runningOperation = ServerMutationCoordinator.CurrentOperation;
            if (!string.IsNullOrWhiteSpace(runningOperation))
            {
                if (_mutationControlSnapshots.Count == 0)
                {
                    foreach (Button control in GetServerMutationControls())
                    {
                        _mutationControlSnapshots[control] = new MutationControlSnapshot
                        {
                            IsEnabled = control.IsEnabled,
                            HelpText = AutomationProperties.GetHelpText(control)
                        };
                    }
                }

                string busyHelp = "Disabled while the server change “"
                    + runningOperation
                    + "” is running. This control returns when that operation stops.";
                foreach (Button control in GetServerMutationControls())
                {
                    control.IsEnabled = false;
                    AutomationProperties.SetHelpText(control, busyHelp);
                }
            }
            else if (_mutationControlSnapshots.Count > 0)
            {
                foreach (KeyValuePair<Button, MutationControlSnapshot> snapshot in _mutationControlSnapshots)
                {
                    snapshot.Key.IsEnabled = snapshot.Value.IsEnabled;
                    AutomationProperties.SetHelpText(snapshot.Key, snapshot.Value.HelpText ?? string.Empty);
                }

                _mutationControlSnapshots.Clear();
            }

            UpdateRecoveryControls();
            UpdateUpdateRestartAvailability();
        }

        private IDisposable TryAcquireServerMutation(string operationName, IInputElement focusOrigin)
        {
            IDisposable lease = ServerMutationCoordinator.TryAcquire(operationName);
            if (lease != null)
            {
                return lease;
            }

            string runningOperation = ServerMutationCoordinator.CurrentOperation;
            ShowRecoveryNotice(
                "Another server change is already running",
                "“" + (runningOperation ?? "A server change")
                    + "” must stop before “" + operationName + "” can start. No additional action was queued.",
                focusOrigin);
            return null;
        }

        private async Task<bool> RunInitialServerSetupAsync()
        {
            string completionFile = GetInitialSetupCompletionFile();
            string chocolateyCompletionFile = GetChocolateyCompletionFile();
            string checkpointFile = ProtectedWorkflowState.GetPath(
                "Recovery",
                "initial-server-setup.steps");

            if (File.Exists(completionFile)
                && string.Equals(
                    ProtectedWorkflowState.ReadAllText(completionFile).Trim(),
                    InitialSetupCompletionMarker,
                    StringComparison.Ordinal))
            {
                ResolveRecovery("initial-server-setup");
                RegisterCheckpointCleanupIfNeeded(
                    "initial-server-setup-cleanup",
                    "Initial setup recovery cleanup is incomplete",
                    checkpointFile,
                    ReviewInitialSetupButton,
                    tryImmediately: true);
                return true;
            }

            const string networkOperationKey = "Set the current network address as static";
            const string chocolateyOperationKey = "Install Chocolatey";
            IDisposable mutationLease = TryAcquireServerMutation(
                "Initial server setup",
                ReviewInitialSetupButton);
            if (mutationLease == null)
            {
                return false;
            }

            if (_operationsInFlight.Contains("initial-server-setup")
                || _operationsInFlight.Contains(networkOperationKey)
                || _operationsInFlight.Contains(chocolateyOperationKey))
            {
                mutationLease.Dispose();
                return false;
            }

            _operationsInFlight.Add("initial-server-setup");
            _operationsInFlight.Add(networkOperationKey);
            _operationsInFlight.Add(chocolateyOperationKey);
            bool staticIpWasEnabled = SetStaticIpButton.IsEnabled;
            bool chocolateyWasEnabled = InstallChocolateyButton.IsEnabled;
            bool reviewWasEnabled = ReviewInitialSetupButton.IsEnabled;
            object staticIpContent = SetStaticIpButton.Content;
            object chocolateyContent = InstallChocolateyButton.Content;
            object reviewContent = ReviewInitialSetupButton.Content;
            ReviewInitialSetupButton.IsEnabled = false;
            SetStaticIpButton.IsEnabled = false;
            InstallChocolateyButton.IsEnabled = false;
            ReviewInitialSetupButton.Content = "Initial setup running…";
            SetStaticIpButton.Content = "Initial setup running…";
            InstallChocolateyButton.Content = "Included in initial setup…";
            AutomationProperties.SetName(ReviewInitialSetupButton, "Initial server setup is running");
            AutomationProperties.SetHelpText(ReviewInitialSetupButton, "Disabled while the reviewed initial setup is running. This control returns when setup stops.");
            AutomationProperties.SetName(SetStaticIpButton, "Initial server setup is running");
            AutomationProperties.SetHelpText(SetStaticIpButton, "Disabled while the initial server setup is running. This control returns when the setup stops.");
            AutomationProperties.SetName(InstallChocolateyButton, "Chocolatey installation is included in the running initial setup");
            AutomationProperties.SetHelpText(InstallChocolateyButton, "Disabled while the initial server setup is running. This control returns when the setup stops.");
            ShowRecoveryNotice(
                "Initial server setup is running",
                "Completed steps are preserved while the remaining server setup actions run.",
                ReviewInitialSetupButton);
            try
            {
                RecoverableOperation[] initialOperations = new[]
                    {
                    new RecoverableOperation(
                        "Set the current network address as static",
                        SetCurrentAddressStaticAsync,
                        maxAttempts: 2,
                        retrySafety: RetrySafety.Idempotent)
                    }
                    .Concat(CreateWindowsTaskOperations(networkOperationKey))
                    .ToArray();
                OperationBatchResult combined = await RecoveryRunner.RunAllAsync(
                    initialOperations,
                    checkpointFile);

                if (combined.Succeeded)
                {
                    WriteCompletionMarker(chocolateyCompletionFile, InitialSetupCompletionMarker);
                    WriteCompletionMarker(completionFile, InitialSetupCompletionMarker);
                    bool checkpointCleared = RecoveryRunner.ClearCheckpoint(checkpointFile);
                    ShowRecoveryNotice(
                        "Initial server setup completed",
                        checkpointCleared
                            ? "All setup steps completed. The completion marker was written only after every step succeeded."
                            : "All setup steps completed. Recovery-state cleanup still needs attention and can be retried without repeating server actions.",
                        ReviewInitialSetupButton);
                    ResolveRecovery("initial-server-setup");
                    if (!checkpointCleared)
                    {
                        RegisterCheckpointCleanupIfNeeded(
                            "initial-server-setup-cleanup",
                            "Initial setup recovery cleanup is incomplete",
                            checkpointFile,
                            ReviewInitialSetupButton,
                            tryImmediately: false);
                    }
                    return true;
                }

                ShowBatchFailure(
                    "initial-server-setup",
                    "Initial server setup is incomplete",
                    combined,
                    checkpointFile,
                    RunInitialServerSetupAsync,
                    ReviewInitialSetupButton);
                return false;
            }
            finally
            {
                _operationsInFlight.Remove("initial-server-setup");
                _operationsInFlight.Remove(networkOperationKey);
                _operationsInFlight.Remove(chocolateyOperationKey);
                SetStaticIpButton.Content = staticIpContent;
                InstallChocolateyButton.Content = chocolateyContent;
                ReviewInitialSetupButton.Content = reviewContent;
                SetStaticIpButton.IsEnabled = staticIpWasEnabled;
                InstallChocolateyButton.IsEnabled = chocolateyWasEnabled;
                ReviewInitialSetupButton.IsEnabled = reviewWasEnabled;
                AutomationProperties.SetName(ReviewInitialSetupButton, "Review the initial server setup plan");
                AutomationProperties.SetHelpText(ReviewInitialSetupButton, "Opens the two-key confirmation without changing this server.");
                AutomationProperties.SetName(SetStaticIpButton, "Set the current network address as static");
                AutomationProperties.SetHelpText(SetStaticIpButton, "Sets the current network address as static.");
                AutomationProperties.SetName(InstallChocolateyButton, "Install Chocolatey");
                AutomationProperties.SetHelpText(InstallChocolateyButton, "Installs Chocolatey when no initial setup operation is running.");
                ClearSupplementalRecoveryNotice("Initial server setup is running");
                if (_pendingRecoveryActions.Count > 0)
                {
                    RenderSelectedRecoveryFailure();
                }
                else
                {
                    UpdateRecoveryControls();
                }

                mutationLease.Dispose();
            }
        }

        private void ReviewInitialSetupButton_Click(object sender, RoutedEventArgs e)
        {
            if (_initialSetupAuthorizationRunning
                || InitialSetupConfirmationPanel.Visibility == Visibility.Visible)
            {
                return;
            }

            _initialSetupConfirmationFocusOrigin = ReviewInitialSetupButton;
            _initialSetupAuthorizationCancelled = false;
            _initialSetupAuthorizationGate = new ReviewedDestructiveActionGate();
            InitialSetupKeyOneCheckBox.IsChecked = false;
            InitialSetupKeyTwoCheckBox.IsChecked = false;
            InitialSetupAuthorizationSlider.Value = 0;
            InitialSetupAuthorizationProgress.Value = 0;
            MainContentScroll.IsEnabled = false;
            InitialSetupConfirmationPanel.Visibility = Visibility.Visible;
            UpdateInitialSetupConfirmationState();
            Dispatcher.BeginInvoke(new System.Action(() => InitialSetupKeyOneCheckBox.Focus()));
        }

        private void InitialSetupConfirmationStateChanged(object sender, RoutedEventArgs e)
        {
            UpdateInitialSetupConfirmationState();
        }

        private void InitialSetupAuthorizationSlider_ValueChanged(
            object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateInitialSetupConfirmationState();
        }

        private void UpdateInitialSetupConfirmationState()
        {
            if (InitialSetupKeyOneCheckBox == null
                || InitialSetupKeyTwoCheckBox == null
                || InitialSetupAuthorizationSlider == null
                || InitialSetupAuthorizationProgress == null
                || InitialSetupAuthorizeButton == null
                || InitialSetupConfirmationStatusText == null)
            {
                return;
            }

            bool bothKeys = InitialSetupKeyOneCheckBox.IsChecked == true
                && InitialSetupKeyTwoCheckBox.IsChecked == true;
            InitialSetupAuthorizationSlider.IsEnabled = bothKeys && !_initialSetupAuthorizationRunning;
            if (!bothKeys && InitialSetupAuthorizationSlider.Value != 0)
            {
                InitialSetupAuthorizationSlider.Value = 0;
            }

            double progress = Math.Max(0, Math.Min(100, InitialSetupAuthorizationSlider.Value));
            InitialSetupAuthorizationProgress.Value = progress;
            bool complete = DestructiveActionAuthorization.IsComplete(
                InitialSetupKeyOneCheckBox.IsChecked == true,
                InitialSetupKeyTwoCheckBox.IsChecked == true,
                progress);
            InitialSetupAuthorizeButton.IsEnabled = complete && !_initialSetupAuthorizationRunning;
            InitialSetupConfirmationStatusText.Text = !bothKeys
                ? "Set both independent confirmation keys to enable the slider."
                : complete
                    ? "Authorization is complete. Choose Authorize and run initial setup to begin exactly one recovery batch."
                    : "Confirmation progress: " + ((int)progress) + " of 100. No server change has started.";
            AutomationProperties.SetName(
                InitialSetupConfirmationStatusText,
                InitialSetupConfirmationStatusText.Text);
            RaiseInitialSetupConfirmationLiveRegionChanged();
        }

        private async void InitialSetupAuthorizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_initialSetupAuthorizationRunning
                || _initialSetupAuthorizationGate == null
                || !_initialSetupAuthorizationGate.TryAuthorize(
                    InitialSetupKeyOneCheckBox.IsChecked == true,
                    InitialSetupKeyTwoCheckBox.IsChecked == true,
                    InitialSetupAuthorizationSlider.Value))
            {
                return;
            }

            _initialSetupAuthorizationRunning = true;
            UpdateInitialSetupConfirmationState();
            InitialSetupConfirmationStatusText.Text = "Authorization complete. Starting the reviewed initial setup batch.";
            RaiseInitialSetupConfirmationLiveRegionChanged();
            if (SystemParameters.ClientAreaAnimation)
            {
                var completionAnimation = new DoubleAnimation(
                    1.0,
                    0.45,
                    TimeSpan.FromMilliseconds(180))
                {
                    AutoReverse = true,
                    RepeatBehavior = new RepeatBehavior(2)
                };
                InitialSetupConfirmationPanel.BeginAnimation(OpacityProperty, completionAnimation);
                await Task.Delay(720);
            }

            if (_initialSetupAuthorizationCancelled)
            {
                _initialSetupAuthorizationRunning = false;
                return;
            }

            CloseInitialSetupConfirmation(restoreFocus: false);
            try
            {
                await RunInitialServerSetupAsync();
            }
            finally
            {
                _initialSetupAuthorizationRunning = false;
                RestoreMainWindowFocus(_initialSetupConfirmationFocusOrigin);
            }
        }

        private void InitialSetupEmergencyExitButton_Click(object sender, RoutedEventArgs e)
        {
            _initialSetupAuthorizationCancelled = true;
            _initialSetupAuthorizationGate?.Cancel();
            _initialSetupAuthorizationRunning = false;
            CloseInitialSetupConfirmation(restoreFocus: true);
        }

        private void CloseInitialSetupConfirmation(bool restoreFocus)
        {
            InitialSetupConfirmationPanel.BeginAnimation(OpacityProperty, null);
            InitialSetupConfirmationPanel.Opacity = 1;
            InitialSetupConfirmationPanel.Visibility = Visibility.Collapsed;
            MainContentScroll.IsEnabled = true;
            InitialSetupKeyOneCheckBox.IsChecked = false;
            InitialSetupKeyTwoCheckBox.IsChecked = false;
            InitialSetupAuthorizationSlider.Value = 0;
            InitialSetupAuthorizationProgress.Value = 0;
            if (restoreFocus)
            {
                RestoreMainWindowFocus(_initialSetupConfirmationFocusOrigin);
            }
        }

        private void RaiseInitialSetupConfirmationLiveRegionChanged()
        {
            Dispatcher.BeginInvoke(new System.Action(() =>
            {
                AutomationPeer peer = UIElementAutomationPeer.FromElement(InitialSetupConfirmationStatusText)
                    ?? UIElementAutomationPeer.CreatePeerForElement(InitialSetupConfirmationStatusText);
                peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            }));
        }

        private static string GetInitialSetupCompletionFile()
        {
            return ProtectedWorkflowState.GetPath("State", "TasksComplete.txt");
        }

        private static string GetChocolateyCompletionFile()
        {
            return ProtectedWorkflowState.GetPath("State", "ChocoComplete.txt");
        }

        private void RegisterCheckpointCleanupIfNeeded(
            string operationKey,
            string title,
            string checkpointFile,
            IInputElement focusOrigin,
            bool tryImmediately)
        {
            if (tryImmediately && RecoveryRunner.ClearCheckpoint(checkpointFile))
            {
                ResolveRecovery(operationKey);
                return;
            }

            ShowRecoveryFailure(
                operationKey,
                title,
                new InvalidOperationException("Completed server actions will not be repeated. Only the completed recovery-state file still needs to be removed."),
                () => RetryCheckpointCleanupAsync(operationKey, checkpointFile, focusOrigin),
                focusOrigin);
        }

        private async Task<bool> RetryCheckpointCleanupAsync(
            string operationKey,
            string checkpointFile,
            IInputElement focusOrigin)
        {
            await Task.Yield();
            if (!RecoveryRunner.ClearCheckpoint(checkpointFile))
            {
                return false;
            }

            ResolveRecovery(operationKey);
            ShowRecoveryNotice(
                "Recovery cleanup completed",
                "The completed recovery state was removed without repeating any server action.",
                focusOrigin);
            return true;
        }

        private Task ShowUnavailableInstallerAsync(string roleName, IInputElement focusOrigin)
        {
            IDisposable mutationLease = TryAcquireServerMutation(
                "Open the " + roleName + " installer",
                focusOrigin);
            if (mutationLease == null)
            {
                return Task.CompletedTask;
            }

            try
            {
                ShowRecoveryNotification(
                    roleName + "-installer-unavailable",
                    roleName + " installer is unavailable",
                    "No download or launch was attempted. This installer remains unavailable until a credential-safe build is published and pinned.",
                    null,
                    isError: true,
                    focusOrigin: focusOrigin);
            }
            finally
            {
                mutationLease.Dispose();
            }

            return Task.CompletedTask;
        }

        private async void SideServerButton_Click(object sender, RoutedEventArgs e)
        {
            await ShowUnavailableInstallerAsync("Side Server", SideServerButton);
        }

        private async void OmegaServerPromoteButton_Click(object sender, RoutedEventArgs e)
        {
            await PromoteToOmegaServer();
        }

        private async Task PromoteToOmegaServer()
        {
            await ShowUnavailableInstallerAsync("Omega Server", OmegaServerPromoteButton);
        }

        private void ShowUsage()
        {
            ShowRecoveryFailure(
                "command-line-usage",
                "Command line could not be applied",
                new ArgumentException("Usage: Windows-Server-Tools promotetodc <domainName>, Windows-Server-Tools task, or the verified scheduled continuation command."),
                null);
        }

        private async Task HandleCommandLineArgs(string[] args)
        {
            try
            {
                if (args == null || args.Length <= 1)
                {
                    ResolveRecovery("command-line-request");
                    return;
                }

                string command = CommandLineRequestParser.GetCommandName(args);

                if (command == "promotetodc")
                {
                    if (args.Length != 3)
                    {
                        ShowUsage();
                        return;
                    }

                    DomainNameTextBox.Text = args[2];
                    ShowRecoveryNotice(
                        "Complete promotion in the app",
                        "The domain was loaded. Enter the Directory Services Restore Mode password in the protected field, then choose Submit.",
                        SafeModePasswordBox);
                    SafeModePasswordBox.Focus();
                }
                else if(command == "task" && args.Length == 2)
                {
                    bool succeeded = await CreateRebootContinuationAsync();
                    if (succeeded)
                    {
                        Close();
                    }
                }
                else if (command == "simpsons")
                {
                    if (args.Length != 3 || !VerifyContinuationInvocation(args[2]))
                    {
                        throw new InvalidOperationException("The reboot continuation executable or digest could not be verified. The app will remain open without running setup.");
                    }

                    bool succeeded = await RunSimpsonsSetupAsync();
                    if (succeeded)
                    {
                        Close();
                    }
                }
                else
                {
                    ShowUsage();
                }

                ResolveRecovery("command-line-request");
            }
            catch (Exception ex) when (!RecoveryRunner.IsFatal(ex))
            {
                ErrorLog.Write("Apply command-line request", ex);
                ShowRecoveryFailure(
                    "command-line-request",
                    "The command-line request did not complete",
                    ex,
                    async () =>
                    {
                        await HandleCommandLineArgs(args);
                        return !_pendingRecoveryActions.ContainsKey("command-line-request");
                    });
            }
        }

        private async Task<bool> EnsureChocolateyInstalledAsync()
        {
            IDisposable mutationLease = TryAcquireServerMutation(
                "Install Chocolatey",
                InstallChocolateyButton);
            if (mutationLease == null)
            {
                return false;
            }

            try
            {
                string completionFile = GetChocolateyCompletionFile();
                if (File.Exists(@"C:\ProgramData\chocolatey\bin\choco.exe"))
                {
                    WriteCompletionMarker(completionFile, InitialSetupCompletionMarker);
                    ResolveRecovery("Install Chocolatey");
                    return true;
                }

                bool succeeded = await RunUiOperationAsync(
                    "Install Chocolatey",
                    InstallChocolateyAsync,
                    EnsureChocolateyInstalledAsync,
                    InstallChocolateyButton);
                if (succeeded)
                {
                    WriteCompletionMarker(completionFile, InitialSetupCompletionMarker);
                }

                return succeeded;
            }
            finally
            {
                mutationLease.Dispose();
            }
        }

        private async Task<bool> CreateRebootContinuationAsync()
        {
            IDisposable mutationLease = TryAcquireServerMutation(
                "Create the reboot continuation",
                SimpsonsSetupButton);
            if (mutationLease == null)
            {
                return false;
            }

            try
            {
                return await RunUiOperationAsync(
                    "Create the reboot continuation",
                    () => Task.Run(() => CreateSimpsonsTask()),
                    CreateRebootContinuationAsync,
                    SimpsonsSetupButton);
            }
            finally
            {
                mutationLease.Dispose();
            }
        }


        public void CreateSimpsonsTask()
        {
            string executablePath = StageContinuationExecutable(out string executableSha256);
            string arguments = "simpsons " + executableSha256;

            using (TaskService ts = new TaskService())
            {
                TaskDefinition td = ts.NewTask();
                td.RegistrationInfo.Description = "Resumes the incomplete Windows Server setup after login.";
                td.Principal.UserId = Environment.UserDomainName + "\\" + Environment.UserName;
                td.Principal.LogonType = TaskLogonType.InteractiveToken;
                td.Principal.RunLevel = TaskRunLevel.Highest;

                td.Triggers.Add(new LogonTrigger());
                td.Actions.Add(new ExecAction(executablePath, arguments, null));
                ts.RootFolder.RegisterTaskDefinition(SimpsonsTaskName, td);

                Microsoft.Win32.TaskScheduler.Task registeredTask = ts.GetTask(SimpsonsTaskName);
                ExecAction registeredAction = registeredTask?.Definition?.Actions
                    .OfType<ExecAction>()
                    .FirstOrDefault();
                if (registeredTask == null
                    || registeredAction == null
                    || registeredTask.Definition.Actions.Count != 1
                    || !string.Equals(registeredAction.Path, executablePath, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(registeredAction.Arguments, arguments, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("The reboot continuation could not be verified. The app will remain open so this step can be retried.");
                }
            }
        }

        private static string StageContinuationExecutable(out string sha256)
        {
            string source = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
            {
                throw new FileNotFoundException("The running application executable could not be located for reboot continuation.");
            }

            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (string.IsNullOrWhiteSpace(programFiles))
            {
                throw new InvalidOperationException("The protected Program Files directory could not be located.");
            }

            string continuationDirectory = Path.Combine(programFiles, "Windows-Server-Tools", "Continuation");
            Directory.CreateDirectory(continuationDirectory);
            VerifyProtectedContinuationDirectory(continuationDirectory);

            string destination = Path.Combine(continuationDirectory, "Windows-Server-Tools.exe");
            CopyFileAtomically(source, destination);
            string sourceConfiguration = source + ".config";
            if (File.Exists(sourceConfiguration))
            {
                CopyFileAtomically(sourceConfiguration, destination + ".config");
            }

            string sourceSha256 = ComputeFileSha256(source);
            sha256 = ComputeFileSha256(destination);
            if (!string.Equals(sourceSha256, sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The staged reboot continuation did not match the running application.");
            }

            return destination;
        }

        private static void VerifyProtectedContinuationDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathRooted(directory))
            {
                throw new UnauthorizedAccessException("The reboot continuation directory path is invalid.");
            }

            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (string.IsNullOrWhiteSpace(programFiles))
            {
                throw new UnauthorizedAccessException("The protected Program Files directory could not be located.");
            }

            string expectedDirectory = Path.GetFullPath(
                Path.Combine(programFiles, "Windows-Server-Tools", "Continuation"))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string fullDirectory = Path.GetFullPath(directory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.Equals(fullDirectory, expectedDirectory, StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException("The reboot continuation directory is outside the protected application location.");
            }

            var dangerousIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                new SecurityIdentifier(WellKnownSidType.WorldSid, null).Value,
                new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null).Value,
                new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null).Value
            };
            FileSystemRights writeRights = FileSystemRights.Write
                | FileSystemRights.Modify
                | FileSystemRights.FullControl
                | FileSystemRights.CreateFiles
                | FileSystemRights.CreateDirectories
                | FileSystemRights.WriteData
                | FileSystemRights.AppendData;
            string root = Path.GetPathRoot(fullDirectory);
            string relative = fullDirectory.Substring(root.Length);
            string current = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            foreach (string segment in relative.Split(new[]
            {
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar
            }, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                DirectoryInfo directoryInfo = new DirectoryInfo(current);
                if (!directoryInfo.Exists
                    || (directoryInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new UnauthorizedAccessException("The reboot continuation directory contains a missing or redirected path component.");
                }

                DirectorySecurity security = directoryInfo.GetAccessControl(
                    AccessControlSections.Owner | AccessControlSections.Access);
                SecurityIdentifier owner = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
                if (owner == null || !IsTrustedAdministrativeOwner(owner))
                {
                    throw new UnauthorizedAccessException("The reboot continuation directory owner is not trusted.");
                }

                AuthorizationRuleCollection rules = security.GetAccessRules(
                    true,
                    true,
                    typeof(SecurityIdentifier));
                foreach (FileSystemAccessRule rule in rules.OfType<FileSystemAccessRule>())
                {
                    if (rule.AccessControlType == AccessControlType.Allow
                        && dangerousIdentities.Contains(rule.IdentityReference.Value)
                        && (rule.FileSystemRights & writeRights) != 0)
                    {
                        throw new UnauthorizedAccessException("The reboot continuation directory is writable by a non-administrative identity.");
                    }
                }
            }
        }

        private static bool IsTrustedAdministrativeOwner(SecurityIdentifier owner)
        {
            if (owner.IsWellKnown(WellKnownSidType.LocalSystemSid)
                || owner.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid))
            {
                return true;
            }

            try
            {
                NTAccount account = owner.Translate(typeof(NTAccount)) as NTAccount;
                return account != null
                    && account.Value.EndsWith("\\TrustedInstaller", StringComparison.OrdinalIgnoreCase);
            }
            catch (IdentityNotMappedException)
            {
                return false;
            }
        }

        private static bool VerifyContinuationInvocation(string expectedSha256)
        {
            if (string.IsNullOrWhiteSpace(expectedSha256) || expectedSha256.Length != 64)
            {
                return false;
            }

            string executablePath = Process.GetCurrentProcess().MainModule?.FileName;
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (string.IsNullOrWhiteSpace(programFiles) || !Path.IsPathRooted(programFiles))
            {
                return false;
            }

            string expectedDirectory = Path.GetFullPath(Path.Combine(programFiles, "Windows-Server-Tools", "Continuation"))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (string.IsNullOrWhiteSpace(executablePath)
                || !Path.GetFullPath(executablePath).StartsWith(expectedDirectory, StringComparison.OrdinalIgnoreCase)
                || !File.Exists(executablePath))
            {
                return false;
            }

            VerifyProtectedContinuationDirectory(Path.GetDirectoryName(executablePath));
            if ((File.GetAttributes(executablePath) & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }

            return string.Equals(
                ComputeFileSha256(executablePath),
                expectedSha256,
                StringComparison.OrdinalIgnoreCase);
        }

        private static void RemoveSimpsonsTaskAfterSuccess()
        {
            using (var taskService = new TaskService())
            {
                if (taskService.GetTask(SimpsonsTaskName) != null)
                {
                    taskService.RootFolder.DeleteTask(SimpsonsTaskName, false);
                }
            }
        }

        private static void CopyFileAtomically(string source, string destination)
        {
            string temporary = destination + ".new-" + Guid.NewGuid().ToString("N");
            string backup = destination + ".previous";
            try
            {
                string destinationDirectory = Path.GetDirectoryName(destination);
                VerifyProtectedContinuationDirectory(destinationDirectory);
                File.Copy(source, temporary, false);
                if ((File.GetAttributes(temporary) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new UnauthorizedAccessException("The staged reboot continuation is a redirected file.");
                }

                if (File.Exists(destination))
                {
                    if ((File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new UnauthorizedAccessException("The existing reboot continuation is a redirected file.");
                    }

                    if (File.Exists(backup))
                    {
                        File.Delete(backup);
                    }

                    File.Replace(temporary, destination, backup, true);
                    File.Delete(backup);
                }
                else
                {
                    File.Move(temporary, destination);
                }

                VerifyProtectedContinuationDirectory(destinationDirectory);
                if ((File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new UnauthorizedAccessException("The staged reboot continuation is a redirected file.");
                }
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }

        public static string DomainName
        {
            get { return ReadDomainPart(0); }
        }

        public static string DomainCOM
        {
            get { return ReadDomainPart(1); }
        }

        private static string ReadDomainPart(int index)
        {
            try
            {
                string domainFile = ProtectedWorkflowState.GetPath("State", "Domain.txt");
                if (!File.Exists(domainFile))
                {
                    return string.Empty;
                }

                string[] parts = ProtectedWorkflowState.ReadAllText(domainFile).Trim().Split('.');
                return parts.Length > index ? parts[index] : string.Empty;
            }
            catch (Exception ex) when (!RecoveryRunner.IsFatal(ex))
            {
                ErrorLog.Write("Read the configured domain", ex);
                return string.Empty;
            }
        }

        private static void WriteCompletionMarker(string destination, string value)
        {
            ProtectedWorkflowState.WriteAllTextAtomic(destination, value);
        }

        private static string ComputeFileSha256(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 sha256 = SHA256.Create())
            {
                return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty);
            }
        }

        private async void InstallActiveDirectoryButton_Click(object sender, RoutedEventArgs e)
        {
            string domainName = DomainNameTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(domainName) || !domainName.Contains("."))
            {
                ShowRecoveryFailure(
                    "domain-name-validation",
                    "A valid domain name is required",
                    new ArgumentException("Enter a fully qualified domain name such as example.local."),
                    null,
                    DomainNameTextBox);
                DomainNameTextBox.Focus();
                return;
            }

            SecureString safeModePassword;
            using (SecureString enteredPassword = SafeModePasswordBox.SecurePassword)
            {
                if (enteredPassword == null || enteredPassword.Length == 0)
                {
                    ShowRecoveryFailure(
                        "safe-mode-password-validation",
                        "A Directory Services Restore Mode password is required",
                        new ArgumentException("Enter the password in the protected field, then choose Submit again."),
                        null,
                        SafeModePasswordBox);
                    SafeModePasswordBox.Focus();
                    return;
                }

                safeModePassword = enteredPassword.Copy();
            }

            safeModePassword.MakeReadOnly();
            SafeModePasswordBox.Clear();
            string originalContent = InstallActiveDirectoryButton.Content?.ToString() ?? "Submit";
            InstallActiveDirectoryButton.Content = "Working…";
            bool succeeded = false;
            try
            {
                succeeded = await RunButtonOperationAsync(
                    InstallActiveDirectoryButton,
                    "Install Active Directory and promote this server",
                    async () =>
                    {
                        CreateSimpsonsTask();
                        await InstallActiveDirectoryAndPromoteToDC(
                            domainName,
                            safeModePassword,
                            domainName.Split('.')[0].ToUpperInvariant());
                    },
                    safeModePassword);
            }
            finally
            {
                bool recoveryOwnsPassword = _pendingRecoveryActions.Values.Any(request =>
                    ReferenceEquals(request.SensitiveResource, safeModePassword));
                if (succeeded || !recoveryOwnsPassword)
                {
                    safeModePassword.Dispose();
                }

                InstallActiveDirectoryButton.Content = succeeded ? "DONE" : originalContent;
            }
        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            await RunButtonOperationAsync(
                SetStaticIpButton,
                "Set the current network address as static",
                SetCurrentAddressStaticAsync);
        }

        private async void Button_Click_1(object sender, RoutedEventArgs e)
        {
            await RunSimpsonsSetupAsync();
        }

        private async Task<bool> RunSimpsonsSetupAsync()
        {
            bool buttonWasEnabled = SimpsonsSetupButton.IsEnabled;
            IDisposable mutationLease = TryAcquireServerMutation(
                "Run Simpsons server setup",
                SimpsonsSetupButton);
            if (mutationLease == null)
            {
                return false;
            }

            if (!buttonWasEnabled)
            {
                mutationLease.Dispose();
                ShowRecoveryNotice(
                    "Simpsons setup is not available",
                    "The setup control is currently disabled. No server action was queued.",
                    SimpsonsSetupButton);
                return false;
            }

            const string operationKey = "simpsons-setup";
            SimpsonsSetupButton.IsEnabled = false;
            _operationsInFlight.Add(operationKey);
            if (_pendingRecoveryActions.ContainsKey(operationKey))
            {
                _selectedRecoveryKey = operationKey;
                RenderSelectedRecoveryFailure();
            }
            else
            {
                UpdateRecoveryControls();
            }
            try
            {
                string completionFile = GetSimpsonsCompletionFile();
                if (File.Exists(completionFile)
                    && string.Equals(
                        ProtectedWorkflowState.ReadAllText(completionFile).Trim(),
                        SimpsonsSetupCompletionMarker,
                        StringComparison.Ordinal))
                {
                    return await CompleteSimpsonsCleanupAsync(SimpsonsSetupButton);
                }

                OperationBatchResult result = await SimpsonsSolution();
                if (result.Succeeded)
                {
                    WriteCompletionMarker(completionFile, SimpsonsSetupCompletionMarker);
                    return await CompleteSimpsonsCleanupAsync(SimpsonsSetupButton);
                }

                ShowBatchFailure(
                    operationKey,
                    "Simpsons setup is incomplete",
                    result,
                    GetSimpsonsCheckpointFile(),
                    RunSimpsonsSetupAsync,
                    SimpsonsSetupButton);
                return false;
            }
            catch (Exception ex) when (!RecoveryRunner.IsFatal(ex))
            {
                ShowRecoveryFailure(
                    operationKey,
                    "Simpsons setup could not start",
                    ex,
                    RunSimpsonsSetupAsync,
                    SimpsonsSetupButton);
                return false;
            }
            finally
            {
                _operationsInFlight.Remove(operationKey);
                if (_pendingRecoveryActions.Count > 0)
                {
                    RenderSelectedRecoveryFailure();
                }
                else
                {
                    UpdateRecoveryControls();
                }

                mutationLease.Dispose();
            }
        }

        public Task<OperationBatchResult> SimpsonsSolution()
        {
            string SimpsonsOUScript = ReplaceWithDomainStuff(@"
    Import-Module ActiveDirectory;
    $ouName = 'Simpsons';
    $ouPath = 'DC=jackson,DC=local';
    $distinguishedName = 'OU=Simpsons,DC=jackson,DC=local';
    if (-not (Get-ADOrganizationalUnit -Identity $distinguishedName -ErrorAction SilentlyContinue)) {
        New-ADOrganizationalUnit -Name $ouName -Path $ouPath;
    }
    Write-Host 'OU Simpsons successfully created in jackson.local';
");
            string CreateOUScript = ReplaceWithDomainStuff(@"
    $organizationalUnits = @(
        @{Name = 'Staff'; Path = 'DC=jackson,DC=local'; DistinguishedName = 'OU=Staff,DC=jackson,DC=local'},
        @{Name = 'Sales'; Path = 'OU=Staff,DC=jackson,DC=local'; DistinguishedName = 'OU=Sales,OU=Staff,DC=jackson,DC=local'},
        @{Name = 'HR'; Path = 'OU=Staff,DC=jackson,DC=local'; DistinguishedName = 'OU=HR,OU=Staff,DC=jackson,DC=local'},
        @{Name = 'IT'; Path = 'OU=Staff,DC=jackson,DC=local'; DistinguishedName = 'OU=IT,OU=Staff,DC=jackson,DC=local'}
    );
    foreach ($organizationalUnit in $organizationalUnits) {
        if (-not (Get-ADOrganizationalUnit -Identity $organizationalUnit.DistinguishedName -ErrorAction SilentlyContinue)) {
            New-ADOrganizationalUnit -Name $organizationalUnit.Name -Path $organizationalUnit.Path;
        }
    }
");
            string CreateGroupScript = ReplaceWithDomainStuff(@"
    Import-Module ActiveDirectory;
    $groups = @('IT-Group', 'HR-Group', 'Sales-Group');
    $ou = 'OU=Staff,DC=jackson,DC=local';
    foreach ($group in $groups) {
        if (-not (Get-ADGroup -Filter ""Name -eq '$group'"" -SearchBase $ou -ErrorAction SilentlyContinue)) {
            New-ADGroup -Name $group -GroupScope Global -GroupCategory Security -Path $ou -Description ""$group group"";
        }
    }
    Write-Host 'Groups successfully created';
");
            string SpreadUsersScript = ReplaceWithDomainStuff(@"
    Import-Module ActiveDirectory;
    $assignments = @(
        @{Group = 'HR-Group'; Target = 'OU=HR,OU=Staff,DC=jackson,DC=local'},
        @{Group = 'IT-Group'; Target = 'OU=IT,OU=Staff,DC=jackson,DC=local'},
        @{Group = 'Sales-Group'; Target = 'OU=Sales,OU=Staff,DC=jackson,DC=local'}
    );
    $records = Import-Csv 'C:\lol.csv';
    foreach ($record in $records) {
        $user = Get-ADUser -Identity $record.sAMAccountName -Properties SID;
        $stableBucket = 0;
        foreach ($character in $user.SamAccountName.ToCharArray()) {
            $stableBucket = ($stableBucket + [int][char]$character) % $assignments.Count;
        }
        $assignment = $assignments[$stableBucket];
        $alreadyMember = Get-ADGroupMember -Identity $assignment.Group -ErrorAction Stop |
            Where-Object { $_.SID -eq $user.SID } |
            Select-Object -First 1;
        if (-not $alreadyMember) {
            Add-ADGroupMember -Identity $assignment.Group -Members $user -ErrorAction Stop;
        }
        if (-not $user.DistinguishedName.EndsWith(',' + $assignment.Target, [StringComparison]::OrdinalIgnoreCase)) {
            Move-ADObject -Identity $user.DistinguishedName -TargetPath $assignment.Target -ErrorAction Stop;
        }
    }
");
            string ShareScript = ReplaceWithDomainStuff(@"
    $folders = @(
        @{Name = 'HR'; Group = 'HR-Group'},
        @{Name = 'IT'; Group = 'IT-Group'},
        @{Name = 'Sales'; Group = 'Sales-Group'}
    );
    $basePath = 'C:\\Staff';
    foreach ($folder in $folders) {
        $folderPath = Join-Path $basePath $folder.Name;
        if (-not (Get-SmbShare -Name $folder.Name -ErrorAction SilentlyContinue)) {
            New-SmbShare -Name $folder.Name -Path $folderPath -FullAccess $folder.Group;
        }
        $acl = Get-Acl $folderPath;
        $rule = New-Object System.Security.AccessControl.FileSystemAccessRule($folder.Group, 'FullControl', 'ContainerInherit, ObjectInherit', 'None', 'Allow');
        $acl.SetAccessRule($rule);
        Set-Acl $folderPath $acl;
    }
");

            var operations = new[]
            {
                new RecoverableOperation(
                    "Create staff folders",
                    () => Task.Run(() =>
                    {
                        Directory.CreateDirectory("C:\\Staff");
                        Directory.CreateDirectory("C:\\Staff\\HR");
                        Directory.CreateDirectory("C:\\Staff\\IT");
                        Directory.CreateDirectory("C:\\Staff\\Sales");
                    }),
                    maxAttempts: 2,
                    retrySafety: RetrySafety.Idempotent),
                new RecoverableOperation(
                    "Create the Simpsons organizational unit",
                    () => RunPowerShellScriptAsync(SimpsonsOUScript),
                    maxAttempts: 2,
                    retrySafety: RetrySafety.Idempotent),
                new RecoverableOperation(
                    "Write the Simpsons user import file",
                    () => Task.Run(() => File.WriteAllText("C:\\lol.csv", ReplaceWithDomainStuff(Data.SimpsonsUsers))),
                    maxAttempts: 2,
                    retrySafety: RetrySafety.Idempotent),
                new RecoverableOperation(
                    "Import the Simpsons users",
                    () => ExternalProcessRunner.RunCommandScriptAsync(
                        "Import the Simpsons users",
                        "\"" + GetTrustedSystemExecutable("csvde.exe") + "\" -i -k -f \"C:\\lol.csv\""),
                    maxAttempts: 1,
                    dependencies: new[]
                    {
                        "Create the Simpsons organizational unit",
                        "Write the Simpsons user import file"
                    },
                    retrySafety: RetrySafety.Idempotent),
                new RecoverableOperation(
                    "Create staff organizational units",
                    () => RunPowerShellScriptAsync(CreateOUScript),
                    maxAttempts: 2,
                    retrySafety: RetrySafety.Idempotent),
                new RecoverableOperation(
                    "Create staff groups",
                    () => RunPowerShellScriptAsync(CreateGroupScript),
                    maxAttempts: 2,
                    dependencies: new[] { "Create staff organizational units" },
                    retrySafety: RetrySafety.Idempotent),
                new RecoverableOperation(
                    "Distribute users among staff groups",
                    () => RunPowerShellScriptAsync(SpreadUsersScript),
                    maxAttempts: 2,
                    dependencies: new[]
                    {
                        "Import the Simpsons users",
                        "Create staff groups"
                    },
                    retrySafety: RetrySafety.Idempotent),
                new RecoverableOperation(
                    "Create staff shares",
                    () => RunPowerShellScriptAsync(ShareScript),
                    maxAttempts: 2,
                    dependencies: new[]
                    {
                        "Create staff folders",
                        "Create staff groups"
                    },
                    retrySafety: RetrySafety.Idempotent)
            };

            return RecoveryRunner.RunAllAsync(operations, GetSimpsonsCheckpointFile());
        }

        private static string GetSimpsonsCheckpointFile()
        {
            return ProtectedWorkflowState.GetPath("Recovery", "simpsons-setup.steps");
        }

        private static string GetSimpsonsCompletionFile()
        {
            return ProtectedWorkflowState.GetPath("State", "simpsons-setup.completed");
        }

        private async Task<bool> CompleteSimpsonsCleanupAsync(IInputElement focusOrigin)
        {
            await Task.Yield();
            try
            {
                RemoveSimpsonsTaskAfterSuccess();
                if (!RecoveryRunner.ClearCheckpoint(GetSimpsonsCheckpointFile()))
                {
                    throw new InvalidOperationException(
                        "The server actions are complete, but completed recovery state could not be removed. No server action will be repeated.");
                }

                ResolveRecovery("simpsons-setup");
                ResolveRecovery("simpsons-setup-cleanup");
                ShowSuccessUnlessAnotherFailureIsPending(
                    "Simpsons setup completed",
                    "All directory, user, group, and share steps completed. The continuation task and completed recovery state were removed.",
                    focusOrigin);
                return true;
            }
            catch (Exception ex) when (!RecoveryRunner.IsFatal(ex))
            {
                ShowRecoveryFailure(
                    "simpsons-setup-cleanup",
                    "Simpsons setup cleanup is incomplete",
                    ex,
                    () => CompleteSimpsonsCleanupAsync(focusOrigin),
                    focusOrigin);
                return false;
            }
        }

        public string ReplaceWithDomainStuff(string input)
        {
            if (string.IsNullOrWhiteSpace(DomainName) || string.IsNullOrWhiteSpace(DomainCOM))
            {
                throw new InvalidOperationException("The configured domain is missing or incomplete. Promote the server before running this setup.");
            }

            return input.Replace("DC=jackson","DC=" + DomainName).Replace("DC=local","DC=" + DomainCOM);
        }

        private async void InstallChocolateyButton_Click(object sender, RoutedEventArgs e)
        {
            await RunButtonOperationAsync(
                InstallChocolateyButton,
                "Install Chocolatey",
                InstallChocolateyAsync);
        }

        private static async Task ChocoInstall(string software)
        {
            string[] packages = ParseChocolateyPackages(software);
            await ExternalProcessRunner.RunCommandScriptAsync(
                "Install the selected Chocolatey software",
                "\"C:\\ProgramData\\chocolatey\\bin\\choco.exe\" install "
                    + string.Join(" ", packages)
                    + " -y");
        }

        private static string[] ParseChocolateyPackages(string value)
        {
            string[] packages = (value ?? string.Empty)
                .Split(new[] { ' ', '\t', '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (packages.Length == 0)
            {
                throw new ArgumentException("Enter at least one Chocolatey package name.", nameof(value));
            }

            foreach (string package in packages)
            {
                if (package.Length > 128
                    || package.Any(character => !char.IsLetterOrDigit(character)
                        && character != '.'
                        && character != '-'
                        && character != '_'))
                {
                    throw new ArgumentException(
                        "Chocolatey package names may contain only letters, numbers, periods, hyphens, and underscores.",
                        nameof(value));
                }
            }

            return packages;
        }
        private async void InstallChocolateySoftwareButton_Click(object sender, RoutedEventArgs e)
        {
            string software = ChocoSoftwareTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(software))
            {
                ShowRecoveryFailure(
                    "software-name-validation",
                    "Software name is required",
                    new ArgumentException("Enter one or more Chocolatey package names, then start the installation again."),
                    null,
                    ChocoSoftwareTextBox);
                ChocoSoftwareTextBox.Focus();
                return;
            }

            await RunButtonOperationAsync(
                InstallChocolateySoftwareButton,
                "Install the selected Chocolatey software",
                () => ChocoInstall(software));
        }

        private void CommonInstallStuffButton_Click(object sender, RoutedEventArgs e)
        {
            IDisposable mutationLease = TryAcquireServerMutation(
                "Open commonly installed server features",
                CommonInstallStuffButton);
            if (mutationLease == null)
            {
                return;
            }

            try
            {
                CommonlyInstalledWindowsComponents d = new CommonlyInstalledWindowsComponents();
                d.Show();
            }
            finally
            {
                mutationLease.Dispose();
            }
        }

        private async void Button_Click_2(object sender, RoutedEventArgs e)
        {
            await RunButtonOperationAsync(
                AdapterSettingsButton,
                "Open network adapter settings",
                () => Task.Run(() =>
                {
                    string controlPanel = GetTrustedSystemExecutable("control.exe");
                    var startInfo = new ProcessStartInfo(controlPanel, "ncpa.cpl")
                    {
                        UseShellExecute = false
                    };
                    if (Process.Start(startInfo) == null)
                    {
                        throw new InvalidOperationException("Network adapter settings could not be opened.");
                    }
                }),
                requiresServerMutation: false);
        }

        private async void SCCMButton_Click(object sender, RoutedEventArgs e)
        {
            await ShowUnavailableInstallerAsync("SCCM", SCCMButton);
        }

        private async Task<bool> RunUiOperationAsync(
            string operationName,
            Func<Task> operation,
            Func<Task<bool>> retryAction = null,
            IInputElement focusOrigin = null,
            IDisposable sensitiveResource = null)
        {
            Func<Task<bool>> effectiveRetry = retryAction;
            if (effectiveRetry == null)
            {
                effectiveRetry = () => RunUiOperationAsync(
                    operationName,
                    operation,
                    effectiveRetry,
                    focusOrigin,
                    sensitiveResource);
            }

            OperationResult result = await RecoveryRunner.RunAsync(operationName, operation);
            if (result.Succeeded)
            {
                ResolveRecovery(operationName);
                return true;
            }

            if (result.Indeterminate)
            {
                Func<Task<bool>> confirmedCompleted = async () =>
                {
                    await Task.Yield();
                    ResolveRecovery(operationName);
                    ShowRecoveryNotice(
                        operationName + " was recorded as completed",
                        "The reviewed action will not be replayed.",
                        focusOrigin);
                    return true;
                };
                ShowRecoveryNotification(
                    operationName,
                    operationName + " has an uncertain prior outcome",
                    RecoveryRunner.FriendlyMessage(result.Error)
                        + " Choose completed only if it finished and applied successfully. Choose stopped without completing only after verifying that it is no longer running and did not complete or apply.",
                    effectiveRetry,
                    isError: true,
                    focusOrigin: focusOrigin,
                    sensitiveResource: sensitiveResource,
                    requiresReconciliation: true,
                    completedAction: confirmedCompleted,
                    reconciliationTarget: operationName);
                return false;
            }

            ShowRecoveryFailure(
                operationName,
                operationName + " did not complete",
                result.Error,
                effectiveRetry,
                focusOrigin,
                sensitiveResource);
            return false;
        }

        private async Task<bool> RunButtonOperationAsync(
            Button button,
            string operationName,
            Func<Task> operation,
            IDisposable sensitiveResource = null,
            bool requiresServerMutation = true)
        {
            if (button == null)
            {
                throw new ArgumentNullException(nameof(button));
            }

            bool buttonWasEnabled = button.IsEnabled;
            IDisposable mutationLease = requiresServerMutation
                ? TryAcquireServerMutation(operationName, button)
                : null;
            if (requiresServerMutation && mutationLease == null)
            {
                return false;
            }

            if (!buttonWasEnabled || _operationsInFlight.Contains(operationName))
            {
                mutationLease?.Dispose();
                return false;
            }

            Func<Task<bool>> retry = null;
            retry = () => RunButtonOperationAsync(
                button,
                operationName,
                operation,
                sensitiveResource,
                requiresServerMutation);

            button.IsEnabled = false;
            _operationsInFlight.Add(operationName);
            try
            {
                if (_pendingRecoveryActions.ContainsKey(operationName))
                {
                    _selectedRecoveryKey = operationName;
                    RenderSelectedRecoveryFailure();
                }
                else
                {
                    UpdateRecoveryControls();
                }

                bool succeeded = await RunUiOperationAsync(
                    operationName,
                    operation,
                    retry,
                    button,
                    sensitiveResource);
                if (succeeded)
                {
                    ShowSuccessUnlessAnotherFailureIsPending(
                        operationName + " completed",
                        "The operation completed successfully.",
                        button);
                }

                return succeeded;
            }
            finally
            {
                _operationsInFlight.Remove(operationName);
                if (!requiresServerMutation)
                {
                    button.IsEnabled = true;
                }
                if (_pendingRecoveryActions.Count > 0)
                {
                    RenderSelectedRecoveryFailure();
                }
                else
                {
                    UpdateRecoveryControls();
                }

                mutationLease?.Dispose();
            }
        }

        private void ShowBatchFailure(
            string operationKey,
            string title,
            OperationBatchResult result,
            string checkpointFile,
            Func<Task<bool>> retryAction,
            IInputElement focusOrigin = null)
        {
            string[] failures = result.Failures
                .Select(failure => failure.Blocked
                    ? failure.Name + " (waiting for a prerequisite)"
                    : failure.Name)
                .ToArray();
            int completedCount = result.Results.Count(item => item.Succeeded);
            int resumedCount = result.Results.Count(item => item.Resumed);
            string resumedText = resumedCount > 0
                ? $" {resumedCount} previously completed step(s) were preserved and skipped."
                : string.Empty;
            string message = $"{completedCount} of {result.Results.Count} step(s) completed.{resumedText} "
                + "The app continued past independent failures. Retry resumes only incomplete steps. Failed or waiting steps: "
                + string.Join(", ", failures);
            bool requiresReconciliation = result.Failures.Any(failure => failure.Indeterminate);
            OperationResult reconciliationFailure = result.Failures.FirstOrDefault(failure => failure.Indeterminate);
            OperationResult corruptFailure = result.Failures.FirstOrDefault(failure =>
                !string.IsNullOrWhiteSpace(failure.CorruptionEvidenceToken));
            bool repairsCorruptState = corruptFailure != null;
            if (repairsCorruptState)
            {
                requiresReconciliation = false;
                reconciliationFailure = null;
                message += " Durable recovery state is corrupt. The repair action archives the current evidence, creates a new empty recovery state, and then starts the workflow again.";
            }
            if (requiresReconciliation)
            {
                int uncertainCount = result.Failures.Count(failure => failure.Indeterminate);
                message += " Review the uncertain action " + reconciliationFailure.Name + ". "
                    + "Choose completed only if it finished and applied successfully. Choose stopped without completing only after verifying that it is no longer running and did not complete or apply."
                    + (uncertainCount > 1
                        ? " The remaining uncertain actions will be reviewed separately, one at a time."
                        : string.Empty);
            }

            Exception firstError = result.Failures.FirstOrDefault()?.Error;
            if (firstError != null)
            {
                ErrorLog.Write(title, firstError);
            }

            string reviewedRetryRequestId = Guid.NewGuid().ToString("N") + "-retry";
            string reviewedCompletedRequestId = Guid.NewGuid().ToString("N") + "-completed";
            Func<Task<bool>> reviewedRetry = repairsCorruptState
                ? (Func<Task<bool>>)(() => RepairCorruptCheckpointAndRetryAsync(
                    checkpointFile,
                    corruptFailure.CorruptionEvidenceToken,
                    retryAction))
                : () => RetryBatchAfterUserReviewAsync(
                    checkpointFile,
                    reviewedRetryRequestId,
                    result,
                    retryAction,
                    reconciliationFailure?.Name,
                    IndeterminateReconciliationOutcome.ConfirmedNotAppliedAndStopped);
            Func<Task<bool>> reviewedCompleted = requiresReconciliation
                ? (Func<Task<bool>>)(() => RetryBatchAfterUserReviewAsync(
                    checkpointFile,
                    reviewedCompletedRequestId,
                    result,
                    retryAction,
                    reconciliationFailure.Name,
                    IndeterminateReconciliationOutcome.ConfirmedSucceeded))
                : null;
            ShowRecoveryNotification(
                operationKey,
                title,
                message,
                reviewedRetry,
                isError: true,
                focusOrigin: focusOrigin,
                requiresReconciliation: requiresReconciliation,
                completedAction: reviewedCompleted,
                reconciliationTarget: reconciliationFailure?.Name,
                repairsCorruptState: repairsCorruptState);
        }

        private static async Task<bool> RetryBatchAfterUserReviewAsync(
            string checkpointFile,
            string requestId,
            OperationBatchResult priorResult,
            Func<Task<bool>> retryAction,
            string reconciliationTarget,
            IndeterminateReconciliationOutcome reconciliationOutcome)
        {
            var preparations = new List<ReviewedOperationPreparation>();
            foreach (OperationResult failure in priorResult.Failures)
            {
                bool dependencyOnly = failure.Error is OperationDependencyException
                    || failure.Error is MissingOperationDependencyException
                    || failure.Error is OperationDependencyCycleException;
                if (failure.Indeterminate)
                {
                    if (!string.Equals(failure.Name, reconciliationTarget, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    ReviewedOperationState expectedState = string.Equals(
                        failure.RecoveryState,
                        "running",
                        StringComparison.Ordinal)
                        ? ReviewedOperationState.Running
                        : ReviewedOperationState.Indeterminate;
                    preparations.Add(new ReviewedOperationPreparation(
                        failure.Name,
                        expectedState,
                        failure.UserRetryGeneration,
                        failure.Attempts,
                        reconciliationOutcome,
                        expectedReconciliationToken: failure.ReconciliationToken));
                    continue;
                }

                if (failure.Attempts <= 0 || dependencyOnly)
                {
                    continue;
                }

                preparations.Add(new ReviewedOperationPreparation(
                    failure.Name,
                    ReviewedOperationState.Failed,
                    failure.UserRetryGeneration,
                    failure.Attempts));
            }

            if (preparations.Count > 0
                && !RecoveryRunner.PrepareReviewedRetry(
                    checkpointFile,
                    requestId,
                    preparations))
            {
                throw new OperationStatePersistenceException(
                    reconciliationTarget ?? preparations[0].Name,
                    actionCompleted: false,
                    actionStarted: !string.IsNullOrWhiteSpace(reconciliationTarget));
            }

            return await retryAction();
        }

        private static async Task<bool> RepairCorruptCheckpointAndRetryAsync(
            string checkpointFile,
            string expectedCorruptionEvidenceToken,
            Func<Task<bool>> retryAction)
        {
            if (!RecoveryRunner.RepairCorruptCheckpoint(
                checkpointFile,
                expectedCorruptionEvidenceToken))
            {
                throw new OperationStatePersistenceException(
                    "Repair corrupt recovery state",
                    actionCompleted: false);
            }

            return await retryAction();
        }

        private void ShowRecoveryFailure(
            string operationKey,
            string title,
            Exception exception,
            Func<Task<bool>> retryAction,
            IInputElement focusOrigin = null,
            IDisposable sensitiveResource = null)
        {
            if (exception != null)
            {
                ErrorLog.Write(title, exception);
            }

            ShowRecoveryNotification(
                operationKey,
                title,
                RecoveryRunner.FriendlyMessage(exception),
                retryAction,
                isError: true,
                focusOrigin: focusOrigin,
                sensitiveResource: sensitiveResource);
        }

        private void ShowRecoveryNotice(string title, string message, IInputElement focusOrigin = null)
        {
            ShowRecoveryNotification(
                null,
                title,
                message,
                null,
                isError: false,
                focusOrigin: focusOrigin);
        }

        private void ClearSupplementalRecoveryNotice(string title)
        {
            if (!string.Equals(_supplementalRecoveryTitle, title, StringComparison.Ordinal))
            {
                return;
            }

            _supplementalRecoveryTitle = null;
            _supplementalRecoveryMessage = null;
        }

        private void ShowSuccessUnlessAnotherFailureIsPending(
            string title,
            string message,
            IInputElement focusOrigin = null)
        {
            if (_pendingRecoveryActions.Count > 0)
            {
                RenderSelectedRecoveryFailure();
                return;
            }

            ShowRecoveryNotice(title, message, focusOrigin);
        }

        private void ShowRecoveryNotification(
            string operationKey,
            string title,
            string message,
            Func<Task<bool>> retryAction,
            bool isError,
            IInputElement focusOrigin = null,
            IDisposable sensitiveResource = null,
            bool requiresReconciliation = false,
            Func<Task<bool>> completedAction = null,
            string reconciliationTarget = null,
            bool repairsCorruptState = false)
        {
            if (retryAction != null)
            {
                string key = string.IsNullOrWhiteSpace(operationKey) ? title : operationKey;
                if (_pendingRecoveryActions.TryGetValue(key, out RecoveryRequest previousRequest)
                    && previousRequest.SensitiveResource != null
                    && !ReferenceEquals(previousRequest.SensitiveResource, sensitiveResource))
                {
                    previousRequest.SensitiveResource.Dispose();
                }

                _pendingRecoveryActions[key] = new RecoveryRequest
                {
                    Key = key,
                    Title = title,
                    Message = message,
                    Action = retryAction,
                    CompletedAction = completedAction,
                    FocusOrigin = focusOrigin,
                    SensitiveResource = sensitiveResource,
                    RequiresReconciliation = requiresReconciliation,
                    ReconciliationTarget = reconciliationTarget,
                    RepairsCorruptState = repairsCorruptState
                };
                _selectedRecoveryKey = key;
                RenderSelectedRecoveryFailure();
                return;
            }

            if (_pendingRecoveryActions.Count > 0)
            {
                _supplementalRecoveryTitle = title;
                _supplementalRecoveryMessage = message;
                RenderSelectedRecoveryFailure();
                return;
            }

            bool wasVisible = RecoveryNotification.Visibility == Visibility.Visible;
            _noticeFocusReturnTarget = focusOrigin ?? Keyboard.FocusedElement;
            _noticeOpenedFromReview = false;
            _supplementalRecoveryTitle = null;
            _supplementalRecoveryMessage = null;
            RecoveryTitleText.Text = title;
            RecoveryMessageText.Text = message;
            RecoverySupplementalText.Text = string.Empty;
            RecoverySupplementalText.Visibility = Visibility.Collapsed;
            OpenRecoveryLogButton.Visibility = isError ? Visibility.Visible : Visibility.Collapsed;
            RecoveryNotification.Background = isError
                ? new SolidColorBrush(Color.FromRgb(90, 31, 31))
                : new SolidColorBrush(Color.FromRgb(26, 72, 52));
            RecoveryNotification.Visibility = Visibility.Visible;
            RecoveryNavigationPanel.Visibility = Visibility.Collapsed;
            if (!wasVisible)
            {
                _focusEnteredRecoveryNotification = false;
            }

            UpdateRecoveryControls();
            RaiseRecoveryLiveRegionChanged();
        }

        public void ShowUnexpectedError(Exception exception)
        {
            ShowRecoveryFailure(
                "unexpected-application-error",
                "An unexpected action failed, but the app is still running",
                exception,
                null);
        }

        private async void RetryRecoveryButton_Click(object sender, RoutedEventArgs e)
        {
            await RunSelectedRecoveryActionAsync(_lastFailedAction, completedChoice: false);
        }

        private async void MarkRecoveryCompletedButton_Click(object sender, RoutedEventArgs e)
        {
            await RunSelectedRecoveryActionAsync(_completedReconciliationAction, completedChoice: true);
        }

        private async Task RunSelectedRecoveryActionAsync(
            Func<Task<bool>> selectedAction,
            bool completedChoice)
        {
            if (_isRetrying || selectedAction == null)
            {
                return;
            }

            string retryKey = _selectedRecoveryKey;
            IInputElement focusOrigin = null;
            if (!string.IsNullOrWhiteSpace(retryKey)
                && _pendingRecoveryActions.TryGetValue(retryKey, out RecoveryRequest selectedRequest))
            {
                focusOrigin = selectedRequest.FocusOrigin;
            }

            _isRetrying = true;
            RecoveryTitleText.Text = completedChoice
                ? "Recording the reviewed action as completed"
                : "Retrying the reviewed action";
            RecoveryMessageText.Text = completedChoice
                ? "The reviewed action will not be replayed. Remaining incomplete steps will continue."
                : "Completed steps are being preserved while the reviewed incomplete work is retried.";
            UpdateRecoveryControls();
            RaiseRecoveryLiveRegionChanged();

            try
            {
                bool succeeded = await selectedAction();
                if (succeeded
                    && !string.IsNullOrWhiteSpace(retryKey)
                    && _pendingRecoveryActions.TryGetValue(retryKey, out RecoveryRequest registeredRequest)
                    && (ReferenceEquals(registeredRequest.Action, selectedAction)
                        || ReferenceEquals(registeredRequest.CompletedAction, selectedAction)))
                {
                    _pendingRecoveryActions.Remove(retryKey);
                    registeredRequest.SensitiveResource?.Dispose();
                }

                if (succeeded)
                {
                    ResolveRecovery(retryKey);
                }
                else
                {
                    SelectPendingRecovery();
                }
            }
            catch (Exception ex) when (!RecoveryRunner.IsFatal(ex))
            {
                ErrorLog.Write("Review recovery action " + retryKey, ex);
                if (_pendingRecoveryActions.TryGetValue(retryKey, out RecoveryRequest failedRequest))
                {
                    failedRequest.Message = RecoveryRunner.FriendlyMessage(ex);
                }
            }
            finally
            {
                _isRetrying = false;
                SelectPendingRecovery();
                if (_lastFailedAction == null)
                {
                    ShowRecoveryNotice(
                        "Recovery action completed",
                        "The reviewed action completed successfully.",
                        focusOrigin);
                }
                else
                {
                    RenderSelectedRecoveryFailure();
                }

                RestoreFocusAfterRecoveryAction(focusOrigin);
            }
        }

        private void RestoreFocusAfterRecoveryAction(IInputElement completedActionOrigin)
        {
            Dispatcher.BeginInvoke(new System.Action(() =>
            {
                if (_lastFailedAction != null)
                {
                    if (RetryRecoveryButton.IsVisible && RetryRecoveryButton.IsEnabled)
                    {
                        RetryRecoveryButton.Focus();
                    }
                    else if (MarkRecoveryCompletedButton.IsVisible && MarkRecoveryCompletedButton.IsEnabled)
                    {
                        MarkRecoveryCompletedButton.Focus();
                    }
                    else
                    {
                        OpenRecoveryLogButton.Focus();
                    }

                    return;
                }

                if (DismissRecoveryButton.IsVisible && DismissRecoveryButton.IsEnabled)
                {
                    DismissRecoveryButton.Focus();
                    return;
                }

                RestoreMainWindowFocus(completedActionOrigin);
            }));
        }

        private void SelectPendingRecovery()
        {
            if (!string.IsNullOrWhiteSpace(_selectedRecoveryKey)
                && _pendingRecoveryActions.TryGetValue(_selectedRecoveryKey, out RecoveryRequest selectedRequest))
            {
                _lastFailedAction = selectedRequest.Action;
                _completedReconciliationAction = selectedRequest.CompletedAction;
                return;
            }

            KeyValuePair<string, RecoveryRequest> next = _pendingRecoveryActions.FirstOrDefault();
            _selectedRecoveryKey = next.Key;
            _lastFailedAction = next.Value?.Action;
            _completedReconciliationAction = next.Value?.CompletedAction;
        }

        private void ResolveRecovery(string operationKey)
        {
            if (string.IsNullOrWhiteSpace(operationKey))
            {
                return;
            }

            if (_pendingRecoveryActions.TryGetValue(operationKey, out RecoveryRequest completedRequest))
            {
                _pendingRecoveryActions.Remove(operationKey);
                completedRequest.SensitiveResource?.Dispose();
            }
            if (string.Equals(_selectedRecoveryKey, operationKey, StringComparison.OrdinalIgnoreCase))
            {
                _selectedRecoveryKey = null;
            }

            SelectPendingRecovery();
            UpdateRecoveryControls();
        }

        private void RenderSelectedRecoveryFailure()
        {
            SelectPendingRecovery();
            if (string.IsNullOrWhiteSpace(_selectedRecoveryKey)
                || !_pendingRecoveryActions.TryGetValue(_selectedRecoveryKey, out RecoveryRequest request))
            {
                UpdateRecoveryControls();
                return;
            }

            IReadOnlyList<string> orderedKeys = GetOrderedRecoveryKeys();
            int selectedIndex = IndexOfRecoveryKey(orderedKeys, request.Key);
            bool selectedOperationIsRunning = _operationsInFlight.Contains(request.Key);
            string position = $"Pending action {Math.Max(1, selectedIndex + 1)} of {orderedKeys.Count}.";
            RecoveryPositionText.Text = position;
            AutomationProperties.SetName(RecoveryPositionText, position);
            RecoveryTitleText.Text = selectedOperationIsRunning
                ? request.Title + " is running"
                : request.Title;
            RecoveryMessageText.Text = (selectedOperationIsRunning
                    ? "This action is already running. Completed work is being preserved, and retry returns after the running action stops."
                    : request.Message)
                + " " + position;
            if (!string.IsNullOrWhiteSpace(_supplementalRecoveryTitle)
                || !string.IsNullOrWhiteSpace(_supplementalRecoveryMessage))
            {
                RecoverySupplementalText.Text = "Also reported: "
                    + _supplementalRecoveryTitle
                    + ". "
                    + _supplementalRecoveryMessage;
                RecoverySupplementalText.Visibility = Visibility.Visible;
            }
            else
            {
                RecoverySupplementalText.Text = string.Empty;
                RecoverySupplementalText.Visibility = Visibility.Collapsed;
            }

            _noticeFocusReturnTarget = request.FocusOrigin;
            RecoveryNotification.Background = new SolidColorBrush(Color.FromRgb(90, 31, 31));
            if (selectedOperationIsRunning)
            {
                RecoveryNotification.Background = new SolidColorBrush(Color.FromRgb(26, 72, 52));
            }
            RecoveryNotification.Visibility = Visibility.Visible;
            RecoveryNavigationPanel.Visibility = Visibility.Visible;
            OpenRecoveryLogButton.Visibility = Visibility.Visible;
            UpdateRecoveryControls();
            RaiseRecoveryLiveRegionChanged();
        }

        private void UpdateRecoveryControls()
        {
            bool hasRetry = _lastFailedAction != null;
            bool selectedOperationIsRunning = !string.IsNullOrWhiteSpace(_selectedRecoveryKey)
                && _operationsInFlight.Contains(_selectedRecoveryKey);
            RetryRecoveryButton.Visibility = hasRetry ? Visibility.Visible : Visibility.Collapsed;
            RetryRecoveryButton.IsEnabled = hasRetry && !_isRetrying && !selectedOperationIsRunning;
            if (hasRetry
                && !string.IsNullOrWhiteSpace(_selectedRecoveryKey)
                && _pendingRecoveryActions.TryGetValue(_selectedRecoveryKey, out RecoveryRequest selectedRequest))
            {
                string retryLabel;
                string retryHelp;
                if (selectedOperationIsRunning || _isRetrying)
                {
                    retryLabel = "Already running: " + selectedRequest.Title;
                    retryHelp = "This action is already running. Retry becomes available after it stops.";
                }
                else if (selectedRequest.RequiresReconciliation)
                {
                    string target = selectedRequest.ReconciliationTarget ?? selectedRequest.Title;
                    retryLabel = "Stopped without completing — retry: " + target;
                    retryHelp = "Choose only after verifying that the uncertain action stopped and did not complete or apply.";
                }
                else if (selectedRequest.RepairsCorruptState)
                {
                    retryLabel = "Repair recovery state and continue: " + selectedRequest.Title;
                    retryHelp = "Archives the corrupt recovery evidence, creates empty recovery state, and starts the workflow again only after this explicit action.";
                }
                else
                {
                    retryLabel = "Retry: " + selectedRequest.Title;
                    retryHelp = "Retries only this failed or incomplete action. Completed setup steps are preserved.";
                }
                RetryRecoveryButton.Content = new TextBlock
                {
                    Text = retryLabel,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 220
                };
                AutomationProperties.SetName(RetryRecoveryButton, retryLabel);
                AutomationProperties.SetHelpText(RetryRecoveryButton, retryHelp);

                bool canMarkCompleted = selectedRequest.RequiresReconciliation
                    && selectedRequest.CompletedAction != null;
                MarkRecoveryCompletedButton.Visibility = canMarkCompleted
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                MarkRecoveryCompletedButton.IsEnabled = canMarkCompleted
                    && !_isRetrying
                    && !selectedOperationIsRunning;
                if (canMarkCompleted)
                {
                    string target = selectedRequest.ReconciliationTarget ?? selectedRequest.Title;
                    string completedLabel = selectedOperationIsRunning || _isRetrying
                        ? "Already running: " + target
                        : "It completed — continue: " + target;
                    MarkRecoveryCompletedButton.Content = new TextBlock
                    {
                        Text = completedLabel,
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 220
                    };
                    AutomationProperties.SetName(MarkRecoveryCompletedButton, completedLabel);
                    AutomationProperties.SetHelpText(
                        MarkRecoveryCompletedButton,
                        selectedOperationIsRunning || _isRetrying
                            ? "This action is already running. Review choices return after it stops."
                            : "Choose only if the uncertain action completed and applied successfully. It will not be replayed.");
                }
            }
            else
            {
                MarkRecoveryCompletedButton.Visibility = Visibility.Collapsed;
            }

            IReadOnlyList<string> orderedKeys = GetOrderedRecoveryKeys();
            bool hasMultiple = orderedKeys.Count > 1;
            PreviousRecoveryButton.Visibility = hasMultiple ? Visibility.Visible : Visibility.Collapsed;
            NextRecoveryButton.Visibility = hasMultiple ? Visibility.Visible : Visibility.Collapsed;
            PreviousRecoveryButton.IsEnabled = hasMultiple && !_isRetrying;
            NextRecoveryButton.IsEnabled = hasMultiple && !_isRetrying;

            int pendingCount = _pendingRecoveryActions.Count;
            ReviewPendingRecoveryButton.Visibility = pendingCount > 0
                && RecoveryNotification.Visibility != Visibility.Visible
                ? Visibility.Visible
                : Visibility.Collapsed;
            string reviewLabel = $"Review pending actions ({pendingCount})";
            ReviewPendingRecoveryButton.Content = reviewLabel;
            AutomationProperties.SetName(ReviewPendingRecoveryButton, reviewLabel);
        }

        private void RaiseRecoveryLiveRegionChanged()
        {
            Dispatcher.BeginInvoke(new System.Action(() =>
            {
                string accessibleMessage = RecoveryTitleText.Text + ". " + RecoveryMessageText.Text;
                if (RecoverySupplementalText.Visibility == Visibility.Visible)
                {
                    accessibleMessage += " " + RecoverySupplementalText.Text;
                }

                AutomationProperties.SetName(RecoveryMessageText, accessibleMessage);
                AutomationPeer peer = UIElementAutomationPeer.FromElement(RecoveryMessageText)
                    ?? UIElementAutomationPeer.CreatePeerForElement(RecoveryMessageText);
                peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            }));
        }

        private void ReviewPendingRecoveryButton_Click(object sender, RoutedEventArgs e)
        {
            RenderSelectedRecoveryFailure();
            _noticeOpenedFromReview = true;
            _noticeFocusReturnTarget = ReviewPendingRecoveryButton;
            if (RetryRecoveryButton.Visibility == Visibility.Visible
                && RetryRecoveryButton.IsEnabled)
            {
                RetryRecoveryButton.Focus();
            }
            else
            {
                OpenRecoveryLogButton.Focus();
            }
        }

        private void PreviousRecoveryButton_Click(object sender, RoutedEventArgs e)
        {
            SelectRelativeRecovery(-1);
        }

        private void NextRecoveryButton_Click(object sender, RoutedEventArgs e)
        {
            SelectRelativeRecovery(1);
        }

        private void SelectRelativeRecovery(int offset)
        {
            IReadOnlyList<string> orderedKeys = GetOrderedRecoveryKeys();
            if (orderedKeys.Count == 0)
            {
                return;
            }

            int currentIndex = IndexOfRecoveryKey(orderedKeys, _selectedRecoveryKey);
            int nextIndex = currentIndex < 0
                ? 0
                : (currentIndex + offset + orderedKeys.Count) % orderedKeys.Count;
            _selectedRecoveryKey = orderedKeys[nextIndex];
            SelectPendingRecovery();
            RenderSelectedRecoveryFailure();
        }

        private IReadOnlyList<string> GetOrderedRecoveryKeys()
        {
            return _pendingRecoveryActions.Keys
                .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static int IndexOfRecoveryKey(IReadOnlyList<string> keys, string key)
        {
            for (int index = 0; index < keys.Count; index++)
            {
                if (string.Equals(keys[index], key, StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }

            return -1;
        }

        private void DismissRecoveryButton_Click(object sender, RoutedEventArgs e)
        {
            DismissRecoveryNotification();
        }

        private void RecoveryNotification_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            _focusEnteredRecoveryNotification = true;
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape
                && InitialSetupConfirmationPanel.Visibility == Visibility.Visible)
            {
                _initialSetupAuthorizationCancelled = true;
                _initialSetupAuthorizationGate?.Cancel();
                _initialSetupAuthorizationRunning = false;
                CloseInitialSetupConfirmation(restoreFocus: true);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && RecoveryNotification.Visibility == Visibility.Visible)
            {
                DismissRecoveryNotification();
                e.Handled = true;
            }
        }

        private void DismissRecoveryNotification()
        {
            bool restoreFocus = _focusEnteredRecoveryNotification
                && RecoveryNotification.IsKeyboardFocusWithin;
            IInputElement focusReturnTarget = _noticeFocusReturnTarget;
            if (!_noticeOpenedFromReview
                && !string.IsNullOrWhiteSpace(_selectedRecoveryKey)
                && _pendingRecoveryActions.TryGetValue(_selectedRecoveryKey, out RecoveryRequest selectedRequest))
            {
                focusReturnTarget = selectedRequest.FocusOrigin;
            }

            RecoveryNotification.Visibility = Visibility.Collapsed;
            _focusEnteredRecoveryNotification = false;
            _noticeOpenedFromReview = false;
            UpdateRecoveryControls();
            if (restoreFocus)
            {
                RestoreMainWindowFocus(focusReturnTarget);
            }
        }

        private void RestoreMainWindowFocus(IInputElement preferredTarget)
        {
            UIElement focusTarget = preferredTarget as UIElement;
            if (focusTarget == null || !focusTarget.IsVisible || !focusTarget.IsEnabled)
            {
                focusTarget = ReviewPendingRecoveryButton.IsVisible && ReviewPendingRecoveryButton.IsEnabled
                    ? ReviewPendingRecoveryButton
                    : SetStaticIpButton.IsVisible && SetStaticIpButton.IsEnabled
                        ? SetStaticIpButton
                        : null;
            }

            if (focusTarget != null)
            {
                focusTarget.Focus();
            }
            else
            {
                Focus();
            }
        }

        private void OpenRecoveryLogButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(ErrorLog.LogDirectory);
                ProcessStartInfo startInfo;
                if (File.Exists(ErrorLog.CurrentLogFile))
                {
                    startInfo = new ProcessStartInfo(
                        "explorer.exe",
                        $"/select,\"{ErrorLog.CurrentLogFile}\"");
                }
                else
                {
                    startInfo = new ProcessStartInfo("explorer.exe", $"\"{ErrorLog.LogDirectory}\"");
                }

                startInfo.UseShellExecute = true;
                Process.Start(startInfo);
            }
            catch (Exception ex) when (!RecoveryRunner.IsFatal(ex))
            {
                ErrorLog.Write("Open the recovery log", ex);
                RecoveryMessageText.Text = "The error-log folder could not be opened. Its path is " + ErrorLog.LogDirectory;
                RaiseRecoveryLiveRegionChanged();
            }
        }

        private static async Task DownloadAndLaunchAsync(
            Uri source,
            string destination,
            string expectedSha256,
            string arguments = null)
        {
            await Task.Yield();
            throw new InvalidOperationException(
                "Legacy server-role installer downloads are disabled until credential-safe builds are published and pinned.");
#pragma warning disable CS0162
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            ValidateInstallerUri(source);

            if (string.IsNullOrWhiteSpace(destination))
            {
                throw new ArgumentException("A download destination is required.", nameof(destination));
            }

            if (string.IsNullOrWhiteSpace(expectedSha256) || expectedSha256.Length != 64)
            {
                throw new ArgumentException("A full SHA-256 installer digest is required.", nameof(expectedSha256));
            }

            string destinationDirectory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            string temporaryPath = destination + ".download-" + Guid.NewGuid().ToString("N");
            string backupPath = destination + ".previous";
            string failedReplacementPath = destination + ".failed";
            bool destinationExisted = File.Exists(destination);
            bool replacementApplied = false;
            bool launchCommitted = false;
            try
            {
                await DownloadFileWithTimeoutAsync(source, temporaryPath, TimeSpan.FromMinutes(2));

                var downloadedFile = new FileInfo(temporaryPath);
                if (!downloadedFile.Exists || downloadedFile.Length < 2)
                {
                    throw new InvalidDataException("The downloaded installer was empty.");
                }

                using (FileStream stream = File.OpenRead(temporaryPath))
                {
                    if (stream.ReadByte() != 'M' || stream.ReadByte() != 'Z')
                    {
                        throw new InvalidDataException("The downloaded file is not a Windows executable.");
                    }
                }

                string actualSha256;
                using (FileStream stream = File.OpenRead(temporaryPath))
                using (SHA256 sha256 = SHA256.Create())
                {
                    actualSha256 = BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty);
                }

                if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("The downloaded installer did not match its pinned SHA-256 digest.");
                }

                if (destinationExisted)
                {
                    if (File.Exists(backupPath))
                    {
                        File.Delete(backupPath);
                    }

                    File.Replace(temporaryPath, destination, backupPath, true);
                }
                else
                {
                    File.Move(temporaryPath, destination);
                }
                replacementApplied = true;

                using (var launchHandle = new FileStream(
                    destination,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read))
                {
                    string launchSha256;
                    using (SHA256 sha256 = SHA256.Create())
                    {
                        launchSha256 = BitConverter.ToString(sha256.ComputeHash(launchHandle)).Replace("-", string.Empty);
                    }

                    if (!string.Equals(launchSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException("The installer changed before launch and was not opened.");
                    }

                    var startInfo = new ProcessStartInfo(destination, arguments ?? string.Empty)
                    {
                        UseShellExecute = true
                    };
                    if (Process.Start(startInfo) == null)
                    {
                        throw new InvalidOperationException("The downloaded installer could not be opened.");
                    }
                }
                launchCommitted = true;

                try
                {
                    if (File.Exists(backupPath))
                    {
                        File.Delete(backupPath);
                    }
                }
                catch (Exception cleanupError) when (!RecoveryRunner.IsFatal(cleanupError))
                {
                    ErrorLog.Write("Remove the previous installer after launch", cleanupError);
                }
            }
            catch
            {
                try
                {
                    if (!launchCommitted && replacementApplied && destinationExisted && File.Exists(backupPath))
                    {
                        if (File.Exists(failedReplacementPath))
                        {
                            File.Delete(failedReplacementPath);
                        }

                        File.Replace(backupPath, destination, failedReplacementPath, true);
                        if (File.Exists(failedReplacementPath))
                        {
                            File.Delete(failedReplacementPath);
                        }
                    }
                    else if (!launchCommitted && replacementApplied && !destinationExisted && File.Exists(destination))
                    {
                        File.Delete(destination);
                    }
                }
                catch (Exception rollbackError) when (!RecoveryRunner.IsFatal(rollbackError))
                {
                    ErrorLog.Write("Restore the previous installer after launch failed", rollbackError);
                }

                throw;
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
                catch (Exception cleanupError) when (!RecoveryRunner.IsFatal(cleanupError))
                {
                    ErrorLog.Write("Remove an incomplete installer download", cleanupError);
                }
            }
#pragma warning restore CS0162
        }

        private static async Task DownloadFileWithTimeoutAsync(
            Uri source,
            string destination,
            TimeSpan timeout)
        {
            const long maximumInstallerBytes = 64L * 1024L * 1024L;
            using (var deadline = new CancellationTokenSource(timeout))
            {
                try
                {
                    using (var handler = new HttpClientHandler { AllowAutoRedirect = false })
                    using (var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan })
                    {
                        Uri current = source;
                        for (int redirect = 0; redirect <= 3; redirect++)
                        {
                            using (HttpResponseMessage response = await client.GetAsync(
                                current,
                                HttpCompletionOption.ResponseHeadersRead,
                                deadline.Token).ConfigureAwait(true))
                            {
                                if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400)
                                {
                                    Uri location = response.Headers.Location;
                                    if (location == null)
                                    {
                                        throw new InvalidDataException("The installer download returned a redirect without a destination.");
                                    }

                                    Uri next = location.IsAbsoluteUri ? location : new Uri(current, location);
                                    ValidateInstallerUri(next);
                                    current = next;
                                    continue;
                                }

                                response.EnsureSuccessStatusCode();
                                long? declaredLength = response.Content.Headers.ContentLength;
                                if (declaredLength.HasValue
                                    && (declaredLength.Value <= 0 || declaredLength.Value > maximumInstallerBytes))
                                {
                                    throw new InvalidDataException("The installer download size is outside the supported limit.");
                                }

                                using (Stream input = await response.Content.ReadAsStreamAsync().ConfigureAwait(true))
                                using (var output = new FileStream(
                                    destination,
                                    FileMode.CreateNew,
                                    FileAccess.Write,
                                    FileShare.None,
                                    81920,
                                    useAsync: true))
                                {
                                    var buffer = new byte[81920];
                                    long total = 0;
                                    while (true)
                                    {
                                        int read = await input.ReadAsync(
                                            buffer,
                                            0,
                                            buffer.Length,
                                            deadline.Token).ConfigureAwait(true);
                                        if (read == 0)
                                        {
                                            break;
                                        }

                                        total += read;
                                        if (total > maximumInstallerBytes)
                                        {
                                            throw new InvalidDataException("The installer download exceeded the supported size limit.");
                                        }

                                        await output.WriteAsync(
                                            buffer,
                                            0,
                                            read,
                                            deadline.Token).ConfigureAwait(true);
                                    }

                                    await output.FlushAsync(deadline.Token).ConfigureAwait(true);
                                }

                                return;
                            }
                        }

                        throw new InvalidDataException("The installer download exceeded the redirect limit.");
                    }
                }
                catch (OperationCanceledException ex) when (deadline.IsCancellationRequested)
                {
                    throw new TimeoutException("The installer download did not finish within the two-minute limit.", ex);
                }
            }
        }

        private static void ValidateInstallerUri(Uri source)
        {
            if (source == null
                || !source.IsAbsoluteUri
                || !string.Equals(source.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(source.Host, "raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrEmpty(source.UserInfo))
            {
                throw new InvalidOperationException("Installer downloads require the pinned raw.githubusercontent.com HTTPS origin.");
            }
        }
    }
}
