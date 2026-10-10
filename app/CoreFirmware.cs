using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace EmuWorks;

// Only our original MIT firmware is embedded. Third-party firmware remains optional.
internal static class CoreFirmware
{
    public const string LibraryName = "emuworks-core-0.1";
    private static readonly byte[] Identity = Encoding.ASCII.GetBytes("EMUWORKS_CORE_V1");

    public static bool IsCore(string internalPath)
    {
        if (!File.Exists(internalPath)) return false;
        using var file = File.OpenRead(internalPath);
        if (file.Length < 0x100 + Identity.Length) return false;
        file.Position = 0x100;
        byte[] signature = new byte[Identity.Length];
        file.ReadExactly(signature);
        return signature.AsSpan().SequenceEqual(Identity);
    }

    public static void PrepareLibrary(string root)
    {
        string library = Path.Combine(root, "firmwares", LibraryName);
        Directory.CreateDirectory(library);
        foreach (string name in FirmwareStore.Names)
        {
            using var resource = typeof(CoreFirmware).Assembly.GetManifestResourceStream("Core." + name)
                ?? throw new IOException("Firmware integre absent de cet executable.");
            using var data = new MemoryStream(); resource.CopyTo(data);
            string dest = Path.Combine(library, name);
            byte[] bytes = data.ToArray();
            if (File.Exists(dest) && File.ReadAllBytes(dest).AsSpan().SequenceEqual(bytes)) continue;
            string temporary = dest + ".tmp";
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, dest, true);
        }
        string rom = Path.Combine(root, "rom");
        if (!File.Exists(Path.Combine(rom, "internal.bin")) && !File.Exists(Path.Combine(rom, "external.bin")))
            FirmwareStore.Install(Path.Combine(library, "internal.bin"), Path.Combine(library, "external.bin"), rom);
    }
}
