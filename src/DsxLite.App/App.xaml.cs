using System.Windows;

namespace DsxLite.App;

public partial class App : System.Windows.Application
{
    /// <summary>Started via auto-start / --tray: hide to the system tray immediately.</summary>
    public static bool StartInTray { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        StartInTray = e.Args.Contains("--tray");
        base.OnStartup(e);

        Localization.Initialize();

        // The tray keeps the app alive while the window is hidden.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var window = new MainWindow();
        if (!StartInTray)
            window.Show();
    }
}
