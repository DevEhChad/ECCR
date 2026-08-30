# Changelog

All notable changes to **EhChadsControllerRemapper (ECCR)** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [1.0.8] - 2026-08-30

### 🚀 Added
- **Working DirectInput Wheel Output (vJoy):** `[Wheel] ...` mapping targets now actually reach a real vJoy virtual joystick — steering, throttle, brake, clutch, handbrake, all 7 forward gears + reverse, and up to 32 generic buttons — completing a UI/preset path that previously fed nothing to Windows. Backed by a vendored, redistributable `vJoyInterfaceWrap`/`vJoyInterface` managed wrapper (no NuGet package exists for it on modern .NET).
- **Multi-Device Wheel Combining:** Physical rigs (e.g. Logitech pedals + a Moza wheelbase) can now be mapped to the same Player target using `[Wheel]` outputs and are combined into one virtual DirectInput wheel device, alongside the existing Xbox 360 (ViGEm) output path — each mapping routes independently based on its target type via the new `CompositeFeederService`.
- **Wheel Calibration Visualizer:** The Calibration dialog now shows a live rotating steering wheel dial for steering-axis mappings, and a color-coded vertical pedal-press bar for throttle/brake/clutch/handbrake mappings — shown only for wheel/pedal-category hardware (Moza, Logitech, Fanatec, Thrustmaster, Simagic, generic rigs). Gamepad/controller mappings keep the original bar visualizer.

### 🔄 Changed
- **View Model Decomposition:** Extracted all HidHide device-cloaking state/commands into a dedicated `HidHideViewModel`, and all Auto-Bind Wizard state/preset logic into a dedicated `AutoBindWizardViewModel`, out of the monolithic `MainWindowViewModel`.
- **HidHide Dialog as Embedded Overlay:** Replaced the standalone `HidHideWindow` popup with an embedded `HidHideView` overlay hosted directly inside the main window, matching the styling of the other in-window modals (Settings, Auto-Bind, Update).
- **Shared Category Helper:** Consolidated the duplicated wheel/pedal-hardware category checks (used for auto-bind guessing and preset generation) into a single `DevicePresetService.IsWheelOrPedalCategory` helper.

### 🛠️ Fixed
- **Main Window Minimum Size:** `MainWindow` previously had no minimum size, so dragging it smaller could clip the footer controls (Profile switcher, bulk-selection buttons, Add Mapping) and the HidHide modal off the edge of the window. Added a `MinWidth`/`MinHeight` floor sized to the largest fixed-size modal in the app.
- **Text Overflow Guards:** Column header labels and the Auto-Bind Wizard's preview grid now trim long text with an ellipsis instead of clipping it raw.

---

## [1.0.7] - 2026-08-26

### 🚀 Added
- **Automated Driver Setup on First Run:** ECCR now automatically detects missing prerequisites (`ViGEmBus`, `HidHide`, `vJoy`) upon initial launch and triggers silent installation routines without manual setup required.
- **Unified In-App Driver Manager:** 1-click install, repair, and uninstall actions directly inside the Driver Status dialog with real-time progress indicators.
- **Enhanced System Tray Lifecycle:** Added native right-click tray menu actions (*Open ECCR*, *Exit*) with instant, graceful driver teardown and unmanaged thread termination on shutdown.
- **CLI Startup Flags:** Added support for `--minimized` argument to launch ECCR silently into the system tray on Windows startup.

### 🔄 Changed
- **Rebranding:** Standardized application title and metadata to **EhChads Controller Remapper (ECCR)** across the window title bar, Task Manager, executable properties, and system tray.
- **Dynamic Single-Source Versioning:** Version numbers are now resolved directly from assembly metadata for consistent version displays across settings, update prompts, and title bars.
- **Updated Presets & Axis Detection:** Refined auto-bind wizard channel guessing and automatic axis inversion heuristics for Moza wheelbases, standalone sim pedals, and flight sticks.

### 🛠️ Fixed
- **Driver Health Check Synchronization:** Resolved an issue where driver service status banners could report false-negative running states after fresh installations.
- **Device Hiding Conflict:** Fixed an issue where virtual feeder targets were not properly filtered from the physical HidHide blocklist.

