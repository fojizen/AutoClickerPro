# AutoClicker Pro
# 🖱️ AutoClicker Pro

![AutoClicker Pro Screenshot](Assets/AutoClickerPro.png)

A complete, modern, dark-themed Auto Clicker / Macro utility for Windows — **C#, .NET 8, WPF, MVVM**.

A complete, modern, dark-themed Auto Clicker / Macro utility for Windows — **C#, .NET 8, WPF, MVVM**, Visual Studio 2022 solution.

## Features

- Independent settings for **Left Click** and **Right Click**, each with its own enable/disable switch.
- **CPS (Clicks Per Second)** adjustable from 1–50 via a linked slider + numeric box, per button (numeric boxes reject non-numeric input via an attached behavior).
- **Random CPS variation (jitter)**: optional ±1–5 CPS so click timing isn't perfectly robotic.
- **Hold Mode** (click only while the physical button is held) and **Toggle Mode** (press hotkey/button once to start, again to stop) — chosen independently per button.
- **Global hotkeys** (work even when the app isn't focused), assignable per button via a "Set Hotkey" capture button.
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
- **`HotkeyService`** installs a system-wide low-level **keyboard** hook (`WH_KEYBOARD_LL`) that reports both key-down and key-up for the assigned hotkey, globally, even when another app or game has focus, and never blocks or consumes the key (it always calls `CallNextHookEx`, so normal typing/gaming input elsewhere is untouched). Toggle Mode reacts only to "pressed"; Hold Mode reacts to "pressed" to start and "released" to stop. Physical mouse buttons are never involved in triggering the macro — normal left/right clicks always pass through untouched.
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

- Hotkey capture: click "Set Hotkey" next to a button's hotkey box, then press the desired key combo (e.g. `Ctrl+F6`). Press `Esc` to cancel.
- **Toggle Mode**: press the assigned hotkey once to start, press it again to stop.
- **Hold Mode**: holding the assigned hotkey down clicks continuously; releasing it stops immediately. The physical mouse button is never involved — normal left/right clicks always work normally, whether the macro is running or not.
- Hotkeys work globally: they fire even when another application or game has focus, and they never block or intercept the key from reaching that other application.
- This tool simulates input via the standard Windows `SendInput` API. Some games with kernel-level anti-cheat may block synthetic input — that's outside this app's control.
- Profiles are stored as human-readable JSON, so they can be version-controlled or shared manually if desired.

## Build Status

The project has been successfully built using .NET 8 on Windows.

Tested on:
- Windows 10 x64
- Windows 11 x64

Build command:

dotnet build

Publish command:

dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
