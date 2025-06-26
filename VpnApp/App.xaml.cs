using System;
using System.Windows;

namespace VpnApp
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            // You can add any application-wide startup logic here.
            // For example, loading settings or initializing global resources.

            // Example of handling unhandled exceptions globally for logging
            // AppDomain.CurrentDomain.UnhandledException += (s, exArgs) =>
            // {
            //     // Log the exception (exArgs.ExceptionObject as Exception)
            //     MessageBox.Show($"An unhandled error occurred: {(exArgs.ExceptionObject as Exception)?.Message}", "Application Error", MessageBoxButton.OK, MessageBoxImage.Error);
            // };

            // DispatcherUnhandledException += (s, exArgs) =>
            // {
            //     // Log exArgs.Exception
            //     MessageBox.Show($"An unhandled UI error occurred: {exArgs.Exception.Message}", "UI Error", MessageBoxButton.OK, MessageBoxImage.Error);
            //     exArgs.Handled = true; // Attempt to prevent app from crashing
            // };
        }
    }
}
