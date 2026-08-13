using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Windows_Server_Tools
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            DispatcherUnhandledException += HandleDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += HandleDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += HandleUnobservedTaskException;
            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            DispatcherUnhandledException -= HandleDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException -= HandleDomainUnhandledException;
            TaskScheduler.UnobservedTaskException -= HandleUnobservedTaskException;
            base.OnExit(e);
        }

        private void HandleDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            ErrorLog.Write("Unhandled UI operation", e.Exception);

            if (RecoveryRunner.IsFatal(e.Exception)
                || !RecoveryRunner.CanContinueAfterDispatcherException(e.Exception))
            {
                return;
            }

            e.Handled = true;
            if (MainWindow is MainWindow mainWindow)
            {
                mainWindow.ShowUnexpectedError(e.Exception);
            }
        }

        private void HandleUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            ErrorLog.Write("Unobserved background operation", e.Exception);
            e.SetObserved();

            Dispatcher.BeginInvoke(new System.Action(() =>
            {
                if (MainWindow is MainWindow mainWindow)
                {
                    mainWindow.ShowUnexpectedError(e.Exception);
                }
            }));
        }

        private static void HandleDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            ErrorLog.Write("Unhandled application-domain operation", e.ExceptionObject as Exception);
        }
    }
}
