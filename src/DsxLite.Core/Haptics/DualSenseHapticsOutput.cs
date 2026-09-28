using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Windows.Devices.Enumeration;

namespace DsxLite.Core.Haptics;

/// <summary>
/// Streams haptic waveforms to the DualSense USB audio endpoint. The controller only
/// appears as an audio render device while connected over USB.
/// </summary>
public sealed class DualSenseHapticsOutput : IDisposable
{
    private const int PreferredSampleRate = 48000;

    // KSDATAFORMAT_SUBTYPE_IEEE_FLOAT, the SubFormat of float WaveFormatExtensible mix formats.
    private static readonly Guid IeeeFloatSubType = new("00000003-0000-0010-8000-00aa00389b71");

// WasapiOut is obsolete in NAudio 3.x, but its semantics are proven;
// WasapiPlayer's exclusive-mode flow is undocumented and cannot be hardware-tested here.
#pragma warning disable CS0618
    private WasapiOut? _output;
#pragma warning restore CS0618
    private HapticsWaveProvider? _provider;

    public string? DeviceName { get; private set; }
    public bool IsRunning { get; private set; }

    /// <summary>The live wave provider; null until <see cref="Start"/> succeeds.</summary>
    public HapticsWaveProvider? Provider => _provider;

    /// <summary>Finds the DualSense audio render endpoint (USB only). Caller must dispose it.</summary>
    public static MMDevice? FindAudioDevice() => FindAudioDevice(null);

    /// <summary>
    /// Finds the audio endpoint belonging to a specific controller. Stale endpoints
    /// whose device node no longer exists are filtered out; with multiple live
    /// endpoints, matches via the shared USB device node (ancestor walk on both the
    /// endpoint side and the HID interface side). Falls back to the first live endpoint.
    /// </summary>
    public static MMDevice? FindAudioDevice(string? hidDevicePath)
    {
        var enumerator = new MMDeviceEnumerator();
        List<MMDevice> endpoints = enumerator
            .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .Where(d =>
            {
                try
                {
                    string name = d.FriendlyName;
                    return name.Contains("wireless controller", StringComparison.OrdinalIgnoreCase) ||
                           name.Contains("dualsense", StringComparison.OrdinalIgnoreCase);
                }
                catch { return false; }
            })
            .ToList();
        enumerator.Dispose();

        // Endpoint ID -> SWD\MMDEVAPI\{...} instance ID, via WinRT.
        Dictionary<string, string?> instanceIds = GetAudioEndpointInstanceIds();

        // Drop ghost endpoints whose device node no longer exists.
        List<MMDevice> live = endpoints
            .Where(ep => instanceIds.TryGetValue(ep.ID, out string? iid) &&
                         iid != null && CfgMgr.DevNodeExists(iid))
            .ToList();

        MMDevice? found = null;
        if (hidDevicePath != null && live.Count > 0)
        {
            string? hidNode = GetUsbDeviceNodeIdForHid(hidDevicePath);
            if (hidNode != null)
            {
                found = live.FirstOrDefault(ep =>
                    instanceIds.TryGetValue(ep.ID, out string? iid) &&
                    iid != null &&
                    string.Equals(GetUsbDeviceNodeIdForEndpoint(iid), hidNode,
                        StringComparison.OrdinalIgnoreCase));
            }
        }
        found ??= live.FirstOrDefault();

        foreach (MMDevice ep in endpoints)
            if (!ReferenceEquals(ep, found))
                ep.Dispose();
        return found;
    }

    private static bool IsUsbDeviceNodeId(string id) =>
        id.StartsWith("USB\\VID_", StringComparison.OrdinalIgnoreCase) &&
        !id.Contains("&MI_", StringComparison.OrdinalIgnoreCase);

    /// <summary>USB device node ID of the endpoint's parent USB audio device.</summary>
    public static string? GetUsbDeviceNodeIdForEndpoint(string endpointInstanceId) =>
        CfgMgr.GetAncestorDeviceId(endpointInstanceId, IsUsbDeviceNodeId);

    /// <summary>
    /// USB device node ID for a controller given its HID device path
    /// (\\?\hid#vid_xxxx&amp;pid_xxxx&amp;mi_zz#instance#{guid}). The HID interface devnode
    /// (HID\VID_xxxx&amp;PID_xxxx&amp;MI_zz\instance) shares the USB device node with the
    /// controller's audio interface.
    /// </summary>
    public static string? GetUsbDeviceNodeIdForHid(string hidDevicePath)
    {
        string[] parts = hidDevicePath.Split('#');
        if (parts.Length < 3)
            return null;
        string devNodeId = $"HID\\{parts[1]}\\{parts[2]}".ToUpperInvariant();
        return CfgMgr.GetAncestorDeviceId(devNodeId, IsUsbDeviceNodeId);
    }

