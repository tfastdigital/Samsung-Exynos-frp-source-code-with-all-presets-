# Samsung Exynos FRP Source Code with All Presets

**Powered by Tfast Digital**  |  [tfastdigital.com](https://tfastdigital.com)

This repository contains the full source code for the Samsung Exynos FRP removal tool, including the Windows Forms application, embedded Exynos engine integration, and all included device preset JSON configurations.

## Overview

`SamsungExynos` is a .NET 8.0 Windows Forms tool designed to remove FRP/Zero Knox protection from Samsung Exynos devices. The project includes support for Samsung Exynos chipsets from Exynos 7570 through Exynos 2500 and ships with the complete preset library for device-specific exploit workflows.

## Key Features

- Windows Forms UI for easy FRP reset and device interaction
- Automatic COM port scan for Samsung devices
- Dynamic preset loading from a remote config server
- Local preset fallback and manual preset selection
- Targeted support for Exynos `dpolicy_extract` and `dpolicy_integrity` exploit flows
- Embedded secure payload extraction to the local temp vault
- Log output and progress tracking for every operation

## Included Presets

This repo contains the following preset files in `presets/`:

- `exynos1280_dpolicy_extract.json`
- `exynos1330_dpolicy_extract.json`
- `exynos1380_dpolicy_extract.json`
- `exynos1480_dpolicy_extract.json`
- `exynos1580_dpolicy_extract.json`
- `exynos2100_dpolicy_extract.json`
- `exynos2200_dpolicy_extract.json`
- `exynos2400_dpolicy_extract.json`
- `exynos2500_dpolicy_extract.json`
- `exynos7570_dpolicy_integrity.json`
- `exynos7870_dpolicy_integrity.json`
- `exynos7880_dpolicy_integrity.json`
- `exynos7884_dpolicy_extract.json`
- `exynos7884_dpolicy_extract_no_payload.json`
- `exynos7885_dpolicy_extract.json`
- `exynos7904_dpolicy_integrity.json`
- `exynos7904_dpolicy_integrity_185478.json`
- `exynos850_dpolicy_extract.json`
- `exynos850_dpolicy_extract_no_payload.json`
- `exynos8890_dpolicy_integrity.json`
- `exynos8895_dpolicy_integrity.json`
- `exynos9610_dpolicy_integrity.json`
- `exynos9610_dpolicy_integrity_185479.json`
- `exynos9610_dpolicy_integrity_185480.json`
- `exynos9610_dpolicy_integrity_185481.json`
- `exynos9610_dpolicy_integrity_185482.json`
- `exynos9610_dpolicy_integrity_185521.json`
- `exynos9610_dpolicy_integrity_185721.json`
- `exynos9611_dpolicy_integrity.json`
- `exynos980_dpolicy_integrity.json`
- `exynos9810_dpolicy_integrity.json`
- `exynos9820_dpolicy_integrity.json`
- `exynos9820_dpolicy_integrity_185459.json`
- `exynos9820_dpolicy_integrity_185461.json`
- `exynos9820_dpolicy_integrity_185567.json`
- `exynos9820_dpolicy_integrity_185594.json`
- `exynos9820_dpolicy_integrity_185722.json`
- `exynos9820_dpolicy_integrity_185724.json`
- `exynos9825_dpolicy_integrity.json`
- `exynos990_dpolicy_integrity.json`
- `exynos990_dpolicy_integrity_185566.json`
- `exynos990_dpolicy_integrity_185592.json`
- `exynos990_dpolicy_integrity_185593.json`
- `exynos990_dpolicy_integrity_185595.json`
- `exynos990_dpolicy_integrity_185723.json`
- `exynos990_dpolicy_integrity_185725.json`
- `galaxy_a12_dpolicy_extract.json`
- `generic_dpolicy_integrity_185720.json`

## Supported Devices and Chipsets

This tool currently supports the following Samsung Exynos families:

- Exynos 850
- Exynos 7570, 7870, 7880, 7884, 7885, 7904
- Exynos 8890, 8895
- Exynos 9610, 9611
- Exynos 980, 9810, 9820, 9825
- Exynos 990
- Exynos 1280, 1330, 1380, 1480, 1580
- Exynos 2100, 2200, 2400, 2500

## Build Requirements

- Windows 10 / 11
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022 / 2023 or compatible IDE with Windows Forms support
- `System.IO.Ports` NuGet package (already referenced in the project)

## How to Build

1. Open `SamsungExynos.slnx` in Visual Studio.
2. Restore NuGet packages.
3. Build the solution targeting `net8.0-windows`.
4. Run the application.

## How to Use

1. Connect the Samsung device to Windows.
2. Launch the app.
3. Select a detected COM port.
4. Open the preset dropdown to load configs from the remote server.
5. Choose the preset matching your device chipset.
6. Click `Reset FRP` to run the removal workflow.

## How It Works

- The Windows Forms UI is implemented in `Form1.cs`.
- The application initializes `ExynosFlashEngine` and extracts a secure payload bundle from the embedded resource store.
- When a preset is selected, the app downloads the matching JSON preset from the configured server and writes it to a temporary secure working directory.
- The embedded `ExynosCli.exe` is executed with the selected preset, COM port, and generated session token.
- Logs are shown live in the UI and progress is updated from the Exynos CLI output.

## Local Preset Handling

The app supports both remote preset download and local preset loading.

- Local preset files are stored under `presets/`.
- Remote preset download is controlled by `ConfigServerUrl` in `Form1.cs`.
- If you want to use your own preset server, update `ConfigServerUrl` to your working host.

## Repository Layout

- `SamsungExynos.slnx` — Visual Studio solution file
- `SamsungExynos.csproj` — .NET Windows Forms project
- `Program.cs` — app entrypoint
- `Form1.cs` — UI logic and event handling
- `ExynosFlashEngine.cs` — Exynos device detection, preset loading, and flash orchestration
- `presets/` — device-specific JSON configuration presets
- `Properties/` — project resources and settings
- `Resources/` — embedded resources used by the app

## Notes and Disclaimer

This project is intended for authorized device servicing, research, and recovery. Use at your own risk. Ensure you have proper authorization before working on locked devices.

## Next Steps for Upload

1. Initialize the repository locally using Git.
2. Create the remote GitHub repository at:
   `https://github.com/tfastdigital/Samsung-Exynos-frp-source-code-with-all-presets-`
3. Push your commits to GitHub.

---

If you want, I can also add a `LICENSE` file and a `CONTRIBUTING.md` for this repository.