using DsxLite.Core.DualSense;

namespace DsxLite.Tests;

public class TriggerEffectTests
{
    [Fact]
    public void ToBytes_ModeAtByte0_ParamsFromByte1()
    {
        TriggerEffect effect = TriggerEffect.ContinuousResistance(3, 5);

        byte[] bytes = effect.ToBytes();

        Assert.Equal(10, bytes.Length);
        Assert.Equal((byte)TriggerEffectMode.ContinuousResistance, bytes[0]);
        Assert.Equal(3, bytes[1]);
        Assert.Equal(5, bytes[2]);
        Assert.Equal(0, bytes[3]); // unused params stay zero
    }

    [Fact]
    public void Factory_ClampsToSpecRange()
    {
        TriggerEffect effect = TriggerEffect.ContinuousResistance(99, 99);

        Assert.Equal(9, effect.Params[0]); // start max 9
        Assert.Equal(8, effect.Params[1]); // force max 8
    }

    [Fact]
    public void Weapon_ClampsStartAndEndToValidWindow()
    {
        TriggerEffect effect = TriggerEffect.Weapon(0, 0, 99);

        Assert.Equal(2, effect.Params[0]); // start min 2
        Assert.Equal(3, effect.Params[1]); // end min 3
        Assert.Equal(8, effect.Params[2]); // strength max 8
    }

    [Theory]
    [InlineData(TriggerEffectMode.Off, 0x00)]
    [InlineData(TriggerEffectMode.ContinuousResistance, 0x01)]
    [InlineData(TriggerEffectMode.SectionResistance, 0x02)]
    [InlineData(TriggerEffectMode.Feedback, 0x21)]
    [InlineData(TriggerEffectMode.Weapon, 0x22)]
    [InlineData(TriggerEffectMode.Vibration, 0x23)]
    [InlineData(TriggerEffectMode.Bow, 0x25)]
    [InlineData(TriggerEffectMode.Galloping, 0x26)]
    [InlineData(TriggerEffectMode.Machine, 0x27)]
    public void ModeBytes_MatchProtocolDocumentation(TriggerEffectMode mode, byte expected)
    {
        Assert.Equal(expected, (byte)mode);
    }

    [Fact]
    public void GetParamSpec_OffHasNoParams_AllOtherModesHaveSpecs()
    {
        foreach (TriggerEffectMode mode in Enum.GetValues<TriggerEffectMode>())
        {
            TriggerParamSpec[] spec = TriggerEffect.GetParamSpec(mode);
            if (mode == TriggerEffectMode.Off)
                Assert.Empty(spec);
            else
                Assert.NotEmpty(spec);
        }
    }

    [Fact]
    public void Clone_IsIndependentCopy()
    {
        TriggerEffect original = TriggerEffect.Machine(1, 9, 7, 5, 60, 30);
        TriggerEffect clone = original.Clone();

        Assert.Equal(original.ToBytes(), clone.ToBytes());

        clone.Params[0] = 99;
        Assert.NotEqual(clone.Params[0], original.Params[0]);
    }

    [Fact]
    public void Off_HasNoParams_AndModeZero()
    {
        byte[] bytes = TriggerEffect.Off().ToBytes();
        Assert.All(bytes, b => Assert.Equal(0, b));
    }
}
