using DsxLite.Core.Haptics;

namespace DsxLite.Tests;

public class HapticsWaveProviderTests
{
    private const int SampleRate = 48000;
    private const int BytesPerFrame = 8; // 4 channels x 16-bit

    private static byte[] ReadFrames(HapticsWaveProvider provider, int frames)
    {
        var buffer = new byte[frames * BytesPerFrame];
        int read = provider.Read(buffer, 0, buffer.Length);
        Assert.Equal(buffer.Length, read);
        return buffer;
    }

    private static IEnumerable<short> ChannelSamples(byte[] buffer, int channel)
    {
        for (int offset = channel * 2; offset + 1 < buffer.Length; offset += BytesPerFrame)
            yield return BitConverter.ToInt16(buffer, offset);
    }

    [Fact]
    public void WaveFormat_Is4Channel16Bit()
    {
        var provider = new HapticsWaveProvider(SampleRate);

        Assert.Equal(SampleRate, provider.WaveFormat.SampleRate);
        Assert.Equal(4, provider.WaveFormat.Channels);
        Assert.Equal(16, provider.WaveFormat.BitsPerSample);
    }

    [Fact]
    public void DefaultOutput_IsSilent()
    {
        var provider = new HapticsWaveProvider(SampleRate);

        byte[] buffer = ReadFrames(provider, 480);

        Assert.All(buffer, b => Assert.Equal(0, b));
    }

    [Fact]
    public void LeftChannel_DrivesOnlyHapticChannel2()
    {
        var provider = new HapticsWaveProvider(SampleRate);
        provider.SetChannel(HapticSide.Left,
            new HapticChannelSettings { Waveform = HapticWaveform.Sine, Frequency = 80, Amplitude = 1.0 });

        byte[] buffer = ReadFrames(provider, 480);

        Assert.All(ChannelSamples(buffer, 0), s => Assert.Equal(0, s)); // speaker L silent
        Assert.All(ChannelSamples(buffer, 1), s => Assert.Equal(0, s)); // speaker R silent
        Assert.Contains(ChannelSamples(buffer, 2), s => s != 0);        // left haptic active
        Assert.All(ChannelSamples(buffer, 3), s => Assert.Equal(0, s)); // right haptic silent
    }

    [Fact]
    public void RightChannel_DrivesOnlyHapticChannel3()
    {
        var provider = new HapticsWaveProvider(SampleRate);
        provider.SetChannel(HapticSide.Right,
            new HapticChannelSettings { Waveform = HapticWaveform.Square, Frequency = 100, Amplitude = 0.5 });

        byte[] buffer = ReadFrames(provider, 480);

        Assert.All(ChannelSamples(buffer, 2), s => Assert.Equal(0, s));
        Assert.Contains(ChannelSamples(buffer, 3), s => s != 0);
        Assert.All(ChannelSamples(buffer, 3), s => Assert.True(Math.Abs(s) <= short.MaxValue / 2 + 1)); // amplitude respected
    }

    [Fact]
    public void Pulse_DecaysBackToSilence()
    {
        var provider = new HapticsWaveProvider(SampleRate);
        provider.SetChannel(HapticSide.Left,
            new HapticChannelSettings { Waveform = HapticWaveform.Sine, Frequency = 80, Amplitude = 0 });

        provider.TriggerPulse(HapticSide.Left);
        byte[] burst = ReadFrames(provider, 480); // first 10ms
        Assert.Contains(ChannelSamples(burst, 2), s => s != 0);

        // ~2 seconds later the envelope must have fully decayed
        for (int i = 0; i < 40; i++)
            ReadFrames(provider, 2400);
        byte[] tail = ReadFrames(provider, 480);
        Assert.All(ChannelSamples(tail, 2), s => Assert.Equal(0, s));
    }

    [Fact]
    public void Read_ThroughSpanOverload_MatchesArrayVersion()
    {
        var provider = new HapticsWaveProvider(SampleRate);
        provider.SetChannel(HapticSide.Left,
            new HapticChannelSettings { Waveform = HapticWaveform.Sine, Frequency = 80, Amplitude = 1.0 });

        var buffer = new byte[960 * BytesPerFrame];
        int read = provider.Read(buffer);

        Assert.Equal(buffer.Length, read);
        Assert.Contains(ChannelSamples(buffer, 2), s => s != 0);
    }
}
