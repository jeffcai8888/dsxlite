using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DsxLite.Core.Haptics;

public enum AudioToHapticsMode
{
    /// <summary>Low-passed game audio waveform drives the actuators directly (texture).</summary>
    Waveform,

    /// <summary>Audio envelope modulates an 80 Hz carrier (impact/thump).</summary>
    Envelope,
}

/// <summary>
/// Captures game audio via WASAPI loopback and converts it into haptic samples:
/// low-pass filter, gain, optional envelope-over-carrier, optional resampling.
/// The haptics render side pulls frames through <see cref="IHapticsSampleSource"/>.
/// </summary>
public sealed class AudioToHapticsEngine : IHapticsSampleSource, IDisposable
{
    private const double CarrierHz = 80.0;
    private const double AttackSeconds = 0.005;
    private const double ReleaseSeconds = 0.15;

    private readonly MMDevice _captureDevice;
    private readonly StereoRingBuffer _ring = new(capacityFrames: 16384);
    private readonly List<float> _resampleScratch = new(capacity: 8192);

    private WasapiLoopbackCapture? _capture;
    private LinearResampler? _resampler;
    private volatile FilterPair _filters = new(160, 48000);
    private volatile EnvelopePair _envelopes = new(48000);
    private int _captureRate = 48000;
    private double _carrierPhase;
    private double _carrierStep;

    private volatile float _gain = 1.5f;
    private volatile int _cutoffHz = 160;
    private volatile AudioToHapticsMode _mode = AudioToHapticsMode.Waveform;

    private sealed record FilterPair(BiquadLowpass Left, BiquadLowpass Right)
    {
        public FilterPair(int cutoffHz, int rate) : this(
            new BiquadLowpass(cutoffHz, rate), new BiquadLowpass(cutoffHz, rate)) { }
    }

    private sealed record EnvelopePair(EnvelopeFollower Left, EnvelopeFollower Right)
    {
        public EnvelopePair(int rate) : this(
            new EnvelopeFollower(AttackSeconds, ReleaseSeconds, rate),
            new EnvelopeFollower(AttackSeconds, ReleaseSeconds, rate)) { }
    }

    public AudioToHapticsEngine(MMDevice captureDevice)
    {
        _captureDevice = captureDevice;
    }

    public string CaptureDeviceName => _captureDevice.FriendlyName;

    /// <summary>Output gain multiplier, 0..4.</summary>
    public float Gain { get => _gain; set => _gain = value; }

    /// <summary>Low-pass cutoff in Hz; applied on the capture thread.</summary>
    public int CutoffHz { get => _cutoffHz; set => _cutoffHz = value; }

    public AudioToHapticsMode Mode { get => _mode; set => _mode = value; }

    /// <summary>Latest envelope levels for VU display (0..1+).</summary>
    public float LevelLeft { get; private set; }
    public float LevelRight { get; private set; }

    public bool IsRunning { get; private set; }

    /// <summary>Render endpoints usable as capture sources (excludes the controller itself).</summary>
    public static List<MMDevice> ListCaptureSources()
    {
        var result = new List<MMDevice>();
        var enumerator = new MMDeviceEnumerator();
        foreach (MMDevice device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            string name;
            try { name = device.FriendlyName; }
            catch { device.Dispose(); continue; }
            if (name.Contains("wireless controller", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("dualsense", StringComparison.OrdinalIgnoreCase))
            {
                device.Dispose();
                continue;
            }
            result.Add(device);
        }
        enumerator.Dispose();
        return result;
    }

    public static MMDevice GetDefaultCaptureSource()
    {
        var enumerator = new MMDeviceEnumerator();
        MMDevice device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        enumerator.Dispose();
        return device;
    }

    /// <summary>Starts loopback capture. <paramref name="targetSampleRate"/> is the haptics stream rate.</summary>
    public void Start(int targetSampleRate)
    {
        if (IsRunning)
            return;

        _capture = new WasapiLoopbackCapture(_captureDevice);
        WaveFormat captureFormat = _capture.WaveFormat;
        _captureRate = captureFormat.SampleRate;
        _carrierStep = CarrierHz / targetSampleRate;
        _resampler = _captureRate == targetSampleRate ? null : new LinearResampler(_captureRate, targetSampleRate);
        _filters = new FilterPair(_cutoffHz, _captureRate);
        _envelopes = new EnvelopePair(_captureRate);

        _capture.DataAvailable += OnDataAvailable;
        _capture.StartRecording();
        IsRunning = true;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0 || _capture == null)
            return;

        WaveFormat format = _capture.WaveFormat;
        Span<float> stereo = BytesToStereo(e.Buffer.AsSpan(0, e.BytesRecorded), format);
        if (stereo.IsEmpty)
            return;

        FilterPair filters = _filters;
        EnvelopePair envelopes = _envelopes;
        int cutoff = _cutoffHz;
        float gain = _gain;
        AudioToHapticsMode mode = _mode;

        var output = new float[stereo.Length];
        for (int i = 0; i < stereo.Length; i += 2)
        {
            double left = filters.Left.Process(stereo[i]);
            double right = filters.Right.Process(stereo[i + 1]);
            double envL = envelopes.Left.Process(left);
            double envR = envelopes.Right.Process(right);

            double outL, outR;
            if (mode == AudioToHapticsMode.Waveform)
            {
                outL = left * gain;
                outR = right * gain;
            }
            else
            {
                _carrierPhase += _carrierStep;
                if (_carrierPhase >= 1.0)
                    _carrierPhase -= 1.0;
                double carrier = Math.Sin(2.0 * Math.PI * _carrierPhase);
                outL = envL * carrier * gain;
                outR = envR * carrier * gain;
            }

            LevelLeft = (float)envL;
            LevelRight = (float)envR;
            output[i] = (float)Math.Clamp(outL, -1.0, 1.0);
            output[i + 1] = (float)Math.Clamp(outR, -1.0, 1.0);
        }

        if (_resampler is { IsPassthrough: false } resampler)
        {
            _resampleScratch.Clear();
            resampler.Process(output, _resampleScratch);
            _ring.Write(_resampleScratch.ToArray());
        }
        else
        {
            _ring.Write(output);
        }
    }

    /// <summary>Extracts stereo float frames from capture bytes; takes channels 0/1 of multi-channel formats.</summary>
    private static Span<float> BytesToStereo(Span<byte> bytes, WaveFormat format)
    {
        if (format.Encoding != WaveFormatEncoding.IeeeFloat || format.BitsPerSample != 32)
            return Span<float>.Empty; // loopback is virtually always float32; skip exotic formats

        Span<float> samples = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(bytes);
        int channels = format.Channels;
        if (channels == 2)
            return samples;

        int frames = samples.Length / channels;
        var stereo = new float[frames * 2];
        for (int f = 0; f < frames; f++)
        {
            stereo[f * 2] = samples[f * channels];
            stereo[f * 2 + 1] = channels > 1 ? samples[f * channels + 1] : samples[f * channels];
        }
        return stereo;
    }

    /// <summary>Render-thread pull: fills interleaved stereo frames (zeros when starved).</summary>
    public void Read(Span<float> interleavedStereo) => _ring.Read(interleavedStereo);

    public void Stop()
    {
        if (!IsRunning)
            return;
        IsRunning = false;
        if (_capture != null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            try { _capture.StopRecording(); }
            catch { }
            _capture.Dispose();
            _capture = null;
        }
    }

    public void Dispose()
    {
        Stop();
        _captureDevice.Dispose();
    }
}
