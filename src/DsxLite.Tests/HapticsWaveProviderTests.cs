using DsxLite.Core.Haptics;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace DsxLite.Tests;

public class HapticsWaveProviderTests
{
    private const int SampleRate = 48000;
    private const int ChannelCount = 4;

    private static float[] ReadFrames(HapticsWaveProvider provider, int frames)
    {
        var buffer = new float[frames * ChannelCount];
        int read = provider.Read(buffer, 0, buffer.Length);
        Assert.Equal(buffer.Length, read);
        return buffer;
    }

    private static IEnumerable<float> ChannelSamples(float[] buffer, int channel)
    {
        for (int i = channel; i < buffer.Length; i += ChannelCount)
            yield return buffer[i];
    }

    [Fact]
    public void WaveFormat_Is4ChannelFloat()
    {
        var provider = new HapticsWaveProvider(SampleRate);

        Assert.Equal(SampleRate, provider.WaveFormat.SampleRate);
        Assert.Equal(ChannelCount, provider.WaveFormat.Channels);
        Assert.Equal(32, provider.WaveFormat.BitsPerSample);
    }

    [Fact]
    public void DefaultOutput_IsSilent()
    {
        var provider = new HapticsWaveProvider(SampleRate);

        float[] buffer = ReadFrames(provider, 480);

        Assert.All(buffer, s => Assert.Equal(0f, s));
    }

    [Fact]
    public void LeftChannel_DrivesOnlyHapticChannel2()
    {
        var provider = new HapticsWaveProvider(SampleRate);
        provider.SetChannel(HapticSide.Left,
            new HapticChannelSettings { Waveform = HapticWaveform.Sine, Frequency = 80, Amplitude = 1.0 });

        float[] buffer = ReadFrames(provider, 480);

        Assert.All(ChannelSamples(buffer, 0), s => Assert.Equal(0f, s)); // speaker L silent
        Assert.All(ChannelSamples(buffer, 1), s => Assert.Equal(0f, s)); // speaker R silent
        Assert.Contains(ChannelSamples(buffer, 2), s => s != 0f);        // left haptic active
        Assert.All(ChannelSamples(buffer, 3), s => Assert.Equal(0f, s)); // right haptic silent
    }

    [Fact]
    public void RightChannel_DrivesOnlyHapticChannel3_AndRespectsAmplitude()
    {
        var provider = new HapticsWaveProvider(SampleRate);
        provider.SetChannel(HapticSide.Right,
            new HapticChannelSettings { Waveform = HapticWaveform.Square, Frequency = 100, Amplitude = 0.5 });

        float[] buffer = ReadFrames(provider, 480);

        Assert.All(ChannelSamples(buffer, 2), s => Assert.Equal(0f, s));
        Assert.Contains(ChannelSamples(buffer, 3), s => s != 0f);
        Assert.All(ChannelSamples(buffer, 3), s => Assert.True(Math.Abs(s) <= 0.5f + 1e-6f));
    }

    [Fact]
    public void Pulse_DecaysBackToSilence()
    {
        var provider = new HapticsWaveProvider(SampleRate);
        provider.SetChannel(HapticSide.Left,
            new HapticChannelSettings { Waveform = HapticWaveform.Sine, Frequency = 80, Amplitude = 0 });

        provider.TriggerPulse(HapticSide.Left);
        float[] burst = ReadFrames(provider, 480); // first 10ms
        Assert.Contains(ChannelSamples(burst, 2), s => s != 0f);

        // ~2 seconds later the envelope must have fully decayed
        for (int i = 0; i < 40; i++)
            ReadFrames(provider, 2400);
        float[] tail = ReadFrames(provider, 480);
        Assert.All(ChannelSamples(tail, 2), s => Assert.Equal(0f, s));
    }

    [Fact]
    public void ConvertsToPcm16_ForSharedModeMixFormats()
    {
        var provider = new HapticsWaveProvider(SampleRate);
        provider.SetChannel(HapticSide.Left,
            new HapticChannelSettings { Waveform = HapticWaveform.Sine, Frequency = 80, Amplitude = 1.0 });

        IWaveProvider pcm16 = new SampleToWaveProvider16(provider);
        Assert.Equal(16, pcm16.WaveFormat.BitsPerSample);
        Assert.Equal(ChannelCount, pcm16.WaveFormat.Channels);

        var bytes = new byte[480 * 8];
        int read = pcm16.Read(bytes);
        Assert.Equal(bytes.Length, read);

        bool hapticChannelActive = false;
        for (int offset = 2 * 2; offset + 1 < bytes.Length; offset += 8)
            if (BitConverter.ToInt16(bytes, offset) != 0)
                hapticChannelActive = true;
        Assert.True(hapticChannelActive);
    }
}
