using HidSharp;

namespace DsxLite.Core.DualSense;

public static class DualSenseEnumerator
{
    /// <summary>
    /// Finds all DualSense HID interfaces (USB and Bluetooth, standard and Edge).
    /// Windows exposes several HID collections per controller; only the ones carrying
    /// the main input report are usable.
    /// </summary>
    public static List<DualSenseDevice> FindAll()
    {
        var result = new List<DualSenseDevice>();
        foreach (int pid in new[] { DualSenseIds.ProductId, DualSenseIds.EdgeProductId })
        {
            foreach (HidDevice device in DeviceList.Local.GetHidDevices(DualSenseIds.VendorId, pid))
            {
                int inputLen;
                int outputLen;
                try
                {
                    inputLen = device.GetMaxInputReportLength();
                    outputLen = device.GetMaxOutputReportLength();
                }
                catch
                {
                    continue;
                }

                ConnectionType? connection = inputLen switch
                {
                    >= DualSenseIds.InputReportBtSize => ConnectionType.Bluetooth,
                    >= DualSenseIds.InputReportUsbSize => ConnectionType.Usb,
                    _ => null,
                };
                if (connection is null || outputLen < DualSenseIds.OutputReportUsbSize)
                    continue;

                result.Add(new DualSenseDevice(device, connection.Value));
            }
        }
        return result;
    }
}
