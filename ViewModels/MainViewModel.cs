using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using AutoClickerPro.Models;
using AutoClickerPro.Services;

namespace AutoClickerPro.ViewModels;

/// <summary>
/// Top-level ViewModel for the application. Owns the ClickEngine and HotkeyService, and
/// exposes the two per-button ViewModels the view binds to.
/// </summary>
public sealed class MainViewModel : ViewModelBase, IDisposable
{
    private readonly ClickEngine _engine = new();
    private readonly HotkeyService _hotkeyService = new();
    private readonly ProfileService _profileService = new();

    private readonly Dictionary<MouseButtonTarget, int> _registeredHotkeyIds = new();

    public ButtonSettingsViewModel LeftClick { get; private set; }
    public ButtonSettingsViewModel RightClick { get; private set; }

    public ObservableCollection<string> ProfileNames { get; } = new();

    private string _currentProfileName = "Default";
    public string CurrentProfileName
    {
        get => _currentProfileName;
        set => SetField(ref _currentProfileName, value);
    }

    private string _statusMessage = "Ready";
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    // ---- Commands ----------------------------------------------------------------------------

    /// <summary>
    /// Generic Start/Pause/Stop commands taking a ButtonSettingsViewModel as CommandParameter.
    /// Using one generic command (instead of separate Left/Right commands) lets a single
    /// reusable XAML DataTemplate drive both the Left-Click and Right-Click panels.
    /// </summary>
    public ICommand StartCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand StopCommand { get; }

    public ICommand StartAllCommand { get; }
    public ICommand PauseAllCommand { get; }
    public ICommand StopAllCommand { get; }

    public ICommand SaveProfileCommand { get; }
    public ICommand LoadProfileCommand { get; }
    public ICommand DeleteProfileCommand { get; }

    public MainViewModel(Window ownerWindow)
    {
        // The window is no longer needed by HotkeyService (it now uses a system-wide
        // keyboard hook instead of RegisterHotKey/WM_HOTKEY), but the parameter is kept so
        // the view's construction call (`new MainViewModel(this)`) doesn't need to change,
        // and so a window handle remains available here if a future feature needs one
        // (e.g. parenting a dialog).
        _ = ownerWindow;

        var profile = MacroProfile.CreateDefault();
        LeftClick = new ButtonSettingsViewModel(profile.LeftClick);
        RightClick = new ButtonSettingsViewModel(profile.RightClick);

        _engine.RealTimeCpsUpdated += OnRealTimeCpsUpdated;
        _engine.StateChanged += OnEngineStateChanged;

        // Installs the global low-level keyboard hook. This never blocks or consumes any
        // key, so normal typing and every other application's input keeps working exactly
        // as before - we only ever observe key down/up events, never swallow them.
        _hotkeyService.Start();

        StartCommand = new RelayCommand<ButtonSettingsViewModel>(vm => { if (vm != null) StartMacro(vm); });
        PauseCommand = new RelayCommand<ButtonSettingsViewModel>(vm => { if (vm != null) PauseMacro(vm); });
        StopCommand = new RelayCommand<ButtonSettingsViewModel>(vm => { if (vm != null) StopMacro(vm); });

        StartAllCommand = new RelayCommand(() => { StartMacro(LeftClick); StartMacro(RightClick); });
        PauseAllCommand = new RelayCommand(() => { PauseMacro(LeftClick); PauseMacro(RightClick); });
        StopAllCommand = new RelayCommand(() => { StopMacro(LeftClick); StopMacro(RightClick); });

        SaveProfileCommand = new RelayCommand(SaveProfile);
        LoadProfileCommand = new RelayCommand<string>(LoadProfile);
        DeleteProfileCommand = new RelayCommand<string>(DeleteProfile);

        RefreshProfileList();

        // Hotkeys are (re)registered whenever the user changes them; wired up via the view's
        // "commit" event on the hotkey capture control (see MainWindow.xaml.cs), which calls
        // RegisterHotkeyFor below. We register the defaults up front here.
        RegisterHotkeyFor(LeftClick);
        RegisterHotkeyFor(RightClick);
    }

    // ---- Hotkey-driven start/stop, for both Toggle Mode and Hold Mode ----------------------
    //
    // Toggle Mode: only Pressed matters. First press starts, next press stops.
    // Hold Mode:   Pressed starts, Released stops - the physical mouse button is never
    //              involved; only the assigned hotkey drives clicking.

    private void OnHotkeyPressed(ButtonSettingsViewModel vm)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (!vm.IsEnabled) return;

