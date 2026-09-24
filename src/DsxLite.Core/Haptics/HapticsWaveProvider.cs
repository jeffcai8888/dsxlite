using NAudio.Wave;

namespace DsxLite.Core.Haptics;

/// <summary>
/// Generates the 4-channel float PCM stream the DualSense expects on its USB audio
/// endpoint: channels 0/1 (speaker/headset) stay silent, channels 2/3 drive the
/// left/right haptic actuators. Each side supports a continuous waveform plus
/// a decaying one-shot pulse.
/// </summary>
public sealed class HapticsWaveProvider : ISampleProvider
{
    public const int ChannelCount = 4;
    public const int LeftHapticChannel = 2;
    public const int RightHapticChannel = 3;

    /// <summary>Output channels the left/right actuators are written to (probing aid).</summary>
    public int LeftOutputChannel { get; set; } = LeftHapticChannel;
    public int RightOutputChannel { get; set; } = RightHapticChannel;

    /// <summary>Pulse decay time constant in seconds.</summary>
    private const double PulseDecaySeconds = 0.15;

    private readonly int _sampleRate;
    private readonly double _pulseDecayPerSample;
    private readonly Random _noise = new();
    private readonly ChannelRuntime[] _channels = [new(), new()];

    public HapticsWaveProvider(int sampleRate)
    {
        _sampleRate = sampleRate;
        _pulseDecayPerSample = Math.Exp(-1.0 / (PulseDecaySeconds * sampleRate));
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, ChannelCount);
    }

    public WaveFormat WaveFormat { get; }

    private sealed class ChannelRuntime
    {
        public volatile HapticChannelSettings Settings = new();
        public double Phase;
        public float PulseEnvelope; // written by UI thread, decayed on audio thread
    }

    private ChannelRuntime Channel(HapticSide side) => _channels[side == HapticSide.Left ? 0 : 1];

    public void SetChannel(HapticSide side, HapticChannelSettings settings) =>
        Channel(side).Settings = settings;

    /// <summary>Plays a short decaying burst at full amplitude on top of the continuous signal.</summary>
    public void TriggerPulse(HapticSide side) => Channel(side).PulseEnvelope = 1f;

    public int Read(float[] buffer, int offset, int count) =>
        Read(buffer.AsSpan(offset, count));

    public int Read(Span<float> buffer)
    {
        int frames = buffer.Length / ChannelCount;
        Span<float> span = buffer[..(frames * ChannelCount)];
        span.Clear();

        for (int frame = 0; frame < frames; frame++)
        {
            for (int ch = 0; ch < 2; ch++)
            {
                ChannelRuntime c = _channels[ch];
                HapticChannelSettings s = c.Settings;

                double amplitude = Math.Max(s.Amplitude, c.PulseEnvelope);
                if (c.PulseEnvelope > 0.0001f)
                    c.PulseEnvelope *= (float)_pulseDecayPerSample;
                else
                    c.PulseEnvelope = 0f;

                if (amplitude > 0.0001)
                {
                    float sample = (float)(Wave(c, s) * amplitude);
                    int channel = ch == 0 ? LeftOutputChannel : RightOutputChannel;
                    span[frame * ChannelCount + channel] = sample;
                }

                c.Phase += s.Frequency / _sampleRate;
                if (c.Phase >= 1.0)
                    c.Phase -= 1.0;
            }
        }

        return frames * ChannelCount;
    }

    private double Wave(ChannelRuntime c, HapticChannelSettings s) => s.Waveform switch
    {
        HapticWaveform.Sine => Math.Sin(2.0 * Math.PI * c.Phase),
        HapticWaveform.Square => c.Phase < 0.5 ? 1.0 : -1.0,
        HapticWaveform.Pulse => c.Phase < 0.08 ? 1.0 : 0.0,
        HapticWaveform.Noise => _noise.NextDouble() * 2.0 - 1.0,
        _ => 0.0,
    };
}

public enum HapticSide
{
    Left,
    Right,
}
