using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CamledianDrive.Services;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace CamledianDrive;

public partial class MainWindow : Window
{
    private readonly IMountService _mountService = new RcloneMountService();
    private readonly DispatcherTimer _stateTimer;
    private WinForms.NotifyIcon? _trayIcon;
    private WinForms.ToolStripMenuItem? _trayOpenDriveItem;
    private WinForms.ToolStripMenuItem? _trayDisconnectItem;
    private bool _allowClose;
    private bool _isBusy;
    private bool _shownTrayHint;
    private bool? _lastMounted;

    public MainWindow()
    {
        InitializeComponent();
        InitializeTrayIcon();

        MaxHeight = SystemParameters.WorkArea.Height;

        SourceInitialized += (_, _) => UpdateMaxSizeForCurrentScreen();
        LocationChanged += (_, _) => UpdateMaxSizeForCurrentScreen();
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;

        _stateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _stateTimer.Tick += StateTimer_Tick;
    }

    private void UpdateMaxSizeForCurrentScreen()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;

        var workingArea = WinForms.Screen.FromHandle(handle).WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(this);

        MaxWidth = workingArea.Width / dpi.DpiScaleX;
        MaxHeight = workingArea.Height / dpi.DpiScaleY;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var saved = CredentialService.Load();
            if (saved is not null)
            {
                UsernameBox.Text = saved.Username;
                PasswordBox.Password = saved.Password;
                RememberCredentialsCheckBox.IsChecked = true;
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Přihlášení se nepodařilo načíst: {ex.Message}";
        }

