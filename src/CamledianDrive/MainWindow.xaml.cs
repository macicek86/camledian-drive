using System.Diagnostics;
using System.Windows;
using CamledianDrive.Services;

namespace CamledianDrive;

public partial class MainWindow : Window
{
    private readonly IMountService _mountService = new RcloneMountService();

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        var username = UsernameBox.Text.Trim();
        var password = PasswordBox.Password;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            StatusText.Text = "Vyplň uživatelské jméno a heslo.";
            return;
        }

        SetBusy(true);
        StatusText.Text = "Připojuji…";

        try
        {
            await _mountService.MountAsync(username, password);
            StatusText.Text = "Připojeno jako Camledian Drive (X:)";
            DisconnectButton.IsEnabled = true;
            ConnectButton.IsEnabled = false;
            OpenDrive();
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            ConnectButton.IsEnabled = true;
            DisconnectButton.IsEnabled = false;
        }
        finally
        {
            PasswordBox.Password = string.Empty;
            SetBusy(false, keepConnectDisabled: await _mountService.IsMountedAsync());
        }
    }

    private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        StatusText.Text = "Odpojuji…";

        try
        {
            await _mountService.UnmountAsync();
            StatusText.Text = "Nepřipojeno";
            DisconnectButton.IsEnabled = false;
            ConnectButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static void OpenDrive()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = @"X:\",
                UseShellExecute = true
            });
        }
        catch
        {
            // Mount is already useful even if Explorer could not be opened.
        }
    }

    private void SetBusy(bool busy, bool keepConnectDisabled = false)
    {
        UsernameBox.IsEnabled = !busy;
        PasswordBox.IsEnabled = !busy;
        if (busy)
        {
            ConnectButton.IsEnabled = false;
            DisconnectButton.IsEnabled = false;
        }
        else if (!keepConnectDisabled)
        {
            ConnectButton.IsEnabled = true;
        }
    }
}
