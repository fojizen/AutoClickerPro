using System.Windows;
using System.Windows.Threading;
using AutoClickerPro.Helpers;
using AutoClickerPro.Services;
using AutoClickerPro.Views;

namespace AutoClickerPro;

/// <summary>
/// Application entry point. Responsible for enforcing a single running instance and for
/// wiring up application-wide exception handling so no unhandled exception - on the UI
/// thread, a background thread, or an unobserved Task - can silently crash or corrupt the
/// app while it may be running unattended for a long time.
/// </summary>
public partial class App : Application
{
    private SingleInstanceGuard? _instanceGuard;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ---- Single instance enforcement ----------------------------------------------------
        _instanceGuard = new SingleInstanceGuard();
        if (!_instanceGuard.IsFirstInstance)
        {
            MessageBox.Show(
                "AutoClicker Pro is already running.",
                "AutoClicker Pro",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            _instanceGuard.Dispose();
            Shutdown();
            return;
        }

        // ---- Initialize DI container --------------------------------------------------------
        AppServices.Initialize();

        // ---- Global exception handling ------------------------------------------------------
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // Only now create and show the main window.
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ShowError(e.Exception);
        e.Handled = true;
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            ShowError(ex);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
        Dispatcher.Invoke(() => ShowError(e.Exception));
    }

    private static void ShowError(Exception ex)
    {
        MessageBox.Show(
            $"An unexpected error occurred:\n\n{ex.Message}",
            "AutoClicker Pro",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instanceGuard?.Dispose();
        base.OnExit(e);
    }
}
