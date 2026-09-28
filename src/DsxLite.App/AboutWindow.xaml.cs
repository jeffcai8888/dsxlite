using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;

namespace DsxLite.App;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        string version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "unknown";
        // Strip the +commit hash suffix if present.
        int plus = version.IndexOf('+');
        if (plus > 0)
            version = version[..plus];
        VersionText.Text = $"{Localization.Get("VersionLabel")} {version}  |  .NET {Environment.Version}";
    }

    private void OnLinkClicked(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void OnOkClicked(object sender, RoutedEventArgs e) => Close();
}
