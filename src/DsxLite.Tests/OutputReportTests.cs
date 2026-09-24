using DsxLite.Core.DualSense;

namespace DsxLite.Tests;

public class OutputReportTests
{
    private static byte[] BuildUsb(DualSenseOutputState state)
    {
        byte seq = 0;
        return state.BuildReport(ConnectionType.Usb, ref seq);
    }

    [Fact]
    public void Usb_DefaultReport_HasIdFlagsAndZeroedEffects()
    {
        byte[] report = BuildUsb(new DualSenseOutputState());

        Assert.Equal(48, report.Length);
        Assert.Equal(0x02, report[0]);
        // flag0: compatible vibration + haptics select, no trigger motors
        Assert.Equal(0x03, report[1]);
        // flag1: mute led + power save + lightbar + player leds
        Assert.Equal(0x17, report[2]);
        // trigger effect blocks fully zeroed
        for (int i = 11; i <= 30; i++)
            Assert.Equal(0, report[i]);
    }

    [Fact]
    public void Usb_RightTriggerEffect_SetsFlagAndBlockAtOffset11()
    {
        var state = new DualSenseOutputState
        {
            RightTriggerEffect = TriggerEffect.Vibration(4, 6, 80),
        };

        byte[] report = BuildUsb(state);

        Assert.Equal(0x03 | 0x04, report[1]); // right trigger motor enable
        Assert.Equal(0x23, report[11]);       // mode
        Assert.Equal(4, report[12]);          // position
        Assert.Equal(6, report[13]);          // amplitude
        Assert.Equal(80, report[14]);         // frequency
        Assert.Equal(0, report[21]);          // left trigger untouched
    }

    [Fact]
    public void Usb_LeftTriggerEffect_SetsFlagAndBlockAtOffset21()
    {
        var state = new DualSenseOutputState
        {
            LeftTriggerEffect = TriggerEffect.Weapon(2, 8, 8),
        };

        byte[] report = BuildUsb(state);

        Assert.Equal(0x03 | 0x08, report[1]); // left trigger motor enable
        Assert.Equal(0x22, report[21]);
        Assert.Equal(2, report[22]);
        Assert.Equal(8, report[23]);
        Assert.Equal(8, report[24]);
    }

    [Fact]
    public void Usb_BothTriggers_SetBothFlags()
    {
        var state = new DualSenseOutputState
        {
            LeftTriggerEffect = TriggerEffect.Feedback(5, 5),
            RightTriggerEffect = TriggerEffect.Bow(0, 8, 4, 4),
        };

        byte[] report = BuildUsb(state);

        Assert.Equal(0x03 | 0x04 | 0x08, report[1]);
        Assert.Equal(0x25, report[11]); // right = Bow
        Assert.Equal(0x21, report[21]); // left = Feedback
    }

    [Fact]
    public void Usb_RumbleAndLeds_LandAtExpectedOffsets()
    {
        var state = new DualSenseOutputState
        {
            MotorLeft = 200,
            MotorRight = 100,
            LightbarRed = 255,
            LightbarGreen = 128,
            LightbarBlue = 64,
            PlayerLeds = 0x15,
            MuteLed = 1,
            LightbarSetup = 2,
        };

        byte[] report = BuildUsb(state);

        Assert.Equal(100, report[3]);  // motor right
        Assert.Equal(200, report[4]);  // motor left
        Assert.Equal(1, report[9]);    // mute led
        Assert.Equal(2, report[42]);   // lightbar setup
        Assert.Equal(0x15, report[44]); // player leds
        Assert.Equal(255, report[45]);
        Assert.Equal(128, report[46]);
        Assert.Equal(64, report[47]);
    }

    [Fact]
    public void Bt_ReportStructure_HeaderTagAndSeq()
    {
        var state = new DualSenseOutputState();
        byte seq = 0;

        byte[] first = state.BuildReport(ConnectionType.Bluetooth, ref seq);
        byte[] second = state.BuildReport(ConnectionType.Bluetooth, ref seq);

        Assert.Equal(78, first.Length);
        Assert.Equal(0x31, first[0]);
        Assert.Equal(0x10, first[2]); // magic tag
        Assert.Equal(0x00, first[1]); // seq 0 in high nibble
        Assert.Equal(0x10, second[1]); // seq 1 in high nibble
    }

    [Fact]
    public void Bt_TriggerEffectBlock_AtPayloadOffset10()
    {
        var state = new DualSenseOutputState
        {
            RightTriggerEffect = TriggerEffect.SectionResistance(2, 6, 8),
        };
        byte seq = 0;

        byte[] report = state.BuildReport(ConnectionType.Bluetooth, ref seq);

        Assert.Equal(0x02, report[13]); // mode at payload offset 10 + header 3
        Assert.Equal(2, report[14]);
        Assert.Equal(6, report[15]);
        Assert.Equal(8, report[16]);
    }

    [Fact]
    public void Bt_Crc32_MatchesTrailer()
    {
        var state = new DualSenseOutputState
        {
            MotorLeft = 55,
            LightbarBlue = 200,
            RightTriggerEffect = TriggerEffect.Machine(0, 9, 7, 7, 40, 20),
        };
        byte seq = 0;

        byte[] report = state.BuildReport(ConnectionType.Bluetooth, ref seq);

        uint expected = Crc32.Compute(0xA2, report.AsSpan(0, report.Length - 4));
        uint actual = BitConverter.ToUInt32(report, report.Length - 4);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Usb_ClearingRumbleBits_ProducesZeroFlag0()
    {
        // Audio-driven haptics require flag0 bits 0/1 cleared (verified by hardware probe).
        var state = new DualSenseOutputState
        {
            EnableCompatibleVibration = false,
            EnableHapticsSelect = false,
        };

        byte[] report = BuildUsb(state);

        Assert.Equal(0x00, report[1]);
    }

    [Fact]
    public void Usb_AudioRouting_FlagsAndFields()
    {
        var state = new DualSenseOutputState
        {
            EnableAudioControl = true,
            AudioControl = 0x30,
            EnableSpeakerVolume = true,
            SpeakerVolume = 0x64,
            EnableAudioControl2 = true,
            AudioControl2 = 0x02,
        };

        byte[] report = BuildUsb(state);

        Assert.Equal(0x03 | 0x20 | 0x80, report[1]);  // rumble defaults + speaker vol + audio control
        Assert.Equal(0x17 | 0x80, report[2]);          // flag1 + audio_control2 enable
        Assert.Equal(0x64, report[6]);                 // speaker volume
        Assert.Equal(0x30, report[8]);                 // audio control
        Assert.Equal(0x02, report[38]);                // audio control2 (payload 37 + report id)
    }
}
