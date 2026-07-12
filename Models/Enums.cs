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
