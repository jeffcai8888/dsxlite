namespace DsxLite.Core.DualSense;

/// <summary>校准后的运动数据，角速度单位为度/秒，加速度单位为标准重力加速度 g。</summary>
public readonly record struct DualSenseMotion(
    double GyroPitchDps, double GyroYawDps, double GyroRollDps,
    double AccelXG, double AccelYG, double AccelZG);
