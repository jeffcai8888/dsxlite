using System.Text;
using DsxLite.Core.DualSense;

namespace DsxLite.Tests;

public class Crc32Tests
{
    [Fact]
    public void Compute_MatchesStandardCrc32CheckValue()
    {
        // Standard CRC-32 check value: CRC32("123456789") == 0xCBF43926.
        // Our implementation takes a seed byte followed by data, so split the string.
        uint crc = Crc32.Compute((byte)'1', Encoding.ASCII.GetBytes("23456789"));

        Assert.Equal(0xCBF43926u, crc);
    }
}