    /// <summary>
    /// Maps audio endpoint IDs (MMDevice.ID, "{0.0.0.00000000}.{guid}") to their
    /// SWD\MMDEVAPI\{...} instance IDs via WinRT DeviceInformation (NAudio cannot
    /// read the instance ID on all systems).
    /// </summary>
    public static Dictionary<string, string?> GetAudioEndpointInstanceIds()
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        try
        {
            // AQS filter for audio render endpoints; DeviceClass overload has no
            // additionalProperties variant.
            const string aqs = "System.Devices.InterfaceClassGuid:=\"{E6327CAD-DCEC-4949-AE8A-991E976A79D2}\"";
            DeviceInformationCollection infos = DeviceInformation
                .FindAllAsync(aqs, ["System.Devices.DeviceInstanceId"])
                .AsTask().GetAwaiter().GetResult();
            foreach (DeviceInformation info in infos)
            {
                // info.Id: \\?\SWD#MMDEVAPI#{0.0.0.00000000}.{guid}#{...}
                string[] parts = info.Id.Split('#');
                if (parts.Length < 3)
                    continue;
                result[parts[2]] = info.Properties.TryGetValue("System.Devices.DeviceInstanceId", out object? value)
                    ? value as string
                    : null;
            }
        }
        catch { /* WinRT unavailable */ }
        return result;
    }

    /// <summary>
    /// Maps audio endpoint IDs to their USB device-instance segments via ancestor walk.
    /// </summary>
    public static Dictionary<string, string?> GetAudioEndpointUsbSegments()
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach ((string endpointId, string? instanceId) in GetAudioEndpointInstanceIds())
        {
            string? segment = null;
            if (instanceId != null)
            {
                string? ancestor = CfgMgr.GetAncestorDeviceId(instanceId,
                    id => ExtractUsbInstanceSegment(id) != null);
                if (ancestor != null)
                    segment = ExtractUsbInstanceSegment(ancestor);
            }
            result[endpointId] = segment;
        }
        return result;
    }

    /// <summary>
    /// Extracts the USB device-instance segment shared between a HID device path
    /// (\\?\hid#vid_054c&amp;pid_0ce6&amp;mi_03#8&amp;1a2b3c4&amp;0&amp;0000#{...}) and an audio
    /// endpoint instance ID (USB\VID_054C&amp;PID_0CE6&amp;MI_02\8&amp;1a2b3c4&amp;0&amp;0002).
    /// Returns e.g. "8&amp;1a2b3c4&amp;0", or null when the pattern is absent.
    /// </summary>
    /// <summary>
    /// Extracts the USB device-instance segment shared between a HID device path
    /// (\\?\hid#vid_054c&amp;pid_0ce6&amp;mi_03#9&amp;13a1d093&amp;0&amp;0000#{...}) and a USB
    /// device node (USB\VID_054C&amp;PID_0CE6\9&amp;13A1D093&amp;0) or interface node
    /// (USB\VID_054C&amp;PID_0CE6&amp;MI_02\9&amp;13A1D093&amp;0&amp;0002).
    /// Returns the first three fields, e.g. "9&amp;13a1d093&amp;0", or null when absent.
    /// </summary>
    public static string? ExtractUsbInstanceSegment(string deviceId)
    {
        foreach (string part in deviceId.Split('#', '\\'))
        {
            string[] fields = part.Split('&');
            if (fields.Length is < 3 or > 4)
                continue;
            bool numeric01 = fields[0].Length > 0 && fields[0].All(char.IsDigit) &&
                             fields[2].Length > 0 && fields[2].All(char.IsDigit);
            bool lastOk = fields.Length == 3 ||
                          (fields[3].Length == 4 && fields[3].All(c => char.IsDigit(c) || c is >= 'a' and <= 'f' or >= 'A' and <= 'F'));
            if (numeric01 && lastOk)
                return string.Join('&', fields[0], fields[1], fields[2]);
        }
        return null;
    }

    /// <summary>Lists all active render endpoints with their mix formats, for diagnostics.</summary>
    public static List<string> DescribeRenderDevices()
    {
        var lines = new List<string>();
        using var enumerator = new MMDeviceEnumerator();
        foreach (MMDevice device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (device)
            {
                try
                {
                    using AudioClient client = device.CreateAudioClient();
                    WaveFormat mix = client.MixFormat;
                    string kind = IsFloat(mix) ? "float" : mix.Encoding.ToString();
                    lines.Add($"{device.FriendlyName} — {mix.Channels}ch {mix.BitsPerSample}bit {mix.SampleRate}Hz {kind}");
                }
                catch (Exception ex)
                {
                    lines.Add($"{device.FriendlyName} — 读取格式失败: {ex.Message}");
                }
            }
        }
        return lines;
    }

    private static bool IsFloat(WaveFormat format) =>
        format.Encoding == WaveFormatEncoding.IeeeFloat ||
        (format is WaveFormatExtensible extensible && extensible.SubFormat == IeeeFloatSubType);

    /// <summary>
    /// Wraps an ISampleProvider as an IWaveProvider reporting an exact target format
    /// (e.g. the device's WAVEFORMATEXTENSIBLE mix format). The DualSense audio endpoint
    /// rejects plain WAVEFORMATEX float formats with E_INVALIDARG, so the mix format
    /// must be cloned byte-for-byte.
    /// </summary>
    private sealed class ExactFormatFloatWrapper : IWaveProvider
    {
        private readonly ISampleProvider _source;

        public ExactFormatFloatWrapper(ISampleProvider source, WaveFormat format)
        {
            _source = source;
            WaveFormat = format;
        }

        public WaveFormat WaveFormat { get; }

        public int Read(Span<byte> buffer)
        {
            Span<float> floats = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(buffer);
            return _source.Read(floats) * sizeof(float);
        }
    }

    /// <summary>Converts the float provider to whatever the mix format needs.</summary>
    private static IWaveProvider? AdaptTo(HapticsWaveProvider provider, WaveFormat mix)
    {
        if (mix.Channels != HapticsWaveProvider.ChannelCount)
            return null;
        if (IsFloat(mix) && mix.BitsPerSample == 32)
            return new ExactFormatFloatWrapper(provider, mix);
        return (mix.Encoding, mix.BitsPerSample) switch
        {
            (WaveFormatEncoding.Pcm, 16) => new SampleToWaveProvider16(provider),
            (WaveFormatEncoding.Pcm, 24) => new SampleToWaveProvider24(provider),
            (WaveFormatEncoding.Pcm, 32) => new SampleToWaveProvider(provider),
            _ => null,
        };
    }

    /// <summary>
    /// Opens the audio endpoint and starts streaming. Throws with a readable
    /// message when the endpoint is missing or its format is unusable.
    /// </summary>
    /// <param name="hidDevicePath">Optional HID path of the target controller (multi-controller).</param>
    public void Start(string? hidDevicePath = null)
    {
        if (IsRunning)
            return;

        // Not disposed here: WasapiOut owns the device for the lifetime of the stream.
        MMDevice? device = FindAudioDevice(hidDevicePath);
        if (device == null)
        {
            throw new InvalidOperationException(
                "未找到手柄音频设备(仅 USB 连接时存在)。" +
                "可用 \"dotnet run --project src/DsxLite.Cli -- --audio\" 查看系统中的音频输出设备。");
        }

        DeviceName = device.FriendlyName;
        using AudioClient audioClient = device.CreateAudioClient();
        WaveFormat mixFormat = audioClient.MixFormat;

        IWaveProvider? wave = null;
        AudioClientShareMode shareMode = AudioClientShareMode.Shared;
        _provider = new HapticsWaveProvider(mixFormat.SampleRate);
        wave = AdaptTo(_provider, mixFormat);

        if (wave == null)
        {
            // Mix format unusable: try exclusive mode at 48kHz float, then 16-bit PCM.
            _provider = new HapticsWaveProvider(PreferredSampleRate);
            if (audioClient.IsFormatSupported(AudioClientShareMode.Exclusive, _provider.WaveFormat))
            {
                wave = new SampleToWaveProvider(_provider);
            }
            else
            {
                var pcm16 = new WaveFormat(PreferredSampleRate, 16, HapticsWaveProvider.ChannelCount);
                if (audioClient.IsFormatSupported(AudioClientShareMode.Exclusive, pcm16))
                    wave = new SampleToWaveProvider16(_provider);
            }
            shareMode = AudioClientShareMode.Exclusive;
        }

        if (wave == null)
        {
            device.Dispose();
            throw new InvalidOperationException(
                $"手柄音频设备格式不受支持({mixFormat.Channels} 声道/{mixFormat.BitsPerSample} 位/{mixFormat.SampleRate}Hz)");
        }

        try
        {
#pragma warning disable CS0618 // WasapiOut is obsolete in NAudio 3.x, but its semantics are
                               // proven; WasapiPlayer's exclusive-mode flow is undocumented
                               // and cannot be hardware-tested here.
            _output = new WasapiOut(device, shareMode, false, 50);
#pragma warning restore CS0618
            _output.Init(wave);
            _output.Play();
            // The endpoint or session may be muted/quiet; haptics follow audio volume.
            try
            {
                device.AudioEndpointVolume.Mute = false;
                device.AudioEndpointVolume.MasterVolumeLevelScalar = 1.0f;
            }
            catch { /* volume control unavailable */ }
        }
        catch
        {
            device.Dispose();
            _provider = null;
            throw;
        }
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
