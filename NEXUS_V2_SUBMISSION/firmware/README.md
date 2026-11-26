# NEXUS V2 firmware (rev B, untested)

ESP-IDF project for the ESP32-P4 on the NEXUS V2 board.

| Input | Sends to the computer |
|---|---|
| Keys 1-6 | Keyboard F13-F18 (the companion app maps them to any shortcut) |
| Encoder clockwise / counter-clockwise | Volume Up / Volume Down |
| Encoder push | Mute toggle |

## Build and flash
```
idf.py set-target esp32p4
idf.py build
idf.py -p <port> flash monitor
```
Flash and logs go over USB-Serial-JTAG on header J3 (pins 5/6 = D-/D+, pin 2 = GND).
To force download mode, hold BOOT (SW7) and tap RESET (SW8).

## Status / next steps
- Untested: no board has been built yet.
- Set `COUNTS_PER_STEP` after counting the FlyRobo encoder's detents.
- Next: MIPI-DSI display (esp_lcd + waveshare/esp_lcd_ili9881c) with an LVGL UI (stats, modes, notifications), plus a USB CDC channel for the desktop companion app.
