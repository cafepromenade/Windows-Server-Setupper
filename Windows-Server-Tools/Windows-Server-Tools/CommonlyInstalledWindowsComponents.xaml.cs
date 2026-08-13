using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Windows_Server_Tools
{
    /// <summary>
    /// Interaction logic for CommonlyInstalledWindowsComponents.xaml
    /// </summary>
    public partial class CommonlyInstalledWindowsComponents : Window
    {
        private Func<Task<bool>> _retryAction;
        private readonly Dictionary<string, FeatureRecoveryRequest> _pendingRetryActions =
            new Dictionary<string, FeatureRecoveryRequest>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _operationsInFlight =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string _selectedRetryKey;
        private bool _isRetrying;
        private FeatureRecoveryRequest _displayedStatusRequest;
        private bool _focusEnteredStatusPanel;
        private bool _statusOpenedFromReview;
        private readonly Dictionary<Button, MutationControlSnapshot> _mutationControlSnapshots =
            new Dictionary<Button, MutationControlSnapshot>();

        private sealed class MutationControlSnapshot
        {
            public bool IsEnabled { get; set; }

            public string HelpText { get; set; }
        }

        private sealed class FeatureRecoveryRequest
        {
            public string Key { get; set; }

            public string Message { get; set; }

            public Func<Task<bool>> Action { get; set; }

            public IInputElement FocusOrigin { get; set; }
        }

        public CommonlyInstalledWindowsComponents()
        {
            InitializeComponent();
            try
            {
                LogoService logoService = LogoService.CreateDefault();
                AppLogoImage.Source = logoService.LoadPresentationSource(logoService.LoadSettings());
            }
            catch (Exception ex) when (!RecoveryRunner.IsFatal(ex))
            {
                ErrorLog.Write("Load application logo in server features", ex);
            }
            ServerMutationCoordinator.StateChanged += ServerMutationCoordinator_StateChanged;
            ApplyServerMutationControlState();
        }

        protected override void OnClosed(EventArgs e)
        {
            ServerMutationCoordinator.StateChanged -= ServerMutationCoordinator_StateChanged;
            base.OnClosed(e);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (IIS.IsVisible && IIS.IsEnabled && !IsKeyboardFocusWithin)
            {
                IIS.Focus();
            }
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
            Button[] controls = { IIS, FileAndStorage };
            if (!string.IsNullOrWhiteSpace(runningOperation))
            {
                if (_mutationControlSnapshots.Count == 0)
                {
                    foreach (Button control in controls)
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
                foreach (Button control in controls)
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

            UpdateRetryControl();
        }

        private async void IIS_Click(object sender, RoutedEventArgs e)
        {
            await RunFeatureOperationAsync(
                IIS,
                "Install IIS and all of its components",
                "# Install IIS and all its components\r\n" +
                "$requestedFeatures = @('Web-Server')\r\n" +
                "$installResult = Install-WindowsFeature -Name $requestedFeatures -IncludeAllSubFeature -IncludeManagementTools -ErrorAction Stop\r\n" +
                "if (-not $installResult.Success) { throw ('Windows feature installation reported failure for: ' + ($requestedFeatures -join ', ')) }\r\n" +
                "$notInstalled = @()\r\n" +
                "foreach ($featureName in $requestedFeatures) {\r\n" +
                "    $feature = Get-WindowsFeature -Name $featureName -ErrorAction Stop\r\n" +
                "    if ($null -eq $feature -or $feature.InstallState -ne 'Installed') { $notInstalled += $featureName }\r\n" +
                "}\r\n" +
                "if ($notInstalled.Count -gt 0) { throw ('Windows features remain not installed: ' + ($notInstalled -join ', ')) }\r\n");
        }

        private async void FileAndStorage_Click(object sender, RoutedEventArgs e)
        {
            await RunFeatureOperationAsync(
                FileAndStorage,
                "Install File and Storage Services",
                "# Install File and Storage Services and all its components\r\n" +
                "$requestedFeatures = @('FS-FileServer', 'FS-BranchCache', 'FS-Data-Deduplication', 'FS-DFS-Namespace', 'FS-DFS-Replication', 'FS-FileServer-VSS', 'FS-NFS-Service', 'FS-Resource-Manager', 'FS-SMB1', 'FS-SMB2', 'FS-SMB3', 'FS-SyncShareService', 'FS-FileServer-Resource-Manager', 'FS-iSCSITarget-Server', 'FS-iSCSITarget-VSS-VDS', 'FS-VSS-Agent', 'Storage-Services')\r\n" +
                "$installResult = Install-WindowsFeature -Name $requestedFeatures -IncludeManagementTools -ErrorAction Stop\r\n" +
                "if (-not $installResult.Success) { throw ('Windows feature installation reported failure for: ' + ($requestedFeatures -join ', ')) }\r\n" +
                "$notInstalled = @()\r\n" +
                "foreach ($featureName in $requestedFeatures) {\r\n" +
                "    $feature = Get-WindowsFeature -Name $featureName -ErrorAction Stop\r\n" +
                "    if ($null -eq $feature -or $feature.InstallState -ne 'Installed') { $notInstalled += $featureName }\r\n" +
                "}\r\n" +
                "if ($notInstalled.Count -gt 0) { throw ('Windows features remain not installed: ' + ($notInstalled -join ', ')) }\r\n");
        }

        private async Task<bool> RunFeatureOperationAsync(Button button, string operationName, string script)
        {
            if (button == null)
            {
                throw new ArgumentNullException(nameof(button));
            }

            bool buttonWasEnabled = button.IsEnabled;
            IDisposable mutationLease = ServerMutationCoordinator.TryAcquire(operationName);
            if (mutationLease == null)
            {
                string runningOperation = ServerMutationCoordinator.CurrentOperation;
                ShowOperationStatus(new FeatureRecoveryRequest
                {
                    Key = operationName,
                    Message = "“" + (runningOperation ?? "A server change")
                        + "” must stop before “" + operationName + "” can start. No additional action was queued.",
                    FocusOrigin = button
                }, isError: false);
                return false;
            }

            if (!buttonWasEnabled || _operationsInFlight.Contains(operationName))
            {
                mutationLease.Dispose();
                return false;
            }

            Func<Task<bool>> retry = null;
            retry = () => RunFeatureOperationAsync(button, operationName, script);
            button.IsEnabled = false;
            _operationsInFlight.Add(operationName);
            if (_pendingRetryActions.ContainsKey(operationName))
            {
                _selectedRetryKey = operationName;
                RenderPendingFailure();
            }
            else
            {
                UpdateRetryControl();
            }
            bool renderFailureAfterCompletion = false;

            try
            {
                OperationResult result = await RecoveryRunner.RunAsync(
                    operationName,
                    () => MainWindow.RunPowerShellScriptAsync(script),
                    maxAttempts: 2,
                    retrySafety: RetrySafety.Idempotent);
                if (result.Succeeded)
                {
                    ResolvePendingRetry(operationName);
                    if (_pendingRetryActions.Count == 0)
                    {
                        ShowOperationStatus(new FeatureRecoveryRequest
                        {
                            Key = operationName,
                            Message = operationName + " completed successfully.",
                            FocusOrigin = button
                        }, isError: false);
                    }
                    else
                    {
                        RenderPendingFailure();
                    }

                    return true;
                }

                _pendingRetryActions[operationName] = new FeatureRecoveryRequest
                {
                    Key = operationName,
                    Message = operationName + " did not complete. " + RecoveryRunner.FriendlyMessage(result.Error),
                    Action = retry,
                    FocusOrigin = button
                };
                _selectedRetryKey = operationName;
                SelectPendingRetry();
                renderFailureAfterCompletion = true;
                return false;
            }
            finally
            {
                _operationsInFlight.Remove(operationName);
                if (renderFailureAfterCompletion)
                {
                    RenderPendingFailure();
                }
                else
                {
                    UpdateRetryControl();
                }

                mutationLease.Dispose();
            }
        }

        private async void RetryOperationButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isRetrying
                || string.IsNullOrWhiteSpace(_selectedRetryKey)
                || !_pendingRetryActions.TryGetValue(
                    _selectedRetryKey,
                    out FeatureRecoveryRequest retryRequest)
                || retryRequest.Action == null
                || !ReferenceEquals(retryRequest.Action, _retryAction))
            {
                return;
            }

            Func<Task<bool>> retryAction = retryRequest.Action;
            string retryKey = retryRequest.Key;
            _displayedStatusRequest = retryRequest;
            _isRetrying = true;
            SetStatusText(
                "Retrying " + retryKey + ". Completed work is being preserved.");
            UpdateRetryControl();
            RaiseStatusLiveRegionChanged();
            bool retryDelegateCompleted = false;

            try
            {
                retryDelegateCompleted = await retryAction();
            }
            catch (Exception ex) when (!RecoveryRunner.IsFatal(ex))
            {
                ErrorLog.Write("Retry " + retryKey, ex);
                if (_pendingRetryActions.TryGetValue(retryKey, out FeatureRecoveryRequest failedRequest))
                {
                    failedRequest.Message = retryKey + " still did not complete. "
                        + RecoveryRunner.FriendlyMessage(ex);
                }
            }
            finally
            {
                _isRetrying = false;
                SelectPendingRetry();
                if (retryDelegateCompleted)
                {
                    UpdateRetryControl();
                }
                else
                {
                    RenderPendingFailure();
                }

                RestoreFocusAfterRetry(retryRequest);
            }
        }

        private void ResolvePendingRetry(string operationKey)
        {
            if (!string.IsNullOrWhiteSpace(operationKey))
            {
                _pendingRetryActions.Remove(operationKey);
                if (string.Equals(_selectedRetryKey, operationKey, StringComparison.OrdinalIgnoreCase))
                {
                    _selectedRetryKey = null;
                }
            }

            SelectPendingRetry();
            UpdateRetryControl();
        }

        private void SelectPendingRetry()
        {
            if (!string.IsNullOrWhiteSpace(_selectedRetryKey)
                && _pendingRetryActions.TryGetValue(_selectedRetryKey, out FeatureRecoveryRequest selectedRequest))
            {
                _retryAction = selectedRequest.Action;
                return;
            }

            KeyValuePair<string, FeatureRecoveryRequest> next = _pendingRetryActions.FirstOrDefault();
            _selectedRetryKey = next.Key;
            _retryAction = next.Value?.Action;
        }

        private void RenderPendingFailure()
        {
            SelectPendingRetry();
            if (string.IsNullOrWhiteSpace(_selectedRetryKey)
                || !_pendingRetryActions.TryGetValue(_selectedRetryKey, out FeatureRecoveryRequest request))
            {
                UpdateRetryControl();
                return;
            }

            bool selectedOperationIsRunning = _operationsInFlight.Contains(request.Key);
            string remaining = _pendingRetryActions.Count > 1
                ? $" {_pendingRetryActions.Count} pending actions remain available to review."
                : string.Empty;
            IReadOnlyList<string> orderedKeys = GetOrderedRetryKeys();
            int selectedIndex = IndexOfRetryKey(orderedKeys, request.Key);
            string position = $"Pending action {Math.Max(1, selectedIndex + 1)} of {orderedKeys.Count}.";
            OperationPositionText.Text = position;
            AutomationProperties.SetName(OperationPositionText, position);
            _displayedStatusRequest = request;
            ShowOperationStatus(new FeatureRecoveryRequest
            {
                Key = request.Key,
                Message = selectedOperationIsRunning
                    ? request.Key + " is currently running. Completed work is being preserved, and retry returns after the action stops. " + position + remaining
                    : request.Message + " " + position + remaining,
                Action = request.Action,
                FocusOrigin = request.FocusOrigin
            }, isError: !selectedOperationIsRunning);
        }

        private void ShowOperationStatus(FeatureRecoveryRequest statusRequest, bool isError)
        {
            if (statusRequest == null)
            {
                return;
            }

            bool wasVisible = OperationStatusPanel.Visibility == Visibility.Visible;
            _displayedStatusRequest = statusRequest;
            OperationStatusPanel.Background = isError
                ? new SolidColorBrush(Color.FromRgb(90, 31, 31))
                : new SolidColorBrush(Color.FromRgb(26, 72, 52));
            SetStatusText(statusRequest.Message);
            OperationStatusPanel.Visibility = Visibility.Visible;
            OperationNavigationPanel.Visibility = statusRequest.Action == null
                ? Visibility.Collapsed
                : Visibility.Visible;
            if (!wasVisible)
            {
                _focusEnteredStatusPanel = false;
            }

            UpdateRetryControl();
            RaiseStatusLiveRegionChanged();
        }

        private void UpdateRetryControl()
        {
            bool selectedOperationIsRunning = !string.IsNullOrWhiteSpace(_selectedRetryKey)
                && _operationsInFlight.Contains(_selectedRetryKey);
            RetryOperationButton.Visibility = _retryAction == null
                ? Visibility.Collapsed
                : Visibility.Visible;
            RetryOperationButton.IsEnabled = _retryAction != null
                && !_isRetrying
                && !selectedOperationIsRunning;

            string selectedOperationName = !string.IsNullOrWhiteSpace(_selectedRetryKey)
                && _pendingRetryActions.TryGetValue(
                    _selectedRetryKey,
                    out FeatureRecoveryRequest selectedRequest)
                ? selectedRequest.Key
                : null;
            if (!string.IsNullOrWhiteSpace(selectedOperationName))
            {
                string retryLabel = selectedOperationIsRunning || _isRetrying
                    ? "Already running: " + selectedOperationName
                    : "Retry " + selectedOperationName;
                RetryOperationButtonText.Text = retryLabel;
                AutomationProperties.SetName(RetryOperationButton, retryLabel);
                AutomationProperties.SetHelpText(
                    RetryOperationButton,
                    selectedOperationIsRunning || _isRetrying
                        ? "This server feature action is already running. Retry becomes available after it stops."
                        : "Retries the pending server feature action " + selectedOperationName + ".");
            }

            IReadOnlyList<string> orderedKeys = GetOrderedRetryKeys();
            bool hasMultiple = orderedKeys.Count > 1;
            PreviousOperationButton.Visibility = hasMultiple ? Visibility.Visible : Visibility.Collapsed;
            NextOperationButton.Visibility = hasMultiple ? Visibility.Visible : Visibility.Collapsed;
            PreviousOperationButton.IsEnabled = hasMultiple && !_isRetrying;
            NextOperationButton.IsEnabled = hasMultiple && !_isRetrying;

            int pendingCount = _pendingRetryActions.Count;
            ReviewPendingActionsButton.Visibility = pendingCount == 0
                ? Visibility.Collapsed
                : Visibility.Visible;
            string reviewLabel = "Review pending actions (" + pendingCount + ")";
            if (selectedOperationIsRunning)
            {
                reviewLabel += " — selected action is running";
            }

            ReviewPendingActionsButtonText.Text = reviewLabel;
            AutomationProperties.SetName(ReviewPendingActionsButton, reviewLabel);
        }

        private void SetStatusText(string message)
        {
            string statusMessage = string.IsNullOrWhiteSpace(message)
                ? "No server feature operation status is available."
                : message;
            OperationStatusText.Text = statusMessage;
            AutomationProperties.SetName(OperationStatusText, statusMessage);
        }

        private void RaiseStatusLiveRegionChanged()
        {
            Dispatcher.BeginInvoke(new System.Action(() =>
            {
                AutomationPeer peer = UIElementAutomationPeer.FromElement(OperationStatusText)
                    ?? UIElementAutomationPeer.CreatePeerForElement(OperationStatusText);
                peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            }));
        }

        private void OperationStatusPanel_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            _focusEnteredStatusPanel = true;
        }

        private void DismissOperationButton_Click(object sender, RoutedEventArgs e)
        {
            DismissOperationStatus();
        }

        private void ReviewPendingActionsButton_Click(object sender, RoutedEventArgs e)
        {
            RenderPendingFailure();
            _statusOpenedFromReview = true;
            if (RetryOperationButton.IsVisible && RetryOperationButton.IsEnabled)
            {
                RetryOperationButton.Focus();
            }
            else
            {
                DismissOperationButton.Focus();
            }
        }

        private void PreviousOperationButton_Click(object sender, RoutedEventArgs e)
        {
            SelectRelativeRetry(-1);
        }

        private void NextOperationButton_Click(object sender, RoutedEventArgs e)
        {
            SelectRelativeRetry(1);
        }

        private void SelectRelativeRetry(int offset)
        {
            IReadOnlyList<string> orderedKeys = GetOrderedRetryKeys();
            if (orderedKeys.Count == 0)
            {
                return;
            }

            int currentIndex = IndexOfRetryKey(orderedKeys, _selectedRetryKey);
            int nextIndex = currentIndex < 0
                ? 0
                : (currentIndex + offset + orderedKeys.Count) % orderedKeys.Count;
            _selectedRetryKey = orderedKeys[nextIndex];
            SelectPendingRetry();
            RenderPendingFailure();
        }

        private IReadOnlyList<string> GetOrderedRetryKeys()
        {
            return _pendingRetryActions.Keys
                .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static int IndexOfRetryKey(IReadOnlyList<string> keys, string key)
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

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && OperationStatusPanel.Visibility == Visibility.Visible)
            {
                DismissOperationStatus();
                e.Handled = true;
            }
        }

        private void DismissOperationStatus()
        {
            bool restoreFocus = _focusEnteredStatusPanel && OperationStatusPanel.IsKeyboardFocusWithin;
            IInputElement focusOrigin = _statusOpenedFromReview
                ? ReviewPendingActionsButton
                : _displayedStatusRequest?.FocusOrigin;
            OperationStatusPanel.Visibility = Visibility.Collapsed;
            _focusEnteredStatusPanel = false;
            _statusOpenedFromReview = false;
            _displayedStatusRequest = null;
            if (restoreFocus)
            {
                UIElement focusTarget = focusOrigin as UIElement;
                if (focusTarget == null || !focusTarget.IsVisible || !focusTarget.IsEnabled)
                {
                    focusTarget = ReviewPendingActionsButton.IsVisible && ReviewPendingActionsButton.IsEnabled
                        ? ReviewPendingActionsButton
                        : IIS.IsVisible && IIS.IsEnabled
                            ? IIS
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
        }

        private void RestoreFocusAfterRetry(FeatureRecoveryRequest completedRequest)
        {
            Dispatcher.BeginInvoke(new System.Action(() =>
            {
                if (_pendingRetryActions.Count > 0)
                {
                    if (RetryOperationButton.IsVisible && RetryOperationButton.IsEnabled)
                    {
                        RetryOperationButton.Focus();
                    }
                    else
                    {
                        DismissOperationButton.Focus();
                    }

                    return;
                }

                UIElement focusTarget = completedRequest?.FocusOrigin as UIElement;
                if (focusTarget == null || !focusTarget.IsVisible || !focusTarget.IsEnabled)
                {
                    focusTarget = DismissOperationButton.IsVisible && DismissOperationButton.IsEnabled
                        ? DismissOperationButton
                        : IIS.IsVisible && IIS.IsEnabled
                            ? IIS
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
            }));
        }
    }
}
