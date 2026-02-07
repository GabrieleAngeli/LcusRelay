# Installation (Windows)

This document explains how to install the artifact produced by the GitHub Actions pipeline.

## 1) Download the Installer (Recommended)
From the GitHub Actions run:
- Open the workflow run.
- Download the artifact named `LcusRelay-Setup-win-x64`.

You will get an installer executable: `LcusRelay-Setup.exe`.

## 2) Install
- Run `LcusRelay-Setup.exe`.
- The app installs under `C:\Program Files\LcusRelay`.
- Windows "Programs and Features" will show an uninstall entry.
- The installer also places an uninstaller in the install folder (`unins*.exe`).

## 3) Run
Launch `LcusRelay` from the Start Menu, or run:
- `C:\Program Files\LcusRelay\LcusRelay.Tray.exe`

## 4) Configure
On first run the config is created at:
- `%APPDATA%\LcusRelay\config.json`

You can edit it from the tray menu:
- `Edit config`

---

## 1) Download the Artifact (Portable)
From the GitHub Actions run:
- Open the workflow run.
- Download the artifact named `LcusRelay-win-x64`.

You will get a zip file: `LcusRelay-win-x64.zip`.

## 2) Unzip (Portable)
Extract the zip to a folder, for example:
- `C:\Apps\LcusRelay\`

Inside you will find `LcusRelay.Tray.exe` and its supporting files.

## 3) Run (Portable)
Double-click `LcusRelay.Tray.exe`.
The tray icon will appear near the clock.

## 4) Configure (Portable)
On first run the config is created at:
- `%APPDATA%\LcusRelay\config.json`

You can edit it from the tray menu:
- `Edit config`

## 5) (Optional) Start with Windows
Open Settings from the tray menu and enable:
- `Start with Windows`

This sets a registry key under:
- `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`

## Uninstall (Installer)
1. Exit the tray app (right-click the tray icon -> `Exit`).
2. Use Windows "Programs and Features" to uninstall **LcusRelay**, or run the uninstaller:
   - `C:\Program Files\LcusRelay\unins*.exe`
3. Remove config and logs if you want a clean uninstall:
   - `%APPDATA%\LcusRelay\config.json`
   - `%APPDATA%\LcusRelay\lcusrelay.log`
   - `%APPDATA%\LcusRelay\state.json`
4. If you enabled "Start with Windows", disable it from the app Settings first, or remove the registry entry:
   - `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`

## Uninstall (Portable)
1. Exit the tray app (right-click the tray icon -> `Exit`).
2. Delete the install folder (e.g. `C:\Apps\LcusRelay\`).
3. Remove config and logs if you want a clean uninstall:
   - `%APPDATA%\LcusRelay\config.json`
   - `%APPDATA%\LcusRelay\lcusrelay.log`
   - `%APPDATA%\LcusRelay\state.json`
4. If you enabled "Start with Windows", disable it from the app Settings first, or remove the registry entry:
   - `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`

## Notes
- If Windows SmartScreen blocks the app, click `More info` -> `Run anyway`.
- The build is self-contained, so no .NET runtime installation is required.
