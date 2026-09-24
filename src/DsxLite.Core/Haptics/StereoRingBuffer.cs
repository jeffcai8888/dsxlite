namespace DsxLite.Core.Haptics;

/// <summary>
/// Thread-safe ring buffer of interleaved stereo float frames. Single writer
/// (capture thread), single reader (audio render thread). Overflow drops the
/// oldest frames; underflow zero-fills.
/// </summary>
public sealed class StereoRingBuffer
{
    private readonly float[] _buffer; // interleaved L/R, length = 2 * capacity frames
    private readonly object _gate = new();
    private int _readFrame;
    private int _writeFrame;
    private int _usedFrames;

    public StereoRingBuffer(int capacityFrames)
    {
        _buffer = new float[capacityFrames * 2];
    }

    public int CapacityFrames => _buffer.Length / 2;

    public void Write(ReadOnlySpan<float> interleavedFrames)
    {
        int frames = interleavedFrames.Length / 2;
        if (frames == 0)
            return;

        lock (_gate)
        {
            int capacity = CapacityFrames;
            if (frames > capacity)
            {
                // Keep only the newest capacity frames.
                interleavedFrames = interleavedFrames.Slice((frames - capacity) * 2);
                frames = capacity;
            }

            // Drop oldest frames to make room.
            int overflow = _usedFrames + frames - capacity;
            if (overflow > 0)
            {
                _readFrame = (_readFrame + overflow) % capacity;
                _usedFrames -= overflow;
            }

            for (int frame = 0; frame < frames; frame++)
            {
                _buffer[_writeFrame * 2] = interleavedFrames[frame * 2];
                _buffer[_writeFrame * 2 + 1] = interleavedFrames[frame * 2 + 1];
                _writeFrame = (_writeFrame + 1) % capacity;
            }
            _usedFrames += frames;
        }
    }

    /// <summary>Fills <paramref name="dst"/> (interleaved pairs); missing frames are zeros.</summary>
    public void Read(Span<float> dst)
    {
        int framesWanted = dst.Length / 2;
        lock (_gate)
        {
            int capacity = CapacityFrames;
            for (int frame = 0; frame < framesWanted; frame++)
            {
                if (_usedFrames > 0)
                {
                    dst[frame * 2] = _buffer[_readFrame * 2];
                    dst[frame * 2 + 1] = _buffer[_readFrame * 2 + 1];
                    _readFrame = (_readFrame + 1) % capacity;
                    _usedFrames--;
                }
                else
                {
                    dst[frame * 2] = 0f;
                    dst[frame * 2 + 1] = 0f;
                }
            }
        }
    }
}
