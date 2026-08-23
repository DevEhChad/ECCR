# Changelog

All notable changes to **EhChadsControllerRemapper (ECCR)** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [1.0.0] - 2026-08-23

### Added
* DirectInput polling engine using SharpDX with dedicated background worker threads.
* Simultaneous dual-engine feeder routing:
  * DirectInput virtual wheel routing via native `vJoy` P/Invoke.
  * Virtual Xbox 360 controller emulation via `ViGEmBus`.
* Per-device mapping groups with collapsible UI panels and bulk target device modifiers.
* Device preset discovery and Auto-Bind wizard for Moza, Logitech, PlayStation, and generic controllers.
* Real-time deadzone sliders, axis range calibration dialog, and signal inversion.
* HidHide cloaking and application exemption whitelist management interface.
* System tray integration with background execution, start on Windows boot, and close-to-tray handling.
* In-app update manager backed by Velopack and GitHub Releases.
* Application branding, iconography, and native vector glyph badges.