using System.Windows;
using System.Windows.Threading;
using AutoClickerPro.Helpers;
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
        // Two instances would both try to register the same global hotkeys and low-level mouse
        // hook, leading to confusing, conflicting behavior. Refuse to start a second instance,
        // and do this before any window is created.
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

        // ---- Global exception handling ------------------------------------------------------
        // UI-thread exceptions (data binding, event handlers, etc.)
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // Exceptions on any non-UI thread (e.g. inside a raw Task not awaited from the UI thread).
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;

        // Exceptions from a Task that faulted but whose exception was never observed/awaited -
        // relevant here because ClickEngine's loops are fire-and-forget background Tasks.
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
        // Background click-loop exceptions land here if not already handled internally.
        // Mark observed so the process isn't torn down, and let the user know.
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
