# LcusRelay Full Guide (Software + Circuit)

This guide explains how the software works and how to build the relay circuit shown in the attached photos (USB relay module with a Songle `SRD-05VDC-SL-C` relay and a 3‑screw terminal block).

## Safety First
- Mains voltage (110/230V AC) can be lethal.
- Always unplug power before wiring.
- Use an enclosure and insulated terminals.
- If you are not confident, ask a qualified electrician.

## 1) How the Software Works

### Core Concepts
- The tray app runs in the background and listens for events (Windows session, hotkeys, schedules, software signals).
- Each event becomes a `trigger` string (e.g. `session:lock`, `hotkey:toggle`).
- Triggers are matched against `rules` in `config.json`.
- Rules execute a list of actions (`relay`, `blink`, `run`, `webhook`, `delay`).

### Triggers You Can Use
- Session: `session:logon`, `session:logoff`, `session:lock`, `session:unlock`
- Hotkeys: `hotkey:{name}`
- Schedules: `schedule:{name}`
- Signals: `signal:meeting:on/off`, `signal:rdp:on/off`, `signal:{name}:on/off`

### Actions You Can Use
- `relay` with `state: On|Off|Toggle`
- `blink` for a timed ON/OFF sequence
- `run` to execute a process
- `webhook` to call an HTTP endpoint
- `delay` for waiting between actions

### Series Guard (Prevent Auto Events from Overriding Manual State)
You can define:
- `series`: a label for the origin of a rule (examples: `manual`, `session`, `schedule`)
- `allowWhenLastSeries`: if present, the rule runs only if the **last relay state change** came from one of those series

This is what makes behavior like this possible:
- If you manually turn the relay off with `Ctrl+Alt+L`, a later `session:unlock` won't force it on.
- Schedule-based rules can still be configured to always apply if you leave `allowWhenLastSeries` empty.

### Where State Is Saved
The last relay change is saved to:
- `%APPDATA%\\LcusRelay\\state.json`

This means the series logic persists across logout/login.

---

## 2) Software Setup

### Install and Run
- Build with `.NET 8 SDK` and run the tray app.
- On first run, it creates `%APPDATA%\\LcusRelay\\config.json`.

### Quick Test
- Right-click the tray icon.
- Use `Turn On` / `Turn Off` / `Toggle`.

### Basic Example (Session Rules + Manual Hotkey)
```json
{
  "rules": [
    { "trigger": "session:lock", "series": "session",
      "actions": [{ "type": "relay", "state": "Off" }] },
    { "trigger": "session:unlock", "series": "session",
      "allowWhenLastSeries": ["session"],
      "actions": [{ "type": "relay", "state": "On" }] }
  ],
  "hotkeys": [
    { "name": "toggle", "modifiers": ["Control","Alt"], "key": "L",
      "series": "manual",
      "actions": [{ "type": "relay", "state": "Toggle" }] }
  ]
}
```

---

## 3) Hardware: USB Relay Module (as in the Photos)

### What You Have in the Photos
- A 1‑channel USB relay board.
- Songle relay labeled `SRD-05VDC-SL-C`.
- A 3‑screw terminal block for the relay contacts.
- USB connector for power + serial control.

The relay is **5V coil**, controlled by the board. The terminal block is the **isolated switch**.

### Terminal Block Pins
Most 1‑channel modules use this order:
- **COM**: Common
- **NO**: Normally Open (connected to COM only when relay is ON)
- **NC**: Normally Closed (connected to COM when relay is OFF)

If the board is not labeled, use a multimeter (continuity) to confirm.

### Typical AC Lamp Wiring
- Phase/Live (L) -> **COM**
- **NO** -> Lamp phase
- Neutral (N) -> Lamp neutral directly
- Earth -> Lamp earth directly (if present)

If you want the lamp ON when the relay is OFF, use **NC** instead of **NO**.

### Typical Low‑Voltage DC Wiring
You can also switch low‑voltage DC:
- Positive -> **COM**
- **NO** -> Positive input of the load
- Negative -> goes directly to the load

### Notes About the Photos
The photos show:
- The Songle relay and the 3‑screw terminal block (front view).
- Soldered wires on the back side of the board (you can either solder directly or use the screw terminals).
- The compact USB relay module assembled and ready to plug in.

---

## 4) Power and Ratings

The relay label typically states:
- `10A 250VAC`
- `10A 30VDC`

Stay well below these limits for safety and long‑term reliability.

---

## 5) Troubleshooting

### The relay does not switch
- Check the COM port in `config.json`.
- If auto‑detect is on, confirm the device appears as CH340.
- Verify the USB cable and driver.

### It switches but the lamp does not turn on
- Verify you used **COM + NO** (not NC).
- Check that the live wire goes through the relay.
- Confirm the lamp is functional.

### It switches at the wrong times
- Review `rules`, `series`, and `allowWhenLastSeries`.
- Check the tray log at `%APPDATA%\\LcusRelay\\lcusrelay.log`.

---

## 6) Recommended Enclosure
- Use a closed plastic or metal box.
- Add strain relief for cables.
- Keep mains wiring insulated and separated from the USB board.
