# Notes on the LCUS-1 Relay and CH340

The original PowerShell script sends this serial packet:

- Start: `0xA0`
- Address: `1..255`
- Cmd: `0x01` (ON) / `0x00` (OFF)
- Checksum: `(start + addr + cmd) & 0xFF`

The tray app uses the same logic in `LcusSerialRelayController`.

## CH340 Auto-detect
If `serial.port` is empty and `serial.autoDetectCh340=true`, the tray app tries to find a device where:
- Friendly name contains `CH340` / `USB-SERIAL` / `USB Serial`
- or PNPDeviceID contains `VID_1A86&PID_7523`

Then it extracts `(COMx)` from the device name.

If not found, set `COMx` manually in `config.json`.
