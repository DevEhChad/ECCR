# Changelog

All notable changes to **EhChadsControllerRemapper (ECCR)** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [1.1.0] - 2026-08-31

### 🚀 Added
- **Real Wheel Force Feedback:** A game's force-feedback effects (spring, damper, friction, constant/ramp/periodic forces) now relay live from its virtual `[Wheel]` device all the way back to the real wheel — decoded from vJoy's PID packet callback and replayed as native DirectInput effects on the physical device. This was previously flagged as out of scope; `vJoyFeederService` was output-only.
- **Wheel Settings Tab:** New per-device tab on the Device Mapping page for rotation degrees, force-feedback master toggle + strength (0–200%), individual spring/damper/friction effect toggles, invert-force, an "FFB not detected" capability banner, and a shortcut into HidHide cloaking.
- **Controller Mapping Redesign:** The Controller Mapping page is now a tabbed layout (Button Mapping, Deadzones, Motion, Sticks, Touchpad, More Options) scoped to a device picker, alongside a profile switcher and virtual-device indicator in the header.
- **Gyro-Aim & Touch-Aim:** DualSense/DualShock 4 gyro yaw/pitch or touchpad finger position can now drive the Right Stick of a chosen Player's virtual Xbox 360 controller directly, with live gyro/touch readouts on the new Motion/Touchpad tabs.
- **Virtual DualShock 4 Output:** Added a ViGEm-backed virtual DualShock 4 target (`[DS4] ...`) alongside the existing Xbox 360 and vJoy wheel outputs, for games that expect a native PlayStation controller layout.
- **"+ New Binding" Restored:** Re-added the page-level control for starting a brand-new binding from scratch on both the Controller Mapping and Device Mapping pages, placing it in an "Uncategorized Device"/"Uncategorized Wheel Device" group in the correct section until a real input is captured.
- **Capture Hardening:** Click-to-Bind now auto-cancels after 7 seconds of no input, Esc cancels an in-progress capture without touching the existing binding, and Backspace clears it — with an on-page hint explaining all three.
- **Live HidHide Auto-Cloaking:** A physical device is now cloaked from every other application the instant it becomes a mapping source (and reconciled for every device already in a loaded profile), closing the gap where a game could see both the raw device and ECCR's virtual output at once and receive doubled/fighting input.
- **Independent Wheel Device Slots (1–8):** `[Wheel]` mapping targets now assign to one of 8 independent "Wheel Slot" vJoy destinations via a new dropdown, fully decoupled from the 4 Xbox "Player" slots — a rig can now spread a wheel+pedals, a separate shifter, and a handbrake across different vJoy devices instead of being limited to the same 4 slots Xbox-bound controllers share. The Auto-Bind Wizard's DirectInput Wheel mode picks up the same Wheel Slot selector.
- **Physical Input Nicknames:** Every physical button/axis can now be renamed via a new "✎" editor next to its row in the mapping grid — useful for wheels/rigs whose printed button legend ECCR can't infer (most brands besides Moza don't distinguish a PlayStation-legend vs. Xbox-legend SKU). The nickname persists per device and shows everywhere that input's name appears (mapping grid, Calibration dialog, Click-to-Bind capture).

### 🔄 Changed
- **Full Shutdown Teardown:** Exiting ECCR now also live-disables HidHide cloaking and the virtual output devices, so every physical device is visible and every virtual one is gone the moment the process exits — the saved on/off preferences are untouched and simply reapply on next launch.
- **Device Mapping Page Redesign:** Rebuilt to match the new Controller Mapping page's tab-strip/device-picker pattern via a shared `DeviceMappingGroupsPanel`, instead of a single flat grid.
- **`WheelDeviceSettings` Is Now Live:** Converted to an observable model wired into profile save/load and the new Wheel Settings tab — previously scaffolded but completely unused by the app.
- **Wheel Rotation Is Now a Single Device-Level Setting:** Consolidated the previously duplicated rotation-degrees fields (one on the mapping, one on the device) into one authoritative value on `WheelDeviceSettings`, shared live by the Wheel Settings tab and the Calibration dialog's steering dial so the two can no longer disagree. Old profiles migrate their existing value automatically on first load.
- **Faster Gyro/Touch-Aim Polling:** Raised the Sony HID poll rate from 10ms (100Hz) to 4ms (250Hz) to match the main DirectInput poll loop, reducing input lag on Gyro-Aim and Touch-Aim.
- **Consistent Dropdown Styling:** Added a baseline style for every `ComboBox` in the app (min-width, corner radius, text trimming) and filled in missing ellipsis-trimming/width on the Channel, Player/Wheel Slot, device-picker, and Gyro/Touch-Aim dropdowns, so long labels no longer overlap or clip at normal window widths.

### 🛠️ Fixed
- **Re-Bind Cancel No Longer Blanked the Label:** Canceling an in-progress re-bind of an already-bound key used to permanently show "Click to Bind" even though the underlying binding was untouched; canceling now correctly restores the real label.
- **New Bindings Landing in the Wrong Section:** "+ New Binding" used to guess an already-connected real device as the source, which could silently place a brand-new binding under an unrelated device's group (sometimes in the wrong section entirely) instead of leaving it unbound for Click-to-Bind.
- **HidHide Cloak Toggle Needed a Manual Re-Enable to Take Effect:** Checking/unchecking an individual device in the HidHide dialog only edited the driver's blocklist, never its live enforcement flag — so a toggle silently did nothing until the user manually disabled and re-enabled the master "Cloaking Active" switch. Per-device toggles (single, select-all/deselect-all, and dialog-close sync) now force the driver to re-apply immediately.
- **Wheel Rotation Dial Could Disagree With Wheel Settings:** The Calibration dialog's steering dial read a separate, disconnected rotation-degrees value from the one set on the Wheel Settings tab, so the visual could show the wrong lock-to-lock range for the wheel's actual configured rotation.
- **Auto-Bind Wizard Couldn't Target Wheel Slots Beyond 4:** Applying a DirectInput Wheel preset always wrote to whichever of the 4 Xbox Player slots was selected, so a wheel-mode preset could never land on Wheel Slots 5–8.

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