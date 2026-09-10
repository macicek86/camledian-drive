using System.Windows;

namespace CamledianDrive;

public partial class MainWindow : Window
{
    private const string WebDavEndpoint = "https://admin.camledian.art/webdav/";

    public MainWindow()
    {
        InitializeComponent();
    }

    private void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        // The first implementation will validate credentials and hand the mount
        // request to an isolated mount service. Passwords must never be logged.
        StatusText.Text = $"Připraveno k připojení: {WebDavEndpoint}";
    }
}
