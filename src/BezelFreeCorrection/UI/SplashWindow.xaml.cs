using System.Windows;

namespace BezelFreeCorrection.UI;

// Tiny always-on-top window shown at process start while the rest of
// the app (NVAPI init, monitor enumeration, gallery load) is warming
// up. A startup that takes a few seconds would otherwise look like a
// silent launch failure; the splash shows the product name, version
// and an indeterminate progress bar until the HUD is ready to take
// over.
public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
        VersionLabel.Text = $"v{AppInfo.Version}";
    }

    // Updates the subtitle line while the host app is still loading —
    // lets the caller announce what's currently being done ("Detecting
    // displays…", "Checking for updates…", etc.) so the user knows the
    // app is alive.
    public void SetStatus(string text)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => StatusLabel.Text = text);
            return;
        }
        StatusLabel.Text = text;
    }
}
