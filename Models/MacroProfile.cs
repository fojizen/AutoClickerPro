namespace AutoClickerPro.Models;

/// <summary>
/// A named, persistable bundle of left- and right-click settings.
/// Serialized to JSON in the Profiles folder next to the executable.
/// </summary>
public class MacroProfile
{
    public string Name { get; set; } = "Default";

    public ClickSettings LeftClick { get; set; } = new() { Target = MouseButtonTarget.Left, Hotkey = "F6" };

    public ClickSettings RightClick { get; set; } = new() { Target = MouseButtonTarget.Right, Hotkey = "F7" };

    public static MacroProfile CreateDefault() => new();
}
