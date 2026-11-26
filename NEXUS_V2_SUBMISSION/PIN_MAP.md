# NEXUS V2 – controls and ESP32-P4 pin map

## Controls
| Ref | Function | ESP32-P4 pin | Package pin | Pull-up | Active level |
|---|---|---|---|---|---|
| SW1 | Programmable shortcut 1 (top-left) | GPIO23 | 25 | R16 10 kΩ to 3V3 (+ internal) | LOW when pressed |
| SW2 | Programmable shortcut 2 (top-right) | GPIO21 | 23 | R17 10 kΩ | LOW |
| SW3 | Programmable shortcut 3 (middle-left) | GPIO19 | 20 | R18 10 kΩ | LOW |
| SW4 | Programmable shortcut 4 (middle-right) | GPIO17 | 18 | R19 10 kΩ | LOW |
| SW5 | Programmable shortcut 5 (bottom-left) | GPIO15 | 16 | R20 10 kΩ | LOW |
| SW6 | Programmable shortcut 6 (bottom-right) | GPIO13 | 14 | R21 10 kΩ | LOW |
| ENC1 A | Encoder phase A (volume) | GPIO1 | 1 | R22 10 kΩ + C14 100 nF | quadrature |
| ENC1 B | Encoder phase B (volume) | GPIO2 | 2 | R23 10 kΩ + C15 100 nF | quadrature |
| ENC1 SW | Encoder push → mute toggle | GPIO3 | 3 | R24 10 kΩ + C16 100 nF | LOW when pressed |

Board: 54 × 118 mm strip that sits BESIDE the display (right side of the slab).
Keys: 2 columns × 3 rows at 19.0 mm pitch (PCB x = 20.5 / 39.5 mm, y = 12 / 31 / 50 mm), upper part of the strip.
Encoder shaft centred between the columns at PCB (30.0, 74.0) mm, 24 mm knob, ≈ 3 mm clear of the bottom keycaps.

## Firmware notes
- Buttons: debounce in firmware (e.g. 5–10 ms stable-state filter); internal pull-ups may stay enabled.
- Encoder: decode A/B as quadrature (ESP32-P4 PCNT peripheral, or a state-machine on GPIO interrupts). Clockwise → volume up, counter-clockwise → volume down.
  The detent count (pulses per detent) of the FlyRobo EC11 is NOT documented – count it on the real part and set the counts-per-detent divider in firmware.
  The R22–R24/C14–C16 RC (≈1 ms) adds hardware filtering; keep firmware debounce on the push switch.
- Shortcuts / volume / mute are sent to the computer by the desktop companion app over USB (HS OTG on the USB-C port).

## Other assigned pins
| Signal | GPIO / pin |
|---|---|
| Touch I2C SCL / SDA (display, + backlight reg 0x96 @ 0x45) | GPIO28 / GPIO29 |
| User LED | GPIO5 |
| UART0 TX / RX | GPIO37 / GPIO38 – NOT routed (board space); flash + logs via USB-Serial-JTAG on J3 |
| BOOT | GPIO35 (strap) |
| USB-Serial-JTAG D- / D+ (J3 pins 5/6 – flashing + logs) | GPIO24 / GPIO25 |
| USB HS OTG (USB-C) | dedicated USB_DM / USB_DP |
| DSI 2-lane + clock | dedicated MIPI pins |
| QSPI flash | dedicated flash pins |

## Part verification status
| Part | Status |
|---|---|
| C&K PTS526 SPG15 SMTR2 LFS (SW1–SW8) | VERIFIED against C&K datasheet page B-35 (G leads, with ground pin): 320 gf, 1.5 mm, pins 1-2 / 3-4 commons, 5-6 frame ground. |
| FlyRobo EC11 15 mm half-shaft, SKU 3370 (ENC1) | Footprint matches the ALPS EC11E reference pattern exactly (round 2.6 mm tab holes for clone tolerance). FlyRobo publishes no datasheet -> confirm with 5 caliper measurements (see manufacturing/NOT_FOR_FABRICATION.txt). |

## Debug / flash header J3 (fitted from the BACK, reachable through the back-cover window)
| J3 pin | Signal |
|---|---|
| 1 | 3V3 |
| 2 | GND |
| 3, 4 | not connected (UART dropped) |
| 5 / 6 | USB-Serial-JTAG D- / D+ (wire to a USB cable: D-, D+, GND) |
| 7 | CHIP_PU (reset) |
| 8 | BOOT (GPIO35) |

BOOT (SW7) and RESET (SW8) sit on the back of the board and are pressed through pin-holes in the back cover.
