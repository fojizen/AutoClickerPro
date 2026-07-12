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

    // Virtual-key codes for modifier keys. Windows reports the specific left/right variant
    // (e.g. VK_LSHIFT/VK_RSHIFT) through the low-level hook, not the generic VK_SHIFT, so we
    // track both forms to be safe across OS versions and keyboard drivers.
    internal const uint VK_SHIFT = 0x10;
    internal const uint VK_CONTROL = 0x11;
    internal const uint VK_MENU = 0x12; // Alt
    internal const uint VK_LSHIFT = 0xA0;
    internal const uint VK_RSHIFT = 0xA1;
    internal const uint VK_LCONTROL = 0xA2;
    internal const uint VK_RCONTROL = 0xA3;
    internal const uint VK_LMENU = 0xA4;
    internal const uint VK_RMENU = 0xA5;

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

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
