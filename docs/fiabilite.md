# Backups, recovery and shutdown

The 0.2.0 application stores and synchronizes scripts natively in C#. Node.js is only needed for optional developer tools and legacy command-line scripts.

## Backup history

With emulation stopped, open **Scripts > Create backup** to save a dated version. **Backup history / restore** lists source-script counts and storage type (Ion or Code). Select a version to restore or import a `manifest.json` copied from another installation. SHA-256 hashes are checked before replacement, and current files are backed up first. Earlier version 1 manifests remain readable.

Version 2 backups contain Python source scripts, `rom/serie.txt` when present and application preferences from `settings.json` when present. Code scripts are independent of Ion scripts. Restoration does not install firmware or restore the complete running calculator, graph functions, Python console variables or Core session history. Preferences absent from an older backup retain their current value. New settings are applied after restoration.

Automatic backups are created before external firmware startup, synchronization from the calculator, script edits and restoration. Versions under `rom/sauvegardes/` are not automatically purged; old versions may be removed while emulation is stopped.

## Safe synchronization

Native storage supports Epsilon/Omega's 32,768-byte layout and Upsilon's 64,000-byte layout. It validates headers, footers, record boundaries and names, preserves non-Python records and the delegate, and clears the cache at its layout-specific position. Invalid or unexpectedly empty storage does not overwrite existing source scripts.

Each session has an independent SRAM dump under `rom/.sessions/`. A failed startup never imports an older dump. Normal shutdown pauses the CPU before the final dump. Forced termination may recover an earlier periodic save. Dumps are retained for diagnosis.

Files are written through temporary files and replacement; script directories keep a rollback copy. A lock prevents application instances from modifying the same ROM. External editors and command-line tools should be used while emulation is stopped. Firmware installation journals both images and recovers an interrupted installation at the next launch.

## Developer checks

Run native storage and application lifecycle checks with the .NET SDK:

```powershell
dotnet run --project tests/EmuWorks.Tests.csproj -- .
```

Tests use isolated temporary data and a separate screen port. No user firmware is required. The optional JavaScript storage tests remain available for legacy tools:

```powershell
node --test tests/storage.test.cjs
```

For a real ARM firmware integration check, build the app and supply matching images separately:

```powershell
dotnet build app/EmuWorks.csproj -c Release
node tests/renode.integration.cjs 'C:\Program Files\Renode\bin\Renode.exe' 'C:\Firmware' 'app\bin\Release\net8.0-windows\EmuWorks.exe'
```

The integration test picks an isolated port, removes Node.js from the Renode/helper PATH, and checks boot, framebuffer delivery, native script injection and backup round trips. Node.js runs the developer test harness only. Logs and temporary test data are retained for inspection.