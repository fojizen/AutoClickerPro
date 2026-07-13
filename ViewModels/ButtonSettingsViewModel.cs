using AutoClickerPro.Models;

namespace AutoClickerPro.ViewModels;

/// <summary>
/// Data-bindable wrapper around a single button's ClickSettings. One instance drives the
/// entire "Left Click" or "Right Click" panel in the UI: enable toggle, CPS slider + numeric
/// box (kept in sync), random-variation controls, mode selector, hotkey, and live status.
/// </summary>
public sealed class ButtonSettingsViewModel : ViewModelBase
{
    public ClickSettings Model { get; }

    public MouseButtonTarget Target => Model.Target;

    public string DisplayName => Target == MouseButtonTarget.Left ? "Left Click" : "Right Click";

    public ButtonSettingsViewModel(ClickSettings model)
    {
        Model = model;
        _cps = model.Cps;
        _isEnabled = model.IsEnabled;
        _useRandomVariation = model.UseRandomVariation;
        _randomVariation = model.RandomVariation;
        _mode = model.Mode;
        _hotkey = model.Hotkey;
        _delayMs = model.DelayMs;
    }

    private bool _isEnabled;
    public bool IsEnabled
    {
        get => _isEnabled;
        set { if (SetField(ref _isEnabled, value)) Model.IsEnabled = value; }
    }

    private double _cps;
    /// <summary>Bound to both the Slider and the numeric TextBox - editing either updates the other.</summary>
    public double Cps
    {
        get => _cps;
        set
        {
            double clamped = Math.Clamp(value, MinCps, MaxCps);
            if (SetField(ref _cps, clamped)) Model.Cps = clamped;
        }
    }

    public double MinCps => 1;
    public double MaxCps => 50;

    private bool _useRandomVariation;
    public bool UseRandomVariation
    {
        get => _useRandomVariation;
        set { if (SetField(ref _useRandomVariation, value)) Model.UseRandomVariation = value; }
    }

    private double _randomVariation = 2;
    /// <summary>Jitter magnitude in CPS, clamped to the 1-5 range requested by the product spec.</summary>
    public double RandomVariation
    {
        get => _randomVariation;
        set
        {
            double clamped = Math.Clamp(value, 1, 5);
            if (SetField(ref _randomVariation, clamped)) Model.RandomVariation = clamped;
        }
    }

    private double _delayMs;
    /// <summary>Extra fixed delay in ms added after every click, on top of the CPS-derived interval. Bound to both the Slider and the numeric TextBox - editing either updates the other.</summary>
    public double DelayMs
    {
        get => _delayMs;
        set
        {
            double clamped = Math.Clamp(value, MinDelayMs, MaxDelayMs);
            if (SetField(ref _delayMs, clamped)) Model.DelayMs = clamped;
        }
    }

    public double MinDelayMs => 0;
    public double MaxDelayMs => 1000;

    private ClickMode _mode;
    public ClickMode Mode
    {
        get => _mode;
        set
        {
            if (SetField(ref _mode, value))
            {
                Model.Mode = value;
                // Keep the two radio-button-style boolean properties in sync with the underlying enum.
                OnPropertyChanged(nameof(IsHoldMode));
                OnPropertyChanged(nameof(IsToggleMode));
            }
        }
    }

    public bool IsHoldMode
    {
        get => Mode == ClickMode.Hold;
        set { if (value) Mode = ClickMode.Hold; }
    }

    public bool IsToggleMode
    {
        get => Mode == ClickMode.Toggle;
        set { if (value) Mode = ClickMode.Toggle; }
    }

    private string _hotkey;
    public string Hotkey
    {
        get => _hotkey;
        set { if (SetField(ref _hotkey, value)) Model.Hotkey = value; }
    }

    // ---- Live status, updated by MainViewModel while the engine runs -----------------------

    private MacroState _state = MacroState.Stopped;
    public MacroState State
    {
        get => _state;
        set
        {
            if (SetField(ref _state, value))
            {
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public string StatusText => State switch
    {
        MacroState.Running => "Running",
        MacroState.Paused => "Paused",
        _ => "Stopped"
    };

    private double _realTimeCps;
    public double RealTimeCps
    {
        get => _realTimeCps;
        set => SetField(ref _realTimeCps, value);
    }
}
