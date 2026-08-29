using Microsoft.Win32;
using MultiTool.Modules.BatchFileOrganizer;

namespace MultiTool.Platform.Windows;

public sealed class WindowsBatchFileOrganizerFolderPicker : IBatchFileOrganizerFolderPicker
{
    public string? SelectFolder()
    {
        var picturesFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        var dialog = new OpenFolderDialog
        {
            Title = "Select a folder to organize",
            AddToRecent = false,
            DefaultDirectory = picturesFolder,
            InitialDirectory = picturesFolder,
            Multiselect = false
        };

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
