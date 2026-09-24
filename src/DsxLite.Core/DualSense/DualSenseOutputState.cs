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
    private const byte Flag0SpeakerVolume = 0x20;
    private const byte Flag0AudioControl = 0x80;

    // valid_flag1 bits
    private const byte Flag1MuteLed = 0x01;
    private const byte Flag1PowerSave = 0x02;
    private const byte Flag1Lightbar = 0x04;
    private const byte Flag1PlayerLeds = 0x10;
    private const byte Flag1AudioControl2 = 0x80;

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
    /// flag0 bit0/bit1: classic rumble path. Both default on (previous behavior);
    /// clearing them selects the audio-driven haptics path for probing.
    /// </summary>
    public bool EnableCompatibleVibration { get; set; } = true;
    public bool EnableHapticsSelect { get; set; } = true;

    /// <summary>Audio routing controls (flag0 bit7/bit5, flag1 bit7).</summary>
    public bool EnableAudioControl { get; set; }
    public byte AudioControl { get; set; }
    public bool EnableSpeakerVolume { get; set; }
    public byte SpeakerVolume { get; set; } = 0x64;
    public bool EnableAudioControl2 { get; set; }
    public byte AudioControl2 { get; set; }

    /// <summary>
    /// Builds the wire-format output report. For Bluetooth, <paramref name="seq"/> is
    /// advanced (mod 16) after each report.
    /// </summary>
    public byte[] BuildReport(ConnectionType connection, ref byte seq)
    {
        var payload = new byte[47];

        byte flag0 = 0;
        if (EnableCompatibleVibration)
            flag0 |= Flag0CompatibleVibration;
        if (EnableHapticsSelect)
            flag0 |= Flag0HapticsSelect;
        if (RightTriggerEffect.Mode != TriggerEffectMode.Off)
            flag0 |= Flag0RightTriggerMotor;
        if (LeftTriggerEffect.Mode != TriggerEffectMode.Off)
            flag0 |= Flag0LeftTriggerMotor;
        if (EnableSpeakerVolume)
            flag0 |= Flag0SpeakerVolume;
        if (EnableAudioControl)
            flag0 |= Flag0AudioControl;

        byte flag1 = Flag1MuteLed | Flag1PowerSave | Flag1Lightbar | Flag1PlayerLeds;
        if (EnableAudioControl2)
            flag1 |= Flag1AudioControl2;

        payload[0] = flag0;
        payload[1] = flag1;
        payload[2] = MotorRight;
        payload[3] = MotorLeft;
        payload[5] = SpeakerVolume;
        payload[7] = AudioControl;
        payload[8] = MuteLed;
        // 9 power save control left at 0

        Array.Copy(RightTriggerEffect.ToBytes(), 0, payload, 10, 10);
        Array.Copy(LeftTriggerEffect.ToBytes(), 0, payload, 20, 10);

        payload[37] = AudioControl2;
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
