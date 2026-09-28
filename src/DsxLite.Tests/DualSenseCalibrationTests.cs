using System.Buffers.Binary;
using DsxLite.Core.DualSense;

namespace DsxLite.Tests;

public class DualSenseCalibrationTests
{
    // 独立通过 Python struct.pack + zlib.crc32(b'\xA3' + report[:37]) 得到 0x9285CF65。
    internal static byte[] FactoryReport() => Convert.FromHexString(
        "05640038ff2c014c047cfc080750fbfc088cf1f401bc022923aae4e02ec0e0581b78ec123465cf8592");

    internal static byte[] WithValue(byte[] source, int offset, short value)
    {
        byte[] result = (byte[])source.Clone();
        BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(offset), value);
        return result;
    }

    internal static DualSenseInputState Raw() => new()
    {
        IsFullReport = true, GyroPitch = 100, GyroYaw = -200, GyroRoll = 300,
        AccelX = 9001, AccelY = -8000, AccelZ = 1000,
    };

    [Theory]
    [InlineData(ConnectionType.Usb)]
    [InlineData(ConnectionType.Bluetooth)]
    public void Parse_UsesAllOffsetsAndLinuxFormula(ConnectionType connection)
    {
        var calibration = DualSenseCalibration.Parse(FactoryReport(), connection);
        Assert.Equal(DualSenseCalibrationStatus.Factory, calibration.Status);
        Assert.Null(calibration.FailureReason);
        var motion = calibration.Convert(Raw())!.Value;
        Assert.Equal(60, motion.GyroPitchDps, 10); // 零偏仅影响分母。
        Assert.Equal(-80, motion.GyroYawDps, 10);
        Assert.Equal(60, motion.GyroRollDps, 10);
        Assert.Equal(15998.0 / 15999, motion.AccelXG, 10); // 整数中点为 1002，而非 1001.5。
        Assert.Equal(-1, motion.AccelYG, 10);
        Assert.Equal(0, motion.AccelZG, 10);
    }

    [Theory]
    [InlineData(9001, 15998.0 / 15999)]
    [InlineData(-6998, -16000.0 / 15999)]
    [InlineData(1002, 0)]
    [InlineData(-32768, -67540.0 / 15999)]
    public void Convert_AccelOddSpanPreservesIntegerMidpoint(short raw, double expected)
    {
        var state = Raw();
        state.AccelX = raw;
        Assert.Equal(expected, DualSenseCalibration.Parse(FactoryReport(), ConnectionType.Usb)
            .Convert(state)!.Value.AccelXG, 10);
    }

    [Fact]
    public void Parse_ShortExtremesPromoteArithmeticToInt()
    {
        byte[] report = FactoryReport();
        foreach (int offset in new[] { 1, 3, 5, 9, 13, 17, 25, 29, 33 })
            report = WithValue(report, offset, short.MinValue);
        foreach (int offset in new[] { 7, 11, 15, 19, 21, 23, 27, 31 })
            report = WithValue(report, offset, short.MaxValue);
        var calibration = DualSenseCalibration.Parse(report, ConnectionType.Usb);
        Assert.Equal(DualSenseCalibrationStatus.Factory, calibration.Status);
        var state = new DualSenseInputState
        {
            IsFullReport = true, GyroPitch = short.MinValue, GyroYaw = short.MaxValue,
            GyroRoll = -1, AccelX = short.MinValue, AccelY = short.MaxValue, AccelZ = 0,
        };
        var motion = calibration.Convert(state)!.Value;
        Assert.Equal(-32768.0 * 65534 / 65535, motion.GyroPitchDps, 8);
        Assert.Equal(32767.0 * 65534 / 65535, motion.GyroYawDps, 8);
        Assert.Equal(-65534.0 / 65535, motion.GyroRollDps, 10);
        Assert.Equal(-65536.0 / 65535, motion.AccelXG, 10);
        Assert.Equal(65534.0 / 65535, motion.AccelYG, 10);
        Assert.Equal(0, motion.AccelZG);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(40)]
    [InlineData(42)]
    public void Parse_InvalidLengthsFallBack(int size) => AssertFallback(
        DualSenseCalibration.Parse(new byte[size], ConnectionType.Usb), "长度");

    [Fact]
    public void Parse_WrongIdFallsBack()
    {
        byte[] report = FactoryReport();
        report[0] = 0x06;
        AssertFallback(DualSenseCalibration.Parse(report, ConnectionType.Usb), "ID");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(35)]
    [InlineData(36)]
    [InlineData(37)]
    [InlineData(40)]
    public void Parse_BluetoothDetectsCorruptionButUsbDoesNotCheckCrc(int offset)
    {
        byte[] report = FactoryReport();
        report[offset] ^= 1;
        AssertFallback(DualSenseCalibration.Parse(report, ConnectionType.Bluetooth), "CRC");
        Assert.Equal(DualSenseCalibrationStatus.Factory,
            DualSenseCalibration.Parse(report, ConnectionType.Usb).Status);
    }

    [Fact]
    public void Parse_BluetoothRequiresFeatureSeedNotOutputSeed()
    {
        byte[] report = FactoryReport();
        BinaryPrimitives.WriteUInt32LittleEndian(report.AsSpan(37), Crc32.Compute(0xA2, report.AsSpan(0, 37)));
        AssertFallback(DualSenseCalibration.Parse(report, ConnectionType.Bluetooth), "CRC");
    }

    [Theory]
    [InlineData(1, 7, 9)]
    [InlineData(3, 11, 13)]
    [InlineData(5, 15, 17)]
    public void Parse_AnyZeroGyroDenominatorFallsBackEntireGroup(int bias, int plus, int minus)
    {
        byte[] report = WithValue(WithValue(WithValue(FactoryReport(), bias, 321), plus, 321), minus, 321);
        AssertFallback(DualSenseCalibration.Parse(report, ConnectionType.Usb), "陀螺仪");
    }

    [Theory]
    [InlineData(23, 25, 50)]
    [InlineData(27, 29, 50)]
    [InlineData(31, 33, 50)]
    [InlineData(23, 25, 49)]
    [InlineData(27, 29, 49)]
    [InlineData(31, 33, 49)]
    public void Parse_NonPositiveAccelSpanFallsBackEntireGroup(int plus, int minus, short value)
    {
        byte[] report = WithValue(WithValue(FactoryReport(), plus, value), minus, 50);
        AssertFallback(DualSenseCalibration.Parse(report, ConnectionType.Usb), "加速度计");
    }

    [Theory]
    [InlineData(-700)]
    [InlineData(-701)]
    public void Parse_NonPositiveSpeedSumFallsBack(short plus) => AssertFallback(
        DualSenseCalibration.Parse(WithValue(FactoryReport(), 19, plus), ConnectionType.Usb), "转速");

    [Fact]
    public void Parse_NoInventedThresholdRejectsSmallPositiveSpans()
    {
        byte[] report = WithValue(WithValue(FactoryReport(), 19, 1), 21, 0);
        report = WithValue(WithValue(WithValue(report, 1, 0), 7, 1), 9, 0);
        report = WithValue(WithValue(report, 23, 1), 25, 0);
        Assert.Equal(DualSenseCalibrationStatus.Factory, DualSenseCalibration.Parse(report, ConnectionType.Usb).Status);
    }

    [Theory]
    [InlineData(ConnectionType.Usb)]
    [InlineData(ConnectionType.Bluetooth)]
    public void Read_RequestsCalibrationReportOnce(ConnectionType connection)
    {
        int calls = 0;
        var calibration = DualSenseCalibration.Read(connection, buffer =>
        {
            calls++;
            Assert.Equal(41, buffer.Length);
            Assert.Equal(0x05, buffer[0]);
            Assert.All(buffer.Skip(1), value => Assert.Equal(0, value));
            FactoryReport().CopyTo(buffer, 0);
        });
        Assert.Equal(1, calls);
        Assert.Equal(DualSenseCalibrationStatus.Factory, calibration.Status);
    }

    [Theory]
    [InlineData(ConnectionType.Usb)]
    [InlineData(ConnectionType.Bluetooth)]
    public void Read_ExceptionIsObservableAndDoesNotPreventNominalMotion(ConnectionType connection)
    {
        var calibration = DualSenseCalibration.Read(connection, _ => throw new IOException("feature unavailable"));
        AssertFallback(calibration, "feature unavailable");
        Assert.Contains(nameof(IOException), calibration.FailureReason);
    }

    [Theory]
    [InlineData(ConnectionType.Usb)]
    [InlineData(ConnectionType.Bluetooth)]
    public void Read_UnfilledBufferIsNotClaimedAsSuccessful(ConnectionType connection) =>
        AssertFallback(DualSenseCalibration.Read(connection, _ => { }));

    [Fact]
    public void Nominal_IsNotReadAndSimpleReportNeverHasMotion()
    {
        Assert.Equal(DualSenseCalibrationStatus.NotRead, DualSenseCalibration.Nominal.Status);
        Assert.Null(DualSenseCalibration.Nominal.FailureReason);
        AssertNominalMotion(DualSenseCalibration.Nominal);
        Assert.Null(DualSenseCalibration.Nominal.Convert(default));
        Assert.Null(DualSenseCalibration.Parse(FactoryReport(), ConnectionType.Usb).Convert(default));
        Assert.Null(DualSenseCalibration.Parse([], ConnectionType.Usb).Convert(default));
    }

    [Theory]
    [InlineData(1000, 1100, 100)]
    [InlineData(2000, 300, -200)]
    [InlineData(-2000, 300, -200)]
    public void Parse_GyroBiasOutsideEndpointsUsesBothAbsoluteDistances(short bias, short plus, short minus)
    {
        byte[] report = WithValue(WithValue(WithValue(FactoryReport(), 1, bias), 7, plus), 9, minus);
        var calibration = DualSenseCalibration.Parse(report, ConnectionType.Usb);
        Assert.Equal(DualSenseCalibrationStatus.Factory, calibration.Status);
        double denominator = Math.Abs((int)plus - bias) + Math.Abs((int)minus - bias);
        Assert.Equal(100.0 * 1200 / denominator, calibration.Convert(Raw())!.Value.GyroPitchDps, 10);
        // 解析另一个设备的校准，不得覆盖首个设备的系数。
        _ = DualSenseCalibration.Parse(FactoryReport(), ConnectionType.Usb);
        Assert.Equal(100.0 * 1200 / denominator, calibration.Convert(Raw())!.Value.GyroPitchDps, 10);
    }

    [Fact]
    public void Parse_UnknownConnectionFallsBack() =>
        AssertFallback(DualSenseCalibration.Parse(FactoryReport(), (ConnectionType)99), "连接");

    [Fact]
    public void Read_NullReaderIsAProgrammingError() =>
        Assert.Throws<ArgumentNullException>(() => DualSenseCalibration.Read(ConnectionType.Usb, null!));

    [Fact]
    public void Parse_DoesNotRetainMutableReportBuffer()
    {
        byte[] report = FactoryReport();
        var calibration = DualSenseCalibration.Parse(report, ConnectionType.Usb);
        var before = calibration.Convert(Raw());
        Array.Clear(report);
        Assert.Equal(before, calibration.Convert(Raw()));
    }

    private static void AssertFallback(DualSenseCalibration calibration, string? reason = null)
    {
        Assert.Equal(DualSenseCalibrationStatus.NominalFallback, calibration.Status);
        Assert.False(string.IsNullOrWhiteSpace(calibration.FailureReason));
        if (reason is not null)
            Assert.Contains(reason, calibration.FailureReason, StringComparison.OrdinalIgnoreCase);
        AssertNominalMotion(calibration);
    }

    private static void AssertNominalMotion(DualSenseCalibration calibration)
    {
        var raw = Raw();
        var motion = calibration.Convert(raw)!.Value;
        Assert.Equal(raw.GyroPitch / 64.0, motion.GyroPitchDps);
        Assert.Equal(raw.GyroYaw / 64.0, motion.GyroYawDps);
        Assert.Equal(raw.GyroRoll / 64.0, motion.GyroRollDps);
        Assert.Equal(raw.AccelX / 8192.0, motion.AccelXG);
        Assert.Equal(raw.AccelY / 8192.0, motion.AccelYG);
        Assert.Equal(raw.AccelZ / 8192.0, motion.AccelZG);
    }
}
