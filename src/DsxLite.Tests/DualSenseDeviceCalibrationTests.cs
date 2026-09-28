using System.Collections.Concurrent;
using DsxLite.Core.DualSense;
using HidSharp;

namespace DsxLite.Tests;

public class DualSenseDeviceCalibrationTests
{
    [Theory]
    [InlineData(ConnectionType.Usb)]
    [InlineData(ConnectionType.Bluetooth)]
    public void Open_ReadsCalibrationBeforeInputAndPublishesPairedSnapshot(ConnectionType connection)
    {
        var hid = new FakeDevice();
        using var device = new DualSenseDevice(hid, connection);
        using var changed = new AutoResetEvent(false);
        device.StateChanged += (_, _) => changed.Set();
        Assert.Same(DualSenseInputSnapshot.Empty, device.CurrentSnapshot);
        Assert.True(device.Open());
        Assert.Equal(DualSenseCalibrationStatus.Factory, device.CurrentSnapshot.Calibration.Status);
        Assert.Null(device.CurrentSnapshot.Motion);
        hid.Stream!.Reports.Add(Input(connection));
        Assert.True(changed.WaitOne(TimeSpan.FromSeconds(5)));
        var snapshot = device.CurrentSnapshot;
        Assert.True(hid.Stream.ReadSawCalibration);
        Assert.Equal(snapshot.Raw, device.CurrentState);
        Assert.Equal((short)100, snapshot.Raw.GyroPitch);
        Assert.Equal(60, snapshot.Motion!.Value.GyroPitchDps);
        Assert.Equal(((short)100, (short)-200, (short)300), device.GyroBias);
        if (connection == ConnectionType.Bluetooth)
        {
            hid.Stream.Reports.Add([1, 128, 128, 128, 128, 8, 0, 0, 0, 0]);
            Assert.True(changed.WaitOne(TimeSpan.FromSeconds(5)));
            Assert.Null(device.CurrentSnapshot.Motion);
            Assert.Equal(60, snapshot.Motion.Value.GyroPitchDps); // 已持有的上一帧快照保持不变。
        }
    }

    [Fact]
    public void Open_ReadFailureStillPublishesNominalFullInput()
    {
        var hid = new FakeDevice { FeatureFailure = new IOException("fake feature failure") };
        using var device = new DualSenseDevice(hid, ConnectionType.Usb);
        using var changed = new AutoResetEvent(false);
        device.StateChanged += (_, _) => changed.Set();
        Assert.True(device.Open());
        Assert.Equal(DualSenseCalibrationStatus.NominalFallback, device.CurrentSnapshot.Calibration.Status);
        Assert.Contains("fake feature failure", device.CurrentSnapshot.Calibration.FailureReason);
        hid.Stream!.Reports.Add(Input(ConnectionType.Usb));
        Assert.True(changed.WaitOne(TimeSpan.FromSeconds(5)));
        Assert.Equal(100 / 64.0, device.CurrentSnapshot.Motion!.Value.GyroPitchDps);
    }

    [Theory]
    [InlineData(ConnectionType.Usb)]
    [InlineData(ConnectionType.Bluetooth)]
    public void Dispose_ReadOnlySessionNeverWritesAnOutputReport(ConnectionType connection)
    {
        var hid = new FakeDevice();
        var device = new DualSenseDevice(hid, connection);
        Assert.True(device.Open());
        device.Dispose();
        Assert.Equal(0, hid.Stream!.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Dispose_OutputSessionStillAttemptsNeutralReset(bool failWrites)
    {
        var hid = new FakeDevice { FailWrites = failWrites };
        var device = new DualSenseDevice(hid, ConnectionType.Usb);
        Assert.True(device.Open());
        device.ApplyOutput();
        device.Dispose();
        Assert.Equal(2, hid.Stream!.Writes);
    }

    [Fact]
    public void Reopen_DropsPreviousInputCalibrationAndOutputOwnership()
    {
        var hid = new FakeDevice();
        using var device = new DualSenseDevice(hid, ConnectionType.Usb);
        using var changed = new AutoResetEvent(false);
        using var disconnected = new AutoResetEvent(false);
        device.StateChanged += (_, _) => changed.Set();
        device.Disconnected += (_, _) => disconnected.Set();
        Assert.True(device.Open());
        hid.Stream!.Reports.Add(Input(ConnectionType.Usb));
        Assert.True(changed.WaitOne(TimeSpan.FromSeconds(5)));
        device.ApplyOutput();
        device.Dispose();
        Assert.True(disconnected.WaitOne(TimeSpan.FromSeconds(5)));
        hid.FeatureFailure = new IOException("reopen failed calibration");
        Assert.True(device.Open());
        Assert.Equal(default, device.CurrentState);
        Assert.Null(device.CurrentSnapshot.Motion);
        Assert.Equal(DualSenseCalibrationStatus.NominalFallback, device.CurrentSnapshot.Calibration.Status);
        Assert.Equal(default, device.GyroBias);
        device.Dispose();
        Assert.Equal(0, hid.Stream!.Writes);
    }

    private static byte[] Input(ConnectionType connection)
    {
        var report = new byte[connection == ConnectionType.Usb ? 64 : 78];
        report[0] = connection == ConnectionType.Usb ? (byte)1 : (byte)0x31;
        report[(connection == ConnectionType.Usb ? 1 : 2) + 15] = 100;
        return report;
    }

    private sealed class FakeDevice : HidDevice
    {
        public Exception? FeatureFailure { get; set; }
        public bool FailWrites { get; init; }
        public FakeStream? Stream { get; private set; }
        public override int ProductID => DualSenseIds.ProductId;
        public override int ReleaseNumberBcd => 0;
        public override int VendorID => DualSenseIds.VendorId;
        public override string DevicePath => "fake-calibration-device";
        public override string GetFileSystemName() => "fake";
        public override int GetMaxInputReportLength() => 78;
        public override int GetMaxOutputReportLength() => 78;
        public override int GetMaxFeatureReportLength() => 41;
        public override string GetProductName(GetStringFlags flags) => "Fake DualSense";
        protected override DeviceStream OpenDeviceDirectly(OpenConfiguration configuration) =>
            Stream = new FakeStream(this, FeatureFailure, FailWrites);
    }

    private sealed class FakeStream(FakeDevice device, Exception? featureFailure, bool failWrites) : HidStream(device)
    {
        public BlockingCollection<byte[]> Reports { get; } = new();
        public bool FeatureRead { get; private set; }
        public bool ReadSawCalibration { get; private set; }
        public int Writes { get; private set; }
        public override int ReadTimeout { get; set; }
        public override int WriteTimeout { get; set; }
        public override void GetFeature(byte[] buffer, int offset, int count)
        {
            Assert.Equal(0x05, buffer[offset]);
            Assert.Equal(41, count);
            FeatureRead = true;
            if (featureFailure is not null) throw featureFailure;
            DualSenseCalibrationTests.FactoryReport().CopyTo(buffer, offset);
        }
        public override int Read(byte[] buffer, int offset, int count)
        {
            ReadSawCalibration = FeatureRead;
            if (!Reports.TryTake(out byte[]? report, TimeSpan.FromSeconds(5)))
                throw new IOException("Fake stream closed");
            report.CopyTo(buffer, offset);
            return report.Length;
        }
        public override void Write(byte[] buffer, int offset, int count)
        {
            Writes++;
            if (failWrites) throw new IOException("Fake write failure");
        }
        public override void SetFeature(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) Reports.CompleteAdding();
            base.Dispose(disposing);
        }
    }
}
