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
    private bool _hasSentOutput;
    private DualSenseInputSnapshot _snapshot = DualSenseInputSnapshot.Empty;

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

    /// <summary>同一次发布的原始输入与运动量，使用同一份校准。</summary>
    public DualSenseInputSnapshot CurrentSnapshot
    {
        get { lock (_stateLock) return _snapshot; }
    }

    public DualSenseInputState CurrentState => CurrentSnapshot.Raw;

    /// <summary>工厂零偏仅用于诊断；生产换算使用 CurrentSnapshot.Motion。</summary>
    public (short Pitch, short Yaw, short Roll) GyroBias => CurrentSnapshot.Calibration.GyroBias;

    /// <summary>Opens the HID stream and starts the input thread.</summary>
    public bool Open()
    {
        if (_stream != null)
            return true;
        lock (_stateLock)
            _snapshot = DualSenseInputSnapshot.Empty;
        lock (_ioLock)
            _hasSentOutput = false;
        if (!_device.TryOpen(out HidStream? stream))
            return false;

        _stream = stream;
        _stream.ReadTimeout = 1000;

        // 两种连接都必须先尝试校准读取，再发布首份输入。
        // 蓝牙读取此特性报告还会请求完整的 0x31 输入报告。
        var calibration = DualSenseCalibration.Read(Connection, _stream.GetFeature);
        lock (_stateLock)
            _snapshot = DualSenseInputSnapshot.Create(default, calibration);

        _running = true;
        _readThread = new Thread(ReadLoop)
        {
            IsBackground = true,
            Name = "DualSense input",
        };
        _readThread.Start();
        return true;
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
                    _snapshot = DualSenseInputSnapshot.Create(parsed, _snapshot.Calibration);
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
        // 写入报错时数据仍可能已到达设备，退出时继续尝试复位。
        _hasSentOutput = true;
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
                if (_stream != null && _hasSentOutput)
                {
                    // 只读诊断不得发送输出报告，退出时也不例外。
                    // 对已经尝试输出的会话，尽力关闭扳机效果和震动。
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
