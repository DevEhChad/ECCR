# EhChads Controller Remapper (ECCR)

<p align="center">
  <img src="Assets/ECCR-logo.png" alt="ECCR Logo" width="128" height="128" />
</p>

<p align="center">
  <strong>A lightweight, high-performance DirectInput remapper and virtual controller emulator built with Avalonia (.NET 10).</strong>
</p>

---

## 🎮 Overview

**EhChads Controller Remapper (ECCR)** is designed to bridge the gap between unsupported DirectInput devices (flight sticks, racing wheels, pedals, shifters, button boxes, and legacy gamepads) and modern games that exclusively support XInput (Xbox) or DualShock 4 controllers.

ECCR feeds virtual controller inputs with near-zero latency, manages input conflicts via HidHide, and offers quick visual calibration tools in a clean, modern dark UI.

---

## ✨ Features

- **DirectInput to Virtual Controller Emulation**
    - Maps any detected DirectInput joystick, steering wheel, pedal set, or custom USB controller to a virtual **Xbox 360** or **DualShock 4** target controller using the ViGEmBus client.
    - Multi-controller slot targeting (Player 1 through Player 4).

- **In-App Driver Management**
    - **ViGEmBus:** Integrated detection, health monitoring, and in-app automated installer/repair workflow.
    - **HidHide Integration:** Detects, installs, and manages HidHide to block "double-input" ghosting by hiding physical DirectInput devices from games while ECCR remaps them.

- **Auto-Bind Wizard & Calibration**
    - Step-by-step interactive wizard for rapid button and axis assignment.
    - Fine-grained axis calibration with deadzone controls, sensitivity curves, and axis inversion.

- **Custom Profiles & Presets**
    - Save, load, and switch custom button/axis mapping presets per device.
    - Clean local JSON storage structure for easy backup and sharing.

- **Background Operation & Tray Support**
    - Minimizes cleanly to the Windows System Tray with quick restore and shutdown actions.
    - Optional "Run on Windows Startup" background toggle.

- **Seamless Auto-Updates**
    - Powered by Velopack for instant, delta-compressed background updates directly from GitHub releases.

---

## 🛠️ Tech Stack & Dependencies

- **Framework:** .NET 10.0 (Windows x64)
- **UI Architecture:** [Avalonia UI 11.2](https://avaloniaui.net/) + [CommunityToolkit.Mvvm](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/)
- **DirectInput Engine:** [SharpDX.DirectInput](http://sharpdx.org/)
- **Virtual Controllers:** [Nefarius.ViGEm.Client](https://github.com/nefarius/ViGEmClient)
- **Device Hiding:** [Nefarius.Drivers.HidHide](https://github.com/nefarius/HidHide)
- **Installer & Packaging:** [Velopack](https://velopack.io/)

---

## 🚀 Getting Started

### Prerequisites
1. Windows 10/11 (64-bit).
2. [ViGEmBus Driver](https://github.com/nefarius/ViGEmBus/releases) *(Can be installed directly within ECCR's Driver Settings dialog)*.
3. [HidHide Driver](https://github.com/nefarius/HidHide/releases) *(Optional, for hiding physical hardware to prevent double-inputs)*.

### Installation
1. Download the latest `ECCR-Setup.exe` from the [Releases](https://github.com/DevEhChad/ECCR/releases) page.
2. Run the installer. ECCR will launch automatically and install into your user profile.

---

## 🏗️ Building from Source

```powershell
# Clone repository
git clone [https://github.com/DevEhChad/ECCR.git](https://github.com/DevEhChad/ECCR.git)
cd ECCR

# Restore dependencies
dotnet restore

# Run in development mode
dotnet run --project ECCR.csproj

# Publish release binary
dotnet publish -c Release -r win-x64 --no-self-contained -o ./publish