# PowerGauge

System tray utility for showing battery % for supported Razer wireless mice.

## Supported Devices

- `Razer Viper Ultimate` (Wired & 2.4 GHz Wireless)
- `Razer Naga V2 HyperSpeed` (2.4 GHz HyperSpeed Wireless receiver)

## Features

- Supports Windows and macOS
- Shows battery level, charging status, and connection status
- When multiple supported mice are detected, the tray icon displays the lowest battery % while the tray menu lists all detected devices

## Limitations

- Charging detection is based on observed device responses and may need refinement for docked charging.
- AA-powered mice (`Razer Naga V2 HyperSpeed`) estimate battery % from cell voltage calibrated to 1.5V alkaline batteries; 1.5V lithium cells hold a flat voltage plateau for most of their life before dropping near empty.
- Bluetooth LE mode and Linux are not implemented yet.
