namespace MultiTool.Presentation.Dialogs;

public interface IConfirmationService
{
    bool Confirm(string title, string message);
}
