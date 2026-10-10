using System.Buffers.Binary;
using System.Text;
using EmuWorks;

internal static partial class Tests
{
    static byte[] MakeDfu(params (uint Address, byte[] Data)[] elements)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes("DfuSe")); writer.Write((byte)1); writer.Write(0u); writer.Write((byte)1);
        writer.Write(Encoding.ASCII.GetBytes("Target")); writer.Write((byte)0); writer.Write(1u); writer.Write(new byte[255]);
        writer.Write((uint)elements.Sum(p => 8 + p.Data.Length)); writer.Write((uint)elements.Length);
        foreach (var element in elements) { writer.Write(element.Address); writer.Write((uint)element.Data.Length); writer.Write(element.Data); }
        writer.Write((ushort)0xFFFF); writer.Write((ushort)0xFFFF); writer.Write((ushort)0xFFFF); writer.Write((ushort)0x011A);
        writer.Write(Encoding.ASCII.GetBytes("UFD")); writer.Write((byte)16); writer.Write(0u);
        byte[] result = stream.ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(6), (uint)result.Length);
        FixDfuCrc(result); return result;
    }
    static void FixDfuCrc(byte[] data) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(data.Length - 4), DfuFirmware.Crc(data.AsSpan(0, data.Length - 4)));
    static void FirmwareToolsTests()
    {
        Check(DfuFirmware.Crc("123456789"u8) == 0x340BC6D9, "DfuSe CRC convention mismatch");
        byte[] inside = new byte[64]; BitConverter.GetBytes(0x20040000u).CopyTo(inside, 0); BitConverter.GetBytes(0x08000009u).CopyTo(inside, 4);
        byte[] outside = { 11, 22, 33, 44 };
        byte[] dfu = MakeDfu((0x90000000, outside), (0x08000000, inside[..32]), (0x08000020, inside[32..]));
        var pair = DfuFirmware.Parse(dfu);
        Check(pair.Internal.SequenceEqual(inside) && pair.External.SequenceEqual(outside), "DFU extraction changed flash contents");
        byte[] numworksSize = (byte[])dfu.Clone(); BinaryPrimitives.WriteUInt32LittleEndian(numworksSize.AsSpan(6), (uint)dfu.Length - 16); FixDfuCrc(numworksSize);
        Check(DfuFirmware.Parse(numworksSize).Internal.SequenceEqual(inside), "NumWorks prefix length rejected");
        string source = Path.Combine(root, "firmware.dfu"), directory = Path.Combine(root, "dfu-extraction"); File.WriteAllBytes(source, dfu);
        DfuFirmware.Extract(source, directory); FirmwareStore.Validate(Path.Combine(directory, "internal.bin"), Path.Combine(directory, "external.bin"));
        byte[] corrupted = (byte[])dfu.Clone(); corrupted[300] ^= 1;
        ExpectStorageFailure(() => DfuFirmware.Parse(corrupted));
        ExpectStorageFailure(() => DfuFirmware.Parse(dfu[..^1]));
        ExpectStorageFailure(() => DfuFirmware.Parse(MakeDfu((0x08000000, inside))));
        ExpectStorageFailure(() => DfuFirmware.Parse(MakeDfu((0x08000000, inside), (0x90000000, outside), (0x90000001, outside))));
        ExpectStorageFailure(() => DfuFirmware.Parse(MakeDfu((0x08000000, inside), (0x90000000, outside), (0x20000000, outside))));
        Console.WriteLine("PASS DfuSe extraction, split elements, CRC, truncation, unsupported addresses and incomplete pairs");
    }
}
