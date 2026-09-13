# EmuWorks

**NumWorks N0110 calculator emulator.** A **microcontroller-level** emulator: it runs the actual ARM Cortex-M7 machine code of the NumWorks firmware on an emulated STM32F730, along with the ST7789V screen on the FMC bus, the 9×6 keyboard matrix, the battery ADC, and the CRC32 unit. Not a UI reimplementation — the original firmware, exactly as it runs on the calculator.

All in a single window: Renode is launched headlessly, and its screen is forwarded to the application via a local socket.

## ⚠️ This repository contains no firmware

Epsilon is published by NumWorks under the **Creative Commons BY-NC-SA 4.0** license (Attribution, NonCommercial, ShareAlike). Redistributing its binaries would impose these conditions on this entire repository, so we do not distribute any — not Epsilon, not Omega, nor the example scripts provided with the calculator.

**You compile your own**, from the official sources, using the provided GitHub workflow: `renode/build-firmware-n0110.yml`. It takes two minutes, and it produces exactly the two images the emulator loads.

The code in this repository — peripheral models, tools, application — is original work licensed under MIT. It describes the **hardware** (STM32F730 registers, display protocol, keyboard wiring), not the firmware.

## Prerequisites

| | |
|---|---|
| [Renode](https://renode.io) 1.16 | `winget install Renode.Renode` |
| [Node.js](https://nodejs.org) | `winget install OpenJS.NodeJS.LTS` — used for script synchronization |
| .NET Desktop Runtime 8 | for the application; or `dotnet publish` from `app/` |

> ⚠️ **Install the folder in a path without spaces**, for example
> `C:\EmuWorks\`. Renode 1.16 fails silently ("Could not tokenize")
> on paths containing spaces — this is the project's main pitfall. The exact
> location is up to you: nothing is hardcoded, the application passes the project
> root to Renode via the `EMUWORKS_BASE` environment variable.

## Getting a firmware

1. Fork [numworks/epsilon](https://github.com/numworks/epsilon).
2. Copy `renode/build-firmware-n0110.yml` into `.github/workflows/` on your fork's default branch.
3. Actions → **Firmware N0110 (Renode)** → Run workflow.
4. Download the artifact and place the two images into `firmwares//internal.bin` and `external.bin`.

The artifact contains two variants:

| | |
|---|---|
| `epsilon.internal.bin` / `epsilon.external.bin` | boots directly to the home screen |
| `epsilon.onboarding.*` | includes the first-use onboarding wizard |

The difference is just a build target (`make epsilon.dfu` vs `epsilon.onboarding.dfu`), not a patch. The first one is preferred here: the emulator always performs a cold boot, so the language wizard would reappear on every launch.

Since a fork does not inherit tags, the workflow takes a `repository` field in addition to `ref`: leave it as `numworks/epsilon` to build upstream, or enter your fork and branch to compile your own modifications.

## Usage

Launch `EmuWorks.exe`. A single window offers:

- firmware selection from those placed in `firmwares/`;
- Python script management: add, delete, or open them in your editor;
- the calculator screen, controlled by your PC keyboard — scaled by an **integer** factor (2×, 3×…) to remain crisp, and centered in a frame; resizing the window scales the calculator;
- automatic saving upon exit.

### The `rom/` folder **is** the calculator

    rom/
    ├── internal.bin      internal flash → 0x08000000
    ├── external.bin      external flash → 0x90000000
    ├── scripts/*.py      Python scripts, as real text files
    └── serie.txt         the serial number displayed by the calculator

On startup, the `.py` files from the folder are injected into the calculator; on exit, what the calculator contains is written back to the folder. **The folder is the authority on startup, the calculator is the authority on exit.** This means you can edit your scripts in your code editor, version them, and share them — which the real calculator's USB connection does not allow.

The storage address is dynamically found at each boot by searching for the magic number `0xEE0BDDBA` in SRAM: it differs from one firmware to another, and the folder adapts without any reconfiguration.

## How it works

    EmuWorks.exe ──stdin──▶ Renode (headless)
         ▲                    │
         │                    ├── numworks_n0110.repl   the board
         └──socket 3555───────┤   NumWorksDisplay.cs    ST7789V on FMC bus
            RGB565 frame      │   NumWorksKeyboard.cs   9×6 matrix
                              │   NumWorksAdc.cs        battery
                              │   NumWorksCrc.cs        hardware CRC32
                              └── MemFile.cs            memory ↔ files

Renode normally opens its own windows. Here it runs with `--disable-xwt`, driven via standard input: key presses become monitor commands, and `NumWorksDisplay` serves the framebuffer over a local socket — a request byte, and a 320×240 RGB565 frame in response.

The complete technical documentation — still-stubbed registers, how to diagnose a firmware that fails to boot, the two screen orientations — is in [`renode/README.md`](renode/README.md).

## License

The content of this repository — Renode peripheral models, tools, application, documentation — is licensed under the **MIT** license, see [LICENSE](LICENSE).

It does not cover, and this repository does not distribute:

| | |
|---|---|
| [Epsilon](https://github.com/numworks/epsilon), the NumWorks firmware | © NumWorks — CC BY-NC-SA 4.0 |
| Omega, its community fork | same license |
| [Renode](https://github.com/renode/renode), the emulation infrastructure | © Antmicro — MIT |

The peripheral models describe the hardware of the STM32F730 and the N0110 board: registers, ST7789V display protocol, keyboard matrix wiring. They are written based on component documentation and observation of the firmware's behavior, not derived from its code.
