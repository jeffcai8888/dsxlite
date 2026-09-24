namespace DsxLite.Core.DualSense;

public static class DualSenseIds
{
    public const int VendorId = 0x054C;
    public const int ProductId = 0x0CE6;
    public const int EdgeProductId = 0x0DF2;

    public const byte InputReportUsb = 0x01;
    public const int InputReportUsbSize = 64;
    public const byte InputReportBt = 0x31;
    public const int InputReportBtSize = 78;

    public const byte OutputReportUsb = 0x02;
    public const int OutputReportUsbSize = 48;
    public const byte OutputReportBt = 0x31;
    public const int OutputReportBtSize = 78;

    public const byte FeatureReportCalibration = 0x05;
    public const int FeatureReportCalibrationSize = 41;

    public const int TouchpadWidth = 1920;
    public const int TouchpadHeight = 1080;

    public const double AccelUnitsPerG = 8192.0;
    public const double GyroUnitsPerDegreeSec = 1024.0;
}

public enum ConnectionType
{
    Usb,
    Bluetooth,
}

public enum BatteryState
{
    Discharging,
    Charging,
    Full,
    Unknown,
}
