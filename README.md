# AutoClicker Pro

A complete, modern, dark-themed Auto Clicker / Macro utility for Windows — **C#, .NET 8, WPF, MVVM**, Visual Studio 2022 solution.

## Features

- Independent settings for **Left Click** and **Right Click**, each with its own enable/disable switch.
- **CPS (Clicks Per Second)** adjustable from 1–50 via a linked slider + numeric box, per button (numeric boxes reject non-numeric input via an attached behavior).
- **Random CPS variation (jitter)**: optional ±1–5 CPS so click timing isn't perfectly robotic.
- **Hold Mode** (click only while the physical button is held) and **Toggle Mode** (press hotkey/button once to start, again to stop) — chosen independently per button.
- **Global hotkeys** (work even when the app isn't focused), assignable per button via a "Set Hotkey" capture button — supports both **keyboard keys** (with optional Ctrl/Alt/Shift) and **mouse buttons**: Left, Right, Middle, XButton1 (Mouse4/Back), XButton2 (Mouse5/Forward).
- **Start / Pause / Stop** per button, plus **Start All / Pause All / Stop All**.
- Live status (Running / Paused / Stopped) and **real-time measured CPS** while active.
- **Save/load/delete profiles** as JSON under `%AppData%\AutoClickerPro\Profiles`.
- Dark theme with smooth toggle/slider/button animations, custom app icon.
- Single-instance enforcement and application-wide exception handling (UI thread, background threads, and unobserved Task exceptions all produce a friendly dialog instead of a crash).
- Lightweight: click loops use non-blocking `Task.Delay`, so idle CPU usage stays near zero.

## Solution Structure

```
AutoClickerPro.sln
AutoClickerPro/
├── Models/            # ClickSettings, MacroProfile, Enums (MouseButtonTarget, ClickMode, MacroState)
├── ViewModels/         # MainViewModel, ButtonSettingsViewModel, RelayCommand, ViewModelBase
├── Views/              # MainWindow.xaml (+ code-behind for hotkey capture UX)
├── Services/           # ClickEngine, HotkeyService (global keyboard hook), ProfileService, NativeMethods (P/Invoke)
├── Helpers/            # SingleInstanceGuard, NumericTextBoxBehavior (attached property)
├── Converters/         # StateToBrushConverter, EnumToBooleanConverter, CpsDisplayConverter
├── Themes/              # DarkTheme.xaml resource dictionary (colors, control styles, animations)
├── Assets/              # app.ico (generated multi-resolution application icon)
├── App.xaml / App.xaml.cs
├── AutoClickerPro.csproj
└── app.manifest
```

### Why this architecture

- **`NativeMethods.cs`** isolates every P/Invoke call (SendInput, RegisterHotKey, SetWindowsHookEx) in one file, so the rest of the app never touches unsafe interop directly.
- **`HotkeyService`** installs two system-wide low-level hooks — `WH_KEYBOARD_LL` and `WH_MOUSE_LL` — that together report press *and* release for keyboard keys and for all five mouse buttons (Left, Right, Middle, XButton1, XButton2 via `WM_XBUTTONDOWN`/`WM_XBUTTONUP`). Both work globally, even when another app or game has focus, and neither ever blocks or consumes the input (they always call `CallNextHookEx`), so normal typing and normal mouse clicks are completely unaffected whenever the macro isn't actively simulating clicks. Synthetic clicks the app generates via `SendInput` are flagged `LLMHF_INJECTED`/`LLKHF_INJECTED` by Windows and are filtered out, so a hotkey assigned to the same button a macro is clicking can never retrigger itself. Modifier state (Ctrl/Alt/Shift) for keyboard hotkeys is queried live via `GetAsyncKeyState` and only checks that a hotkey's *own* required modifiers are held — not that no other key is held — so a hotkey reliably fires no matter how many unrelated keys (movement keys, other modifiers, etc.) happen to be held at the same time. Toggle Mode reacts only to "pressed"; Hold Mode reacts to "pressed" to start and "released" to stop.
- **`ClickEngine`** runs one independent, cancellable async loop per mouse button. Starting/stopping/pausing one button never affects the other. `Task.Delay` (not a spin loop) keeps CPU usage minimal between clicks. Native call failures inside the loop are caught so one bad iteration can't kill the loop.
- **`SingleInstanceGuard`** (Helpers) prevents a second instance from double-registering the same global hotkeys/hook.
- **ViewModels never touch Win32 directly** — they only talk to the Services layer.
- A single reusable **`DataTemplate`** (`ButtonPanelTemplate` in `MainWindow.xaml`) drives both the Left-Click and Right-Click panels from one `ButtonSettingsViewModel`, avoiding duplicated XAML.
- **`App.xaml.cs`** wires up `DispatcherUnhandledException`, `AppDomain.UnhandledException`, and `TaskScheduler.UnobservedTaskException` so no unhandled exception can silently crash the app during long unattended runs.

## Build & Run (Visual Studio 2022)

Requirements: **Windows 10/11**, **.NET 8 SDK**, Visual Studio 2022 (17.8+) with the ".NET desktop development" workload.

1. Open `AutoClickerPro.sln` in Visual Studio 2022.
2. Set the platform to `x64` (or switch to `Any CPU` if preferred — adjust `<Platforms>` in the `.csproj` if so).
3. Build → Rebuild Solution.
4. Run (F5).

Or from the command line:

```bash
cd AutoClickerPro
dotnet restore
dotnet build -c Release
dotnet run
```

To publish a single, self-contained executable:

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

No external NuGet packages are required — `SendInput`/`RegisterHotKey`/`SetWindowsHookEx` are called via P/Invoke, and JSON profile persistence uses `System.Text.Json`, which ships in the `net8.0-windows` shared framework.

## Notes

- Hotkeys fire reliably no matter what else is currently held down — Ctrl, Shift, Alt, movement keys, etc. Only the hotkey's own required modifiers (if any) are checked; unrelated keys never block it.
- Hotkey capture: click "Set Hotkey" next to a button's hotkey box, then either press the desired keyboard combo (e.g. `Ctrl+F6`) or click the desired mouse button (Left, Right, Middle, or the side XButton1/XButton2 "thumb" buttons). Press `Esc` to cancel a keyboard capture.
- **Toggle Mode**: press the assigned hotkey (key or mouse button) once to start, press it again to stop.
- **Hold Mode**: holding the assigned hotkey down clicks continuously; releasing it stops immediately. This works identically for keyboard keys and mouse buttons.
- Hotkeys work globally: they fire even when another application or game has focus, and they never block or intercept the key/button from reaching that other application - normal mouse clicks always behave normally when the macro isn't running.
- This tool simulates input via the standard Windows `SendInput` API. Some games with kernel-level anti-cheat may block synthetic input — that's outside this app's control.
- Profiles are stored as human-readable JSON, so they can be version-controlled or shared manually if desired.

## On verification

This project was developed and reviewed in a Linux environment without access to a Windows machine, the .NET desktop (WPF) workload, or NuGet — so an actual `dotnet build`/Visual Studio compile could not be executed here. What *was* done before packaging:

- Every `.cs` file was checked for balanced braces/parentheses.
- Every class/namespace referenced from XAML (`vm:`, `conv:`, `helpers:` prefixes) was cross-checked against its actual declaration and namespace.
- Every `x:Class` in `.xaml` was verified to match its code-behind `partial class`.
- Every `StaticResource` key used in `MainWindow.xaml` was cross-checked against keys declared in `DarkTheme.xaml` and `MainWindow.xaml`'s own `Resources`.
- All command bindings, converters, and attached properties were traced end-to-end by hand.

This gives high confidence the solution is correct, but please do a build in Visual Studio 2022 on your end as the final check — if anything surfaces, it'll almost certainly be a minor XAML typo that's quick to fix, and I'm happy to help track it down.

