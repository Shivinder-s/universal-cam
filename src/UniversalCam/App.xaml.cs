namespace UniversalCam;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, ex) =>
        {
            Console.WriteLine($"[FATAL] UI thread: {ex.Exception}");
            ex.Handled = false;
        };
        System.AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            Console.WriteLine($"[FATAL] {ex.ExceptionObject}");
        base.OnStartup(e);
    }
}
