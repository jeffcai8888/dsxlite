using NAudio.Wave;

namespace DsxLite.Core.Haptics;

/// <summary>
/// Generates the 4-channel 16-bit PCM stream the DualSense expects on its USB audio
/// endpoint: channels 0/1 (speaker/headset) stay silent, channels 2/3 drive the
/// left/right haptic actuators. Each side supports a continuous waveform plus
/// a decaying one-shot pulse.
/// </summary>
public sealed class HapticsWaveProvider : IWaveProvider
{
    public const int ChannelCount = 4;
    public const int LeftHapticChannel = 2;
    public const int RightHapticChannel = 3;

    private const int BytesPerSample = 2;
    private const int BytesPerFrame = BytesPerSample * ChannelCount;

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
        WaveFormat = WaveFormat.CreateCustomFormat(
            WaveFormatEncoding.Pcm, sampleRate, ChannelCount,
            sampleRate * BytesPerFrame, BytesPerFrame, 8 * BytesPerSample);
    }

    public WaveFormat WaveFormat { get; }

    private sealed class ChannelRuntime
    {
        public volatile HapticChannelSettings Settings = new();
        public double Phase;
        public float PulseEnvelope; // written by UI thread, decayed on audio thread
    }

    private static ChannelRuntime Channel(HapticSide side, ChannelRuntime[] channels) =>
        channels[side == HapticSide.Left ? 0 : 1];

    public void SetChannel(HapticSide side, HapticChannelSettings settings) =>
        Channel(side, _channels).Settings = settings;

    /// <summary>Plays a short decaying burst at full amplitude on top of the continuous signal.</summary>
    public void TriggerPulse(HapticSide side) => Channel(side, _channels).PulseEnvelope = 1f;

    public int Read(Span<byte> buffer)
    {
        var rented = new byte[buffer.Length];
        int read = Read(rented, 0, rented.Length);
        rented.AsSpan(0, read).CopyTo(buffer);
        return read;
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        int frames = count / BytesPerFrame;
        Span<byte> span = buffer.AsSpan(offset, frames * BytesPerFrame);
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

                if (amplitude <= 0.0001)
                    continue;

                double sample = Wave(c, s) * amplitude;
                short pcm = (short)Math.Clamp(sample * short.MaxValue, short.MinValue, short.MaxValue);
                int sampleOffset = frame * BytesPerFrame + (LeftHapticChannel + ch) * BytesPerSample;
                BitConverter.TryWriteBytes(span.Slice(sampleOffset, BytesPerSample), pcm);

                c.Phase += s.Frequency / _sampleRate;
                if (c.Phase >= 1.0)
                    c.Phase -= 1.0;
            }
        }

        return frames * BytesPerFrame;
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
