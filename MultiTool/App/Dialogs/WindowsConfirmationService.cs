namespace MultiTool.Presentation.Dialogs;

public sealed class WindowsConfirmationService : IConfirmationService
{
    public bool Confirm(string title, string message) =>
        System.Windows.MessageBox.Show(
            message,
            title,
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning,
            System.Windows.MessageBoxResult.No) == System.Windows.MessageBoxResult.Yes;
}
