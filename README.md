# LcusRelay (Tray App) - Lamp/Relay Automation for Windows Sessions

A small C# application built on .NET 8 that can control a USB board featuring a CH340 chip, a relay, and a minor hardware modification—through multiple trigger types: Windows session events, hotkeys, cron-based schedules, and process/state signals.
It is designed to switch the 5V output power on and off.

This repository evolved from the original PowerShell script `scripts/LcusRelay.ps1` into a **Windows tray app** (background program with an icon near the clock) with a minimal UI and an **"IF event THEN action"** engine.

## What It Does (MVP)
- Runs in the background (tray icon).
- Controls an LCUS-1 relay over serial (protocol `A0 <addr> <cmd> <checksum>`, same as the PS script).
- Main triggers:
  - `session:logon`  -> Turn Off (default)
  - `session:logoff` -> Turn Off (default)
  - `session:lock` / `session:unlock` (configurable)
  - Cron schedules (optional)
  - Global hotkeys (e.g. `Ctrl+Alt+L` for Toggle)
  - Process-based "signals" (e.g. Teams/Zoom): `signal:meeting:on` / `signal:meeting:off`
- Configuration in JSON at `%APPDATA%\LcusRelay\config.json`
- Quick menu:
  - Turn On / Turn Off / Toggle
  - Edit config
  - Reload config
  - Settings (minimal)
  - Exit

## Build & Run (Windows)
Requirements: **.NET 8 SDK**.

```bash
dotnet build .\LcusRelay.sln -c Release
dotnet run --project .\src\LcusRelay.Tray\LcusRelay.Tray.csproj
```

## Where Is the Config?
At first run it is generated here:
- `%APPDATA%\LcusRelay\config.json`

## Auto Update (GitHub Releases)
The app can check GitHub Releases on startup and, after user approval, download and run the installer.
See `docs/CONFIG.md` for the `update` section.

## "Start with Windows" Note
In Settings you can enable "Start with Windows". It uses the registry key
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run` (current user only).

## Roadmap (next steps)
- UI to edit rules/hotkeys without touching JSON
- MSIX + WinGet
- Richer telemetry/logging (OpenTelemetry)
- "Webcam in use" signal (privacy & permissions considerations)

---

## Structure
- `src/LcusRelay.Core` - core engine (actions/config/serial driver)
- `src/LcusRelay.Tray` - WinForms tray app (Windows session triggers, hotkeys, schedules, process signals, UI)
- `scripts/` - original PowerShell script
