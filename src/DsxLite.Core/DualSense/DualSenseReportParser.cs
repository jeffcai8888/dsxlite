namespace DsxLite.Core.DualSense;

/// <summary>
/// Parses DualSense input reports. Layout follows the Linux hid-playstation driver:
/// USB report 0x01 carries the 63-byte common report at offset 1;
/// Bluetooth report 0x31 carries it at offset 2 (with a seq byte and trailing CRC32);
/// Bluetooth also emits a truncated 10-byte 0x01 report until calibration is read.
/// </summary>
public static class DualSenseReportParser
{
    public static bool TryParse(ReadOnlySpan<byte> report, ConnectionType connection, out DualSenseInputState state)
    {
        state = default;
        if (report.Length == 0)
            return false;

        byte id = report[0];
        int offset;

        if (id == DualSenseIds.InputReportUsb && report.Length == 10 && connection == ConnectionType.Bluetooth)
        {
            // Truncated Bluetooth report: sticks, hat, buttons, analog triggers.
            state.IsFullReport = false;
            state.LeftStickX = report[1];
            state.LeftStickY = report[2];
            state.RightStickX = report[3];
            state.RightStickY = report[4];
            byte b0 = report[5];
            state.DPadHat = b0 & 0x0F;
            state.Square = (b0 & 0x10) != 0;
            state.Cross = (b0 & 0x20) != 0;
            state.Circle = (b0 & 0x40) != 0;
            state.Triangle = (b0 & 0x80) != 0;
            ParseButtons1(report[6], ref state);
            byte b2 = report[7];
            state.PS = (b2 & 0x01) != 0;
            state.TouchpadClick = (b2 & 0x02) != 0;
            state.LeftTrigger = report[8];
            state.RightTrigger = report[9];
            return true;
        }

        if (connection == ConnectionType.Usb && id == DualSenseIds.InputReportUsb &&
            report.Length >= DualSenseIds.InputReportUsbSize)
        {
            offset = 1;
        }
        else if (connection == ConnectionType.Bluetooth && id == DualSenseIds.InputReportBt &&
                 report.Length >= DualSenseIds.InputReportBtSize - 4)
        {
            offset = 2;
        }
        else
        {
            return false;
        }

        state.IsFullReport = true;
        state.LeftStickX = report[offset + 0];
        state.LeftStickY = report[offset + 1];
        state.RightStickX = report[offset + 2];
        state.RightStickY = report[offset + 3];
        state.LeftTrigger = report[offset + 4];
        state.RightTrigger = report[offset + 5];

        byte buttons0 = report[offset + 7];
        state.DPadHat = buttons0 & 0x0F;
        state.Square = (buttons0 & 0x10) != 0;
        state.Cross = (buttons0 & 0x20) != 0;
        state.Circle = (buttons0 & 0x40) != 0;
        state.Triangle = (buttons0 & 0x80) != 0;
        ParseButtons1(report[offset + 8], ref state);
        byte buttons2 = report[offset + 9];
        state.PS = (buttons2 & 0x01) != 0;
        state.TouchpadClick = (buttons2 & 0x02) != 0;
        state.MuteButton = (buttons2 & 0x04) != 0;
        // DualSense Edge extra buttons share buttons[2] bits 4-7.
        state.Fn1 = (buttons2 & 0x10) != 0;
        state.Fn2 = (buttons2 & 0x20) != 0;
        state.LeftPaddle = (buttons2 & 0x40) != 0;
        state.RightPaddle = (buttons2 & 0x80) != 0;

        state.GyroPitch = ReadInt16(report, offset + 15);
        state.GyroYaw = ReadInt16(report, offset + 17);
        state.GyroRoll = ReadInt16(report, offset + 19);
        state.AccelX = ReadInt16(report, offset + 21);
        state.AccelY = ReadInt16(report, offset + 23);
        state.AccelZ = ReadInt16(report, offset + 25);

        state.Touch1 = ParseTouch(report.Slice(offset + 32, 4));
        state.Touch2 = ParseTouch(report.Slice(offset + 36, 4));

        byte status0 = report[offset + 52];
        int capacity = status0 & 0x0F;
        state.BatteryPercent = Math.Min(capacity * 10, 100);
        state.Battery = (status0 >> 4) switch
        {
            0x0 => BatteryState.Discharging,
            0x1 => BatteryState.Charging,
            0x2 => BatteryState.Full,
            _ => BatteryState.Unknown,
        };
        return true;
    }

    private static void ParseButtons1(byte b, ref DualSenseInputState state)
    {
        state.L1 = (b & 0x01) != 0;
        state.R1 = (b & 0x02) != 0;
        state.L2Button = (b & 0x04) != 0;
        state.R2Button = (b & 0x08) != 0;
        state.Create = (b & 0x10) != 0;
        state.Options = (b & 0x20) != 0;
        state.L3 = (b & 0x40) != 0;
        state.R3 = (b & 0x80) != 0;
    }

    private static TouchPoint ParseTouch(ReadOnlySpan<byte> data)
    {
        var point = new TouchPoint
        {
            Active = (data[0] & 0x80) == 0,
            Id = data[0] & 0x7F,
            X = data[1] | ((data[2] & 0x0F) << 8),
            Y = (data[2] >> 4) | (data[3] << 4),
        };
        return point;
    }

    private static short ReadInt16(ReadOnlySpan<byte> data, int offset)
    {
        return (short)(data[offset] | (data[offset + 1] << 8));
    }
}
