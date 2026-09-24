namespace DsxLite.Core.Haptics;

/// <summary>Second-order Butterworth low-pass filter (RBJ biquad), mono.</summary>
public sealed class BiquadLowpass
{
    private double _a0, _a1, _a2, _b1, _b2;
    private double _x1, _x2, _y1, _y2;

    public BiquadLowpass(double cutoffHz, double sampleRate)
    {
        Configure(cutoffHz, sampleRate);
    }

    public void Configure(double cutoffHz, double sampleRate)
    {
        double w0 = 2.0 * Math.PI * cutoffHz / sampleRate;
        double cosW0 = Math.Cos(w0);
        double alpha = Math.Sin(w0) / (2.0 * Math.Sqrt(0.5)); // Q = 1/sqrt(2), Butterworth
        double a0Inv = 1.0 / (1.0 + alpha);

        _a0 = (1.0 - cosW0) * 0.5 * a0Inv;
        _a1 = (1.0 - cosW0) * a0Inv;
        _a2 = _a0;
        _b1 = -2.0 * cosW0 * a0Inv;
        _b2 = (1.0 - alpha) * a0Inv;
    }

    public double Process(double x)
    {
        double y = _a0 * x + _a1 * _x1 + _a2 * _x2 - _b1 * _y1 - _b2 * _y2;
        _x2 = _x1;
        _x1 = x;
        _y2 = _y1;
        _y1 = y;
        return y;
    }
}

/// <summary>Peak envelope follower with independent attack/release times.</summary>
public sealed class EnvelopeFollower
{
    private readonly double _attackPerSample;
    private readonly double _releasePerSample;
    private double _level;

    public EnvelopeFollower(double attackSeconds, double releaseSeconds, double sampleRate)
    {
        _attackPerSample = 1.0 - Math.Exp(-1.0 / (attackSeconds * sampleRate));
        _releasePerSample = 1.0 - Math.Exp(-1.0 / (releaseSeconds * sampleRate));
    }

    public double Level => _level;

    public double Process(double x)
    {
        double magnitude = Math.Abs(x);
        double rate = magnitude > _level ? _attackPerSample : _releasePerSample;
        _level += (magnitude - _level) * rate;
        return _level;
    }
}

/// <summary>Linear-interpolating resampler for stereo (interleaved L/R) float frames.</summary>
public sealed class LinearResampler
{
    private readonly double _step; // input samples consumed per output sample
    private double _position;

    public LinearResampler(int inputRate, int outputRate)
    {
        _step = (double)inputRate / outputRate;
    }

    public bool IsPassthrough => Math.Abs(_step - 1.0) < 1e-9;

    /// <summary>Converts interleaved input frames, appending output frames to <paramref name="output"/>.</summary>
    public void Process(ReadOnlySpan<float> interleavedInput, List<float> output)
    {
        int inputFrames = interleavedInput.Length / 2;
        if (inputFrames == 0)
            return;

        while (true)
        {
            int index = (int)_position;
            double frac = _position - index;

            double left, right;
            if (index + 1 < inputFrames)
            {
                left = Lerp(Frame(interleavedInput, index, 0), Frame(interleavedInput, index + 1, 0), frac);
                right = Lerp(Frame(interleavedInput, index, 1), Frame(interleavedInput, index + 1, 1), frac);
            }
            else if (index < inputFrames)
            {
                // Last input frame: hold it (cross-buffer interpolation happens on the next call).
                left = Frame(interleavedInput, index, 0);
                right = Frame(interleavedInput, index, 1);
            }
            else
            {
                break;
            }

            output.Add((float)left);
            output.Add((float)right);
            _position += _step;
        }

        // Keep the tail position relative to the next call's buffer.
        _position -= inputFrames;
        if (_position < 0)
            _position = 0;
    }

    private static double Frame(ReadOnlySpan<float> data, int frame, int channel) => data[frame * 2 + channel];
    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
