using System.Threading;
using System.Windows;

namespace UniversalCam;

public partial class App : System.Windows.Application
{
    private static Mutex? _instanceMutex;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        _instanceMutex = new Mutex(true, "UniversalCam_SingleInstance", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show("UniversalCam is already running.", "UniversalCam",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, ex) =>
        {
            Console.WriteLine($"[FATAL] UI thread: {ex.Exception}");
            ex.Handled = false;
        };
        System.AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            Console.WriteLine($"[FATAL] {ex.ExceptionObject}");
        base.OnStartup(e);
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _instanceMutex?.ReleaseMutex();
        base.OnExit(e);
    }
}
