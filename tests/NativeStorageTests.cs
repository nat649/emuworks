using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EmuWorks;

internal static partial class Tests
{
    static void NativeStorageTests()
    {
        foreach (bool upsilon in new[] { false, true })
        {
            int size = upsilon ? 64020 : 32788, header = upsilon ? 8 : 0, footer = upsilon ? 64012 : 32772, cache = upsilon ? 0 : 32780;
            byte[] original = new byte[size];
            BitConverter.GetBytes(0xEE0BDDBAu).CopyTo(original, header); BitConverter.GetBytes(0xEE0BDDBAu).CopyTo(original, footer);
            BitConverter.GetBytes(0x2000abcd).CopyTo(original, footer + 4);
            BitConverter.GetBytes(0x20001111).CopyTo(original, cache);
            string code = "print('été')\n" + (upsilon ? "# padding\n" + new string(' ', 40000) : "");
            byte[] body = new byte[Encoding.UTF8.GetByteCount(code) + 2]; body[0] = 64; Encoding.UTF8.GetBytes(code).CopyTo(body, 1);
            var records = new[] { new ScriptStorage.Record("check.py", body), new ScriptStorage.Record("f.func", new byte[] { 9, 8, 7 }) };
            byte[] region = ScriptStorage.Rebuild(original, records);
            Check(BitConverter.ToUInt32(region, footer + 4) == 0x2000abcd && BitConverter.ToUInt32(region, cache) == 0, "Native storage changed delegate or retained cache");
            byte[] sram = new byte[0x40000]; region.CopyTo(sram, 0x1000);
            var location = ScriptStorage.Locate(sram); Check(location.Start == 0x1000, "Native storage address mismatch");
            string rom = Path.Combine(root, upsilon ? "native-upsilon" : "native-epsilon"); Directory.CreateDirectory(rom);
            string dump = Path.Combine(rom, "sram.bin"); File.WriteAllBytes(dump, sram);
            ScriptStorage.Execute("pull", dump, rom);
            Check(File.ReadAllText(Path.Combine(rom, "scripts/check.py")) == code, "Native storage changed UTF-8 script text");
            ScriptStorage.Execute("push", rom);
            var rebuilt = ScriptStorage.Parse(File.ReadAllBytes(Path.Combine(rom, ".storage.bin")));
            Check(rebuilt.Single(r => r.Name == "check.py").Body[0] == 64, "Native storage changed script flags");
            Check(rebuilt.Single(r => r.Name == "f.func").Body.SequenceEqual(new byte[] { 9, 8, 7 }), "Native storage erased non-Python records");
            byte[] bad = (byte[])region.Clone(); bad[footer] = 0;
            ExpectStorageFailure(() => ScriptStorage.Parse(bad));
            ExpectStorageFailure(() => ScriptStorage.Rebuild(region, new[] { new ScriptStorage.Record("huge.py", new byte[64000]) }));
            region.CopyTo(sram, 0x18000); ExpectStorageFailure(() => ScriptStorage.Locate(sram));
            Check(BitConverter.ToUInt32(original, cache) == 0x20001111, "Failed rebuild modified its source");
        }
        string backupRoot = Path.Combine(root, "native-backups"), backupRom = Path.Combine(backupRoot, "rom");
        Directory.CreateDirectory(Path.Combine(backupRom, "scripts")); string file = Path.Combine(backupRom, "scripts/user.py");
        File.WriteAllText(file, "original"); File.WriteAllText(Path.Combine(backupRom, "serie.txt"), "EmuWorks");
        new AppSettings { Zoom = 3, CheckUpdatesOnLaunch = true, CustomKeyboard = true, KeyBindings = new() { ["F2"] = "EXE" } }.Save(backupRoot);
        string saved = ScriptStorage.Backup(backupRom);
        File.WriteAllText(file, "edited"); new AppSettings { Zoom = 1 }.Save(backupRoot); File.WriteAllText(Path.Combine(backupRom, "serie.txt"), "Changed");
        ScriptStorage.Restore(backupRom, saved);
        Check(File.ReadAllText(file) == "original" && AppSettings.Load(backupRoot).Zoom == 3 && File.ReadAllText(Path.Combine(backupRom, "serie.txt")) == "EmuWorks", "Native backup did not restore scripts and settings");
        File.WriteAllText(Path.Combine(saved, "user.py"), "tampered"); ExpectStorageFailure(() => ScriptStorage.Restore(backupRom, saved));
        Check(File.ReadAllText(file) == "original", "Invalid backup overwrote current scripts");
        string legacy = Path.Combine(backupRoot, "legacy"); Directory.CreateDirectory(legacy); byte[] legacyCode = Encoding.UTF8.GetBytes("legacy script");
        File.WriteAllBytes(Path.Combine(legacy, "legacy.py"), legacyCode);
        ScriptStorage.WriteJson(Path.Combine(legacy, "manifest.json"), new { version = 1, files = new Dictionary<string, string> { ["legacy.py"] = Convert.ToHexString(SHA256.HashData(legacyCode)).ToLowerInvariant() } });
        ScriptStorage.Restore(backupRom, legacy); Check(File.ReadAllText(Path.Combine(backupRom, "scripts/legacy.py")) == "legacy script", "Legacy backup incompatible");
        ExpectStorageFailure(() => new AppSettings { Zoom = 9 }.Validate());
        Console.WriteLine("PASS native Epsilon/Upsilon storage, large scripts, record preservation, legacy backups, integrity and settings restoration");
    }
    static void ExpectStorageFailure(Action action)
    {
        try { action(); throw new Exception("Invalid storage operation was accepted"); }
        catch (IOException) { }
    }
}
