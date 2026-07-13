using System.Diagnostics;
using System.Threading;
using System.Windows.Input;
using AutoClickerPro.Models;

namespace AutoClickerPro.Services;

/// <summary>
/// Detects global hotkey presses AND releases for both keyboard keys and mouse buttons
/// (Left, Right, Middle, XButton1/Mouse4, XButton2/Mouse5), via system-wide low-level hooks
/// (WH_KEYBOARD_LL and WH_MOUSE_LL). This works:
///   - even when another application or game has focus (these are system-wide hooks, not
///     tied to any window's message loop), and
///   - without ever blocking or consuming the key/button - every event is passed on to
///     CallNextHookEx unmodified, so normal typing/clicking/gaming input is completely
///     unaffected whenever the macro isn't actively simulating clicks, and
///   - without ever mistaking our own simulated clicks (sent via SendInput) for a real
///     hotkey press: synthetic events are flagged LLKHF_INJECTED / LLMHF_INJECTED by Windows
///     and are filtered out here, which is what prevents a hotkey assigned to the same
///     button a macro is clicking from re-triggering itself in a feedback loop.
///
/// Each registered hotkey (e.g. "F6", "Ctrl+F6", or a mouse button) gets its own id and fires:
///   - Pressed  exactly once when the combo transitions from "not held" to "held" (keyboard
///     auto-repeat while holding a key is filtered out so Pressed never fires twice for one
///     physical press).
///   - Released exactly once when the primary key/button of the combo is let go.
///
/// This gives callers everything needed for both interaction models:
///   Toggle Mode -> react to Pressed only (start on 1st press, stop on 2nd press).
///   Hold Mode   -> react to Pressed to start, Released to stop.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    // ---- Keyboard hook state -----------------------------------------------------------------

    private IntPtr _keyboardHookHandle;
    private readonly NativeMethods.LowLevelKeyboardProc _keyboardProc;

    // ---- Mouse hook state ---------------------------------------------------------------------

    private IntPtr _mouseHookHandle;
    private readonly NativeMethods.LowLevelMouseProc _mouseProc;
    private readonly MouseHookEvent[] _mouseEventBuffer = new MouseHookEvent[256];
    private int _mouseEventHead;
    private int _mouseEventTail;
    private readonly AutoResetEvent _mouseEventSignal = new(false);
    private Thread? _mouseWorker;
    private volatile bool _mouseWorkerRunning;
    private int _mouseButtonStateMask;

    private struct MouseHookEvent
    {
        public bool IsPressed;
        public HotkeyMouseButton Button;
    }

    // ---- Shared registration bookkeeping -------------------------------------------------------

    private readonly Dictionary<int, KeyboardRegistration> _keyboardRegistrations = new();
    private readonly Dictionary<int, MouseRegistration> _mouseRegistrations = new();
    private int _nextId = 1;

    private sealed class KeyboardRegistration
    {
        public required uint Vk;
        public required bool RequireCtrl;
        public required bool RequireAlt;
        public required bool RequireShift;
        public required Action OnPressed;
        public required Action OnReleased;
        public bool IsActive; // true while this combo is currently considered "held"
    }

    private sealed class MouseRegistration
    {
        public required HotkeyMouseButton Button;
        public required Action OnPressed;
        public required Action OnReleased;
        public bool IsActive;
    }

    /// <summary>Prefix used to encode a mouse-button hotkey as a plain string, e.g. "Mouse Left".</summary>
    private const string MousePrefix = "Mouse ";

    public HotkeyService()
    {
        _keyboardProc = KeyboardHookCallback;
        _mouseProc = MouseHookCallback;
    }

    /// <summary>Installs both system-wide hooks (keyboard and mouse). Call once, e.g. at app startup.</summary>
    public void Start()
    {
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        IntPtr moduleHandle = NativeMethods.GetModuleHandle(curModule.ModuleName);

        if (_keyboardHookHandle == IntPtr.Zero)
        {
            _keyboardHookHandle = NativeMethods.SetWindowsHookEx(
                NativeMethods.WH_KEYBOARD_LL, _keyboardProc, moduleHandle, 0);
        }

        if (_mouseHookHandle == IntPtr.Zero)
        {
            _mouseHookHandle = NativeMethods.SetWindowsHookEx(
                NativeMethods.WH_MOUSE_LL, _mouseProc, moduleHandle, 0);
        }

        StartMouseWorker();
    }

    /// <summary>Removes both hooks. Safe to call multiple times.</summary>
    public void Stop()
    {
        if (_keyboardHookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHookHandle);
            _keyboardHookHandle = IntPtr.Zero;
        }

        if (_mouseHookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHookHandle);
            _mouseHookHandle = IntPtr.Zero;
        }

        StopMouseWorker();
    }

    /// <summary>
    /// Registers a hotkey from a display string. Accepts either a keyboard combo like "F6" or
    /// "Ctrl+Alt+Shift+F6", or a mouse-button string produced by <see cref="MouseButtonToHotkeyString"/>
    /// (e.g. "Mouse Left", "Mouse Right", "Mouse Middle", "Mouse X1", "Mouse X2").
    /// Returns the registration id (use to Unregister later), or -1 if the string was empty or invalid.
    /// </summary>
    public int Register(string hotkeyText, Action onPressed, Action onReleased)
    {
        if (string.IsNullOrWhiteSpace(hotkeyText))
            return -1;

        if (TryParseMouse(hotkeyText, out HotkeyMouseButton mouseButton))
        {
            int mouseId = _nextId++;
            _mouseRegistrations[mouseId] = new MouseRegistration
            {
                Button = mouseButton,
                OnPressed = onPressed,
                OnReleased = onReleased
            };
            return mouseId;
        }

        if (!TryParseKeyboard(hotkeyText, out uint modifiers, out uint vk))
            return -1;

        int id = _nextId++;
        _keyboardRegistrations[id] = new KeyboardRegistration
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
        _keyboardRegistrations.Remove(id);
        _mouseRegistrations.Remove(id);
    }

    public void UnregisterAll()
    {
        _keyboardRegistrations.Clear();
        _mouseRegistrations.Clear();
    }

    // ---- Keyboard hook callback ---------------------------------------------------------------

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // Check the cheap stuff (nCode, registration count) before ever touching lParam - when
        // no keyboard hotkey is registered (e.g. the user only assigned a mouse button), this
        // skips the struct marshal entirely on every single keystroke system-wide, keeping the
        // hook procedure as fast as possible so Windows never has reason to delay or drop it.
        if (nCode >= 0 && _keyboardRegistrations.Count > 0)
        {
            int msg = wParam.ToInt32();
            bool isDown = msg is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN;
            bool isUp = msg is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP;

            if (isDown || isUp)
            {
                var data = System.Runtime.InteropServices.Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                if ((data.flags & NativeMethods.LLKHF_INJECTED) == 0)
                {
                    if (isDown)
                        EvaluateKeyPress(data.vkCode);
                    else
                        EvaluateKeyRelease(data.vkCode);
                }
            }
        }

        // Always pass the event through - never swallow real user input.
        return NativeMethods.CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
    }

    private void EvaluateKeyPress(uint vk)
    {
        // Modifier state is queried live from the OS (GetAsyncKeyState) rather than tracked
        // via our own down/up bookkeeping. This is the key fix for reliability: it can never
        // drift out of sync with reality (e.g. from a key-up event swallowed by another app
        // that briefly took exclusive input focus), and it reflects the true global state of
        // Ctrl/Alt/Shift regardless of how many *other*, unrelated keys are currently held.
        bool ctrl = NativeMethods.IsKeyDown(NativeMethods.VK_CONTROL);
        bool alt = NativeMethods.IsKeyDown(NativeMethods.VK_MENU);
        bool shift = NativeMethods.IsKeyDown(NativeMethods.VK_SHIFT);

        foreach (var reg in _keyboardRegistrations.Values)
        {
            if (reg.Vk != vk) continue;

            // Per-registration active flag is the sole repeat guard: the first genuine
            // key-down sets it and fires Pressed; OS auto-repeat key-downs for the same key
            // arrive while it's still true and are ignored; a real key-up clears it.
            if (reg.IsActive) continue;

            // Require only that the hotkey's OWN modifiers are held - deliberately NOT an
            // exact match. A plain "F6" hotkey must fire whether or not the user also happens
            // to be holding Ctrl, Shift, W/A/S/D, or anything else unrelated at that instant;
            // an exact-match check was the root cause of hotkeys silently failing to activate
            // whenever other keys were held.
            if (reg.RequireCtrl && !ctrl) continue;
            if (reg.RequireAlt && !alt) continue;
            if (reg.RequireShift && !shift) continue;

            reg.IsActive = true;
            reg.OnPressed();
        }
    }

    private void EvaluateKeyRelease(uint vk)
    {
        foreach (var reg in _keyboardRegistrations.Values)
        {
            if (reg.Vk != vk) continue;
            if (!reg.IsActive) continue;

            reg.IsActive = false;
            reg.OnReleased();
        }
    }

    // ---- Mouse hook callback -----------------------------------------------------------------

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _mouseRegistrations.Count > 0)
        {
            int msg = wParam.ToInt32();
            bool isDown = msg is NativeMethods.WM_LBUTTONDOWN or NativeMethods.WM_RBUTTONDOWN
                                or NativeMethods.WM_MBUTTONDOWN or NativeMethods.WM_XBUTTONDOWN;
            bool isUp = msg is NativeMethods.WM_LBUTTONUP or NativeMethods.WM_RBUTTONUP
                              or NativeMethods.WM_MBUTTONUP or NativeMethods.WM_XBUTTONUP;

            if (isDown || isUp)
            {
                var data = System.Runtime.InteropServices.Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);

                if ((data.flags & NativeMethods.LLMHF_INJECTED) == 0)
                {
                    HotkeyMouseButton button = msg switch
                    {
                        NativeMethods.WM_LBUTTONDOWN or NativeMethods.WM_LBUTTONUP => HotkeyMouseButton.Left,
                        NativeMethods.WM_RBUTTONDOWN or NativeMethods.WM_RBUTTONUP => HotkeyMouseButton.Right,
                        NativeMethods.WM_MBUTTONDOWN or NativeMethods.WM_MBUTTONUP => HotkeyMouseButton.Middle,
                        _ => (ushort)(data.mouseData >> 16) == NativeMethods.XBUTTON2
                                ? HotkeyMouseButton.XButton2
                                : HotkeyMouseButton.XButton1
                    };

                    if (isDown)
                    {
                        if (TryMarkMouseButtonPressed(button))
                        {
                            EnqueueMouseEvent(new MouseHookEvent { IsPressed = true, Button = button });
                        }
                    }
                    else if (TryReleaseMouseButton(button))
                    {
                        EnqueueMouseEvent(new MouseHookEvent { IsPressed = false, Button = button });
                    }
                }
            }
        }

        return NativeMethods.CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
    }

    private void StartMouseWorker()
    {
        if (_mouseWorker is { IsAlive: true })
            return;

        _mouseWorkerRunning = true;
        _mouseWorker = new Thread(ProcessMouseEvents)
        {
            IsBackground = true,
            Name = "AutoClickerPro.MouseHotkeyWorker"
        };
        _mouseWorker.Start();
    }

    private void StopMouseWorker()
    {
        if (_mouseWorker is null)
            return;

        _mouseWorkerRunning = false;
        _mouseEventSignal.Set();

        if (_mouseWorker.IsAlive)
            _mouseWorker.Join(250);

        _mouseWorker = null;
    }

    private void ProcessMouseEvents()
    {
        while (_mouseWorkerRunning)
        {
            _mouseEventSignal.WaitOne();

            while (_mouseWorkerRunning && TryDequeueMouseEvent(out MouseHookEvent evt))
            {
                if (evt.IsPressed)
                    EvaluateMousePress(evt.Button);
                else
                    EvaluateMouseRelease(evt.Button);
            }
        }
    }

    private bool TryEnqueueMouseEvent(MouseHookEvent evt)
    {
        while (true)
        {
            int head = Volatile.Read(ref _mouseEventHead);
            int tail = Volatile.Read(ref _mouseEventTail);
            int nextTail = (tail + 1) & (_mouseEventBuffer.Length - 1);

            if (nextTail == head)
                return false;

            if (Interlocked.CompareExchange(ref _mouseEventTail, nextTail, tail) == tail)
            {
                _mouseEventBuffer[tail] = evt;
                Thread.MemoryBarrier();
                _mouseEventSignal.Set();
                return true;
            }
        }
    }

    private bool TryDequeueMouseEvent(out MouseHookEvent evt)
    {
        evt = default;

        while (true)
        {
            int head = Volatile.Read(ref _mouseEventHead);
            int tail = Volatile.Read(ref _mouseEventTail);
            if (head == tail)
                return false;

            evt = _mouseEventBuffer[head];
            int nextHead = (head + 1) & (_mouseEventBuffer.Length - 1);
            if (Interlocked.CompareExchange(ref _mouseEventHead, nextHead, head) == head)
                return true;
        }
    }

    private bool TryMarkMouseButtonPressed(HotkeyMouseButton button)
    {
        int mask = ButtonToMask(button);
        while (true)
        {
            int current = Volatile.Read(ref _mouseButtonStateMask);
            if ((current & mask) != 0)
                return false;

            int updated = current | mask;
            if (Interlocked.CompareExchange(ref _mouseButtonStateMask, updated, current) == current)
                return true;
        }
    }

    private bool TryReleaseMouseButton(HotkeyMouseButton button)
    {
        int mask = ButtonToMask(button);
        while (true)
        {
            int current = Volatile.Read(ref _mouseButtonStateMask);
            if ((current & mask) == 0)
                return false;

            int updated = current & ~mask;
            if (Interlocked.CompareExchange(ref _mouseButtonStateMask, updated, current) == current)
                return true;
        }
    }

    private static int ButtonToMask(HotkeyMouseButton button) => button switch
    {
        HotkeyMouseButton.Left => 0x01,
        HotkeyMouseButton.Right => 0x02,
        HotkeyMouseButton.Middle => 0x04,
        HotkeyMouseButton.XButton1 => 0x08,
        HotkeyMouseButton.XButton2 => 0x10,
        _ => 0x00
    };

    private void EnqueueMouseEvent(MouseHookEvent evt)
    {
        _ = TryEnqueueMouseEvent(evt);
    }

    private void EvaluateMousePress(HotkeyMouseButton button)
    {
        foreach (var reg in _mouseRegistrations.Values)
        {
            if (reg.Button != button) continue;
            if (reg.IsActive) continue;

            reg.IsActive = true;
            reg.OnPressed();
        }
    }

    private void EvaluateMouseRelease(HotkeyMouseButton button)
    {
        foreach (var reg in _mouseRegistrations.Values)
        {
            if (reg.Button != button) continue;
            if (!reg.IsActive) continue;

            reg.IsActive = false;
            reg.OnReleased();
        }
    }

    // ---- Parsing / formatting -----------------------------------------------------------------

    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;

    /// <summary>Parses "Ctrl+Alt+Shift+Key" style strings produced by the hotkey capture control.</summary>
    public static bool TryParseKeyboard(string text, out uint modifiers, out uint vk)
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

    /// <summary>Parses a mouse-button hotkey string produced by <see cref="MouseButtonToHotkeyString"/>.</summary>
    public static bool TryParseMouse(string text, out HotkeyMouseButton button)
    {
        button = default;
        if (!text.StartsWith(MousePrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        string suffix = text[MousePrefix.Length..].Trim();
        switch (suffix.ToLowerInvariant())
        {
            case "left": button = HotkeyMouseButton.Left; return true;
            case "right": button = HotkeyMouseButton.Right; return true;
            case "middle": button = HotkeyMouseButton.Middle; return true;
            case "x1": button = HotkeyMouseButton.XButton1; return true;
            case "x2": button = HotkeyMouseButton.XButton2; return true;
            default: return false;
        }
    }

    /// <summary>
    /// Produces the canonical display/storage string for a mouse-button hotkey (e.g. "Mouse Left",
    /// "Mouse X1"), used by the hotkey capture UI and round-tripped through profile JSON.
    /// </summary>
    public static string MouseButtonToHotkeyString(HotkeyMouseButton button) => button switch
    {
        HotkeyMouseButton.Left => MousePrefix + "Left",
        HotkeyMouseButton.Right => MousePrefix + "Right",
        HotkeyMouseButton.Middle => MousePrefix + "Middle",
        HotkeyMouseButton.XButton1 => MousePrefix + "X1",
        HotkeyMouseButton.XButton2 => MousePrefix + "X2",
        _ => throw new ArgumentOutOfRangeException(nameof(button))
    };

    public void Dispose()
    {
        UnregisterAll();
        Stop();
    }
}
