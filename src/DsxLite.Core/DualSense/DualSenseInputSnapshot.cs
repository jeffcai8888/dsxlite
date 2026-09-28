namespace DsxLite.Core.DualSense;

/// <summary>一次发布的不可变快照，包含原始输入、物理运动量和对应校准。</summary>
public sealed record DualSenseInputSnapshot
{
    private DualSenseInputSnapshot(DualSenseInputState raw, DualSenseCalibration calibration)
    {
        Raw = raw;
        Calibration = calibration;
        Motion = calibration.Convert(raw);
    }

    // Raw 及其嵌套 TouchPoint 均为值类型，调用方获取的是副本。
    public DualSenseInputState Raw { get; }
    public DualSenseMotion? Motion { get; }
    public DualSenseCalibration Calibration { get; }

    public static DualSenseInputSnapshot Empty { get; } = Create(default, DualSenseCalibration.Nominal);

    public static DualSenseInputSnapshot Create(DualSenseInputState raw, DualSenseCalibration calibration)
    {
        ArgumentNullException.ThrowIfNull(calibration);
        return new(raw, calibration);
    }
}
