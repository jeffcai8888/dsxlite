namespace DsxLite.Core.DualSense;

/// <summary>
/// Adaptive trigger effect modes as documented by the community and used by DS4Windows/DSX.
/// </summary>
public enum TriggerEffectMode : byte
{
    Off = 0x00,
    ContinuousResistance = 0x01,
    SectionResistance = 0x02,
    Feedback = 0x21,
    Weapon = 0x22,
    Vibration = 0x23,
    Bow = 0x25,
    Galloping = 0x26,
    Machine = 0x27,
}

public readonly record struct TriggerParamSpec(string Label, int Min, int Max, int Default);

/// <summary>
/// One 10-byte adaptive trigger effect block: mode byte + 9 parameter bytes.
/// </summary>
public sealed class TriggerEffect
{
    public TriggerEffectMode Mode { get; set; } = TriggerEffectMode.Off;
    public byte[] Params { get; } = new byte[9];

    public byte[] ToBytes()
    {
        var bytes = new byte[10];
        bytes[0] = (byte)Mode;
        Array.Copy(Params, 0, bytes, 1, 9);
        return bytes;
    }

    public TriggerEffect Clone()
    {
        var clone = new TriggerEffect { Mode = Mode };
        Array.Copy(Params, clone.Params, 9);
        return clone;
    }

    public static TriggerEffect Off() => new();

    public static TriggerEffect ContinuousResistance(int start, int force) =>
        Build(TriggerEffectMode.ContinuousResistance, start, force);

    public static TriggerEffect SectionResistance(int start, int end, int force) =>
        Build(TriggerEffectMode.SectionResistance, start, end, force);

    public static TriggerEffect Feedback(int position, int strength) =>
        Build(TriggerEffectMode.Feedback, position, strength);

    public static TriggerEffect Weapon(int start, int end, int strength) =>
        Build(TriggerEffectMode.Weapon, start, end, strength);

    public static TriggerEffect Vibration(int position, int amplitude, int frequency) =>
        Build(TriggerEffectMode.Vibration, position, amplitude, frequency);

    public static TriggerEffect Bow(int start, int end, int strength, int snapForce) =>
        Build(TriggerEffectMode.Bow, start, end, strength, snapForce);

    public static TriggerEffect Galloping(int start, int end, int firstFoot, int secondFoot, int frequency) =>
        Build(TriggerEffectMode.Galloping, start, end, firstFoot, secondFoot, frequency);

    public static TriggerEffect Machine(int start, int end, int strengthA, int strengthB, int frequency, int period) =>
        Build(TriggerEffectMode.Machine, start, end, strengthA, strengthB, frequency, period);

    private static TriggerEffect Build(TriggerEffectMode mode, params int[] values)
    {
        var effect = new TriggerEffect { Mode = mode };
        var spec = GetParamSpec(mode);
        for (int i = 0; i < values.Length && i < 9; i++)
        {
            int v = values[i];
            if (i < spec.Length)
                v = Math.Clamp(v, spec[i].Min, spec[i].Max);
            effect.Params[i] = (byte)Math.Clamp(v, 0, 255);
        }
        return effect;
    }

    /// <summary>Parameter metadata for UI: label, valid range, sensible default.</summary>
    public static TriggerParamSpec[] GetParamSpec(TriggerEffectMode mode) => mode switch
    {
        TriggerEffectMode.Off => [],
        TriggerEffectMode.ContinuousResistance =>
        [
            new("起始位置", 0, 9, 0),
            new("力度", 0, 8, 4),
        ],
        TriggerEffectMode.SectionResistance =>
        [
            new("起始位置", 0, 9, 2),
            new("结束位置", 0, 9, 6),
            new("力度", 0, 8, 4),
        ],
        TriggerEffectMode.Feedback =>
        [
            new("位置", 0, 9, 4),
            new("强度", 0, 8, 4),
        ],
        TriggerEffectMode.Weapon =>
        [
            new("起始位置", 2, 7, 2),
            new("结束位置", 3, 8, 7),
            new("强度", 0, 8, 8),
        ],
        TriggerEffectMode.Vibration =>
        [
            new("位置", 0, 9, 4),
            new("振幅", 0, 8, 4),
            new("频率", 0, 255, 40),
        ],
        TriggerEffectMode.Bow =>
        [
            new("起始位置", 0, 8, 0),
            new("结束位置", 1, 8, 8),
            new("强度", 0, 8, 4),
            new("回弹力度", 0, 8, 4),
        ],
        TriggerEffectMode.Galloping =>
        [
            new("起始位置", 0, 8, 0),
            new("结束位置", 1, 9, 9),
            new("第一脚", 0, 6, 2),
            new("第二脚", 1, 7, 4),
            new("频率", 0, 255, 40),
        ],
        TriggerEffectMode.Machine =>
        [
            new("起始位置", 0, 8, 0),
            new("结束位置", 1, 9, 9),
            new("强度A", 0, 7, 4),
            new("强度B", 0, 7, 4),
            new("频率", 0, 255, 40),
            new("周期", 0, 255, 20),
        ],
        _ => [],
    };
}
