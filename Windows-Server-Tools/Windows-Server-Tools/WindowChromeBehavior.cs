using System;
using System.Windows;
using System.Windows.Input;

namespace Windows_Server_Tools
{
    internal static class WindowChromeBehavior
    {
        public static void HandleTitleBarMouseDown(Window window, MouseButtonEventArgs e)
        {
            if (window == null || e == null || e.ChangedButton != MouseButton.Left)
            {
                return;
            }

            if (e.ClickCount == 2)
            {
                ToggleMaximize(window);
                e.Handled = true;
                return;
            }

            try
            {
                window.DragMove();
                e.Handled = true;
            }
            catch (InvalidOperationException)
            {
            }
        }

        public static void ToggleMaximize(Window window)
        {
            if (window.WindowState == WindowState.Maximized)
            {
                SystemCommands.RestoreWindow(window);
            }
            else
            {
                SystemCommands.MaximizeWindow(window);
            }
        }
    }

    public partial class MainWindow
    {
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            WindowChromeBehavior.HandleTitleBarMouseDown(this, e);
        }

        private void MinimizeWindowButton_Click(object sender, RoutedEventArgs e)
        {
            SystemCommands.MinimizeWindow(this);
        }

        private void MaximizeRestoreWindowButton_Click(object sender, RoutedEventArgs e)
        {
            WindowChromeBehavior.ToggleMaximize(this);
        }

        private void CloseWindowButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }

    public partial class CommonlyInstalledWindowsComponents
    {
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            WindowChromeBehavior.HandleTitleBarMouseDown(this, e);
        }

        private void MinimizeWindowButton_Click(object sender, RoutedEventArgs e)
        {
            SystemCommands.MinimizeWindow(this);
        }

        private void MaximizeRestoreWindowButton_Click(object sender, RoutedEventArgs e)
        {
            WindowChromeBehavior.ToggleMaximize(this);
        }

        private void CloseWindowButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
