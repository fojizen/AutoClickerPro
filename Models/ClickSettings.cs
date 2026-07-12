namespace AutoClickerPro.Models;

/// <summary>
/// Plain-data settings for one mouse button's clicking behaviour.
/// This is the object that gets persisted to disk as part of a profile.
/// Kept free of WPF/INotifyPropertyChanged concerns so it stays a clean, testable POCO;
/// the ViewModel layer wraps it for data-binding.
/// </summary>
public class ClickSettings
{
    public MouseButtonTarget Target { get; set; }

    /// <summary>Whether this button's macro is enabled at all.</summary>
    public bool IsEnabled { get; set; }

    /// <summary>Target clicks-per-second, adjustable 1-50 via slider or numeric box.</summary>
    public double Cps { get; set; } = 10;

    /// <summary>When true, actual click interval is randomly jittered by +/- RandomVariation each click.</summary>
    public bool UseRandomVariation { get; set; }

    /// <summary>Maximum CPS deviation applied per click when UseRandomVariation is true (spec range: 1-5).</summary>
    public double RandomVariation { get; set; } = 2;

    public ClickMode Mode { get; set; } = ClickMode.Toggle;

    /// <summary>Human readable hotkey string, e.g. "F6" or "Ctrl+Alt+F6".</summary>
    public string Hotkey { get; set; } = string.Empty;

    /// <summary>Creates a deep-enough copy (all value types + one string) for profile save/duplication.</summary>
    public ClickSettings Clone() => new()
    {
        Target = Target,
        IsEnabled = IsEnabled,
        Cps = Cps,
        UseRandomVariation = UseRandomVariation,
        RandomVariation = RandomVariation,
        Mode = Mode,
        Hotkey = Hotkey
    };
}
