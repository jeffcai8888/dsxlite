using DsxLite.Cli;

namespace DsxLite.Tests;

public class CalibrationDiagnosticTests
{
    [Fact]
    public void Defaults_AreBounded()
    {
        Assert.True(CalibrationDiagnostic.TryReadOptions(["--calibration"], out int index, out int samples));
        Assert.Equal(0, index);
        Assert.Equal(100, samples);
    }

    [Fact]
    public void ExplicitDeviceAndSampleCount_AreAccepted()
    {
        Assert.True(CalibrationDiagnostic.TryReadOptions(
            ["--samples", "3", "--calibration", "--device", "2"], out int index, out int samples));
        Assert.Equal(2, index);
        Assert.Equal(3, samples);
    }

    [Theory]
    [InlineData("--device -1")]
    [InlineData("--device")]
    [InlineData("--device text")]
    [InlineData("--device 2147483648")]
    [InlineData("--samples 0")]
    [InlineData("--samples 10001")]
    [InlineData("--samples 1 --samples 2")]
    [InlineData("--vigem")]
    [InlineData("--triggers")]
    [InlineData("--audio")]
    [InlineData("--calibration")]
    public void InvalidOrOutputOptions_AreRejected(string options)
    {
        string[] args = ["--calibration", .. options.Split(' ')];
        Assert.False(CalibrationDiagnostic.TryReadOptions(args, out _, out _));
    }

    [Fact]
    public void MissingDiagnosticMode_IsRejected()
    {
        Assert.False(CalibrationDiagnostic.TryReadOptions([], out _, out _));
    }
}
