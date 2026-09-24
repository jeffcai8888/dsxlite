namespace DsxLite.Core.DualSense;

/// <summary>
/// Mutable output state for a DualSense, serialized into HID output reports.
/// Layout follows the Linux hid-playstation dualsense_output_report_common struct
/// (47-byte payload; USB prefixes report ID 0x02, Bluetooth wraps it in report 0x31
/// with a sequence nibble, 0x10 tag and trailing CRC32 seeded with 0xA2).
/// </summary>
public sealed class DualSenseOutputState
{
    // valid_flag0 bits
    private const byte Flag0CompatibleVibration = 0x01;
    private const byte Flag0HapticsSelect = 0x02;
    private const byte Flag0RightTriggerMotor = 0x04;
    private const byte Flag0LeftTriggerMotor = 0x08;

    // valid_flag1 bits
    private const byte Flag1MuteLed = 0x01;
    private const byte Flag1PowerSave = 0x02;
    private const byte Flag1Lightbar = 0x04;
    private const byte Flag1PlayerLeds = 0x10;

    // valid_flag2 bits
    private const byte Flag2LightbarSetup = 0x02;

    /// <summary>Large (left) rumble motor 0-255.</summary>
    public byte MotorLeft { get; set; }

    /// <summary>Small (right) rumble motor 0-255.</summary>
    public byte MotorRight { get; set; }

    public byte LightbarRed { get; set; }
    public byte LightbarGreen { get; set; }
    public byte LightbarBlue { get; set; }

    /// <summary>5-bit player indicator LED mask.</summary>
    public byte PlayerLeds { get; set; }

    /// <summary>LED brightness: 0 = full, higher = dimmer.</summary>
    public byte LedBrightness { get; set; }

    /// <summary>0 = normal, 2 = light out.</summary>
    public byte LightbarSetup { get; set; }

    /// <summary>Mute button LED: 0 = off, 1 = on, 2 = pulsing.</summary>
    public byte MuteLed { get; set; }

    public TriggerEffect LeftTriggerEffect { get; set; } = TriggerEffect.Off();
    public TriggerEffect RightTriggerEffect { get; set; } = TriggerEffect.Off();

    /// <summary>
    /// Builds the wire-format output report. For Bluetooth, <paramref name="seq"/> is
    /// advanced (mod 16) after each report.
    /// </summary>
    public byte[] BuildReport(ConnectionType connection, ref byte seq)
    {
        var payload = new byte[47];

        byte flag0 = Flag0CompatibleVibration | Flag0HapticsSelect;
        if (RightTriggerEffect.Mode != TriggerEffectMode.Off)
            flag0 |= Flag0RightTriggerMotor;
        if (LeftTriggerEffect.Mode != TriggerEffectMode.Off)
            flag0 |= Flag0LeftTriggerMotor;

        payload[0] = flag0;
        payload[1] = Flag1MuteLed | Flag1PowerSave | Flag1Lightbar | Flag1PlayerLeds;
        payload[2] = MotorRight;
        payload[3] = MotorLeft;
        // 4-7 audio volumes / control left at 0 (untouched)
        payload[8] = MuteLed;
        // 9 power save control left at 0

        Array.Copy(RightTriggerEffect.ToBytes(), 0, payload, 10, 10);
        Array.Copy(LeftTriggerEffect.ToBytes(), 0, payload, 20, 10);

        payload[38] = Flag2LightbarSetup;
        payload[41] = LightbarSetup;
        payload[42] = LedBrightness;
        payload[43] = PlayerLeds;
        payload[44] = LightbarRed;
        payload[45] = LightbarGreen;
        payload[46] = LightbarBlue;

        if (connection == ConnectionType.Usb)
        {
            var report = new byte[DualSenseIds.OutputReportUsbSize];
            report[0] = DualSenseIds.OutputReportUsb;
            Array.Copy(payload, 0, report, 1, 47);
            return report;
        }

        var bt = new byte[DualSenseIds.OutputReportBtSize];
        bt[0] = DualSenseIds.OutputReportBt;
        bt[1] = (byte)(seq << 4);
        seq = (byte)((seq + 1) & 0x0F);
        bt[2] = 0x10; // magic tag required by the controller
        Array.Copy(payload, 0, bt, 3, 47);
        uint crc = Crc32.Compute(0xA2, bt.AsSpan(0, bt.Length - 4));
        BitConverter.TryWriteBytes(bt.AsSpan(bt.Length - 4), crc);
        return bt;
    }
}
