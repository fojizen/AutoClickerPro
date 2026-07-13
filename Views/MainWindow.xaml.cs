using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AutoClickerPro.Models;
using AutoClickerPro.Services;
using AutoClickerPro.ViewModels;
using Hardcodet.Wpf.TaskbarNotification;

namespace AutoClickerPro.Views;

public partial class MainWindow : Window
{
    private MainViewModel? _vm;
    private TaskbarIcon? _trayIcon;
    private bool _minimizeToTray;

    private ButtonSettingsViewModel? _hotkeyCaptureTarget;
    private Button? _hotkeyCaptureButton;
    private string? _hotkeyPreviousValue;

    public MainWindow()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            var engine = AppServices.GetService<ClickEngine>();
            var hotkeyService = AppServices.GetService<HotkeyService>();
            var profileService = AppServices.GetService<ProfileService>();

            _vm = new MainViewModel(engine, hotkeyService, profileService);
            DataContext = _vm;

            SetupTrayIcon();
        };

        Closing += (_, _) => _vm?.Dispose();

        PreviewKeyDown += MainWindow_PreviewKeyDown;
        PreviewMouseDown += MainWindow_PreviewMouseDown;
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "AutoClicker Pro - Running",
            Visibility = Visibility.Collapsed
        };

        var contextMenu = new ContextMenu();
        var showItem = new MenuItem { Header = "Show" };
        showItem.Click += (_, _) =>
        {
            Show();
            WindowState = WindowState.Normal;
            _trayIcon.Visibility = Visibility.Collapsed;
        };
        contextMenu.Items.Add(showItem);
        contextMenu.Items.Add(new Separator());
        var exitItem = new MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) => _vm?.ExitApplication();
        contextMenu.Items.Add(exitItem);

        _trayIcon.ContextMenu = contextMenu;
        _trayIcon.DoubleClickCommand = _vm?.ShowWindowCommand;
    }

    private void SetHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        if (button.DataContext is not ButtonSettingsViewModel target) return;

        _hotkeyCaptureTarget = target;
        _hotkeyCaptureButton = button;
        _hotkeyPreviousValue = target.Hotkey;

        button.Content = "Press a key or mouse button...";
        Keyboard.Focus(this);
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_hotkeyCaptureTarget == null) return;

        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                   or Key.LeftShift or Key.RightShift or Key.System)
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            _hotkeyCaptureTarget.Hotkey = _hotkeyPreviousValue ?? string.Empty;
            EndHotkeyCapture();
            e.Handled = true;
            return;
        }

        var sb = new StringBuilder();
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) sb.Append("Ctrl+");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) sb.Append("Alt+");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) sb.Append("Shift+");

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        sb.Append(key);

        CommitHotkey(sb.ToString());
        e.Handled = true;
    }

    private void MainWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_hotkeyCaptureTarget == null) return;

        HotkeyMouseButton? button = e.ChangedButton switch
        {
            MouseButton.Left => HotkeyMouseButton.Left,
            MouseButton.Right => HotkeyMouseButton.Right,
            MouseButton.Middle => HotkeyMouseButton.Middle,
            MouseButton.XButton1 => HotkeyMouseButton.XButton1,
            MouseButton.XButton2 => HotkeyMouseButton.XButton2,
            _ => null
        };

        if (button == null) return;

        CommitHotkey(HotkeyService.MouseButtonToHotkeyString(button.Value));

        e.Handled = true;
    }

    private void CommitHotkey(string hotkeyText)
    {
        if (_hotkeyCaptureTarget == null) return;

        _hotkeyCaptureTarget.Hotkey = hotkeyText;
        _vm?.RegisterHotkeyFor(_hotkeyCaptureTarget);

        EndHotkeyCapture();
    }

    private void EndHotkeyCapture()
    {
        if (_hotkeyCaptureButton != null)
            _hotkeyCaptureButton.Content = "Set Hotkey";

        _hotkeyCaptureTarget = null;
        _hotkeyCaptureButton = null;
        _hotkeyPreviousValue = null;
    }

    private void ProfileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_vm == null) return;
        if (sender is ComboBox { SelectedItem: string name } && !string.IsNullOrWhiteSpace(name))
        {
            _vm.LoadProfileCommand.Execute(name);
        }
    }

    // ---- Tray icon support -------------------------------------------------------------------

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && _minimizeToTray)
        {
            _minimizeToTray = false;
            Hide();
            if (_trayIcon != null) _trayIcon.Visibility = Visibility.Visible;
        }
    }

    private void MinimizeToTray_Click(object sender, RoutedEventArgs e)
    {
        _minimizeToTray = true;
        WindowState = WindowState.Minimized;
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_trayIcon != null)
        {
            _trayIcon.Visibility = Visibility.Collapsed;
            _trayIcon.Dispose();
        }
    }
}
