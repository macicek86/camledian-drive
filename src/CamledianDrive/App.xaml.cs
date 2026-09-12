using System.Threading;
using System.Windows;

namespace CamledianDrive;

public partial class App : System.Windows.Application
{
    private const string MutexName = "CamledianDrive-SingleInstance-Mutex";
    private const string ShowEventName = "CamledianDrive-SingleInstance-ShowEvent";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _showEvent;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instanceMutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            // Another instance is already running – ask it to come to the
            // foreground instead of starting a second copy of the app.
            try
            {
                using var existingShowEvent = EventWaitHandle.OpenExisting(ShowEventName);
                existingShowEvent.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
            }

            Shutdown();
            return;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        var listenerThread = new Thread(() => ListenForActivation(window))
        {
            IsBackground = true
        };
        listenerThread.Start();
    }

    private void ListenForActivation(MainWindow window)
    {
        while (_showEvent?.WaitOne() == true)
        {
            window.Dispatcher.Invoke(window.BringToForeground);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_instanceMutex is not null)
        {
            _instanceMutex.ReleaseMutex();
            _instanceMutex.Dispose();
        }

        base.OnExit(e);
    }
}
