using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Windows_Server_Tools
{
    public partial class MainWindow
    {
        private LogoService _logoService;
        private LogoSettings _logoSettings;
        private bool _updatingLogoUi;

        private void StartLogoService()
        {
            try
            {
                _logoService = LogoService.CreateDefault();
                _logoSettings = _logoService.LoadSettings();
                RenderLogoSettings();
                ApplyLogoPresentation();
            }
            catch (Exception ex) when (!RecoveryRunner.IsFatal(ex))
            {
                ErrorLog.Write("Start application logo settings", ex);
                SetLogoStatus(
                    "The shipped logo remains active because local logo settings could not be loaded. "
                    + RecoveryRunner.FriendlyMessage(ex),
                    true);
            }
        }

        private void RenderLogoSettings()
        {
            if (_logoSettings == null)
            {
                return;
            }

            _updatingLogoUi = true;
            try
            {
                SelectComboTag(LogoPresetComboBox, _logoSettings.Preset);
                SelectComboTag(LogoFitComboBox, _logoSettings.Fit);
                LogoBackgroundTextBox.Text = _logoSettings.Background;
                LogoFocalXSlider.Value = _logoSettings.FocalX * 100;
                LogoFocalYSlider.Value = _logoSettings.FocalY * 100;
                bool custom = string.Equals(_logoSettings.Preset, "custom", StringComparison.Ordinal);
                LogoFitComboBox.IsEnabled = custom;
                LogoBackgroundTextBox.IsEnabled = custom;
                LogoFocalXSlider.IsEnabled = custom;
                LogoFocalYSlider.IsEnabled = custom;
                ApplyLogoRenderingButton.IsEnabled = custom;
            }
            finally
            {
                _updatingLogoUi = false;
            }
        }

        private void ApplyLogoPresentation()
        {
            System.Windows.Media.ImageSource source = _logoService.LoadPresentationSource(_logoSettings);
            AppLogoImage.Source = source;
            TitleBarLogoImage.Source = source;
            LogoPreviewImage.Source = source;
        }

        private void LogoPresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updatingLogoUi || _logoService == null)
            {
                return;
            }

            string preset = SelectedComboTag(LogoPresetComboBox);
            if (string.Equals(preset, "custom", StringComparison.Ordinal))
            {
                LogoSettings stored = _logoService.LoadSettings();
                if (!string.Equals(stored.Preset, "custom", StringComparison.Ordinal))
                {
                    _updatingLogoUi = true;
                    SelectComboTag(LogoPresetComboBox, _logoSettings.Preset);
                    _updatingLogoUi = false;
                    SetLogoStatus("Choose a valid local image before selecting the custom logo.", true);
                    return;
                }

                _logoSettings = stored;
            }
            else
            {
                _logoSettings = _logoService.ApplyPreset(preset);
            }

            RenderLogoSettings();
            ApplyLogoPresentation();
            SetLogoStatus("The selected shipped logo preset is active.", false);
        }

        private void ChooseCustomLogoButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Choose a local application logo",
                Filter = "Supported images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
                Multiselect = false,
                CheckFileExists = true,
                DereferenceLinks = true
            };
            if (dialog.ShowDialog(this) != true)
            {
                SetLogoStatus("No image was selected. The current logo remains active.", false);
                return;
            }

            try
            {
                byte[] bytes = ReadBoundedLocalLogo(dialog.FileName);
                _logoSettings = _logoService.ImportCustom(
                    bytes,
                    SelectedComboTag(LogoFitComboBox) ?? "contain",
                    LogoBackgroundTextBox.Text,
                    LogoFocalXSlider.Value / 100,
                    LogoFocalYSlider.Value / 100);
                RenderLogoSettings();
                ApplyLogoPresentation();
                SetLogoStatus(
                    "The image was validated locally and converted to verified 256-pixel and 48-pixel PNG display assets. The source path was not stored.",
                    false);
            }
            catch (Exception ex) when (!RecoveryRunner.IsFatal(ex))
            {
                SetLogoStatus(
                    "The custom image was rejected and the prior logo remains active. "
                    + RecoveryRunner.FriendlyMessage(ex),
                    true);
            }
        }

        private void ApplyLogoRenderingButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _logoSettings = _logoService.UpdateCustomRendering(
                    SelectedComboTag(LogoFitComboBox),
                    LogoBackgroundTextBox.Text,
                    LogoFocalXSlider.Value / 100,
                    LogoFocalYSlider.Value / 100);
                RenderLogoSettings();
                ApplyLogoPresentation();
                SetLogoStatus("The custom fit, focal point, and background were applied locally.", false);
            }
            catch (Exception ex) when (!RecoveryRunner.IsFatal(ex))
            {
                ErrorLog.Write("Render custom application logo", ex);
                SetLogoStatus(
                    "The rendering change was rejected and the prior logo remains active. "
                    + RecoveryRunner.FriendlyMessage(ex),
                    true);
            }
        }

        private void ResetLogoButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _logoService.Reset();
                _logoSettings = _logoService.LoadSettings();
                RenderLogoSettings();
                ApplyLogoPresentation();
                SetLogoStatus("The shipped server-and-mail logo is active and custom cached files were removed.", false);
            }
            catch (Exception ex) when (!RecoveryRunner.IsFatal(ex))
            {
                ErrorLog.Write("Reset application logo", ex);
                SetLogoStatus("The logo could not be reset. " + RecoveryRunner.FriendlyMessage(ex), true);
            }
        }

        private void SetLogoStatus(string message, bool isError)
        {
            LogoStatusText.Text = message;
            LogoStatusText.Foreground = isError
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(179, 38, 30))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(26, 90, 58));
            AutomationProperties.SetName(LogoStatusText, message);
        }

        private static byte[] ReadBoundedLocalLogo(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length <= 0 || stream.Length > LogoService.MaximumSourceBytes)
                {
                    throw new InvalidDataException("The selected image must be between 1 byte and 5 MiB.");
                }

                byte[] bytes = new byte[checked((int)stream.Length)];
                int offset = 0;
                while (offset < bytes.Length)
                {
                    int read = stream.Read(bytes, offset, bytes.Length - offset);
                    if (read == 0)
                    {
                        throw new EndOfStreamException("The selected image ended while it was being read.");
                    }
                    offset += read;
                }
                return bytes;
            }
        }

        private static string SelectedComboTag(ComboBox comboBox)
        {
            return (comboBox.SelectedItem as ComboBoxItem)?.Tag as string;
        }

        private static void SelectComboTag(ComboBox comboBox, string tag)
        {
            ComboBoxItem item = comboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(candidate => string.Equals(
                    candidate.Tag as string,
                    tag,
                    StringComparison.Ordinal));
            comboBox.SelectedItem = item;
        }
    }
}
