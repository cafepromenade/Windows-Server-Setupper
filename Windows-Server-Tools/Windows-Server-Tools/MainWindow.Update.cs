using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Threading;

namespace Windows_Server_Tools
{
    public partial class MainWindow
    {
        private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(6);
        private static readonly TimeSpan UpdateNetworkTimeout = TimeSpan.FromSeconds(15);
        private readonly SemaphoreSlim _updateCheckLock = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource _updateLifetimeCancellation = new CancellationTokenSource();
        private UpdateService _updateService;
        private Timer _updateTimer;
        private CancellationTokenSource _updateDownloadCancellation;
        private UpdateManifest _availableUpdate;
        private UpdateInstallState _stagedUpdateState;
        private string _stagedUpdatePath;
        private IInputElement _updateFocusOrigin;
        private bool _updateDownloadInProgress;

        private void StartUpdateService()
        {
            if (_updateService != null)
            {
                return;
            }

            try
            {
                string configuredUrl = ConfigurationManager.AppSettings["UpdateManifestUrl"];
                if (!Uri.TryCreate(configuredUrl, UriKind.Absolute, out Uri manifestUri))
                {
                    throw new InvalidDataException("The configured update manifest URL is invalid.");
                }

                string stagingMarker = ProtectedWorkflowState.GetPath(
                    "Updates",
                    "Staging",
                    "package.marker");
                string stagingDirectory = Path.GetDirectoryName(stagingMarker);
                string statePath = ProtectedWorkflowState.GetPath("Updates", "update-state.json");
                _updateService = new UpdateService(manifestUri, stagingDirectory, statePath);
                RestoreStagedUpdateState();
                _updateTimer = new Timer(
                    UpdateTimerElapsed,
                    null,
                    UpdateCheckInterval,
                    UpdateCheckInterval);
                _ = CheckForUpdatesAsync(false, null);
            }
            catch (Exception ex) when (!RecoveryRunner.IsFatal(ex))
            {
                ErrorLog.Write("Start automatic updates", ex);
                ShowUpdateFailure(
                    "Automatic update checks are unavailable",
                    RecoveryRunner.FriendlyMessage(ex),
                    CheckForUpdatesButton);
            }
        }

        private void StopUpdateService()
        {
            _updateTimer?.Dispose();
            _updateTimer = null;
            _updateDownloadCancellation?.Cancel();
            _updateLifetimeCancellation.Cancel();
            _updateDownloadCancellation?.Dispose();
            _updateDownloadCancellation = null;
            _updateService?.Dispose();
            _updateService = null;
        }

        private void UpdateTimerElapsed(object state)
        {
            Dispatcher.BeginInvoke(new Action(() => _ = CheckForUpdatesAsync(false, null)));
        }

        private void RestoreStagedUpdateState()
        {
            UpdateInstallState state = _updateService.LoadState();
            if (state == null)
            {
                return;
            }

            Version currentVersion = GetCurrentApplicationVersion();
            Version targetVersion = Version.Parse(state.TargetVersion);
            if (currentVersion >= targetVersion)
            {
                _updateService.ClearStateAndStagedPackage();
                return;
            }

            if (!_updateService.ValidateStagedPackage(state))
            {
                _updateService.ClearStateAndStagedPackage();
                ShowUpdateFailure(
                    "Staged update was discarded",
                    "The staged installer was missing or corrupt. The installed version remains unchanged; check again to download a verified copy.",
                    CheckForUpdatesButton);
                return;
            }

            _stagedUpdateState = state;
            _stagedUpdatePath = state.StagedPath;
            ShowUpdateReady(
                targetVersion,
                state.InstallerLaunched
                    ? "The previous installer launch did not complete. The prior installed version is still active, and the verified package can be retried."
                    : "The verified update is staged locally.");
        }

