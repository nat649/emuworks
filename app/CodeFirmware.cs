// SPDX-License-Identifier: MIT
using System.Text;
namespace EmuWorks;
internal static class CodeFirmware
{
    public const string LibraryName = "emuworks-code-0.1";
    public static bool IsCode(string path)
    {
        if (!File.Exists(path)) return false;
        using var stream = File.OpenRead(path);
        byte[] id = Encoding.ASCII.GetBytes("EMUWORKS_CODE_V1");
        if (stream.Length < 0x100 + id.Length) return false;
        stream.Position = 0x100;
        byte[] actual = new byte[id.Length]; stream.ReadExactly(actual);
        return actual.AsSpan().SequenceEqual(id);
    }
    public static void PrepareLibrary(string root)
    {
        string dir = Path.Combine(root, "firmwares", LibraryName);
        Directory.CreateDirectory(dir);
        foreach (string name in FirmwareStore.Names)
        {
            using var stream = typeof(CodeFirmware).Assembly.GetManifestResourceStream("Code." + name)
                ?? throw new IOException("Embedded Code firmware is missing.");
            using var memory = new MemoryStream(); stream.CopyTo(memory);
            byte[] data = memory.ToArray(); string target = Path.Combine(dir, name);
            if (File.Exists(target) && File.ReadAllBytes(target).AsSpan().SequenceEqual(data)) continue;
            File.WriteAllBytes(target + ".tmp", data); File.Move(target + ".tmp", target, true);
        }
        var assembly = typeof(CodeFirmware).Assembly;
        string licenses = Path.Combine(dir, "licenses"); Directory.CreateDirectory(licenses);
        foreach (string resource in assembly.GetManifestResourceNames().Where(n => n.StartsWith("Code.License.", StringComparison.Ordinal))) {
            using var source = assembly.GetManifestResourceStream(resource);
            using var destination = File.Create(Path.Combine(licenses, resource.Substring("Code.License.".Length)));
            source.CopyTo(destination);
        }
        string scripts = Path.Combine(root, "rom", "code-scripts");
        if (!Directory.Exists(scripts))
        {
            Directory.CreateDirectory(scripts);
            File.WriteAllText(Path.Combine(scripts, "hello.py"), "print('Hello from EmuWorks Code!')\nfor i in range(5):\n    print(i, i * i)\n");
        }
    }
    public static void PackScripts(string rom)
    {
        string dir = Path.Combine(rom, "code-scripts"); Directory.CreateDirectory(dir);
        string[] files = Directory.GetFiles(dir, "*.py").OrderBy(Path.GetFileName, StringComparer.Ordinal).ToArray();
        if (files.Length > 16) throw new IOException("Code supports up to 16 scripts.");
        using var data = new MemoryStream(); using var writer = new BinaryWriter(data, Encoding.UTF8, true);
        writer.Write(0x45574353u); writer.Write(files.Length); writer.Write(new byte[16 * 40]);
        for (int i = 0; i < files.Length; i++)
        {
            string name = Path.GetFileName(files[i]);
            if (name.Length > 31 || !System.Text.RegularExpressions.Regex.IsMatch(name, @"^[A-Za-z_][A-Za-z0-9_]*\.py$"))
                throw new IOException("Use an ASCII Python module name of at most 31 characters: " + name);
            byte[] text = new UTF8Encoding(false, true).GetBytes(File.ReadAllText(files[i], new UTF8Encoding(false, true)));
            if (text.Length > 16384 || text.Contains((byte)0)) throw new IOException("Script must be valid UTF-8, without NUL, and at most 16 KiB: " + name);
            long offset = data.Length; data.Position = offset; writer.Write(text);
            data.Position = 8 + i * 40; byte[] filename = new byte[32]; Encoding.ASCII.GetBytes(name).CopyTo(filename, 0);
            writer.Write(filename); writer.Write((uint)offset); writer.Write(text.Length);
        }
        string destination = Path.Combine(rom, "code-scripts.bin");
        File.WriteAllBytes(destination + ".tmp", data.ToArray()); File.Move(destination + ".tmp", destination, true);
    }
}
