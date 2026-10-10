using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EmuWorks;

// Native Ion storage and backup support; the Windows application needs no Node.js.
internal static class ScriptStorage
{
    const uint Magic = 0xEE0BDDBA, SramBase = 0x20000000;
    record Layout(int Size, int Header, int End, int Cache);
    static readonly Layout[] Formats = { new(32788, 0, 32772, 32780), new(64020, 8, 64012, 0) };
    internal record Record(string Name, byte[] Body);
    internal record Located(int Start, byte[] Region);
    internal sealed class StorageMetadata
    {
        public string address { get; set; } = "";
        public Dictionary<string, byte> flags { get; set; } = new();
    }
    internal sealed class Manifest
    {
        public int version { get; set; } = 2;
        public string kind { get; set; } = "ion";
        public Dictionary<string, string> files { get; set; } = new();
        public Dictionary<string, string> settings { get; set; } = new();
    }
    static uint U32(byte[] data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
    static Layout Format(byte[] region) => Formats.FirstOrDefault(f => f.Size == region.Length
        && U32(region, f.Header) == Magic && U32(region, f.End) == Magic)
        ?? throw new IOException("Truncated storage or invalid markers.");
    internal static List<Record> Parse(byte[] region)
    {
        var f = Format(region); var records = new List<Record>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int p = f.Header + 4;
        while (p + 2 <= f.End)
        {
            int size = BinaryPrimitives.ReadUInt16LittleEndian(region.AsSpan(p, 2));
            if (size == 0) return records;
            if (size < 4 || p + size > f.End) throw new IOException("Record exceeds storage bounds.");
            int end = Array.IndexOf(region, (byte)0, p + 2, size - 2);
            if (end <= p + 2) throw new IOException("Invalid record name.");
            string name = Encoding.Latin1.GetString(region, p + 2, end - p - 2);
            if (!names.Add(name)) throw new IOException("Duplicate record: " + name);
            records.Add(new(name, region.AsSpan(end + 1, p + size - end - 1).ToArray()));
            p += size;
        }
        throw new IOException("Storage terminator missing.");
    }
    internal static Located Locate(byte[] sram)
    {
        if (sram.Length != 0x40000) throw new IOException("SRAM dump must contain exactly 256 KiB.");
        var found = new List<Located>();
        for (int off = 0; off + 4 <= sram.Length; off += 4)
        {
            if (U32(sram, off) != Magic) continue;
            foreach (var f in Formats)
            {
                int start = off - f.Header;
                if (start < 0 || start + f.Size > sram.Length) continue;
                byte[] region = sram.AsSpan(start, f.Size).ToArray();
                try { Parse(region); found.Add(new(start, region)); } catch (IOException) { }
            }
        }
        return found.Count == 1 ? found[0] : throw new IOException("Valid storage was not found or is ambiguous.");
    }
    internal static byte[] Rebuild(byte[] region, IEnumerable<Record> records)
    {
        Parse(region); var f = Format(region); byte[] output = (byte[])region.Clone();
        Array.Clear(output, f.Header + 4, f.End - f.Header - 4);
        int p = f.Header + 4;
        foreach (var record in records)
        {
            byte[] name = Encoding.Latin1.GetBytes(record.Name);
            int size = 3 + name.Length + record.Body.Length;
            if (size > ushort.MaxValue || p + size + 2 > f.End) throw new IOException("Script storage is full.");
            BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(p, 2), (ushort)size);
            name.CopyTo(output, p + 2); record.Body.CopyTo(output, p + 3 + name.Length); p += size;
        }
        Array.Clear(output, f.Cache, 8);
        Parse(output); return output;
    }
    internal static void AtomicWrite(string file, byte[] data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        string temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(data); stream.Flush(true); }
            File.Move(temp, file, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    internal static void WriteJson<T>(string file, T data) => AtomicWrite(file, JsonSerializer.SerializeToUtf8Bytes(data, new JsonSerializerOptions { WriteIndented = true }));
    static void SafeName(string name)
    {
        if (!Regex.IsMatch(name, @"^[a-zA-Z0-9_][a-zA-Z0-9_.-]*\.py$", RegexOptions.CultureInvariant)
            || Regex.IsMatch(name, @"^(con|prn|aux|nul|com[1-9]|lpt[1-9])\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new IOException("Invalid script filename: " + name);
    }
    static Dictionary<string, byte[]> ReadScripts(string dir)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(dir)) return result;
        foreach (string file in Directory.GetFileSystemEntries(dir).OrderBy(f => f))
        {
            string name = Path.GetFileName(file); SafeName(name);
            if ((File.GetAttributes(file) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                throw new IOException("Regular script file expected: " + name);
            if (!result.TryAdd(name, File.ReadAllBytes(file))) throw new IOException("Ambiguous script names.");
        }
        return result;
    }
    static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    static void Recover(string rom, string folder)
    {
        string live = Path.Combine(rom, folder), old = Path.Combine(rom, "." + folder + "-rollback");
        if (!Directory.Exists(live) && Directory.Exists(old)) Directory.Move(old, live);
    }
    static string ScriptsFolder(string rom) => CodeFirmware.IsCode(Path.Combine(rom, "internal.bin")) ? "code-scripts" : "scripts";
    static Dictionary<string, string> SettingsFiles(string rom) => new()
    {
        ["serial.txt"] = Path.Combine(rom, "serie.txt"),
        ["application.json"] = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(rom))!, "settings.json")
    };
    internal static string Backup(string rom, string forcedFolder = null)
    {
        string folder = forcedFolder ?? ScriptsFolder(rom); Recover(rom, folder);
        var files = ReadScripts(Path.Combine(rom, folder));
        string root = Path.Combine(rom, "sauvegardes"); Directory.CreateDirectory(root);
        string id = DateTime.UtcNow.ToString("yyyy-MM-ddTHH-mm-ss-fffZ") + "-" + Guid.NewGuid().ToString("N")[..8];
        string stage = Path.Combine(root, ".tmp-" + id), destination = Path.Combine(root, id);
        Directory.CreateDirectory(stage);
        try
        {
            var manifest = new Manifest { kind = folder == "code-scripts" ? "code" : "ion" };
            foreach (var file in files) { AtomicWrite(Path.Combine(stage, file.Key), file.Value); manifest.files[file.Key] = Hash(file.Value); }
            foreach (var file in SettingsFiles(rom).Where(f => File.Exists(f.Value)))
            {
                byte[] data = File.ReadAllBytes(file.Value);
                AtomicWrite(Path.Combine(stage, ".settings", file.Key), data); manifest.settings[file.Key] = Hash(data);
            }
            WriteJson(Path.Combine(stage, "manifest.json"), manifest); Directory.Move(stage, destination);
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
        return destination;
    }
    static void ReplaceScripts(string rom, string folder, Dictionary<string, byte[]> files)
    {
        Recover(rom, folder);
        string live = Path.Combine(rom, folder), old = Path.Combine(rom, "." + folder + "-rollback"), stage = Path.Combine(rom, "." + folder + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            foreach (var file in files) { SafeName(file.Key); AtomicWrite(Path.Combine(stage, file.Key), file.Value); }
            if (Directory.Exists(old)) Directory.Delete(old, true);
            if (Directory.Exists(live)) Directory.Move(live, old);
            try { Directory.Move(stage, live); }
            catch { if (Directory.Exists(old)) Directory.Move(old, live); throw; }
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }
    internal static void Restore(string rom, string source)
    {
        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(Path.Combine(source, "manifest.json"))) ?? throw new IOException("Invalid backup.");
        if (manifest.version != 1 && manifest.version != 2 || manifest.files == null || manifest.settings == null
            || manifest.kind != "ion" && manifest.kind != "code") throw new IOException("Unsupported backup format.");
        string folder = manifest.kind == "code" ? "code-scripts" : "scripts";
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.files)
        {
            SafeName(file.Key); byte[] data = File.ReadAllBytes(Path.Combine(source, file.Key));
            if (Hash(data) != file.Value || !files.TryAdd(file.Key, data)) throw new IOException("Altered or ambiguous backup: " + file.Key);
        }
        var settings = new Dictionary<string, byte[]>(); var paths = SettingsFiles(rom);
        foreach (var file in manifest.settings)
        {
            if (!paths.ContainsKey(file.Key)) throw new IOException("Unknown backup setting.");
            byte[] data = File.ReadAllBytes(Path.Combine(source, ".settings", file.Key));
            if (Hash(data) != file.Value) throw new IOException("Altered backup setting.");
            if (file.Key == "application.json") (JsonSerializer.Deserialize<AppSettings>(data) ?? throw new IOException("Invalid backup settings.")).Validate();
            settings[file.Key] = data;
        }
        Backup(rom, folder); ReplaceScripts(rom, folder, files);
        foreach (var file in settings) AtomicWrite(paths[file.Key], file.Value);
    }
    static void Reference(string dump, string rom, bool pull)
    {
        var located = Locate(File.ReadAllBytes(dump)); var records = Parse(located.Region);
        Directory.CreateDirectory(rom); Recover(rom, "scripts");
        string metadataFile = Path.Combine(rom, ".storage.json");
        var meta = !pull && File.Exists(metadataFile) ? JsonSerializer.Deserialize<StorageMetadata>(File.ReadAllText(metadataFile))! : new StorageMetadata();
        meta.flags ??= new(); var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records.Where(r => r.Name.EndsWith(".py", StringComparison.Ordinal)))
        {
            SafeName(record.Name);
            if (record.Body.Length < 2 || record.Body[^1] != 0) throw new IOException("Unterminated script: " + record.Name);
            files.Add(record.Name, record.Body.AsSpan(1, record.Body.Length - 2).ToArray());
            meta.flags.TryAdd(record.Name, record.Body[0]);
        }
        if (pull)
        {
            if (files.Count == 0 && ReadScripts(Path.Combine(rom, "scripts")).Count > 0) throw new IOException("Python storage is empty; existing scripts were preserved.");
            Backup(rom, "scripts"); ReplaceScripts(rom, "scripts", files);
        }
        else Directory.CreateDirectory(Path.Combine(rom, "scripts"));
        meta.address = "0x" + (SramBase + located.Start).ToString("x");
        AtomicWrite(Path.Combine(rom, ".storage.bin"), located.Region); WriteJson(metadataFile, meta);
    }
    static void Push(string rom)
    {
        Recover(rom, "scripts");
        var meta = JsonSerializer.Deserialize<StorageMetadata>(File.ReadAllText(Path.Combine(rom, ".storage.json"))) ?? throw new IOException("Invalid storage metadata.");
        byte[] region = File.ReadAllBytes(Path.Combine(rom, ".storage.bin"));
        uint address = Convert.ToUInt32(meta.address.Replace("0x", ""), 16);
        if (address % 4 != 0 || address < SramBase || (ulong)address + (uint)region.Length > SramBase + 0x40000) throw new IOException("Invalid storage address.");
        var records = Parse(region).Where(r => !r.Name.EndsWith(".py", StringComparison.Ordinal)).ToList();
        foreach (var file in ReadScripts(Path.Combine(rom, "scripts")))
        {
            byte[] code = file.Value.Length > 0 && file.Value[^1] == 0 ? file.Value[..^1] : file.Value;
            if (code.Contains((byte)0)) throw new IOException("NUL byte in script: " + file.Key);
            byte[] body = new byte[code.Length + 2]; body[0] = meta.flags.GetValueOrDefault(file.Key); code.CopyTo(body, 1);
            records.Add(new(file.Key, body));
        }
        string path = Path.GetFullPath(Path.Combine(rom, ".storage.bin")).Replace('\\', '/');
        if (path.Contains('"') || path.Contains('\n') || path.Contains('\r')) throw new IOException("Invalid ROM path.");
        AtomicWrite(Path.Combine(rom, ".storage.bin"), Rebuild(region, records));
        AtomicWrite(Path.Combine(rom, "load.resc"), Encoding.UTF8.GetBytes("mem Load \"" + path + "\" 0x" + address.ToString("x") + "\n"));
    }
    internal static void Execute(params string[] args)
    {
        if (args.Length < 2) throw new IOException("Storage command and path required.");
        switch (args[0])
        {
            case "backup": Console.WriteLine("Backup: " + Backup(args[1])); break;
            case "restore": Restore(args[1], args[2]); break;
            case "ref": Reference(args[1], args[2], false); break;
            case "pull": Reference(args[1], args[2], true); break;
            case "push": Push(args[1]); break;
            case "sync": Reference(args[1], args[2], false); Push(args[2]); break;
            default: throw new IOException("Unknown storage command.");
        }
    }
}
