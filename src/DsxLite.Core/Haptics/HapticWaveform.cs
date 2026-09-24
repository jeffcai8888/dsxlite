namespace DsxLite.Core.Haptics;

public enum HapticWaveform
{
    Sine,
    Square,
    Pulse,
    Noise,
}

/// <summary>
/// Immutable per-channel haptic settings. Swapped atomically into the wave provider
/// so the UI thread never races the audio thread.
/// </summary>
public sealed record HapticChannelSettings
{
    public HapticWaveform Waveform { get; init; } = HapticWaveform.Sine;

    /// <summary>Carrier frequency in Hz; the actuators respond best at low frequencies.</summary>
    public double Frequency { get; init; } = 80;

    /// <summary>Continuous amplitude, 0..1.</summary>
    public double Amplitude { get; init; }
}
