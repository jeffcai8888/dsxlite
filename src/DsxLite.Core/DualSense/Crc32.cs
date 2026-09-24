namespace DsxLite.Core.DualSense;

/// <summary>
/// CRC-32 (reflected, poly 0xEDB88320) as used to sign DualSense Bluetooth reports.
/// Matches the Linux hid-playstation scheme: crc32_le seeded with 0xFFFFFFFF, one seed
/// byte processed first, final bitwise NOT.
/// </summary>
internal static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int j = 0; j < 8; j++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        return table;
    }

    public static uint Compute(byte seed, ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        crc = (crc >> 8) ^ Table[(crc ^ seed) & 0xFF];
        foreach (byte b in data)
            crc = (crc >> 8) ^ Table[(crc ^ b) & 0xFF];
        return ~crc;
    }
}
