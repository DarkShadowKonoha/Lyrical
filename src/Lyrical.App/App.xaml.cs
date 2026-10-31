using System.Windows;

namespace Lyrical;

public partial class App : Application
{
    // Single-instance guard so two copies don't fight over the media session.
    private static Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, @"Local\Lyrical.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
