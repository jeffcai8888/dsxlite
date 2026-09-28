using DsxLite.Core.ViGEm;

namespace DsxLite.Tests;

public class GyroStickMapperTests
{
    [Fact]
    public void FullScale_IsFiveHundredDegreesPerSecond()
    {
        Assert.Equal(500.0, GyroStickMapper.FullScaleDegreesPerSecond);
    }

    [Theory]
    [InlineData(0.0, 0.0, 0, 0)]
    [InlineData(500.0, 0.0, 32767, 0)]
    [InlineData(-500.0, 0.0, -32768, 0)]
    [InlineData(0.0, 500.0, 0, -32768)]
    [InlineData(0.0, -500.0, 0, 32767)]
    [InlineData(1000.0, 1000.0, 32767, -32768)]
    [InlineData(-1000.0, -1000.0, -32768, 32767)]
    [InlineData(double.MaxValue, double.MaxValue, 32767, -32768)]
    [InlineData(double.MinValue, double.MinValue, -32768, 32767)]
    [InlineData(250.0, 250.0, 16383, -16384)]
    [InlineData(-250.0, -250.0, -16384, 16383)]
    [InlineData(1.0, 1.0, 65, -65)]
    [InlineData(-1.0, -1.0, -65, 65)]
    [InlineData(0.01, -0.01, 0, 0)]
    [InlineData(-0.01, 0.01, 0, 0)]
    public void Map_ClampsAndMapsEachAxis(double yawDps, double pitchDps, int expectedX, int expectedY)
    {
        // 正负端点分别使用 32767 和 -32768；区间内线性缩放后向零截断。
        var result = GyroStickMapper.Map(yawDps, pitchDps);

        Assert.Equal((short)expectedX, result.X);
        Assert.Equal((short)expectedY, result.Y);
    }

    [Theory]
    [InlineData(double.NaN, 0.0, "yawDps")]
    [InlineData(double.PositiveInfinity, 0.0, "yawDps")]
    [InlineData(double.NegativeInfinity, 0.0, "yawDps")]
    [InlineData(0.0, double.NaN, "pitchDps")]
    [InlineData(0.0, double.PositiveInfinity, "pitchDps")]
    [InlineData(0.0, double.NegativeInfinity, "pitchDps")]
    public void Map_RejectsNonFiniteInput(double yawDps, double pitchDps, string parameterName)
    {
        // 非有限输入必须显式拒绝，不能悄悄转成摇杆数值。
        Assert.Throws<ArgumentOutOfRangeException>(parameterName,
            () => GyroStickMapper.Map(yawDps, pitchDps));
    }
}
