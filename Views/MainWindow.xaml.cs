using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AutoClickerPro.Models;
using AutoClickerPro.Services;
using AutoClickerPro.ViewModels;

namespace AutoClickerPro.Views;

public partial class MainWindow : Window
{
    private MainViewModel? _vm;

    /// <summary>Non-null while the user is actively pressing a key or mouse button to define a new hotkey.</summary>
    private ButtonSettingsViewModel? _hotkeyCaptureTarget;
    private Button? _hotkeyCaptureButton;
    private string? _hotkeyPreviousValue;

    public MainWindow()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            // MainViewModel is created once the window exists (kept consistent with its
            // constructor signature; the window handle itself is no longer required for
            // hotkeys, which now use system-wide keyboard/mouse hooks instead of RegisterHotKey).
            _vm = new MainViewModel(this);
            DataContext = _vm;
        };

        Closing += (_, _) => _vm?.Dispose();

        // Captures the key combo or mouse button for hotkey assignment; both handlers are
        // only active while _hotkeyCaptureTarget is set (i.e. right after "Set Hotkey" is clicked).
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        PreviewMouseDown += MainWindow_PreviewMouseDown;
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

        // Ignore bare modifier presses - wait for the actual key.
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                   or Key.LeftShift or Key.RightShift or Key.System)
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            // Cancel capture, restore previous value.
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

    /// <summary>
    /// Captures Left/Right/Middle/XButton1/XButton2 mouse clicks as a hotkey assignment.
    /// WPF's routed mouse events natively cover all five buttons via MouseButtonEventArgs.ChangedButton,
    /// so no extra hook is needed just for the in-app capture UI (the global low-level mouse hook in
    /// HotkeyService is what makes the assigned button work later, system-wide, while running).
    /// </summary>
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

        // Swallow this click entirely - it's being consumed as a hotkey assignment gesture,
        // not a normal UI interaction (e.g. we don't want it to also trigger whatever control
        // happens to be underneath the cursor).
        e.Handled = true;
    }

    /// <summary>Finalizes whatever key/button combo was just captured: assigns it and re-registers the hotkey.</summary>
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
}
