namespace DsxLite.Core.ViGEm;

/// <summary>
/// 将每秒角度表示的陀螺仪角速度映射为 Xbox 360 摇杆坐标。
/// </summary>
public static class GyroStickMapper
{
    /// <summary>摇杆满偏对应的角速度，单位为度/秒。</summary>
    public const double FullScaleDegreesPerSecond = 500.0;

    /// <summary>
    /// 偏航正值映射为 X 正值，俯仰正值映射为 Y 负值。
    /// 每轴正负满偏分别为 32767 和 -32768，超范围钳制，区间内向零截断。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">任一输入为 NaN 或无穷大。</exception>
    public static (short X, short Y) Map(double yawDps, double pitchDps)
    {
        if (!double.IsFinite(yawDps))
            throw new ArgumentOutOfRangeException(nameof(yawDps), yawDps, "偏航角速度必须为有限值。");
        if (!double.IsFinite(pitchDps))
            throw new ArgumentOutOfRangeException(nameof(pitchDps), pitchDps, "俯仰角速度必须为有限值。");

        return (ToAxis(yawDps), ToAxis(-pitchDps));
    }

    private static short ToAxis(double degreesPerSecond)
    {
        // 先归一化并钳制，再分别按正负端点缩放，避免极大有限输入溢出。
        double normalized = Math.Clamp(degreesPerSecond / FullScaleDegreesPerSecond, -1.0, 1.0);
        double scale = normalized < 0 ? -(double)short.MinValue : short.MaxValue;
        return (short)(normalized * scale);
    }
}
