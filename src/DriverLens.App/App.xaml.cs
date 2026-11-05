using System.Windows;

namespace DriverLens.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var viewModel = new MainWindowViewModel();
        var mainWindow = new MainWindow(viewModel);
        mainWindow.Show();
    }
}