---

## [1.0.6] - 2026-08-25

### Added
- **Multiplayer Split-Screen UI Integration (P1–P4)**: Added intuitive visual player selection badges (`P1`, `P2`, `P3`, `P4`) with distinct theme colors across mapping rows, device group headers, and target dropdowns.
- **Dynamic Multi-Device Target Routing**: ViGEmBus now dynamically provisions up to 4 isolated Virtual Xbox 360 controller target channels (`Target #1` through `Target #4`), allowing multiple physical controllers or separate sim-rig peripherals to map independently for local split-screen games.
- **Main Window Live Target Indicator**: Added a live footer status summary reflecting the count of connected physical devices mapped to each virtual player slot.

### Fixed
- **HidHide Device Persistence & Sync**: Fixed an issue where cloaked devices were unblocked on dialog closure by implementing bidirectional driver blocklist synchronization (`SyncBlockedInstances`).
- **HidHide Virtual Device Classification**: Resolved a driver unblock bug where standard HID gamepads with empty product strings were misclassified as virtual devices.
- **Settings Serialization**: Ensured `BlockedInstanceIds`, cloaking state, and application whitelists are committed immediately to `settings.json` upon dialog confirmation.

---

## [1.0.5] - 2026-08-24

### Added
- **Multi-Virtual Target Device Isolation**: Spawns discrete, isolated virtual Xbox 360 controller instances for each target device ID (`Target #1`, `Target #2`, etc.) via ViGEmBus, preventing channel bleeding across multiple simultaneous devices.
- **Bulk Mapping Selection & Deletion**: Added per-row selection checkboxes, group-level batch controls (Select All / Deselect / Remove), and global bulk removal tools on the main footer.
- **Moza ESX & Wheel Hub Support**: Added native detection and profile generation for the Moza ESX steering wheel and DirectInput wheelbases with exact paddle and menu button offsets.
- **HidHide Setting & Cloak Persistence**: Persisted hidden hardware instance IDs (`BlockedInstanceIds`) and global hiding preferences in `settings.json` across app launches.
- **Digital Button-to-Trigger Feeding**: Added digital-to-analog trigger conversion in the virtual feeder so controllers reporting L2/R2 as buttons properly fire full trigger axis values.

### Fixed
- **Bipolar Deadzone Centering**: Resolved stick drift and input conflict on centered analog axes by calculating deadzone offsets around the rest position (0.5).
- **Graceful Application Teardown**: Fixed background thread lockups when exiting from the system tray or main window by unhooking DirectInput joysticks and shutting down driver services cleanly.
- **Moza Paddle Offset Alignment**: Corrected Moza wheelbase button indexing where paddles (indices 6 & 7) were swapped with menu/view buttons (indices 4 & 5).
- **Target Channel String Collisions**: Refactored token matching in `VirtualFeederService` to prevent `[Wheel]` channels and multi-digit button numbers from triggering unintended Xbox face button inputs.

---

## [1.0.4] - 2026-08-23

### Added
- **Interactive Update Dialog**: Added an update prompt modal allowing users to confirm or postpone incoming update downloads.[cite: 4]
- **Post-Update Welcome Modal**: Integrated an automated launch dialog confirming successful installation and version changes upon application restart.[cite: 4]
- **Dynamic Version Resolution**: Implemented runtime manifest and project metadata resolution for accurate version reflection across builds.[cite: 4]

### Changed
- **Minimalist UI Version Scoping**: Cleaned up version indicators across the UI—removed redundant version chips from the native window title bar, header banner, and main window footer, consolidating version information cleanly to the bottom-left of the Settings window.[cite: 4]

### Fixed
- **Velopack 1.x Source Integration**: Configured `GithubSource` using `Velopack.Sources` to ensure seamless release package discovery without external locator dependencies.[cite: 4]
- **Preset Model Source Generation**: Decoupled `PresetBindingItem` from service wrappers to eliminate MVVM source generator compilation collisions.[cite: 4]

---

## [1.0.3] - 2026-08-23

