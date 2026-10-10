using System.Text.Json;

namespace EmuWorks;

internal sealed class FirmwareInfo
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "Unknown";
    public string Source { get; set; } = "Local images";
    public DateTime? ImportedAt { get; set; }
    internal static FirmwareInfo Read(string directory)
    {
        string metadata = Path.Combine(directory, "firmware.json");
        FirmwareInfo info;
        if (File.Exists(metadata)) info = JsonSerializer.Deserialize<FirmwareInfo>(File.ReadAllText(metadata)) ?? throw new IOException("Invalid firmware metadata.");
        else
        {
            string folder = Path.GetFileName(directory);
            info = new() { Name = folder };
            if (folder == CoreFirmware.LibraryName) { info.Name = "EmuWorks Core"; info.Version = "0.1"; info.Source = "Built in"; }
            else if (folder == CodeFirmware.LibraryName) { info.Name = "EmuWorks Code"; info.Version = "0.1"; info.Source = "Built in"; }
        }
        if (string.IsNullOrWhiteSpace(info.Name) || info.Name.Length > 80 || info.Version == null || info.Version.Length > 40) throw new IOException("Invalid firmware information.");
        return info;
    }
    internal void Save(string directory) => ScriptStorage.WriteJson(Path.Combine(directory, "firmware.json"), this);
}
