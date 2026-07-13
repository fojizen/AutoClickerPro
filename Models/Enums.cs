namespace AutoClickerPro.Models;

/// <summary>
/// Which physical mouse button a click-settings block controls.
/// </summary>
public enum MouseButtonTarget
{
    Left,
    Right
}

/// <summary>
/// Hold  = clicking only occurs while the physical button is held down.
/// Toggle = pressing the hotkey/button once starts continuous clicking, pressing again stops it.
/// </summary>
public enum ClickMode
{
    Hold,
    Toggle
}

/// <summary>
/// Overall state of the macro engine, surfaced directly in the UI status indicator.
/// </summary>
public enum MacroState
{
    Stopped,
    Running,
    Paused
}

/// <summary>
/// A physical mouse button that can be assigned as a global hotkey (separate from
/// <see cref="MouseButtonTarget"/>, which is which button the macro *clicks* - a hotkey can
/// be assigned to any of these five buttons regardless of which button's macro it controls).
/// </summary>
public enum HotkeyMouseButton
{
    Left,
    Right,
    Middle,
    XButton1,
    XButton2
}
