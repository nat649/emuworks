# Windows release

Download `EmuWorks-v0.1.0-win-x64.zip` from the [GitHub releases page](https://github.com/nat649/emuworks/releases).

1. Install Renode 1.16. On Windows, you can use `winget install Renode.Renode`.
2. Extract the complete archive to a writable path **without spaces**, such as `C:\EmuWorks`.
3. Open `EmuWorks.exe`. The .NET runtime is included in this build.
4. Core is installed automatically on a fresh launch. For Python, select **emuworks-code-0.1**, click **Installer**, then **Demarrer**.

Core provides arithmetic, parentheses, integer powers, `ANS`, and session history. Code provides a MicroPython console and scripts, with Python and About on its home screen. Code scripts are stored in `rom/code-scripts/`; edit them while stopped and restart to reload. Ctrl+C interrupts Python execution.

Renode runs in the background. If the application cannot find it, set `RENODE_EXE` to your `Renode.exe` path before launching.

Epsilon and Omega are not included. To use your own compatible firmware, import its matching internal/external images. Node.js is required only for Python-script synchronization with these external firmwares; the included Core and Code firmwares run without a desktop Python or Node.js installation.

The application interface currently uses French labels. Documentation and release notes are in English. These firmware images target the emulator; do not flash them onto physical hardware.

## Updating

Stop EmuWorks before updating its files. Preserve your existing `rom/` and `firmwares/` folders: they contain your working firmware, scripts, and library. Extract the new program files into the existing folder. An existing active firmware is not automatically replaced.

## Integrity and licenses

Compare the archive's SHA-256 hash with `SHA256SUMS.txt` from the same release:

```powershell
Get-FileHash .\EmuWorks-v0.1.0-win-x64.zip -Algorithm SHA256
```

The package includes the EmuWorks MIT license, the original firmware licenses, MicroPython and runtime notices, and the .NET runtime license notices. Renode is installed separately. See `RELEASE.json` for the source commit and runtime version used to build this package.
