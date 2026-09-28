using System.Xml.Linq;

namespace DsxLite.Tests;

public class CalibrationLocalizationTests
{
    [Fact]
    public void AllEightLanguages_IncludeNonemptyCalibrationMessages()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "LocalizationResources");
        string[] resources = Directory.GetFiles(directory, "Strings.*.xaml");
        Assert.Equal(8, resources.Length);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        string[] keys = ["CalibrationFactory", "CalibrationFallback", "CalibrationNotRead", "MotionUnavailable"];
        foreach (string path in resources)
        {
            XDocument document = XDocument.Load(path);
            foreach (string key in keys)
            {
                var values = document.Root!.Elements().Where(element => (string?)element.Attribute(x + "Key") == key).ToArray();
                Assert.Single(values);
                Assert.False(string.IsNullOrWhiteSpace(values[0].Value), $"{path}: {key}");
            }
        }
    }
}
