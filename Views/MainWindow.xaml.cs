using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AutoClickerPro.ViewModels;

namespace AutoClickerPro.Views;

public partial class MainWindow : Window
{
    private MainViewModel? _vm;

    /// <summary>Non-null while the user is actively pressing keys to define a new hotkey.</summary>
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
            // hotkeys, which now use a system-wide keyboard hook instead of RegisterHotKey).
            _vm = new MainViewModel(this);
            DataContext = _vm;
        };

        Closing += (_, _) => _vm?.Dispose();

        // Captures the key combo for hotkey assignment; only active while _hotkeyCaptureTarget is set.
        PreviewKeyDown += MainWindow_PreviewKeyDown;
    }

    private void SetHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        if (button.DataContext is not ButtonSettingsViewModel target) return;

        _hotkeyCaptureTarget = target;
        _hotkeyCaptureButton = button;
        _hotkeyPreviousValue = target.Hotkey;

        button.Content = "Press a key...";
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

        string hotkeyText = sb.ToString();
        _hotkeyCaptureTarget.Hotkey = hotkeyText;
        _vm?.RegisterHotkeyFor(_hotkeyCaptureTarget);

        EndHotkeyCapture();
        e.Handled = true;
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
