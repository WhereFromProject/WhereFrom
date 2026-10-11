using System.Windows;

namespace WhereFrom.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        if (e.Args.Length == 1)
            window.OpenStartupFile(e.Args[0]);
    }
}
