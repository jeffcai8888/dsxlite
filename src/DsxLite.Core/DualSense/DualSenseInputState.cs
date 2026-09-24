namespace DsxLite.Core.DualSense;

public struct TouchPoint
{
    public bool Active;
    public int Id;
    public int X;
    public int Y;
}

/// <summary>
/// Parsed state of one DualSense input report. All values are raw device units.
/// </summary>
public struct DualSenseInputState
{
    /// <summary>False for the truncated Bluetooth 0x01 report (no gyro/touch/battery).</summary>
    public bool IsFullReport;

    public byte LeftStickX;
    public byte LeftStickY;
    public byte RightStickX;
    public byte RightStickY;
    public byte LeftTrigger;
    public byte RightTrigger;

    /// <summary>Hat value 0-7 clockwise from up, 8 = centered.</summary>
    public int DPadHat;

    public bool Square;
    public bool Cross;
    public bool Circle;
    public bool Triangle;
    public bool L1;
    public bool R1;
    public bool L2Button;
    public bool R2Button;
    public bool Create;
    public bool Options;
    public bool L3;
    public bool R3;
    public bool PS;
    public bool TouchpadClick;
    public bool MuteButton;

    // DualSense Edge extra buttons
    public bool Fn1;
    public bool Fn2;
    public bool LeftPaddle;
    public bool RightPaddle;

    public short GyroPitch;
    public short GyroYaw;
    public short GyroRoll;
    public short AccelX;
    public short AccelY;
    public short AccelZ;

    public TouchPoint Touch1;
    public TouchPoint Touch2;

    public int BatteryPercent;
    public BatteryState Battery;

    public readonly bool DPadUp => DPadHat is 0 or 1 or 7;
    public readonly bool DPadRight => DPadHat is 1 or 2 or 3;
    public readonly bool DPadDown => DPadHat is 3 or 4 or 5;
    public readonly bool DPadLeft => DPadHat is 5 or 6 or 7;
}
