using MultiTool.Presentation.ViewModels;

namespace MultiTool.Modules.MongoDbBackup.Views;

public partial class MongoDbBackupPage : System.Windows.Controls.UserControl
{
    private MongoDbBackupViewModel? _subscribedViewModel;

    public MongoDbBackupPage()
    {
        InitializeComponent();
    }

    private void HandleLoaded(object sender, System.Windows.RoutedEventArgs eventArgs)
    {
        if (DataContext is not MongoDbBackupViewModel viewModel
            || ReferenceEquals(viewModel, _subscribedViewModel))
        {
            return;
        }

        Unsubscribe();
        _subscribedViewModel = viewModel;
        _subscribedViewModel.ConnectionInputConsumed += HandleConnectionInputConsumed;
    }

    private void HandleUnloaded(object sender, System.Windows.RoutedEventArgs eventArgs) => Unsubscribe();

    private async void HandleBackupNow(object sender, System.Windows.RoutedEventArgs eventArgs)
    {
        if (DataContext is not MongoDbBackupViewModel viewModel)
        {
            return;
        }

        var connectionString = ConnectionStringBox.Password;
        try
        {
            await viewModel.StartBackupAsync(connectionString);
        }
        finally
        {
            connectionString = string.Empty;
            ConnectionStringBox.Clear();
        }
    }

    private void HandleConnectionInputConsumed(object? sender, EventArgs eventArgs) =>
        ConnectionStringBox.Clear();

    private void Unsubscribe()
    {
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.ConnectionInputConsumed -= HandleConnectionInputConsumed;
            _subscribedViewModel = null;
        }
    }
}
