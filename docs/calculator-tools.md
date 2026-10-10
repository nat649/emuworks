# Calculator tools (0.3.0 source build)

These features are available in the current source build. Downloading an older published archive does not update its executable.

## Virtual keyboard

Use the **Keyboard** tab below the screen. Keys are sent to the emulated GPIO matrix; they do not implement calculator functions in the Windows app. **Shift** and **Alpha** toggle held modifiers and are highlighted while active. Press them again to release them. Opening Settings or Developer tools also releases modifiers. For a larger display, uncheck **Show keyboard and tools** in Library.

Core and external GPIO firmwares accept the calculator keys they implement. Code uses its separate ASCII channel: navigation, digits, operators, parentheses and Enter are mapped to text-compatible input. Use the PC keyboard to enter Python names and commands; Code does not implement every scientific calculator key.

## Simulated battery

In **Battery**, choose Full (4050 mV), Medium (3750 mV), Low (3650 mV), Empty (3500 mV), or enter 3000–4300 mV. The app writes the value to the emulated ADC and stores it in `settings.json`. It applies the value before boot and after changes during a session.

The N0110 active-low charging input (PE3) is held high to represent battery operation rather than charging. The firmware determines the actual icon, thresholds and polling delay. This is a voltage simulation, not a charge percentage, automatic drain or USB charging model. A firmware that does not display battery status, including the current Core and Code builds, will not gain an icon. An empty battery can trigger the firmware's normal low-battery shutdown.

## Developer tools

Open **Developer → Open developer tools** for the current calculator:

- Pause or resume emulated execution.
- Display CPU register values and LCD diagnostics.
- Read a 32-bit memory word using a hexadecimal address.
- Export up to 1 MiB of memory to a binary file. Pause first when a consistent dump matters, then resume.
- Send one-line Renode monitor commands and read their output.
- Start a GDB server on an available port, then use your ARM GDB and matching firmware ELF: `target remote localhost:3333` (or the selected port). Renode owns the server and closes it when this calculator stops. The app does not supply firmware symbols or a GDB executable.

Each comparison calculator has its own Developer tools. Commands apply to that session only. Arbitrary monitor commands can alter emulated state or stop Renode; inspect the output before assuming a command succeeded. GDB's listening scope follows Renode's configuration.

Reference: [Renode GDB documentation](https://renode.readthedocs.io/en/latest/debugging/gdb.html).

## Firmware comparison

Click **Compare**, select one library entry above each screen and choose **Start both**. You may select the same firmware twice. The two calculators have independent ROM directories, script storage, serial files, settings, clocks, Renode configuration, command history and screen ports. They do not change the main calculator's firmware or scripts. Use each virtual keyboard independently; there is no synchronized input or pixel-difference analysis.

Comparison data is retained under `comparisons/<session-id>/left/` and `right/`. **Open data folder** shows it. **Stop both** or close the comparison window to stop its Renode processes. Closing the main app also stops the comparison. Selecting another firmware cold-boots it in that comparison calculator; script formats from different firmware families may not be interchangeable. Comparison workspaces are excluded from Git and release packages.

## Import a DFU

Choose **Import firmware → Open DFU** and select a **complete N0110 DfuSe v1** container. EmuWorks checks signatures, sizes, CRC, target boundaries, flash addresses, element overlap, both flash images and the ARM startup vector. Extracted files remain temporary until you choose **Import and install**, which stores the validated pair in your library. The active firmware must be stopped before installation.

Only internal flash at `0x08000000` (64 KiB) and external flash at `0x90000000` (8 MiB) are accepted. Split elements within either flash are assembled with erased (`0xFF`) gaps. Both regions must include their base address. External-only update files, other calculator models, generic DFU files without DfuSe targets, RAM/OTP elements and corrupt containers are rejected. Firmware rights and licenses remain those of its supplier; imported third-party firmware is never added to the repository or package.

Format reference: STMicroelectronics [UM0391 DfuSe File Format Specification](https://community.st.com/ysqtg83639/attachments/ysqtg83639/interface-connectivity-ics-forum/3972/1/UM0391.pdf).

## Validation

The Windows test suite covers DFU CRC and parsing failures, split elements, incomplete pairs, virtual-key dispatch, battery persistence, developer commands and independent comparison lifecycles. Optional real tests use your own firmware and Renode without committing their binaries:

```powershell
dotnet build tests/EmuWorks.Tests.csproj -c Release
./tests/bin/Release/net8.0-windows/EmuWorks.Tests.exe C:/EmuWorks
./tests/bin/Release/net8.0-windows/EmuWorks.Tests.exe --real-tools C:/EmuWorks 'C:/Program Files/Renode/bin/Renode.exe' C:/my-firmware
./tests/bin/Release/net8.0-windows/EmuWorks.Tests.exe --dfu-check C:/firmware.dfu C:/internal.bin C:/external.bin
```
