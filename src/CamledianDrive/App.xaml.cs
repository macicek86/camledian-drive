using System.Windows;

namespace CamledianDrive;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }
}
