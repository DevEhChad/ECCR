<div align="center">
  <img src="Assets/ECCR-logo.png" alt="ECCR Logo" width="140" height="140" />

# EhChads Controller Remapper (ECCR)

**A lightweight, high-performance DirectInput remapper and virtual controller emulator built with .NET 10 and Avalonia UI.**

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011%20x64-0078D6.svg)](https://microsoft.com/windows)
[![Framework](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![UI](https://img.shields.io/badge/UI-Avalonia%2011.2-8A2BE2.svg)](https://avaloniaui.net/)
[![Updater](https://img.shields.io/badge/Updates-Velopack-teal.svg)](https://velopack.io/)

</div>

---

## 🎮 Overview

**EhChads Controller Remapper (ECCR)** bridges the gap between unsupported DirectInput hardware (steering wheels, pedal sets, gated shifters, handbrakes, flight sticks, and legacy gamepads) and modern PC games that exclusively support XInput (Xbox), DualShock 4, or DirectInput wheel devices.

ECCR translates raw peripheral inputs into responsive virtual controller targets with near-zero latency, eliminates "double-input" ghosting via automated HidHide integration, and provides visual calibration and mapping wizards in a clean dark UI. Physical rigs made up of multiple separate devices (e.g. a wheelbase plus a standalone pedal set) can be combined into a single virtual Xbox 360 controller or a single virtual DirectInput wheel, routed independently and simultaneously via the app's composite output engine.

---

## ✨ Key Features

* **🕹️ DirectInput to Virtual Controller Emulation**  
  Map any detected USB device, wheel, or button box to a virtual **Xbox 360** or **DualShock 4** controller powered by **ViGEmBus** with multi-slot targeting (Player 1 through Player 4).

* **🏎️ Virtual DirectInput Wheel Output (vJoy)**  
  Map steering, throttle, brake, clutch, handbrake, all 7 forward gears + reverse, and up to 32 buttons to a real virtual **vJoy** DirectInput wheel device via `[Wheel]` mapping targets — for sim racing titles that expect a wheel rather than a gamepad.

* **🔗 Multi-Device Wheel Combining**  
  Combine separate physical peripherals (e.g. a Moza wheelbase and Logitech pedals) into a single virtual wheel or Xbox 360 target — each mapping routes independently to the correct output through the `CompositeFeederService`.

* **🛡️ Integrated HidHide Cloaking**  
  Built-in physical hardware isolation to prevent game conflict and double-input issues without needing external configuration utilities.

* **🪄 Auto-Bind Wizard & Visual Setup**  
  Interactive listen-to-bind step-by-step wizard for rapid axis and button assignment across all connected hardware.

* **⚡ Live Axis Calibration & Deadzones**  
  Interactive axis visualizers with customizable deadzone limits, sensitivity curves, and instant axis inversion toggles. Wheel and pedal hardware get a dedicated live rotating steering dial and color-coded pedal-press bars in the Calibration dialog.

* **💾 Custom Profiles & Device Presets**  
  Save, load, and manage custom input configurations stored in human-readable local JSON files.

* **🔄 Tray Integration & Background Updates**  
  Minimizes cleanly to the Windows System Tray with optional Windows startup launch and seamless, delta-compressed updates powered by **Velopack**.

---

## 🛠️ Required Drivers

| Driver | Purpose | ECCR Integration |
| :--- | :--- | :--- |
| **ViGEmBus** | Virtual Xbox 360 & DualShock 4 emulation | Automatic health check & 1-click in-app installer |
| **vJoy** | Virtual DirectInput wheel emulation | Automatic health check & 1-click in-app installer |
| **HidHide** | System-level physical device cloaking | Automatic health check & 1-click in-app installer |

---

## 🧰 Tech Stack

| Component | Technology |
| :--- | :--- |
| **Runtime & Language** | .NET 10.0 (C# 14) |
| **UI Framework** | [Avalonia UI 11.2](https://avaloniaui.net/) (Fluent Theme) |
| **Architecture Pattern** | [CommunityToolkit.Mvvm](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/) (MVVM) |
| **DirectInput Engine** | [SharpDX.DirectInput](http://sharpdx.org/) |
| **Virtual Gamepad Emulation** | [Nefarius.ViGEm.Client](https://github.com/nefarius/ViGEmClient) |
| **Virtual Wheel Emulation** | vJoy (`vJoyInterfaceWrap`/`vJoyInterface`, vendored) |
| **Device Cloaking** | [Nefarius.Drivers.HidHide](https://github.com/nefarius/HidHide) |
| **Packaging & Updates** | [Velopack](https://velopack.io/) |

---

## 🚀 Getting Started

### Installation
1. Download `ECCR-Setup.exe` from the [Releases](https://github.com/DevEhChad/ECCR/releases) page.
2. Run the installer. ECCR will install directly into your user profile and launch automatically.
3. If prompted by the top warning banner, click to install or repair missing system drivers.

---

## 🏗️ Building From Source

### Prerequisites
* [.NET 10.0 SDK](https://dotnet.microsoft.com/download)
* Visual Studio 2026 or JetBrains Rider

```powershell
# 1. Clone the repository
git clone [https://github.com/DevEhChad/ECCR.git](https://github.com/DevEhChad/ECCR.git)
cd ECCR

# 2. Restore NuGet dependencies
dotnet restore

# 3. Run development build
dotnet run --project ECCR.csproj

# 4. Publish standalone release
dotnet publish -c Release -r win-x64 --no-self-contained -o ./publish