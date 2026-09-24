using HidSharp;

namespace DsxLite.Core.DualSense;

/// <summary>
/// An openable DualSense HID device with a background input thread and thread-safe
/// output reports (rumble, lightbar, LEDs, adaptive triggers).
/// </summary>
public sealed class DualSenseDevice : IDisposable
{
    private readonly HidDevice _device;
    private readonly object _ioLock = new();
    private readonly object _stateLock = new();
    private readonly DualSenseOutputState _output = new();
    private readonly byte[] _readBuffer;

    private HidStream? _stream;
    private Thread? _readThread;
    private volatile bool _running;
    private byte _outputSeq;
    private short _gyroPitchBias;
    private short _gyroYawBias;
    private short _gyroRollBias;
    private DualSenseInputState _state;

    internal DualSenseDevice(HidDevice device, ConnectionType connection)
    {
        _device = device;
        Connection = connection;
        _readBuffer = new byte[connection == ConnectionType.Bluetooth
            ? DualSenseIds.InputReportBtSize
            : DualSenseIds.InputReportUsbSize];
        string product;
        try { product = device.GetProductName(); }
        catch { product = "DualSense"; }
        DisplayName = $"{(string.IsNullOrWhiteSpace(product) ? "DualSense" : product)} ({(connection == ConnectionType.Usb ? "USB" : "蓝牙")})";
    }

    public ConnectionType Connection { get; }
    public string DisplayName { get; }
    public string DevicePath => _device.DevicePath;
    public bool IsOpen => _stream != null;

    /// <summary>Raised on the read thread after each parsed input report.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Raised on the read thread when the device is unplugged or the link drops.</summary>
    public event EventHandler? Disconnected;

    public DualSenseInputState CurrentState
    {
        get { lock (_stateLock) return _state; }
    }

    /// <summary>Gyro bias read from the calibration feature report (0 over USB / on failure).</summary>
    public (short Pitch, short Yaw, short Roll) GyroBias => (_gyroPitchBias, _gyroYawBias, _gyroRollBias);

    /// <summary>Opens the HID stream and starts the input thread.</summary>
    public bool Open()
    {
        if (_stream != null)
            return true;
        if (!_device.TryOpen(out HidStream? stream))
            return false;

        _stream = stream;
        _stream.ReadTimeout = 1000;

        if (Connection == ConnectionType.Bluetooth)
        {
            // Reading the calibration report also switches Bluetooth input from the
            // truncated 0x01 report to the full 0x31 report.
            try { ReadCalibration(); }
            catch { /* keep going with zero bias */ }
        }

        _running = true;
        _readThread = new Thread(ReadLoop)
        {
            IsBackground = true,
            Name = "DualSense input",
        };
        _readThread.Start();
        return true;
    }

    private void ReadCalibration()
    {
        var buffer = new byte[DualSenseIds.FeatureReportCalibrationSize];
        buffer[0] = DualSenseIds.FeatureReportCalibration;
        _stream!.GetFeature(buffer);
        _gyroPitchBias = BitConverter.ToInt16(buffer, 1);
        _gyroYawBias = BitConverter.ToInt16(buffer, 3);
        _gyroRollBias = BitConverter.ToInt16(buffer, 5);
    }

    private void ReadLoop()
    {
        while (_running)
        {
            int read;
            try
            {
                read = _stream!.Read(_readBuffer);
            }
            catch (TimeoutException)
            {
                continue;
            }
            catch
            {
                break; // device unplugged or stream closed
            }

            if (read <= 0)
                continue;

            if (DualSenseReportParser.TryParse(_readBuffer.AsSpan(0, read), Connection, out DualSenseInputState parsed))
            {
                lock (_stateLock)
                    _state = parsed;
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        _running = false;
        try { Disconnected?.Invoke(this, EventArgs.Empty); }
        catch { /* listener errors must not kill the read thread */ }
    }

    /// <summary>
    /// Mutates the output state under a lock and immediately sends one output report.
    /// </summary>
    public void UpdateOutput(Action<DualSenseOutputState> mutate)
    {
        lock (_ioLock)
        {
            if (_stream == null)
                return;
            mutate(_output);
            SendLocked();
        }
    }

    /// <summary>Resends the current output state (e.g. to stop rumble on shutdown).</summary>
    public void ApplyOutput()
    {
        lock (_ioLock)
        {
            if (_stream == null)
                return;
            SendLocked();
        }
    }

    private void SendLocked()
    {
        byte seq = _outputSeq;
        byte[] report = _output.BuildReport(Connection, ref seq);
        _outputSeq = seq;
        try { _stream!.Write(report); }
        catch { /* transient write failure; next update will retry */ }
    }

    public void Dispose()
    {
        _running = false;
        lock (_ioLock)
        {
            try
            {
                if (_stream != null)
                {
                    // Best effort: neutralize triggers, rumble and lightbar.
                    _output.LeftTriggerEffect = TriggerEffect.Off();
                    _output.RightTriggerEffect = TriggerEffect.Off();
                    _output.MotorLeft = 0;
                    _output.MotorRight = 0;
                    SendLocked();
                }
            }
            catch { }
            finally
            {
                try { _stream?.Dispose(); }
                catch { }
                _stream = null;
            }
        }
    }
}
