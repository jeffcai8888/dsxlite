using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DsxLite.Core.Haptics;

public enum AudioToHapticsMode
{
    /// <summary>Low-passed game audio waveform drives the actuators directly (texture).</summary>
    Waveform,

    /// <summary>Audio envelope modulates an adjustable carrier (impact/thump).</summary>
    Envelope,
}

/// <summary>
/// Captures game audio via WASAPI loopback and converts it into haptic samples:
/// low-pass filter, gain, noise gate, optional mono mix, optional envelope-over-carrier,
/// optional resampling. The haptics render side pulls frames through
/// <see cref="IHapticsSampleSource"/>. All DSP parameters are adjustable live.
/// </summary>
public sealed class AudioToHapticsEngine : IHapticsSampleSource, IDisposable
{
    private readonly MMDevice _captureDevice;
    private readonly StereoRingBuffer _ring = new(capacityFrames: 16384);
    private readonly List<float> _resampleScratch = new(capacity: 8192);

    private WasapiLoopbackCapture? _capture;
    private LinearResampler? _resampler;
    private BiquadLowpass? _lpLeft;
    private BiquadLowpass? _lpRight;
    private EnvelopeFollower? _envLeft;
    private EnvelopeFollower? _envRight;
    private int _captureRate = 48000;
    private double _carrierPhase;

    private volatile float _gain = 1.5f;
    private volatile int _cutoffHz = 160;
    private volatile AudioToHapticsMode _mode = AudioToHapticsMode.Waveform;
    private volatile float _attackMs = 5f;
    private volatile float _releaseMs = 150f;
    private volatile float _carrierHz = 80f;
    private volatile float _gateThreshold;
    private volatile bool _monoMix;

    public AudioToHapticsEngine(MMDevice captureDevice)
    {
        _captureDevice = captureDevice;
    }

    public string CaptureDeviceName => _captureDevice.FriendlyName;

    /// <summary>Output gain multiplier, 0..4.</summary>
    public float Gain { get => _gain; set => _gain = value; }

    /// <summary>Low-pass cutoff in Hz; applied to the live filters on the next sample.</summary>
    public int CutoffHz
    {
        get => _cutoffHz;
        set
        {
            _cutoffHz = value;
            _lpLeft?.Configure(value, _captureRate);
            _lpRight?.Configure(value, _captureRate);
        }
    }

    public AudioToHapticsMode Mode { get => _mode; set => _mode = value; }

    /// <summary>Envelope attack time in milliseconds.</summary>
    public float AttackMs
    {
        get => _attackMs;
        set
        {
            _attackMs = value;
            _envLeft?.SetTimes(value / 1000.0, _releaseMs / 1000.0, _captureRate);
            _envRight?.SetTimes(value / 1000.0, _releaseMs / 1000.0, _captureRate);
        }
    }

    /// <summary>Envelope release time in milliseconds.</summary>
    public float ReleaseMs
    {
        get => _releaseMs;
        set
        {
            _releaseMs = value;
            _envLeft?.SetTimes(_attackMs / 1000.0, value / 1000.0, _captureRate);
            _envRight?.SetTimes(_attackMs / 1000.0, value / 1000.0, _captureRate);
        }
    }

    /// <summary>Carrier frequency (Hz) used in envelope mode.</summary>
    public float CarrierHz { get => _carrierHz; set => _carrierHz = value; }

    /// <summary>Noise gate: channels whose envelope stays below this level (0..1) are silenced.</summary>
    public float GateThreshold { get => _gateThreshold; set => _gateThreshold = value; }

    /// <summary>Mix both channels to mono so both actuators feel the same signal.</summary>
    public bool MonoMix { get => _monoMix; set => _monoMix = value; }

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
        _resampler = _captureRate == targetSampleRate ? null : new LinearResampler(_captureRate, targetSampleRate);
        _lpLeft = new BiquadLowpass(_cutoffHz, _captureRate);
        _lpRight = new BiquadLowpass(_cutoffHz, _captureRate);
        _envLeft = new EnvelopeFollower(_attackMs / 1000.0, _releaseMs / 1000.0, _captureRate);
        _envRight = new EnvelopeFollower(_attackMs / 1000.0, _releaseMs / 1000.0, _captureRate);

        _capture.DataAvailable += OnDataAvailable;
        _capture.StartRecording();
        IsRunning = true;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0 || _capture == null)
            return;

        Span<float> stereo = BytesToStereo(e.Buffer.AsSpan(0, e.BytesRecorded), _capture.WaveFormat);
        if (stereo.IsEmpty || _lpLeft == null || _envLeft == null)
            return;

        BiquadLowpass lpLeft = _lpLeft, lpRight = _lpRight!;
        EnvelopeFollower envLeft = _envLeft, envRight = _envRight!;
        float gain = _gain;
        float gate = _gateThreshold;
        bool mono = _monoMix;
        AudioToHapticsMode mode = _mode;
        double carrierStep = _carrierHz / _captureRate;

        var output = new float[stereo.Length];
        for (int i = 0; i < stereo.Length; i += 2)
        {
            double left = lpLeft.Process(stereo[i]);
            double right = lpRight.Process(stereo[i + 1]);
            if (mono)
            {
                double mix = (left + right) * 0.5;
                left = mix;
                right = mix;
            }

            double envL = envLeft.Process(left);
            double envR = envRight.Process(right);
            LevelLeft = (float)envL;
            LevelRight = (float)envR;

            double outL, outR;
            if (mode == AudioToHapticsMode.Waveform)
            {
                outL = left * gain;
                outR = right * gain;
            }
            else
            {
                _carrierPhase += carrierStep;
                if (_carrierPhase >= 1.0)
                    _carrierPhase -= 1.0;
                double carrier = Math.Sin(2.0 * Math.PI * _carrierPhase);
                outL = envL * carrier * gain;
                outR = envR * carrier * gain;
            }

            if (envL < gate)
                outL = 0;
            if (envR < gate)
                outR = 0;

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
