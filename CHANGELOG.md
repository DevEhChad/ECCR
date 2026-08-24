# Changelog

All notable changes to **EhChadsControllerRemapper (ECCR)** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [1.0.4] - 2026-08-23

### Added
- **Interactive Update Dialog**: Added an update prompt modal allowing users to confirm or postpone incoming update downloads.
- **Post-Update Welcome Modal**: Integrated an automated launch dialog confirming successful installation and version changes upon application restart.
- **Dynamic Version Resolution**: Implemented runtime manifest and project metadata resolution for accurate version reflection across builds.

### Changed
- **Minimalist UI Version Scoping**: Cleaned up version indicators across the UI—removed redundant version chips from the native window title bar, header banner, and main window footer, consolidating version information cleanly to the bottom-left of the Settings window.

### Fixed
- **Velopack 1.x Source Integration**: Configured `GithubSource` using `Velopack.Sources` to ensure seamless release package discovery without external locator dependencies.
- **Preset Model Source Generation**: Decoupled `PresetBindingItem` from service wrappers to eliminate MVVM source generator compilation collisions.

---

## [1.0.3] - 2026-08-23

### Fixed
- **Velopack 1.x Lifecycle**: Restored non-blocking `VelopackApp.Build().Run()` initialization hook in `Program.cs` to enable smooth standalone installation and auto-updates.
- **GitHub Release Metadata**: Updated update source handler to query GitHub releases for `releases.win.json` update assets.
- **Application Teardown**: Ensured all background hooks, HidHide cloaks, and virtual feeder nodes terminate cleanly on app shutdown.

---

## [1.0.2] - 2026-08-23

### Added
- **Hardware-Specific Glyph Detection**: The physical input column now renders platform-accurate symbols (`✕`, `○`, `□`, `△` for Sony PlayStation controllers; `◎`, `⮝`, `⮟`, `⎊`, `⧈`, `1`–`7`/`R` for Sim-Rig hardware; `Ⓐ`, `Ⓑ`, `Ⓧ`, `Ⓨ` for Xbox/XInput gamepads).
- **Sim-Rig Multi-Device Combining**: Added direct support to aggregate discrete hardware devices (e.g., Moza wheelbases, Logitech pedals, Fanatec shifters) into a single virtual DirectInput wheel (`vJoy Target #1`).
- **Device Mode Toggle**: Added bulk toggles to switch entire device mapping groups between DirectInput Wheel mode and Virtual Xbox 360 Controller mode.
- **Clean Application Lifecycle Teardown**: Added `CleanupAndShutdown()` to automatically disconnect virtual Xbox/vJoy nodes, unacquire DirectInput joysticks, and disable HidHide global cloaking when ECCR exits.

### Fixed
- **Auto-Bind Wizard DirectInput Index Alignment**: Resolved a mapping discrepancy where PS5 DualSense face buttons were mapped to incorrect raw button offsets (`Buttons[0]` Square, `Buttons[1]` Cross, `Buttons[2]` Circle, `Buttons[3]` Triangle).
- **Virtual Controller Self-Mapping Loop**: DirectInput polling now filters out ViGEmBus (`0x045E / 0x028E`) and vJoy (`0x1234 / 0x0BE3`) hardware signatures to prevent virtual device input loops during remapping.
- **Listen-to-Bind Output Freezing**: Suspended active virtual feeding while in "Listening" mode so manual key detection does not trigger duplicate inputs.
- **In-Game Button Registration**: Fixed virtual report submission in `ViGEmFeederService` to ensure all Xbox 360 buttons (face keys, bumpers, stick clicks, and D-Pad) fire immediately in games.
- **Channel Dropdown Badge Precedence**: Corrected color and badge evaluation logic in `ButtonBadgeConverter` so Xbox badges (`Ⓐ`, `Ⓑ`, `Ⓧ`, `Ⓨ`) display on virtual target channels.

---

## [1.0.1] - 2026-08-15

### Added
- **HidHide Integration**: Added the embedded HidHide management dialog for cloaking physical devices and adding per-game executable whitelist exceptions.
- **System Integration**: Added options to run ECCR on Windows startup, minimize to system tray, and close to notification area.
- **Automatic Updates**: Integrated Velopack background update checks and one-click in-app updater.
- **Driver Diagnostics**: Added system dependency banner to detect and start missing `ViGEmBus`, `HidHide`, and `vJoy` services.

### Fixed
- Improved polling latency and thread management in high-frequency input loops.
- Fixed profile serialization issues when saving custom deadzones and inverted axes.

---

## [1.0.0] - 2026-08-01

### Added
- **Initial Release**: Core remapping engine for DirectInput joysticks, wheels, and gamepads.
- **Virtual Controllers**: Virtual Xbox 360 controller emulation via ViGEmBus and virtual wheel emulation via vJoy.
- **Profile Management**: JSON profile save/load system for application persistence.
- **Interactive Listening**: Click-to-bind hardware input capture with deadzone and calibration support.