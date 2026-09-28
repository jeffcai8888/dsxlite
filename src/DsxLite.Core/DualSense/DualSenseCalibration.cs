using System.Buffers.Binary;

namespace DsxLite.Core.DualSense;

public enum DualSenseCalibrationStatus
{
    NotRead,
    Factory,
    NominalFallback,
}

/// <summary>
/// 每个设备独立持有的不可变六轴校准。工厂换算遵循 Linux hid-playstation：
/// 陀螺仪零偏仅参与分母计算，不从实时输入中减去。
/// 任一校准参数无效时整组回退，不混用工厂参数与标称参数。
/// </summary>
public sealed class DualSenseCalibration
{
    private const byte FeatureCrcSeed = 0xA3;
    private const int CrcOffset = DualSenseIds.FeatureReportCalibrationSize - sizeof(uint);
    private readonly Axis _pitch;
    private readonly Axis _yaw;
    private readonly Axis _roll;
    private readonly Axis _accelX;
    private readonly Axis _accelY;
    private readonly Axis _accelZ;

    private readonly record struct Axis(int Bias, int Numerator, int Denominator)
    {
        public double Convert(short raw) => ((double)raw - Bias) * Numerator / Denominator;
    }

    private DualSenseCalibration(DualSenseCalibrationStatus status, string? failureReason,
        Axis pitch, Axis yaw, Axis roll, Axis accelX, Axis accelY, Axis accelZ,
        (short Pitch, short Yaw, short Roll) gyroBias = default)
    {
        Status = status;
        FailureReason = failureReason;
        _pitch = pitch;
        _yaw = yaw;
        _roll = roll;
        _accelX = accelX;
        _accelY = accelY;
        _accelZ = accelZ;
        GyroBias = gyroBias;
    }

    public DualSenseCalibrationStatus Status { get; }
    public string? FailureReason { get; }

    /// <summary>工厂零偏仅用于诊断；不要从输入中减去这些值。</summary>
    public (short Pitch, short Yaw, short Roll) GyroBias { get; }

    public static DualSenseCalibration Nominal { get; } = CreateNominal(DualSenseCalibrationStatus.NotRead, null);

    private static DualSenseCalibration CreateNominal(DualSenseCalibrationStatus status, string? reason)
    {
        var gyro = new Axis(0, 1, DualSenseIds.NominalRawGyroUnitsPerDegreeSec);
        var accel = new Axis(0, 1, DualSenseIds.NominalRawAccelUnitsPerG);
        return new(status, reason, gyro, gyro, gyro, accel, accel, accel);
    }

    private static DualSenseCalibration Fallback(string reason) =>
        CreateNominal(DualSenseCalibrationStatus.NominalFallback, reason);

    /// <summary>
    /// 两种连接均读取 0x05 报告。GetFeature 不提供实际返回长度，
    /// 此处只能验证已分配的缓冲区及其内容，不能证明底层传输长度完整。
    /// 读取此特性报告还会请求蓝牙切换为完整输入报告。
    /// </summary>
    public static DualSenseCalibration Read(ConnectionType connection, Action<byte[]> getFeature)
    {
        ArgumentNullException.ThrowIfNull(getFeature);
        var buffer = new byte[DualSenseIds.FeatureReportCalibrationSize];
        buffer[0] = DualSenseIds.FeatureReportCalibration;
        try
        {
            getFeature(buffer);
        }
        catch (Exception ex)
        {
            return Fallback($"读取校准特性报告失败（{ex.GetType().Name}）：{ex.Message}");
        }
        return Parse(buffer, connection);
    }

    /// <summary>严格验证一份 41 字节报告；蓝牙还需校验以 A3 为前缀的 CRC。</summary>
    public static DualSenseCalibration Parse(ReadOnlySpan<byte> report, ConnectionType connection)
    {
        if (connection is not (ConnectionType.Usb or ConnectionType.Bluetooth))
            return Fallback("不支持的校准连接类型。");
        if (report.Length != DualSenseIds.FeatureReportCalibrationSize)
            return Fallback($"校准报告长度无效：{report.Length} 字节，应为 {DualSenseIds.FeatureReportCalibrationSize} 字节。");
        if (report[0] != DualSenseIds.FeatureReportCalibration)
            return Fallback($"校准报告 ID 无效：0x{report[0]:X2}。");
        if (connection == ConnectionType.Bluetooth &&
            Crc32.Compute(FeatureCrcSeed, report[..CrcOffset]) != BinaryPrimitives.ReadUInt32LittleEndian(report[CrcOffset..]))
            return Fallback("蓝牙校准特性报告 CRC 校验不匹配。");

        int speed = ReadShort(report, 19) + ReadShort(report, 21);
        if (speed <= 0)
            return Fallback("陀螺仪校准转速合计必须为正数。");
        var pitch = GyroAxis(report, 1, 7, speed);
        var yaw = GyroAxis(report, 3, 11, speed);
        var roll = GyroAxis(report, 5, 15, speed);
        if (pitch.Denominator <= 0 || yaw.Denominator <= 0 || roll.Denominator <= 0)
            return Fallback("陀螺仪每个轴的校准分母都必须为正数。");
        var accelX = AccelAxis(report, 23);
        var accelY = AccelAxis(report, 27);
        var accelZ = AccelAxis(report, 31);
        if (accelX.Denominator <= 0 || accelY.Denominator <= 0 || accelZ.Denominator <= 0)
            return Fallback("加速度计每个轴的校准跨度都必须为正数。");

        // 所有操作数都是提升为 int 的有界 short 数值，因此正分母可保证
        // 任意原始 short 输入的 double 换算结果均为有限值。
        return new(DualSenseCalibrationStatus.Factory, null, pitch, yaw, roll, accelX, accelY, accelZ,
            ((short)ReadShort(report, 1), (short)ReadShort(report, 3), (short)ReadShort(report, 5)));
    }

    private static Axis GyroAxis(ReadOnlySpan<byte> report, int biasOffset, int plusOffset, int speed)
    {
        int bias = ReadShort(report, biasOffset);
        int denominator = Math.Abs(ReadShort(report, plusOffset) - bias) +
                          Math.Abs(ReadShort(report, plusOffset + 2) - bias);
        return new(0, speed, denominator);
    }

    private static Axis AccelAxis(ReadOnlySpan<byte> report, int plusOffset)
    {
        int plus = ReadShort(report, plusOffset);
        int span = plus - ReadShort(report, plusOffset + 2);
        return new(plus - span / 2, 2, span);
    }

    private static int ReadShort(ReadOnlySpan<byte> report, int offset) =>
        BinaryPrimitives.ReadInt16LittleEndian(report.Slice(offset, sizeof(short)));

    /// <summary>蓝牙简化报告不含运动数据，即使校准有效也返回空值。</summary>
    public DualSenseMotion? Convert(in DualSenseInputState raw) => !raw.IsFullReport ? null : new(
        _pitch.Convert(raw.GyroPitch), _yaw.Convert(raw.GyroYaw), _roll.Convert(raw.GyroRoll),
        _accelX.Convert(raw.AccelX), _accelY.Convert(raw.AccelY), _accelZ.Convert(raw.AccelZ));
}
