using System.Diagnostics;
using System.Windows.Input;

namespace AutoClickerPro.Services;

/// <summary>
/// Detects global hotkey presses AND releases via a system-wide low-level keyboard hook
/// (WH_KEYBOARD_LL), so it works:
///   - even when another application or game has focus (it's a system-wide hook, not tied
///     to any window's message loop), and
///   - without ever blocking or consuming the key - every event is passed on to
///     CallNextHookEx unmodified, so normal typing/gaming input is completely unaffected.
///
/// Each registered hotkey (e.g. "F6" or "Ctrl+F6") gets its own id and fires:
///   - Pressed  exactly once when the combo transitions from "not held" to "held"
///     (OS key-repeat while holding a key is filtered out so Pressed never fires twice
///     for one physical press).
///   - Released exactly once when the primary key of the combo is let go.
///
/// This gives callers everything needed for both interaction models:
///   Toggle Mode -> react to Pressed only (start on 1st press, stop on 2nd press).
///   Hold Mode   -> react to Pressed to start, Released to stop.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private IntPtr _hookHandle;

    // Keep the delegate alive for the lifetime of the hook - otherwise the GC could collect it
    // and crash the process when Windows calls back into freed memory.
    private readonly NativeMethods.LowLevelKeyboardProc _proc;

    private readonly Dictionary<int, Registration> _registrations = new();
    private readonly HashSet<uint> _downKeys = new();
    private int _nextId = 1;

    private sealed class Registration
    {
        public required uint Vk;
        public required bool RequireCtrl;
        public required bool RequireAlt;
        public required bool RequireShift;
        public required Action OnPressed;
        public required Action OnReleased;
        public bool IsActive; // true while this combo is currently considered "held"
    }

    public HotkeyService()
    {
        _proc = HookCallback;
    }

    /// <summary>Installs the system-wide keyboard hook. Call once, e.g. at app startup.</summary>
    public void Start()
    {
        if (_hookHandle != IntPtr.Zero) return;

        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        _hookHandle = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL,
            _proc,
            NativeMethods.GetModuleHandle(curModule.ModuleName),
            0);
    }

    /// <summary>Removes the keyboard hook. Safe to call multiple times.</summary>
    public void Stop()
    {
        if (_hookHandle == IntPtr.Zero) return;
        NativeMethods.UnhookWindowsHookEx(_hookHandle);
        _hookHandle = IntPtr.Zero;
    }

    /// <summary>
    /// Registers a hotkey from a display string like "F6", "Ctrl+F6", "Ctrl+Alt+Shift+F6".
    /// Returns the registration id (use to Unregister later), or -1 if the string was empty
    /// or invalid.
    /// </summary>
    public int Register(string hotkeyText, Action onPressed, Action onReleased)
    {
        if (string.IsNullOrWhiteSpace(hotkeyText))
            return -1;

        if (!TryParse(hotkeyText, out uint modifiers, out uint vk))
            return -1;

        int id = _nextId++;
        _registrations[id] = new Registration
        {
            Vk = vk,
            RequireCtrl = (modifiers & MOD_CONTROL) != 0,
            RequireAlt = (modifiers & MOD_ALT) != 0,
            RequireShift = (modifiers & MOD_SHIFT) != 0,
            OnPressed = onPressed,
            OnReleased = onReleased
        };
        return id;
    }

    public void Unregister(int id)
    {
        if (id < 0) return;
        _registrations.Remove(id);
    }

    public void UnregisterAll() => _registrations.Clear();

    // ---- Hook callback -------------------------------------------------------------------------

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var data = System.Runtime.InteropServices.Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            bool isInjected = (data.flags & NativeMethods.LLKHF_INJECTED) != 0;
            int msg = wParam.ToInt32();
            uint vk = data.vkCode;

            // Ignore keystrokes injected by our own SendInput usage (we only inject mouse
            // clicks today, but this keeps the guard consistent and future-proof).
            if (!isInjected)
            {
                bool isDown = msg is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN;
                bool isUp = msg is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP;

                if (isDown)
                {
                    bool wasAlreadyDown = _downKeys.Contains(vk);
                    _downKeys.Add(vk);

                    // Only evaluate hotkeys on the real transition (ignore OS auto-repeat
                    // while a key is held, which would otherwise fire Pressed repeatedly).
                    if (!wasAlreadyDown)
                        EvaluatePress(vk);
                }
                else if (isUp)
                {
                    _downKeys.Remove(vk);
                    EvaluateRelease(vk);
                }
            }
        }

        // Always pass the event through - never swallow real user input.
        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private bool IsModifierCurrentlyDown(uint generic, uint left, uint right) =>
        _downKeys.Contains(generic) || _downKeys.Contains(left) || _downKeys.Contains(right);

    private void EvaluatePress(uint vk)
    {
        bool ctrl = IsModifierCurrentlyDown(NativeMethods.VK_CONTROL, NativeMethods.VK_LCONTROL, NativeMethods.VK_RCONTROL);
        bool alt = IsModifierCurrentlyDown(NativeMethods.VK_MENU, NativeMethods.VK_LMENU, NativeMethods.VK_RMENU);
        bool shift = IsModifierCurrentlyDown(NativeMethods.VK_SHIFT, NativeMethods.VK_LSHIFT, NativeMethods.VK_RSHIFT);

        foreach (var reg in _registrations.Values)
        {
            if (reg.Vk != vk) continue;
            if (reg.IsActive) continue; // already active (defensive; wasAlreadyDown already guards this)

            // Require an EXACT modifier match so "F6" and "Ctrl+F6" never both fire for the
            // same physical key combination.
            if (reg.RequireCtrl != ctrl) continue;
            if (reg.RequireAlt != alt) continue;
            if (reg.RequireShift != shift) continue;

            reg.IsActive = true;
            reg.OnPressed();
        }
    }

    private void EvaluateRelease(uint vk)
    {
        foreach (var reg in _registrations.Values)
        {
            if (reg.Vk != vk) continue;
            if (!reg.IsActive) continue;

            reg.IsActive = false;
            reg.OnReleased();
        }
    }

    // ---- Parsing ---------------------------------------------------------------------------

    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;

    /// <summary>Parses "Ctrl+Alt+Shift+Key" style strings produced by the hotkey capture control.</summary>
    public static bool TryParse(string text, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;

        string keyPart = parts[^1];
        for (int i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl": modifiers |= MOD_CONTROL; break;
                case "alt": modifiers |= MOD_ALT; break;
                case "shift": modifiers |= MOD_SHIFT; break;
                default: return false;
            }
        }

        if (!Enum.TryParse<Key>(keyPart, true, out var key))
            return false;

        vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        return vk != 0;
    }

    public void Dispose()
    {
        UnregisterAll();
        Stop();
    }
}