        await RefreshMountStateAsync(forceStatusText: true);
        _stateTimer.Start();
    }

    private async void StateTimer_Tick(object? sender, EventArgs e)
    {
        if (_isBusy) return;
        await RefreshMountStateAsync();
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
            var answer = System.Windows.MessageBox.Show(
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
            SetMountedState(true);

            if (RememberCredentialsCheckBox.IsChecked == true)
            {
                try
                {
                    CredentialService.Save(username, password);
                }
                catch (Exception ex)
                {
                    StatusText.Text = $"Připojeno jako Camledian Drive ({DriveLetterOrFallback()}), ale přihlášení se nepodařilo uložit: {ex.Message}";
                    OpenDrive();
                    return;
                }
            }
            else
            {
                CredentialService.Delete();
                PasswordBox.Password = string.Empty;
            }

            StatusText.Text = $"Připojeno jako Camledian Drive ({DriveLetterOrFallback()})";
            OpenDrive();
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            SetMountedState(await _mountService.IsMountedAsync());
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        await DisconnectAsync();
    }

    private async Task DisconnectAsync()
    {
        if (_isBusy) return;

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
            SetBusy(false);
        }
    }

    private void OpenDriveButton_Click(object sender, RoutedEventArgs e)
    {
        OpenDrive();
    }

    private void RememberCredentialsCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        try
        {
            CredentialService.Delete();
        }
        catch
        {
            // Removing remembered credentials must not break an active mount.
        }
    }

    private async Task RefreshMountStateAsync(bool forceStatusText = false)
    {
        bool mounted;
        try
        {
            mounted = await _mountService.IsMountedAsync();
        }
        catch
        {
            return;
        }

        var changed = _lastMounted != mounted;
        SetMountedState(mounted);

        if (forceStatusText || changed)
        {
            StatusText.Text = mounted
                ? $"Připojeno jako Camledian Drive ({DriveLetterOrFallback()})"
                : "Nepřipojeno";
        }
    }

    private string DriveLetterOrFallback() => _mountService.CurrentDriveLetter ?? "X:";

    private void OpenDrive()
    {
        var driveLetter = _mountService.CurrentDriveLetter;
        if (driveLetter is null) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = $@"{driveLetter}\",
                UseShellExecute = true
            });
        }
        catch
        {
            // The mount is still usable even if Explorer could not be opened.
        }
    }

    private void SetMountedState(bool mounted)
    {
        _lastMounted = mounted;
        UpdateControls();
        UpdateTrayState(mounted);
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        UpdateControls();
    }

    private void UpdateControls()
    {
        var mounted = _lastMounted == true;

        UsernameBox.IsEnabled = !_isBusy && !mounted;
        PasswordBox.IsEnabled = !_isBusy && !mounted;
        RememberCredentialsCheckBox.IsEnabled = !_isBusy && !mounted;
        ConnectButton.IsEnabled = !_isBusy && !mounted;
        DisconnectButton.IsEnabled = !_isBusy && mounted;
        OpenDriveButton.IsEnabled = !_isBusy && mounted;
    }

    private void InitializeTrayIcon()
    {
        var menu = new WinForms.ContextMenuStrip();

        var showItem = new WinForms.ToolStripMenuItem("Otevřít Camledian Drive");
        showItem.Click += (_, _) => ShowFromTray();
        menu.Items.Add(showItem);

        _trayOpenDriveItem = new WinForms.ToolStripMenuItem("Otevřít disk")
        {
            Enabled = false
        };
        _trayOpenDriveItem.Click += (_, _) => OpenDrive();
        menu.Items.Add(_trayOpenDriveItem);

        _trayDisconnectItem = new WinForms.ToolStripMenuItem("Odpojit disk")
        {
            Enabled = false
        };
        _trayDisconnectItem.Click += async (_, _) => await DisconnectAsync();
        menu.Items.Add(_trayDisconnectItem);

        menu.Items.Add(new WinForms.ToolStripSeparator());

        var exitItem = new WinForms.ToolStripMenuItem("Ukončit a odpojit disk");
        exitItem.Click += async (_, _) => await ExitApplicationAsync();
        menu.Items.Add(exitItem);

        _trayIcon = new WinForms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "Camledian Drive – zjišťuji stav",
            Visible = true,
            ContextMenuStrip = menu
        };
        _trayIcon.DoubleClick += (_, _) => ShowFromTray();
    }

    private static Drawing.Icon LoadTrayIcon()
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/Assets/ikona-tray.png", UriKind.Absolute));

            if (resource is not null)
            {
                using var stream = resource.Stream;
                using var source = new Drawing.Bitmap(stream);
                using var bitmap = new Drawing.Bitmap(32, 32);
                using (var graphics = Drawing.Graphics.FromImage(bitmap))
                {
                    graphics.InterpolationMode = Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    graphics.DrawImage(source, 0, 0, 32, 32);
                }

                var hIcon = bitmap.GetHicon();

                try
                {
                    using var temporary = Drawing.Icon.FromHandle(hIcon);
                    return (Drawing.Icon)temporary.Clone();
                }
                finally
                {
                    DestroyIcon(hIcon);
                }
            }
        }
        catch
        {
            // Fall through to a system icon if the embedded artwork cannot be loaded.
        }

        return (Drawing.Icon)Drawing.SystemIcons.Application.Clone();
    }

    private void UpdateTrayState(bool mounted)
    {
        if (_trayIcon is null) return;

        _trayIcon.Text = mounted
            ? $"Camledian Drive – připojeno ({DriveLetterOrFallback()})"
            : "Camledian Drive – nepřipojeno";

        if (_trayOpenDriveItem is not null)
        {
            _trayOpenDriveItem.Text = mounted ? $"Otevřít disk {DriveLetterOrFallback()}" : "Otevřít disk";
            _trayOpenDriveItem.Enabled = mounted;
        }

        if (_trayDisconnectItem is not null)
            _trayDisconnectItem.Enabled = mounted && !_isBusy;

        DriveLabelText.Text = mounted ? $"Camledian Drive ({DriveLetterOrFallback()})" : "Camledian Drive";
    }

    private void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    public void BringToForeground() => ShowFromTray();

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;

        e.Cancel = true;
        Hide();

        if (!_shownTrayHint && _trayIcon is not null)
        {
            _shownTrayHint = true;
            _trayIcon.ShowBalloonTip(
                2500,
                "Camledian Drive běží dál",
                "Disk zůstává připojený. Aplikaci najdeš v oznamovací oblasti vedle hodin.",
                WinForms.ToolTipIcon.Info);
        }
    }

    private async Task ExitApplicationAsync()
    {
        if (_isBusy) return;

        SetBusy(true);
        try
        {
            if (await _mountService.IsMountedAsync())
                await _mountService.UnmountAsync();
        }
        catch (Exception ex)
        {
            var result = System.Windows.MessageBox.Show(
                $"Disk se nepodařilo korektně odpojit:\n\n{ex.Message}\n\nPřesto ukončit Camledian Drive?",
                "Camledian Drive",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                SetBusy(false);
                return;
            }
        }

        _allowClose = true;
        _stateTimer.Stop();
        DisposeTrayIcon();
        System.Windows.Application.Current.Shutdown();
    }

    private void DisposeTrayIcon()
    {
        if (_trayIcon is null) return;

        var icon = _trayIcon.Icon;
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        icon?.Dispose();
        _trayIcon = null;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