### Fixed
- **Velopack 1.x Lifecycle**: Restored non-blocking `VelopackApp.Build().Run()` initialization hook in `Program.cs` to enable smooth standalone installation and auto-updates.[cite: 4]
- **GitHub Release Metadata**: Updated update source handler to query GitHub releases for `releases.win.json` update assets.[cite: 4]
- **Application Teardown**: Ensured all background hooks, HidHide cloaks, and virtual feeder nodes terminate cleanly on app shutdown.[cite: 4]

---

## [1.0.2] - 2026-08-23

### Added
- **Hardware-Specific Glyph Detection**: The physical input column now renders platform-accurate symbols (`✕`, `○`, `□`, `△` for Sony PlayStation controllers; `◎`, `⮝`, `⮟`, `⎊`, `⧈`, `1`–`7`/`R` for Sim-Rig hardware; `Ⓐ`, `Ⓑ`, `Ⓧ`, `Ⓨ` for Xbox/XInput gamepads).[cite: 4]
- **Sim-Rig Multi-Device Combining**: Added direct support to aggregate discrete hardware devices (e.g., Moza wheelbases, Logitech pedals, Fanatec shifters) into a single virtual DirectInput wheel (`vJoy Target #1`).[cite: 4]
- **Device Mode Toggle**: Added bulk toggles to switch entire device mapping groups between DirectInput Wheel mode and Virtual Xbox 360 Controller mode.[cite: 4]
- **Clean Application Lifecycle Teardown**: Added `CleanupAndShutdown()` to automatically disconnect virtual Xbox/vJoy nodes, unacquire DirectInput joysticks, and disable HidHide global cloaking when ECCR exits.[cite: 4]

### Fixed
- **Auto-Bind Wizard DirectInput Index Alignment**: Resolved a mapping discrepancy where PS5 DualSense face buttons were mapped to incorrect raw button offsets (`Buttons[0]` Square, `Buttons[1]` Cross, `Buttons[2]` Circle, `Buttons[3]` Triangle).[cite: 4]
- **Virtual Controller Self-Mapping Loop**: DirectInput polling now filters out ViGEmBus (`0x045E / 0x028E`) and vJoy (`0x1234 / 0x0BE3`) hardware signatures to prevent virtual device input loops during remapping.[cite: 4]
- **Listen-to-Bind Output Freezing**: Suspended active virtual feeding while in "Listening" mode so manual key detection does not trigger duplicate inputs.[cite: 4]
- **In-Game Button Registration**: Fixed virtual report submission in `ViGEmFeederService` to ensure all Xbox 360 buttons (face keys, bumpers, stick clicks, and D-Pad) fire immediately in games.[cite: 4]
- **Channel Dropdown Badge Precedence**: Corrected color and badge evaluation logic in `ButtonBadgeConverter` so Xbox badges (`Ⓐ`, `Ⓑ`, `Ⓧ`, `Ⓨ`) display on virtual target channels.[cite: 4]

---

## [1.0.1] - 2026-08-15

### Added
- **HidHide Integration**: Added the embedded HidHide management dialog for cloaking physical devices and adding per-game executable whitelist exceptions.[cite: 4]
- **System Integration**: Added options to run ECCR on Windows startup, minimize to system tray, and close to notification area.[cite: 4]
- **Automatic Updates**: Integrated Velopack background update checks and one-click in-app updater.[cite: 4]
- **Driver Diagnostics**: Added system dependency banner to detect and start missing `ViGEmBus`, `HidHide`, and `vJoy` services.[cite: 4]

### Fixed
- Improved polling latency and thread management in high-frequency input loops.[cite: 4]
- Fixed profile serialization issues when saving custom deadzones and inverted axes.[cite: 4]

---

## [1.0.0] - 2026-08-01

### Added
- **Initial Release**: Core remapping engine for DirectInput joysticks, wheels, and gamepads.[cite: 4]
- **Virtual Controllers**: Virtual Xbox 360 controller emulation via ViGEmBus and virtual wheel emulation via vJoy.[cite: 4]
- **Profile Management**: JSON profile save/load system for application persistence.[cite: 4]
- **Interactive Listening**: Click-to-bind hardware input capture with deadzone and calibration support.[cite: 4]