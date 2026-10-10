using System.Buffers.Binary;

namespace EmuWorks;

// ST UM0391 DfuSe container. Only complete N0110 flash pairs are accepted.
internal static class DfuFirmware
{
    internal static uint Crc(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320u : 0);
        }
        return crc;
    }

    internal static (byte[] Internal, byte[] External) Parse(byte[] data)
    {
        if (data.Length < 27 || data.Length > 10 * 1024 * 1024 || !data.AsSpan(0, 5).SequenceEqual("DfuSe"u8) || data[5] != 1)
            throw new IOException("Expected a DfuSe version 1 file for N0110.");
        int suffix = data.Length - 16;
        uint declared = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(6));
        // NumWorks containers exclude the suffix; other DfuSe exporters include it.
        if (declared != suffix && declared != data.Length || !data.AsSpan(suffix + 8, 3).SequenceEqual("UFD"u8)
            || data[suffix + 11] != 16 || BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(suffix + 6)) != 0x011A
            || Crc(data.AsSpan(0, data.Length - 4)) != BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(data.Length - 4)))
            throw new IOException("Invalid DFU length, suffix or checksum.");
        var inside = new List<(int Offset, byte[] Data)>();
        var outside = new List<(int Offset, byte[] Data)>();
        int cursor = 11;
        void Need(int count) { if (count < 0 || cursor > suffix - count) throw new IOException("Truncated DFU element."); }
        for (int target = 0; target < data[10]; target++)
        {
            Need(274);
            if (!data.AsSpan(cursor, 6).SequenceEqual("Target"u8)) throw new IOException("Invalid DFU target.");
            uint targetSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(cursor + 266));
            uint count = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(cursor + 270));
            cursor += 274;
            if (targetSize > suffix - cursor || count > targetSize / 8) throw new IOException("Invalid DFU target size.");
            int targetEnd = cursor + (int)targetSize;
            for (uint element = 0; element < count; element++)
            {
                Need(8);
                uint address = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(cursor));
                uint size = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(cursor + 4)); cursor += 8;
                if (size == 0 || size > targetEnd - cursor) throw new IOException("Invalid DFU element size.");
                ulong end = (ulong)address + size;
                List<(int Offset, byte[] Data)> destination; uint origin;
                if (address >= 0x08000000 && end <= 0x08010000) { destination = inside; origin = 0x08000000; }
                else if (address >= 0x90000000 && end <= 0x90800000) { destination = outside; origin = 0x90000000; }
                else throw new IOException("Unsupported flash address. Only N0110 internal and external flash are supported.");
                int offset = (int)(address - origin);
                if (destination.Any(p => offset < p.Offset + p.Data.Length && p.Offset < offset + size)) throw new IOException("Overlapping DFU elements.");
                destination.Add((offset, data.AsSpan(cursor, (int)size).ToArray())); cursor += (int)size;
            }
            if (cursor != targetEnd) throw new IOException("DFU target length mismatch.");
        }
        if (cursor != suffix) throw new IOException("Unexpected data after DFU targets.");
        byte[] Assemble(List<(int Offset, byte[] Data)> chunks)
        {
            if (!chunks.Any(p => p.Offset == 0)) throw new IOException("DFU must contain a complete internal/external pair starting at both flash bases.");
            byte[] result = new byte[chunks.Max(p => p.Offset + p.Data.Length)]; Array.Fill(result, (byte)255);
            foreach (var chunk in chunks) chunk.Data.CopyTo(result, chunk.Offset);
            return result;
        }
        return (Assemble(inside), Assemble(outside));
    }

    internal static void Extract(string file, string directory)
    {
        if (new FileInfo(file).Length > 10 * 1024 * 1024) throw new IOException("DFU file is too large.");
        var images = Parse(File.ReadAllBytes(file));
        Directory.CreateDirectory(directory);
        string inside = Path.Combine(directory, "internal.bin"), outside = Path.Combine(directory, "external.bin");
        File.WriteAllBytes(inside, images.Internal); File.WriteAllBytes(outside, images.External);
        FirmwareStore.Validate(inside, outside);
    }
}
