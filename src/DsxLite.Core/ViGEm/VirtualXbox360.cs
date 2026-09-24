using DsxLite.Core.DualSense;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace DsxLite.Core.ViGEm;

/// <summary>
/// Feeds DualSense input into a virtual Xbox 360 controller via ViGEmBus.
/// The driver must be installed separately; constructing the client throws
/// VigemBusNotFoundException when it is missing.
/// </summary>
public sealed class VirtualXbox360 : IDisposable
{
    private const int StickDeadzone = 3;

    private ViGEmClient? _client;
    private IXbox360Controller? _pad;

    public bool IsConnected { get; private set; }

    public void Connect()
    {
        if (IsConnected)
            return;
        _client = new ViGEmClient();
        _pad = _client.CreateXbox360Controller();
        _pad.Connect();
        IsConnected = true;
    }

    /// <summary>
    /// Mirrors one DualSense input state onto the virtual pad. When
    /// <paramref name="gyroToRightStick"/> is set, gyro yaw/pitch drive the right stick.
    /// </summary>
    public void Update(in DualSenseInputState s, bool gyroToRightStick = false)
    {
        if (!IsConnected || _pad == null)
            return;

        _pad.SetAxisValue(Xbox360Axis.LeftThumbX, ToAxis(s.LeftStickX));
        _pad.SetAxisValue(Xbox360Axis.LeftThumbY, ToAxisInverted(s.LeftStickY));

        if (gyroToRightStick && s.IsFullReport)
        {
            // ~500 deg/s at full deflection.
            short x = (short)Math.Clamp(s.GyroYaw * 32767 / (500.0 * DualSenseIds.GyroUnitsPerDegreeSec), short.MinValue, short.MaxValue);
            short y = (short)Math.Clamp(-s.GyroPitch * 32767 / (500.0 * DualSenseIds.GyroUnitsPerDegreeSec), short.MinValue, short.MaxValue);
            _pad.SetAxisValue(Xbox360Axis.RightThumbX, x);
            _pad.SetAxisValue(Xbox360Axis.RightThumbY, y);
        }
        else
        {
            _pad.SetAxisValue(Xbox360Axis.RightThumbX, ToAxis(s.RightStickX));
            _pad.SetAxisValue(Xbox360Axis.RightThumbY, ToAxisInverted(s.RightStickY));
        }

        _pad.SetSliderValue(Xbox360Slider.LeftTrigger, s.LeftTrigger);
        _pad.SetSliderValue(Xbox360Slider.RightTrigger, s.RightTrigger);

        _pad.SetButtonState(Xbox360Button.A, s.Cross);
        _pad.SetButtonState(Xbox360Button.B, s.Circle);
        _pad.SetButtonState(Xbox360Button.X, s.Square);
        _pad.SetButtonState(Xbox360Button.Y, s.Triangle);
        _pad.SetButtonState(Xbox360Button.LeftShoulder, s.L1);
        _pad.SetButtonState(Xbox360Button.RightShoulder, s.R1);
        _pad.SetButtonState(Xbox360Button.Back, s.Create);
        _pad.SetButtonState(Xbox360Button.Start, s.Options);
        _pad.SetButtonState(Xbox360Button.LeftThumb, s.L3);
        _pad.SetButtonState(Xbox360Button.RightThumb, s.R3);
        _pad.SetButtonState(Xbox360Button.Guide, s.PS);
        _pad.SetButtonState(Xbox360Button.Up, s.DPadUp);
        _pad.SetButtonState(Xbox360Button.Down, s.DPadDown);
        _pad.SetButtonState(Xbox360Button.Left, s.DPadLeft);
        _pad.SetButtonState(Xbox360Button.Right, s.DPadRight);
    }

    private static short ToAxis(byte v)
    {
        int centered = v - 128;
        if (Math.Abs(centered) <= StickDeadzone)
            return 0;
        return (short)Math.Clamp(centered * 257, short.MinValue, short.MaxValue);
    }

    private static short ToAxisInverted(byte v) => (short)-ToAxis(v);

    public void Dispose()
    {
        if (_pad != null)
        {
            try { _pad.Disconnect(); }
            catch { }
            _pad = null;
        }
        if (_client != null)
        {
            try { _client.Dispose(); }
            catch { }
            _client = null;
        }
        IsConnected = false;
    }
}
