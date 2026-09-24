using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DsxLite.Core.Haptics;

/// <summary>
/// Streams haptic waveforms to the DualSense USB audio endpoint. The controller only
/// appears as an audio render device while connected over USB.
/// </summary>
public sealed class DualSenseHapticsOutput : IDisposable
{
    private const int PreferredSampleRate = 48000;

    private WasapiOut? _output;
    private HapticsWaveProvider? _provider;

    public string? DeviceName { get; private set; }
    public bool IsRunning { get; private set; }

    /// <summary>The live wave provider; null until <see cref="Start"/> succeeds.</summary>
    public HapticsWaveProvider? Provider => _provider;

    /// <summary>Finds the DualSense audio render endpoint (USB only).</summary>
    public static MMDevice? FindAudioDevice()
    {
        using var enumerator = new MMDeviceEnumerator();
        foreach (MMDevice device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (device)
            {
                string name;
                try { name = device.FriendlyName; }
                catch { continue; }
                if (name.Contains("Wireless Controller") || name.Contains("DualSense"))
                    return device;
            }
        }
        return null;
    }

    /// <summary>
    /// Opens the audio endpoint and starts streaming silence. Throws with a readable
    /// message when the endpoint is missing or its format is unusable.
    /// </summary>
    public void Start()
    {
        if (IsRunning)
            return;

        MMDevice? device = FindAudioDevice();
        if (device == null)
            throw new InvalidOperationException("未找到手柄音频设备(仅 USB 连接时存在)");

        DeviceName = device.FriendlyName;

        WaveFormat mixFormat = device.AudioClient.MixFormat;
        bool mixUsable = mixFormat.Channels == HapticsWaveProvider.ChannelCount &&
                         mixFormat.Encoding == WaveFormatEncoding.Pcm &&
                         mixFormat.BitsPerSample == 16;

        if (mixUsable)
        {
            _provider = new HapticsWaveProvider(mixFormat.SampleRate);
            _output = new WasapiOut(device, AudioClientShareMode.Shared, false, 50);
        }
        else
        {
            var exclusiveFormat = new WaveFormat(PreferredSampleRate, 16, HapticsWaveProvider.ChannelCount);
            if (!device.AudioClient.IsFormatSupported(AudioClientShareMode.Exclusive, exclusiveFormat))
            {
                device.Dispose();
                throw new InvalidOperationException(
                    $"手柄音频设备格式不受支持({mixFormat.Channels} 声道/{mixFormat.BitsPerSample} 位)");
            }
            _provider = new HapticsWaveProvider(PreferredSampleRate);
            _output = new WasapiOut(device, AudioClientShareMode.Exclusive, false, 50);
        }

        _output.Init(_provider);
        _output.Play();
        IsRunning = true;
    }

    public void Stop()
    {
        if (!IsRunning)
            return;
        try { _output?.Stop(); }
        catch { }
        _output?.Dispose();
        _output = null;
        _provider = null;
        IsRunning = false;
    }

    public void Dispose() => Stop();
}
