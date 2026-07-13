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
    private readonly ClickEngine _engine;
    private readonly HotkeyService _hotkeyService;
    private readonly ProfileService _profileService;

    private readonly Dictionary<MouseButtonTarget, int> _registeredHotkeyIds = new();
    private bool _isDisposed;

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

    public ICommand StartCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand StopCommand { get; }

    public ICommand StartAllCommand { get; }
    public ICommand PauseAllCommand { get; }
    public ICommand StopAllCommand { get; }
    public ICommand ExitCommand { get; }

    public ICommand SaveProfileCommand { get; }
    public ICommand LoadProfileCommand { get; }
    public ICommand DeleteProfileCommand { get; }

    public ICommand ShowWindowCommand { get; }

    public MainViewModel(ClickEngine engine, HotkeyService hotkeyService, ProfileService profileService)
    {
        _engine = engine;
        _hotkeyService = hotkeyService;
        _profileService = profileService;

        var profile = MacroProfile.CreateDefault();
        LeftClick = new ButtonSettingsViewModel(profile.LeftClick);
        RightClick = new ButtonSettingsViewModel(profile.RightClick);

        _engine.RealTimeCpsUpdated += OnRealTimeCpsUpdated;
        _engine.StateChanged += OnEngineStateChanged;

        _hotkeyService.Start();

        StartCommand = new RelayCommand<ButtonSettingsViewModel>(vm => { if (vm != null) StartMacro(vm); });
        PauseCommand = new RelayCommand<ButtonSettingsViewModel>(vm => { if (vm != null) PauseMacro(vm); });
        StopCommand = new RelayCommand<ButtonSettingsViewModel>(vm => { if (vm != null) StopMacro(vm); });

        StartAllCommand = new RelayCommand(() => { StartMacro(LeftClick); StartMacro(RightClick); });
        PauseAllCommand = new RelayCommand(() => { PauseMacro(LeftClick); PauseMacro(RightClick); });
        StopAllCommand = new RelayCommand(() => { StopMacro(LeftClick); StopMacro(RightClick); });
        ExitCommand = new RelayCommand(ExitApplication);

        SaveProfileCommand = new RelayCommand(SaveProfile);
        LoadProfileCommand = new RelayCommand<string>(LoadProfile);
        DeleteProfileCommand = new RelayCommand<string>(DeleteProfile);

        ShowWindowCommand = new RelayCommand(() =>
        {
            if (Application.Current.MainWindow is { } window)
            {
                window.Show();
                window.WindowState = WindowState.Normal;
                window.Activate();
            }
        });

        RefreshProfileList();

        RegisterHotkeyFor(LeftClick);
        RegisterHotkeyFor(RightClick);
    }

    // ---- Hotkey-driven start/stop -----------------------------------------------------------

    private void OnHotkeyPressed(ButtonSettingsViewModel vm)
    {
        if (!vm.IsEnabled) return;

        if (vm.Mode == ClickMode.Hold)
        {
            _engine.Start(vm.Model);
        }
        else
        {
            if (vm.State is MacroState.Running or MacroState.Paused)
                _engine.Stop(vm.Target);
            else
                _engine.Start(vm.Model);
        }

        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            StatusMessage = $"{vm.DisplayName} running";
        });
    }

    private void OnHotkeyReleased(ButtonSettingsViewModel vm)
    {
        if (!vm.IsEnabled) return;

        if (vm.Mode == ClickMode.Hold)
        {
            _engine.Stop(vm.Target);

            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                StatusMessage = $"{vm.DisplayName} stopped";
            });
        }
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
        Application.Current.Dispatcher.BeginInvoke(() => vm.State = state);
    }

    private void OnRealTimeCpsUpdated(MouseButtonTarget target, double cps)
    {
        var vm = target == MouseButtonTarget.Left ? LeftClick : RightClick;
        Application.Current.Dispatcher.BeginInvoke(() => vm.RealTimeCps = cps);
    }

    // ---- Hotkeys -----------------------------------------------------------------------------

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
            StatusMessage = $"Could not delete profile: {ex.Message}";
        }
    }

    public void ExitApplication()
    {
        if (_isDisposed) return;

        _isDisposed = true;
        ShutdownServices();
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        if (_isDisposed) return;

        _isDisposed = true;
        ShutdownServices();
    }

    private void ShutdownServices()
    {
        _engine.StopAll();
        _hotkeyService.UnregisterAll();
        _hotkeyService.Stop();
        _hotkeyService.Dispose();
        _engine.Dispose();
    }
}
