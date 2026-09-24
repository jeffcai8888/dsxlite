using DsxLite.Core.Haptics;

namespace DsxLite.Tests;

public class DspTests
{
    private const int Rate = 48000;

    private static double SineAmplitudeAfter(BiquadLowpass filter, double freqHz, int frames)
    {
        // Measure amplitude over the tail after the filter settles.
        double max = 0;
        for (int i = 0; i < frames; i++)
        {
            double y = filter.Process(Math.Sin(2.0 * Math.PI * freqHz * i / Rate));
            if (i > frames / 2)
                max = Math.Max(max, Math.Abs(y));
        }
        return max;
    }

    [Fact]
    public void Lowpass_PassesLowFrequencies()
    {
        var filter = new BiquadLowpass(160, Rate);
        double amp = SineAmplitudeAfter(filter, 40, 48000);
        Assert.True(amp > 0.9, $"40Hz should pass ~unity, got {amp:F3}");
    }

    [Fact]
    public void Lowpass_AttenuatesHighFrequencies()
    {
        var filter = new BiquadLowpass(160, Rate);
        double amp = SineAmplitudeAfter(filter, 2000, 48000);
        Assert.True(amp < 0.02, $"2kHz should be strongly attenuated, got {amp:F3}");
    }

    [Fact]
    public void Lowpass_PassesDc()
    {
        var filter = new BiquadLowpass(160, Rate);
        double y = 0;
        for (int i = 0; i < 48000; i++)
            y = filter.Process(1.0);
        Assert.InRange(y, 0.99, 1.01);
    }

    [Fact]
    public void Envelope_AttacksFastReleasesSlowly()
    {
        var env = new EnvelopeFollower(0.005, 0.15, Rate);

        // Full-scale input for 10ms should reach most of the way up.
        for (int i = 0; i < 480; i++)
            env.Process(1.0);
        Assert.True(env.Level > 0.8, $"after attack, level={env.Level:F3}");

        // Silence for one attack-time should barely decay (release is 30x slower).
        double before = env.Level;
        for (int i = 0; i < 480; i++)
            env.Process(0.0);
        Assert.True(env.Level > before * 0.8, $"release too fast: {before:F3} -> {env.Level:F3}");
    }

    [Fact]
    public void RingBuffer_Roundtrips()
    {
        var ring = new StereoRingBuffer(8);
        ring.Write(new float[] { 1, 2, 3, 4 });
        var dst = new float[4];
        ring.Read(dst);
        Assert.Equal(new float[] { 1, 2, 3, 4 }, dst);
    }

    [Fact]
    public void RingBuffer_UnderflowZeroFills()
    {
        var ring = new StereoRingBuffer(8);
        ring.Write(new float[] { 1, 2 });
        var dst = new float[6];
        ring.Read(dst);
        Assert.Equal(new float[] { 1, 2, 0, 0, 0, 0 }, dst);
    }

    [Fact]
    public void RingBuffer_OverflowDropsOldest()
    {
        var ring = new StereoRingBuffer(2); // 2 frames
        ring.Write(new float[] { 1, 1, 2, 2, 3, 3 }); // 3 frames
        var dst = new float[4];
        ring.Read(dst);
        Assert.Equal(new float[] { 2, 2, 3, 3 }, dst);
    }

    [Fact]
    public void Resampler_PassthroughDetection()
    {
        Assert.True(new LinearResampler(48000, 48000).IsPassthrough);
        Assert.False(new LinearResampler(44100, 48000).IsPassthrough);
    }

    [Fact]
    public void Resampler_DownsampleHalves_FrameCount()
    {
        var resampler = new LinearResampler(96000, 48000);
        var input = new float[960 * 2]; // 960 stereo frames in
        for (int i = 0; i < input.Length; i++)
            input[i] = 0.5f;
        var output = new List<float>();
        resampler.Process(input, output);
        Assert.InRange(output.Count / 2, 470, 490); // ~480 frames out
    }

    [Fact]
    public void Resampler_UpsampleDoubles_AndInterpolates()
    {
        var resampler = new LinearResampler(24000, 48000);
        var input = new float[] { 0, 0, 1, 1 }; // 2 frames: 0 -> 1
        var output = new List<float>();
        resampler.Process(input, output);
        Assert.InRange(output.Count / 2, 3, 5); // ~4 frames out
        Assert.Contains(0.5f, output); // midpoint interpolated
    }
}
