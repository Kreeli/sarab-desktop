# Sarab Desktop

English | [简体中文](README.zh-CN.md)

**An independent Windows desktop GUI for [Sarab by TheMRVX](https://github.com/TheMRVX/sarab).** Sarab provides the virtual GNSS driver and core location functionality. This project adds the desktop controller, English/Chinese UI and installation guidance; it is not an official upstream release.

## Download and install

Get **`SarabDesktop-1.1.0-Setup-x64.exe`** from [Releases](https://github.com/Kreeli/sarab-desktop/releases). A portable ZIP is also available. Both contain the pinned, unmodified Sarab v1.0.0 driver and CLI; friends do not need development tools or a separate driver download.

1. Run Setup, choose English or Simplified Chinese, and open the app.
2. Open **Setup & repair**. It checks Windows architecture, Secure Boot, active test signing, the driver and location permissions.
3. The original driver is test-signed. If needed, read the [installation guide](docs/INSTALL.md), prepare Windows, restart manually, then return to install the driver. Secure Boot and disk protection need explicit attention; the app never changes firmware, decrypts disks or restarts automatically.
4. Allow location access manually in Windows Settings. Choose your own coordinates in Overview and select **Apply and verify**. A fresh driver installation keeps virtual location paused until you do this.

Requires an **x64 PC with Windows 10 1809+ or Windows 11**. ARM64 is not supported. Administrator approval is required for driver installation and location writes. Normal GUI use and logon startup run without elevation. This test-signed upstream driver cannot provide a zero-configuration installation on every stock Windows 11 PC.

## Features

- **English / Simplified Chinese**, switched instantly in Settings and saved automatically. Your location names and existing log entries are preserved.
- Coordinates, DMS, Google Maps coordinate links and optional manual address search. OpenStreetMap / Nominatim is the default; Google Geocoding requires your own API key.
- Apply/pause virtual location, altitude, accuracy and optional drift; independent Windows Geolocator verification instead of trusting a configuration switch.
- Favorites with import/export and undo removal, local logs, system tray and optional sign-in startup (off by default).
- A guided system check, pinned driver-package hashes, explicit installation steps and driver removal.

Your public IP is controlled separately, for example by a VPN. This controller affects the Windows location source used by Sarab; applications may also use their own IP, Wi-Fi or account data. A successful Windows reading does not prove every application's behavior.

## Data and updates

Settings and logs stay in `%LOCALAPPDATA%\SarabDesktop`. API keys use Windows DPAPI encryption for the current user and are excluded from logs and favorite exports. Address lookup contacts only the selected provider when you search; no silent provider fallback or autocomplete is used. Coordinate parsing is offline except when resolving short links.

The release has an unapplied example location and no personal favorites, keys, coordinates or logs from the development machine. Upgrading from 1.0 preserves your local settings. Closing the app normally hides it in the tray; exiting the controller keeps the driver active. **Uninstalling the GUI also keeps the driver.** Remove it explicitly in Setup & repair if desired; Windows may then return to real location sources. See the guide for certificate and boot-setting cleanup.

## Build

Install Visual Studio Build Tools with C++ tools and a Windows SDK including C++/WinRT. Build with PowerShell:

```powershell
.\Build.ps1
.\scripts\Package.ps1 -Iscc 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
```

`Build.ps1` finds VS automatically; use `-VisualStudio` for a custom installation. It downloads and verifies the fixed upstream archive from `dependencies.lock.json`, builds the native reader with static C++ runtime linkage, and compiles WPF against Windows' .NET Framework libraries without NuGet. `Package.ps1` uses Inno Setup 6.5+ to create the installer and portable ZIP. No SDK is needed on end-user PCs. See [contributing](CONTRIBUTING.md), [credits](CREDITS.md), [licenses](THIRD-PARTY-NOTICES.md) and the [upstream relationship](docs/UPSTREAM.md).

CI builds on Windows and runs tests that do not install drivers or modify boot settings. Run local diagnostics with `--self-test <report.json>`, `--render <folder> --language en-US`, or `--ui-smoke <folder>`. The UI smoke test uses isolated settings and reads the current Windows location; it requires a healthy, active driver for its integration checks.

## License and attribution

Desktop GUI and new supporting code: MIT, © 2026 Kreeli and contributors. **Bundled Sarab v1.0.0: MIT, © 2026 TheMRVX**, with its full license retained. The newer upstream main branch uses Apache-2.0 and is not bundled in this release. Keep [NOTICE](NOTICE) and the third-party license when redistributing. [Original Sarab project →](https://github.com/TheMRVX/sarab)
