# EmuWorks

**NumWorks N0110 calculator emulator.** A **microcontroller-level** emulator: it runs the actual ARM Cortex-M7 machine code of the NumWorks firmware on an emulated STM32F730, along with the ST7789V screen on the FMC bus, the 9×6 keyboard matrix, the battery ADC, and the CRC32 unit. Not a UI reimplementation — the original firmware, exactly as it runs on the calculator.

All in a single window: Renode is launched headlessly, and its screen is forwarded to the application via a local socket.

## Windows download

Download the Windows x64 archive from [Releases](https://github.com/nat649/emuworks/releases). It includes the .NET runtime and both built-in firmwares. Install Renode separately. **The published 0.2.0 release and current source use native script storage and do not need Node.js. Published 0.1.x archives still require Node.js for external firmware.** Extract the complete archive to a writable path without spaces, and launch `EmuWorks.exe`. See [Windows release instructions](docs/windows-release.md).

## Calculator tools in the 0.3.0 source build

- **Keyboard:** virtual calculator keys, with toggled Shift/Alpha modifiers and text-compatible input for Code.
- **Developer:** Renode console, CPU register values, memory reads/dumps and an optional GDB server.
- **Compare:** two independent calculators side by side, using separate ROMs, scripts, screen ports and Renode histories.
- **Battery:** adjustable ADC voltage, presets and persistence across boots.
- **Import firmware → Open DFU:** validated extraction of a complete N0110 DfuSe firmware pair.

See [calculator tools](docs/calculator-tools.md) for usage, supported formats and limits. These additions require the current source build; the published 0.2.0 archive does not contain them. Third-party firmware remains user-supplied.
## Built-in firmware: EmuWorks Core

**EmuWorks Core 0.1** is our original MIT-licensed ARM firmware, included in the application. It provides basic arithmetic, decimals, parentheses, integer powers, `ANS` and an eight-entry session history. It runs on the emulated Cortex-M7, using the LCD bus and GPIO keyboard. Its source code and original glyphs are maintained in the separate [emuworks-core repository](https://github.com/nat649/emuworks-core). This emulator keeps the compiled firmware, its MIT license and the exact source revision in [firmware/core](firmware/core/README.md).

Choose **EmuWorks Core → Use selected firmware → Start calculator**. Core is installed automatically on a fresh installation; existing user firmware is preserved. This first version displays six significant digits and does not include Python, graphing or persistent history. No Epsilon/Omega code or assets are included in Core.

**Third-party firmware is not bundled.** To use Epsilon or Omega, supply your own matching internal/external images. The workflow `renode/build-firmware-n0110.yml` can compile Epsilon from its sources. Third-party firmware retains its own license; the project's MIT license does not relicense it.

## Python-only firmware: EmuWorks Code

**EmuWorks Code 0.1** is a separate firmware with only **Python** and **About** on its home screen. It runs MicroPython 1.26.1 on the emulated Cortex-M7 and provides an interactive console, script execution, `math`, imports between scripts, and Ctrl+C interruption.

Select **emuworks-code-0.1**, install it, and start the emulator. Core remains available separately. Code scripts live in `rom/code-scripts/`, independently of Epsilon/Omega scripts. Add or edit files while stopped, then restart Code to reload them. The app embeds both original firmwares; no runtime download is needed.

Sources and build instructions: [emuworks-code](https://github.com/nat649/emuworks-code). Code is based on MicroPython; its dependency notices are included in [firmware/code](firmware/code/THIRD_PARTY_NOTICES.md) and extracted alongside the firmware in the local library. Console variables are session-only; scripts are read-only inside the firmware. See the firmware README for supported features and limits.

## Updating the bundled Core firmware

Core remains embedded in `EmuWorks.exe`: users do not need to download or clone the firmware repository. Building the Windows app also works without an ARM compiler or a Git submodule.

To develop Core, clone [emuworks-core](https://github.com/nat649/emuworks-core) separately. Build and commit its images there, then run `node tools/update-core.cjs ../emuworks-core` from this repository and rebuild the application. The import records the source commit and SHA-256 hashes in `firmware/core/source.json`. See the [integration instructions](firmware/core/README.md).

## Prerequisites

| | |
|---|---|
| [Renode](https://renode.io) 1.16 | `winget install Renode.Renode` |
| Node.js | optional developer tooling only in 0.2.0; application backups and synchronization are native |
| .NET Desktop Runtime 8 | for the application; or `dotnet publish` from `app/` |

> ⚠️ **Install the folder in a path without spaces**, for example
> `C:\EmuWorks\`. Renode 1.16 fails silently ("Could not tokenize")
> on paths containing spaces — this is the project's main pitfall. The exact
> location is up to you: nothing is hardcoded, the application passes the project
> root to Renode via the `EMUWORKS_BASE` environment variable.

## Optional: using third-party firmware

1. Fork [numworks/epsilon](https://github.com/numworks/epsilon).
2. Copy `renode/build-firmware-n0110.yml` into `.github/workflows/` on your fork's default branch.
3. Actions → **Firmware N0110 (Renode)** → Run workflow.
4. Download the artifact and import its matching internal/external images with **Import firmware**, or place them under `firmwares/<name>/internal.bin` and `firmwares/<name>/external.bin`.

The current application performs script backups and synchronization in C#. Its native startup helper runs from the same executable, so external firmware no longer requires Node.js. Older 0.1.x release archives still use Node.js; source changes do not update those archives.

New builds save imported firmware pairs in the library under `imported-<id>` and select their entry in the dropdown. Reimporting the same pair reuses its existing entry. In release **v0.1.0**, importing only updates the active `rom/` images: to add a dropdown entry, create `firmwares/<name>/` beside `EmuWorks.exe`, put the matching images there as `internal.bin` and `external.bin`, and restart EmuWorks.

The artifact contains two variants:

| | |
|---|---|
| `epsilon.internal.bin` / `epsilon.external.bin` | boots directly to the home screen |
| `epsilon.onboarding.*` | includes the first-use onboarding wizard |

The difference is just a build target (`make epsilon.dfu` vs `epsilon.onboarding.dfu`), not a patch. The first one is preferred here: the emulator always performs a cold boot, so the language wizard would reappear on every launch.

Since a fork does not inherit tags, the workflow takes a `repository` field in addition to `ref`: leave it as `numworks/epsilon` to build upstream, or enter your fork and branch to compile your own modifications.

## Usage

Launch `EmuWorks.exe`. A single window offers:

- built-in EmuWorks Core, plus firmware selection/import from your own images;
- Python script management: add, delete, or open them in your editor;
- the calculator screen, controlled by your PC keyboard — scaled by an **integer** factor (2×, 3×…) to remain crisp, and centered in a frame; resizing the window scales the calculator;
- automatic saving upon exit and a backup browser with manual backup and restoration;
- firmware names, versions, image sizes and installed status, with a guided import dialog;
- configurable PC keyboard mappings, integer screen zoom and opt-in update checks;
- a reorganized dashboard with Library, Scripts and Journal tabs.

### The `rom/` folder **is** the calculator

    rom/
    ├── internal.bin      internal flash → 0x08000000
    ├── external.bin      external flash → 0x90000000
    ├── scripts/*.py      Python scripts, as real text files
    └── serie.txt         the serial number displayed by the calculator

On startup, the `.py` files from the folder are injected into the calculator; on exit, what the calculator contains is written back to the folder. **The folder is the authority on startup, the calculator is the authority on exit.** This means you can edit your scripts in your code editor, version them, and share them — which the real calculator's USB connection does not allow.

The storage address is dynamically found at each boot by searching for the magic number `0xEE0BDDBA` in SRAM: it differs from one firmware to another, and the folder adapts without any reconfiguration.

Script synchronization supports the 32,768-byte Epsilon/Omega storage layout and the 64,000-byte Upsilon layout. It validates both markers and all records, preserves the storage delegate, and clears the record cache at the position used by each firmware. Upsilon from `gbraad/numworks-firmwares` at commit `1c918f5` was tested with cold boot, framebuffer delivery, script injection, saving and restoration. Supply its matching N0110 images separately; no Upsilon binaries are bundled.

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

The content of this repository — original EmuWorks Core firmware, Renode peripheral models, tools, application, documentation — is licensed under the **MIT** license, see [LICENSE](LICENSE).

It does not cover, and this repository does not distribute:

| | |
|---|---|
| [Epsilon](https://github.com/numworks/epsilon), the NumWorks firmware | © NumWorks — CC BY-NC-SA 4.0 |
| Omega, its community fork | same license |
| [Renode](https://github.com/renode/renode), the emulation infrastructure | © Antmicro — MIT |

The peripheral models describe the hardware of the STM32F730 and the N0110 board: registers, ST7789V display protocol, keyboard matrix wiring. They are written based on component documentation and observation of the firmware's behavior, not derived from its code.

## Backup history and reliable shutdown

The application now includes **Import firmware** (select the internal and external images from the same N0110 build) and **Restaurer une sauvegarde...**. Script versions are stored under `rom/sauvegardes/`, with SHA-256 integrity checks. Shutdown pauses the machine before the last memory dump; cancelled startup never imports an older session's memory. A forced shutdown may recover an earlier periodic save.

See [backup, recovery and testing instructions](docs/fiabilite.md). Backups include Python source scripts (Ion or Code), the serial setting and application preferences. They do not contain a complete calculator snapshot. Core does not use Python storage; its history lasts for the current session only.
