using System.Runtime.InteropServices;

namespace AutoClickerPro.Services;

/// <summary>
/// All Win32 interop lives here so the rest of the codebase never touches P/Invoke directly.
/// Keeping this isolated makes the "unsafe" surface area easy to audit and to port
/// (e.g. behind an IInputSimulator abstraction) if a cross-platform backend is ever added.
/// </summary>
internal static class NativeMethods
{
    // ---- SendInput: used to synthesize mouse clicks ----------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    internal struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    internal const uint INPUT_MOUSE = 0;
    internal const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    internal const uint MOUSEEVENTF_LEFTUP = 0x0004;
    internal const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    internal const uint MOUSEEVENTF_RIGHTUP = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    // ---- Low-level keyboard hook: used for global hotkey press/release detection -----------
    //
    // We deliberately do NOT use RegisterHotKey/WM_HOTKEY here. RegisterHotKey only ever
    // reports a "pressed" event (with auto-repeat suppressed via MOD_NOREPEAT) - it has no
    // concept of "released", which Hold Mode requires (hold hotkey -> click; release -> stop).
    // A WH_KEYBOARD_LL hook gives us both WM_KEYDOWN/WM_SYSKEYDOWN and WM_KEYUP/WM_SYSKEYUP
    // for every key system-wide, regardless of which window/app has focus, and - critically -
    // it never blocks or consumes the key: we always call CallNextHookEx and never alter the
    // message, so normal keyboard (and, separately, mouse) input to every other application
    // keeps working exactly as if this app weren't running.

    internal delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    internal struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    internal const int WH_KEYBOARD_LL = 13;

    internal const int WM_KEYDOWN = 0x0100;
    internal const int WM_KEYUP = 0x0101;
    internal const int WM_SYSKEYDOWN = 0x0104;
    internal const int WM_SYSKEYUP = 0x0105;

    /// <summary>Flag set on KBDLLHOOKSTRUCT.flags when the event was injected by our own SendInput calls.</summary>
    internal const uint LLKHF_INJECTED = 0x00000010;

    // Generic virtual-key codes for the modifier keys. Querying these via GetAsyncKeyState
    // reports "either the left or right variant is down" automatically - Windows aggregates
    // VK_LCONTROL/VK_RCONTROL into VK_CONTROL for this purpose, so a single check per modifier
    // is sufficient and there's no need to separately track VK_LSHIFT/VK_RSHIFT/etc.
    internal const int VK_SHIFT = 0x10;
    internal const int VK_CONTROL = 0x11;
    internal const int VK_MENU = 0x12; // Alt

    /// <summary>
    /// Queries the CURRENT physical state of a key directly from the OS input state table -
    /// unlike tracking key-down/up messages ourselves, this can never drift out of sync (e.g.
    /// from a missed key-up while another app had exclusive input focus), and it reflects
    /// global state regardless of which thread/window currently has focus. This is what makes
    /// modifier-key checks (Ctrl/Alt/Shift) reliable even while other keys are held.
    /// </summary>
    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int vKey);

    /// <summary>True if the given virtual key is currently physically held down.</summary>
    internal static bool IsKeyDown(int vKey) => (GetAsyncKeyState(vKey) & 0x8000) != 0;

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    // ---- Low-level mouse hook: used for global mouse-button hotkey press/release detection --
    //
    // Mirrors the keyboard hook above but for mouse buttons, so Left/Right/Middle/XButton1/
    // XButton2 can all be assigned as hotkeys, work system-wide (even when another app/game
    // has focus), and are detected via genuine WM_*BUTTONDOWN/UP messages - never blocked or
    // consumed (always passed to CallNextHookEx), so normal mouse clicks keep working exactly
    // as if this app weren't running whenever the macro isn't actively simulating clicks.
    //
    // Critically, this hook must ignore events flagged LLMHF_INJECTED: those are the clicks
    // *we* generate via SendClick/SendInput. Without this filter, a hotkey assigned to the
    // same button a macro is clicking would immediately retrigger itself in a feedback loop.

    internal delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    internal const int WH_MOUSE_LL = 14;

    internal const int WM_LBUTTONDOWN = 0x0201;
    internal const int WM_LBUTTONUP = 0x0202;
    internal const int WM_RBUTTONDOWN = 0x0204;
    internal const int WM_RBUTTONUP = 0x0205;
    internal const int WM_MBUTTONDOWN = 0x0207;
    internal const int WM_MBUTTONUP = 0x0208;
    internal const int WM_XBUTTONDOWN = 0x020B;
    internal const int WM_XBUTTONUP = 0x020C;

    /// <summary>High word of MSLLHOOKSTRUCT.mouseData for WM_XBUTTON* messages identifies which X button.</summary>
    internal const ushort XBUTTON1 = 0x0001;
    internal const ushort XBUTTON2 = 0x0002;

    /// <summary>Flag set on MSLLHOOKSTRUCT.flags when the event was injected by our own SendInput calls.</summary>
    internal const uint LLMHF_INJECTED = 0x00000001;

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    internal static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    internal static extern IntPtr GetModuleHandle(string? lpModuleName);

    /// <summary>Fires one synthetic click (down+up) for the given button.</summary>
    internal static void SendClick(bool leftButton)
    {
        var down = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dwFlags = leftButton ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_RIGHTDOWN
                }
            }
        };
        var up = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dwFlags = leftButton ? MOUSEEVENTF_LEFTUP : MOUSEEVENTF_RIGHTUP
                }
            }
        };

        SendInput(2, new[] { down, up }, Marshal.SizeOf<INPUT>());
    }
}
