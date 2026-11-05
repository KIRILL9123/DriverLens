using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Wpf.Ui.Controls;

namespace DriverLens.App;

public partial class MainWindow : FluentWindow
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.ConfirmInstallCallback = async (deviceItem, systemRestoreDisabledWarning) =>
        {
            var dialog = new Wpf.Ui.Controls.ContentDialog(RootContentDialogPresenter)
            {
                Title = "Подтверждение обновления драйвера",
                PrimaryButtonText = "Обновить",
                CloseButtonText = "Отмена",
                DefaultButton = Wpf.Ui.Controls.ContentDialogButton.Primary
            };

            var sb = new StringBuilder();
            sb.AppendLine("Вы действительно хотите обновить драйвер для устройства:");
            sb.AppendLine($"\"{deviceItem.FriendlyName}\"?\n");
            sb.AppendLine($"Текущий драйвер: {deviceItem.CurrentDriverText}");
            sb.AppendLine($"Предложенный драйвер: {deviceItem.ProposedDriverText}\n");

            if (systemRestoreDisabledWarning)
            {
                sb.AppendLine("⚠️ ВНИМАНИЕ: Восстановление системы (System Restore), похоже, отключено в Windows.");
                sb.AppendLine("Резервная копия драйвера (Snapshot) будет единственным путем отката в случае проблем!");
            }

            dialog.Content = new System.Windows.Controls.TextBlock
            {
                Text = sb.ToString(),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13
            };

            var result = await dialog.ShowAsync(System.Threading.CancellationToken.None);
            return result == Wpf.Ui.Controls.ContentDialogResult.Primary;
        };
    }

    private void ToggleDetails_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is DeviceItemViewModel vm)
        {
            vm.ShowDetailsExpanded = !vm.ShowDetailsExpanded;
        }
    }
}
