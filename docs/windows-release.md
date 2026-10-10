# Windows setup

These instructions cover native script storage introduced in **0.2.0**. The current **0.3.0 source build** also includes virtual keys, developer tools, firmware comparison, simulated battery voltage and DFU import; see [calculator tools](calculator-tools.md). Published 0.1.x releases still require Node.js for external firmware.

1. Install Renode 1.16 (`winget install Renode.Renode`).
2. Extract the complete application package to a writable path without spaces, such as `C:\EmuWorks`.
3. Launch `EmuWorks.exe`. Self-contained packages include the .NET runtime.
4. Select a firmware in **Library**, choose **Use selected firmware**, then **Start calculator**. Core is installed automatically on a fresh installation.

The 0.2.0 app does not require Node.js or desktop Python. Epsilon, Omega and Upsilon images are supplied separately by the user. **Import firmware** guides you through the matching internal/external images, checks N0110 sizes and the ARM startup vector, and accepts a name and version. This does not prove that two individually valid images came from the same build.

## Dashboard

- **Library:** display names, versions, image sizes, validity and installed status. Edit names/versions locally, or open the firmware folder.
- **Scripts:** add, remove or edit Python files. Code keeps its own `rom/code-scripts/` folder; external firmware uses `rom/scripts/`.
- **Create backup / Backup history:** dated script backups, with SHA-256 verification and settings restoration. Version 1 backups from earlier releases remain compatible.
- **Settings:** choose Auto fit or a 1x–4x integer zoom, remap calculator keys, and opt into update checks on launch. Zoom is limited to the available panel size. Code retains its text input and Ctrl+C behavior.
- **Updates:** check the official EmuWorks GitHub release and open its download page when a newer version exists. Downloads and replacement of application files remain manual.
- **Journal:** startup and synchronization diagnostics.

Renode runs in the background. Set `RENODE_EXE` if its executable cannot be found. Close the app before installing new program files; preserve `rom/`, `firmwares/` and `settings.json` when updating.

## Older releases

For v0.1.x external firmware, install Node.js LTS with Add to PATH enabled, check `node --version` in a new terminal and restart EmuWorks. In v0.1.0 an import only changes the active ROM; add matching images as `firmwares/my-firmware/internal.bin` and `external.bin` to create a dropdown entry. Upsilon storage support requires v0.1.1 or newer.

## Integrity and licenses

Compare the package SHA-256 hash with its accompanying `SHA256SUMS.txt`. Packages include original firmware provenance, MIT licenses, MicroPython and .NET runtime notices. Renode is installed separately. `RELEASE.json` records the package version and source commit. Bundled firmware targets the emulator; do not flash it onto physical hardware.