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

        if (!DependencyService.IsWinFspInstalled())
        {
            var answer = MessageBox.Show(
                "Camledian Drive potřebuje systémový ovladač WinFsp. Instalátor je součástí balíčku. Chceš ho teď nainstalovat?",
                "Camledian Drive – WinFsp",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (answer != MessageBoxResult.Yes)
            {
                StatusText.Text = "Bez WinFsp nelze virtuální disk připojit.";
                return;
            }

            SetBusy(true);
            StatusText.Text = "Instaluji WinFsp…";

            try
            {
                await DependencyService.InstallBundledWinFspAsync();
                if (!DependencyService.IsWinFspInstalled())
                {
                    StatusText.Text = "WinFsp byl nainstalován, ale zatím není dostupný. Zkus restartovat Camledian Drive nebo Windows.";
                    return;
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = ex.Message;
                return;
            }
            finally
            {
                SetBusy(false);
            }
        }

        SetBusy(true);
        StatusText.Text = "Připojuji…";

        try
        {
            await _mountService.MountAsync(username, password);
            StatusText.Text = "Připojeno jako Camledian Drive (X:)";
            SetMountedState(true);
            OpenDrive();
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            SetMountedState(false);
        }
        finally
        {
            PasswordBox.Password = string.Empty;
            var mounted = await _mountService.IsMountedAsync();
            SetBusy(false, keepConnectDisabled: mounted);
            SetMountedState(mounted);
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
            SetMountedState(false);
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            SetMountedState(await _mountService.IsMountedAsync());
        }
        finally
        {
            SetBusy(false, keepConnectDisabled: await _mountService.IsMountedAsync());
        }
    }

    private void OpenDriveButton_Click(object sender, RoutedEventArgs e)
    {
        OpenDrive();
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

    private void SetMountedState(bool mounted)
    {
        ConnectButton.IsEnabled = !mounted;
        DisconnectButton.IsEnabled = mounted;
        OpenDriveButton.IsEnabled = mounted;
    }

    private void SetBusy(bool busy, bool keepConnectDisabled = false)
    {
        UsernameBox.IsEnabled = !busy;
        PasswordBox.IsEnabled = !busy;

        if (busy)
        {
            ConnectButton.IsEnabled = false;
            DisconnectButton.IsEnabled = false;
            OpenDriveButton.IsEnabled = false;
            return;
        }

        if (!keepConnectDisabled)
        {
            ConnectButton.IsEnabled = true;
        }
    }
}