            if (vm.Mode == ClickMode.Hold)
            {
                // Hold Mode: pressing the hotkey begins clicking immediately.
                StartMacro(vm);
            }
            else
            {
                // Toggle Mode: press once to start, press again to stop.
                if (vm.State is MacroState.Running or MacroState.Paused)
                    StopMacro(vm);
                else
                    StartMacro(vm);
            }
        });
    }

    private void OnHotkeyReleased(ButtonSettingsViewModel vm)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            // Only Hold Mode reacts to release; Toggle Mode ignores it entirely so a quick
            // tap of the hotkey still leaves the macro running until pressed again.
            if (vm.IsEnabled && vm.Mode == ClickMode.Hold)
                StopMacro(vm);
        });
    }

    // ---- Start/Pause/Stop ---------------------------------------------------------------------

    public void StartMacro(ButtonSettingsViewModel vm)
    {
        if (!vm.IsEnabled)
        {
            StatusMessage = $"{vm.DisplayName} is disabled - enable it first.";
            return;
        }

        _engine.Start(vm.Model);
        StatusMessage = $"{vm.DisplayName} started at {vm.Cps:0.#} CPS";
    }

    public void PauseMacro(ButtonSettingsViewModel vm)
    {
        _engine.Pause(vm.Target);
        StatusMessage = $"{vm.DisplayName} paused";
    }

    public void StopMacro(ButtonSettingsViewModel vm)
    {
        _engine.Stop(vm.Target);
        StatusMessage = $"{vm.DisplayName} stopped";
    }

    private void OnEngineStateChanged(MouseButtonTarget target, MacroState state)
    {
        var vm = target == MouseButtonTarget.Left ? LeftClick : RightClick;
        Application.Current.Dispatcher.Invoke(() => vm.State = state);
    }

    private void OnRealTimeCpsUpdated(MouseButtonTarget target, double cps)
    {
        var vm = target == MouseButtonTarget.Left ? LeftClick : RightClick;
        Application.Current.Dispatcher.Invoke(() => vm.RealTimeCps = cps);
    }

    // ---- Hotkeys -----------------------------------------------------------------------------

    /// <summary>Call after a user assigns a new hotkey to (re)register it with Windows.</summary>
    public void RegisterHotkeyFor(ButtonSettingsViewModel vm)
    {
        if (_registeredHotkeyIds.TryGetValue(vm.Target, out int existingId))
        {
            _hotkeyService.Unregister(existingId);
            _registeredHotkeyIds.Remove(vm.Target);
        }

        if (string.IsNullOrWhiteSpace(vm.Hotkey)) return;

        int id = _hotkeyService.Register(
            vm.Hotkey,
            onPressed: () => OnHotkeyPressed(vm),
            onReleased: () => OnHotkeyReleased(vm));

        if (id == -1)
            StatusMessage = $"Could not register hotkey '{vm.Hotkey}' for {vm.DisplayName} (invalid or already in use).";
        else
            _registeredHotkeyIds[vm.Target] = id;
    }

    // ---- Profiles ------------------------------------------------------------------------------

    private void RefreshProfileList()
    {
        ProfileNames.Clear();
        try
        {
            foreach (var name in _profileService.ListProfileNames())
                ProfileNames.Add(name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"Could not read profiles folder: {ex.Message}";
        }
    }

    private void SaveProfile(object? _)
    {
        try
        {
            var profile = new MacroProfile
            {
                Name = CurrentProfileName,
                LeftClick = LeftClick.Model.Clone(),
                RightClick = RightClick.Model.Clone()
            };
            _profileService.Save(profile);
            RefreshProfileList();
            StatusMessage = $"Profile '{CurrentProfileName}' saved.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"Could not save profile: {ex.Message}";
        }
    }

    private void LoadProfile(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;

        MacroProfile? profile;
        try
        {
            profile = _profileService.Load(name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            StatusMessage = $"Could not load profile '{name}': {ex.Message}";
            return;
        }

        if (profile == null)
        {
            StatusMessage = $"Profile '{name}' could not be loaded.";
            return;
        }

        // Stop any running macros before swapping settings out from under them.
        StopMacro(LeftClick);
        StopMacro(RightClick);

        LeftClick = new ButtonSettingsViewModel(profile.LeftClick);
        RightClick = new ButtonSettingsViewModel(profile.RightClick);
        OnPropertyChanged(nameof(LeftClick));
        OnPropertyChanged(nameof(RightClick));

        CurrentProfileName = profile.Name;
        RegisterHotkeyFor(LeftClick);
        RegisterHotkeyFor(RightClick);

        StatusMessage = $"Profile '{name}' loaded.";
    }

    private void DeleteProfile(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            _profileService.Delete(name);
            RefreshProfileList();
            StatusMessage = $"Profile '{name}' deleted.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"Could not delete profile '{name}': {ex.Message}";
        }
    }

    public void Dispose()
    {
        _hotkeyService.Dispose();
        _engine.StopAll();
        _engine.Dispose();
    }
}