        private async Task CheckForUpdatesAsync(bool manual, IInputElement focusOrigin)
        {
            if (_updateService == null || !_updateCheckLock.Wait(0))
            {
                return;
            }

            _updateFocusOrigin = focusOrigin ?? CheckForUpdatesButton;
            CheckForUpdatesButton.IsEnabled = false;
            if (manual)
            {
                ShowUpdateStatus("Checking for updates", "Contacting the configured HTTPS feed.", false);
            }

            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                _updateLifetimeCancellation.Token))
            {
                timeout.CancelAfter(UpdateNetworkTimeout);
                try
                {
                    UpdateCheckResult result = await _updateService.CheckAsync(
                        GetCurrentApplicationVersion(),
                        timeout.Token);
                    if (result.Availability == UpdateAvailability.Current)
                    {
                        if (manual)
                        {
                            ShowUpdateStatus(
                                "You have the current version",
                                "Installed version " + GetCurrentApplicationVersion() + " is current for the configured feed.",
                                false);
                        }

                        return;
                    }

                    _availableUpdate = result.Manifest;
                    await DownloadAvailableUpdateAsync(result.Manifest);
                }
                catch (OperationCanceledException)
                {
                    if (manual)
                    {
                        ShowUpdateFailure(
                            "Update check stopped",
                            "The check was cancelled or exceeded the 15-second network limit. The installed version was not changed.",
                            _updateFocusOrigin);
                    }
                }
                catch (HttpRequestException ex)
                {
                    ShowUpdateFailure(
                        "Update check could not reach the feed",
                        "The app may be offline. " + RecoveryRunner.FriendlyMessage(ex),
                        _updateFocusOrigin);
                }
                catch (Exception ex) when (!RecoveryRunner.IsFatal(ex))
                {
                    ErrorLog.Write("Check for updates", ex);
                    ShowUpdateFailure(
                        "Update metadata was rejected",
                        RecoveryRunner.FriendlyMessage(ex) + " The installed version was not changed.",
                        _updateFocusOrigin);
                }
                finally
                {
                    CheckForUpdatesButton.IsEnabled = true;
                    _updateCheckLock.Release();
                }
            }
        }

        private async Task DownloadAvailableUpdateAsync(UpdateManifest manifest)
        {
            _updateDownloadCancellation?.Dispose();
            _updateDownloadCancellation = new CancellationTokenSource();
            _updateDownloadInProgress = true;
            UpdateCancelButton.Visibility = Visibility.Visible;
            UpdateRestartButton.Visibility = Visibility.Collapsed;
            UpdateReleaseNotesButton.Visibility = Visibility.Visible;
            UpdateProgress.Visibility = Visibility.Visible;
            UpdateProgress.Value = 0;
            ShowUpdateStatus(
                "Downloading update " + manifest.ParsedVersion,
                "The unsigned installer is downloading from the verified HTTPS URL and will be checked against the exact manifest SHA-256.",
                false);
            var progress = new Progress<int>(value =>
            {
                UpdateProgress.Value = value;
                UpdateStatusText.Text = "Downloading and verifying update "
                    + manifest.ParsedVersion
                    + ": "
                    + value
                    + "%";
                RaiseUpdateLiveRegionChanged();
            });

            try
            {
                string stagedPath = await _updateService.DownloadAndStageAsync(
                    manifest,
                    progress,
                    _updateDownloadCancellation.Token);
                _updateService.SaveReadyState(
                    GetCurrentApplicationVersion(),
                    manifest,
                    stagedPath);
                _stagedUpdatePath = stagedPath;
                _stagedUpdateState = _updateService.LoadState();
                ShowUpdateReady(manifest.ParsedVersion, "The download and SHA-256 validation completed.");
            }
            catch (OperationCanceledException)
            {
                ShowUpdateStatus(
                    "Update download cancelled",
                    "No partial installer was kept and the installed version was not changed.",
                    false);
            }
            catch (Exception ex) when (!RecoveryRunner.IsFatal(ex))
            {
                ErrorLog.Write("Download update", ex);
                ShowUpdateFailure(
                    "Update package was rejected",
                    RecoveryRunner.FriendlyMessage(ex) + " No unverified installer was kept.",
                    _updateFocusOrigin);
            }
            finally
            {
                _updateDownloadInProgress = false;
                UpdateCancelButton.Visibility = Visibility.Collapsed;
                _updateDownloadCancellation?.Dispose();
                _updateDownloadCancellation = null;
            }
        }

        private void ShowUpdateReady(Version version, string detail)
        {
            ShowUpdateStatus(
                "Update " + version + " is ready",
                detail
                    + " This update is unsigned and may trigger Unknown Publisher or Microsoft SmartScreen warnings. Restart occurs only after you choose the action.",
                false);
            UpdateProgress.Visibility = Visibility.Collapsed;
            UpdateCancelButton.Visibility = Visibility.Collapsed;
            UpdateRestartButton.Visibility = Visibility.Visible;
            UpdateLaterButton.Visibility = Visibility.Visible;
            UpdateReleaseNotesButton.Visibility = _availableUpdate?.ParsedReleaseNotesUri != null
                ? Visibility.Visible
                : Visibility.Collapsed;
            UpdateUpdateRestartAvailability();
        }

        private void ShowUpdateFailure(string title, string message, IInputElement focusOrigin)
        {
            _updateFocusOrigin = focusOrigin ?? CheckForUpdatesButton;
            ShowUpdateStatus(title, message, true);
            UpdateProgress.Visibility = Visibility.Collapsed;
            UpdateCancelButton.Visibility = Visibility.Collapsed;
            UpdateRestartButton.Visibility = Visibility.Collapsed;
            UpdateLaterButton.Visibility = Visibility.Visible;
        }

        private void ShowUpdateStatus(string title, string message, bool isError)
        {
            UpdateTitleText.Text = title;
            UpdateStatusText.Text = message;
            UpdateStatusPanel.Background = isError
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(90, 31, 31))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(26, 72, 52));
            UpdateStatusPanel.Visibility = Visibility.Visible;
            UpdateLaterButton.Visibility = Visibility.Visible;
            RaiseUpdateLiveRegionChanged();
        }

        private void UpdateUpdateRestartAvailability()
        {
            if (UpdateRestartButton == null || UpdateRestartButton.Visibility != Visibility.Visible)
            {
                return;
            }

            string reason = GetUpdateRestartBlockReason();
            UpdateRestartButton.IsEnabled = string.IsNullOrWhiteSpace(reason);
            AutomationProperties.SetHelpText(
                UpdateRestartButton,
                string.IsNullOrWhiteSpace(reason)
                    ? "Closes this app and launches the verified unsigned installer."
                    : reason);
            UpdateRestartReasonText.Text = reason ?? string.Empty;
            UpdateRestartReasonText.Visibility = string.IsNullOrWhiteSpace(reason)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private string GetUpdateRestartBlockReason()
        {
            if (_updateDownloadInProgress)
            {
                return "Wait for the update download to finish or cancel it first.";
            }

            string mutation = ServerMutationCoordinator.CurrentOperation;
            if (!string.IsNullOrWhiteSpace(mutation) || _operationsInFlight.Count > 0)
            {
                return "Restart is unavailable while server work is active. Wait for “"
                    + (mutation ?? _operationsInFlight.FirstOrDefault() ?? "the current operation")
                    + "” to stop.";
            }

            if (!string.IsNullOrWhiteSpace(DomainNameTextBox.Text)
                || SafeModePasswordBox.SecurePassword.Length > 0
                || !string.IsNullOrWhiteSpace(ChocoSoftwareTextBox.Text))
            {
                return "Restart is unavailable while unsaved form values are present. Complete or clear the domain, password, and software fields first.";
            }

            return null;
        }

        private async void CheckForUpdatesButton_Click(object sender, RoutedEventArgs e)
        {
            await CheckForUpdatesAsync(true, CheckForUpdatesButton);
        }

        private void UpdateCancelButton_Click(object sender, RoutedEventArgs e)
        {
            _updateDownloadCancellation?.Cancel();
        }

        private void UpdateLaterButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateStatusPanel.Visibility = Visibility.Collapsed;
            RestoreMainWindowFocus(_updateFocusOrigin);
        }

        private void UpdateReleaseNotesButton_Click(object sender, RoutedEventArgs e)
        {
            Uri uri = _availableUpdate?.ParsedReleaseNotesUri;
            if (uri == null)
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception ex) when (!RecoveryRunner.IsFatal(ex))
            {
                ShowUpdateFailure(
                    "Release notes could not be opened",
                    RecoveryRunner.FriendlyMessage(ex),
                    UpdateReleaseNotesButton);
            }
        }

        private void UpdateRestartButton_Click(object sender, RoutedEventArgs e)
        {
            string blockReason = GetUpdateRestartBlockReason();
            if (!string.IsNullOrWhiteSpace(blockReason))
            {
                ShowUpdateFailure("Restart is protected", blockReason, UpdateRestartButton);
                return;
            }

            if (_stagedUpdateState == null
                || string.IsNullOrWhiteSpace(_stagedUpdatePath)
                || !_updateService.ValidateStagedPackage(_stagedUpdateState))
            {
                _updateService.ClearStateAndStagedPackage();
                _stagedUpdateState = null;
                _stagedUpdatePath = null;
                ShowUpdateFailure(
                    "Staged update was discarded",
                    "The installer changed after validation. The installed version remains active; check again to download a verified copy.",
                    UpdateRestartButton);
                return;
            }

            try
            {
                _updateService.MarkInstallerLaunched();
                Process.Start(new ProcessStartInfo(
                    _stagedUpdatePath,
                    "/SILENT /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS")
                {
                    UseShellExecute = true
                });
                Application.Current.Shutdown();
            }
            catch (Exception ex) when (!RecoveryRunner.IsFatal(ex))
            {
                ErrorLog.Write("Launch staged update", ex);
                ShowUpdateFailure(
                    "Update installer did not start",
                    RecoveryRunner.FriendlyMessage(ex)
                        + " The prior installed version remains active and the verified package can be retried.",
                    UpdateRestartButton);
            }
        }

        private void UpdateUnsavedStateChanged(object sender, RoutedEventArgs e)
        {
            UpdateUpdateRestartAvailability();
        }

        private void RaiseUpdateLiveRegionChanged()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                string accessibleMessage = UpdateTitleText.Text + ". " + UpdateStatusText.Text;
                AutomationProperties.SetName(UpdateStatusText, accessibleMessage);
                AutomationPeer peer = UIElementAutomationPeer.FromElement(UpdateStatusText)
                    ?? UIElementAutomationPeer.CreatePeerForElement(UpdateStatusText);
                peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            }));
        }

        private static Version GetCurrentApplicationVersion()
        {
            return Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);
        }
    }
}
