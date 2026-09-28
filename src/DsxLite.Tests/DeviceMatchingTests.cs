using DsxLite.Core.Haptics;

namespace DsxLite.Tests;

public class DeviceMatchingTests
{
    [Fact]
    public void Extract_HidDevicePath()
    {
        string? segment = DualSenseHapticsOutput.ExtractUsbInstanceSegment(
            @"\\?\hid#vid_054c&pid_0ce6&mi_03#8&1a2b3c4&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}");
        Assert.Equal("8&1a2b3c4&0", segment);
    }

    [Fact]
    public void Extract_AudioEndpointInstanceId()
    {
        string? segment = DualSenseHapticsOutput.ExtractUsbInstanceSegment(
            @"USB\VID_054C&PID_0CE6&MI_02\8&1a2b3c4&0&0002");
        Assert.Equal("8&1a2b3c4&0", segment);
    }

    [Fact]
    public void Extract_HidAndAudio_Match()
    {
        string? hid = DualSenseHapticsOutput.ExtractUsbInstanceSegment(
            @"\\?\hid#vid_054c&pid_0ce6&mi_03#9&ff00ee11&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}");
        string? audio = DualSenseHapticsOutput.ExtractUsbInstanceSegment(
            @"USB\VID_054C&PID_0CE6&MI_02\9&ff00ee11&0&0002");
        Assert.Equal(hid, audio);
    }

    [Fact]
    public void Extract_NoMatch_ReturnsNull()
    {
        Assert.Null(DualSenseHapticsOutput.ExtractUsbInstanceSegment(@"USB\VID_054C&PID_0CE6\ABCD1234"));
        Assert.Null(DualSenseHapticsOutput.ExtractUsbInstanceSegment(""));
    }

    [Fact]
    public void Extract_DifferentControllers_DoNotMatch()
    {
        string? a = DualSenseHapticsOutput.ExtractUsbInstanceSegment(
            @"\\?\hid#vid_054c&pid_0ce6&mi_03#8&111&0&0000#{x}");
        string? b = DualSenseHapticsOutput.ExtractUsbInstanceSegment(
            @"USB\VID_054C&PID_0CE6&MI_02\8&222&0&0002");
        Assert.NotEqual(a, b);
    }
}
