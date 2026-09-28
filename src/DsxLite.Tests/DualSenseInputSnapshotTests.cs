using System.Buffers.Binary;
using DsxLite.Core.DualSense;

namespace DsxLite.Tests;

public class DualSenseInputSnapshotTests
{
    [Theory]
    [InlineData(ConnectionType.Usb)]
    [InlineData(ConnectionType.Bluetooth)]
    public void Create_MatchesParsedRawAndCalibratedMotion(ConnectionType connection)
    {
        byte[] report = new byte[connection == ConnectionType.Usb ? 64 : 78];
        report[0] = connection == ConnectionType.Usb ? (byte)0x01 : (byte)0x31;
        int offset = connection == ConnectionType.Usb ? 1 : 2;
        short[] values = [100, -200, 300, 9001, -8000, 1000];
        for (int axis = 0; axis < values.Length; axis++)
            BinaryPrimitives.WriteInt16LittleEndian(report.AsSpan(offset + 15 + axis * 2), values[axis]);
        report[offset] = 201;
        report[offset + 7] = 0x28;
        Assert.True(DualSenseReportParser.TryParse(report, connection, out var raw));
        var calibration = DualSenseCalibration.Parse(DualSenseCalibrationTests.FactoryReport(), connection);
        var snapshot = DualSenseInputSnapshot.Create(raw, calibration);
        Assert.Equal(raw, snapshot.Raw);
        Assert.True(snapshot.Raw.Cross);
        Assert.Equal(201, snapshot.Raw.LeftStickX);
        Assert.Same(calibration, snapshot.Calibration);
        Assert.Equal(calibration.Convert(raw), snapshot.Motion);
        Assert.Equal(60, snapshot.Motion!.Value.GyroPitchDps);
        Assert.Equal(-80, snapshot.Motion.Value.GyroYawDps);
        Assert.Equal(60, snapshot.Motion.Value.GyroRollDps);
    }

    [Fact]
    public void Create_SimpleReportDoesNotKeepPreviousMotion()
    {
        var calibration = DualSenseCalibration.Parse(DualSenseCalibrationTests.FactoryReport(), ConnectionType.Usb);
        var full = DualSenseInputSnapshot.Create(DualSenseCalibrationTests.Raw(), calibration);
        Assert.True(DualSenseReportParser.TryParse([1, 128, 129, 130, 131, 8, 0, 0, 11, 22],
            ConnectionType.Bluetooth, out var raw));
        var simple = DualSenseInputSnapshot.Create(raw, calibration);
        Assert.NotNull(full.Motion);
        Assert.Null(simple.Motion);
        Assert.False(simple.Raw.IsFullReport);
        Assert.Equal(11, simple.Raw.LeftTrigger);
        Assert.Same(calibration, simple.Calibration);
    }

    [Fact]
    public void Create_CopiesRawStructIncludingNestedTouchPoints()
    {
        var raw = DualSenseCalibrationTests.Raw();
        raw.Touch1 = new TouchPoint { X = 123 };
        var snapshot = DualSenseInputSnapshot.Create(raw, DualSenseCalibration.Nominal);
        raw.GyroPitch = 999;
        raw.Touch1.X = 456;
        var copy = snapshot.Raw;
        copy.Touch1.X = 789;
        Assert.Equal(100, snapshot.Raw.GyroPitch);
        Assert.Equal(123, snapshot.Raw.Touch1.X);
        Assert.Equal(100 / 64.0, snapshot.Motion!.Value.GyroPitchDps);
    }

    [Fact]
    public void Create_RejectsNullCalibration() =>
        Assert.Throws<ArgumentNullException>(() => DualSenseInputSnapshot.Create(default, null!));

    [Fact]
    public void Empty_HasNoMotionAndNotReadCalibration()
    {
        Assert.Equal(default, DualSenseInputSnapshot.Empty.Raw);
        Assert.Null(DualSenseInputSnapshot.Empty.Motion);
        Assert.Same(DualSenseCalibration.Nominal, DualSenseInputSnapshot.Empty.Calibration);
    }
}
