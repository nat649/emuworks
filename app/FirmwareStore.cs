using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace EmuWorks;

internal static class FirmwareStore
{
    internal static readonly string[] Names = { "internal.bin", "external.bin" };

    public static void Validate(string internalPath, string externalPath)
    {
        var inside = new FileInfo(internalPath);
        var outside = new FileInfo(externalPath);
        if (!inside.Exists || !outside.Exists) throw new IOException("Il faut les deux images : internal.bin et external.bin.");
        if (inside.Length < 8 || inside.Length > 0x10000 || outside.Length == 0 || outside.Length > 0x800000)
            throw new IOException("Tailles incompatibles avec le N0110 (flash interne 64 Ko, externe 8 Mo).");
        using var reader = new BinaryReader(File.OpenRead(internalPath));
        uint stack = reader.ReadUInt32(), reset = reader.ReadUInt32();
        uint entry = reset & ~1u;
        bool mapped = (entry >= 0x08000000 && entry < 0x08000000 + inside.Length)
                   || (entry >= 0x00200000 && entry < 0x00200000 + inside.Length)
                   || (entry >= 0x90000000 && entry < 0x90000000 + outside.Length);
        if (stack <= 0x20000000 || stack > 0x20040000 || (stack & 3) != 0 || (reset & 1) == 0 || !mapped)
            throw new IOException("Table de demarrage ARM invalide pour le N0110. Selectionne des images binaires extraites, pas un DFU ou un ELF.");
    }

    public static bool Same(string a, string b)
    {
        if (!File.Exists(a) || !File.Exists(b) || new FileInfo(a).Length != new FileInfo(b).Length) return false;
        using var left = File.OpenRead(a);
        using var right = File.OpenRead(b);
        return CryptographicOperations.FixedTimeEquals(SHA256.HashData(left), SHA256.HashData(right));
    }

    private static void AtomicCopy(string source, string dest)
    {
        string temp = dest + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var input = File.OpenRead(source))
            using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { input.CopyTo(output); output.Flush(true); }
            File.Move(temp, dest, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    // Un journal sur disque permet de retrouver la paire precedente apres une interruption.
    public static void Recover(string rom)
    {
        string pending = Path.Combine(rom, ".firmware-pending");
        string marker = Path.Combine(pending, "pending.json");
        if (!File.Exists(marker)) return;
        var existed = JsonSerializer.Deserialize<bool[]>(File.ReadAllText(marker));
        if (existed == null || existed.Length != Names.Length) throw new IOException("Journal d'installation invalide.");
        for (int i = 0; i < Names.Length; i++)
        {
            string dest = Path.Combine(rom, Names[i]);
            if (existed[i]) AtomicCopy(Path.Combine(pending, "old-" + Names[i]), dest);
            else if (File.Exists(dest)) File.Delete(dest);
        }
        File.Delete(marker);
    }

    public static void Install(string internalPath, string externalPath, string rom)
    {
        Validate(internalPath, externalPath);
        Directory.CreateDirectory(rom);
        Recover(rom);
        string pending = Path.Combine(rom, ".firmware-pending");
        Directory.CreateDirectory(pending);
        string[] sources = { internalPath, externalPath };
        var existed = new bool[Names.Length];
        for (int i = 0; i < Names.Length; i++)
        {
            AtomicCopy(sources[i], Path.Combine(pending, "new-" + Names[i]));
            string original = Path.Combine(rom, Names[i]);
            existed[i] = File.Exists(original);
            if (existed[i]) AtomicCopy(original, Path.Combine(pending, "old-" + Names[i]));
        }
        Validate(Path.Combine(pending, "new-internal.bin"), Path.Combine(pending, "new-external.bin"));
        string marker = Path.Combine(pending, "pending.json");
        using (var stream = new FileStream(marker + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
        { JsonSerializer.Serialize(stream, existed); stream.Flush(true); }
        File.Move(marker + ".tmp", marker, true);
        try
        {
            foreach (string name in Names) AtomicCopy(Path.Combine(pending, "new-" + name), Path.Combine(rom, name));
            File.Delete(marker); // commit : les deux images sont remplacees
        }
        catch { Recover(rom); throw; }
    }
}
