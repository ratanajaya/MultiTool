using System.IO;
using Microsoft.Win32;
using MultiTool.Modules.MongoDbBackup;

namespace MultiTool.Platform.Windows;

public sealed class WindowsMongoDbBackupFileDialogService : IMongoDbBackupFileDialogService
{
    public string? SelectMongoDumpExecutable(string? currentPath)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select mongodump.exe",
            Filter = "mongodump executable (mongodump.exe)|mongodump.exe|Executable files (*.exe)|*.exe",
            CheckFileExists = true,
            Multiselect = false,
            FileName = string.IsNullOrWhiteSpace(currentPath) ? "mongodump.exe" : currentPath
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public MongoDbArchiveSelection? SelectArchive(
        string? initialDirectory,
        string defaultFileName)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save MongoDB backup archive",
            Filter = "Compressed MongoDB archive (*.archive.gz)|*.archive.gz",
            AddExtension = true,
            DefaultExt = ".archive.gz",
            OverwritePrompt = true,
            FileName = defaultFileName,
            InitialDirectory = Directory.Exists(initialDirectory) ? initialDirectory : string.Empty
        };
        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        return new MongoDbArchiveSelection(dialog.FileName, OverwriteExisting: File.Exists(dialog.FileName));
    }
}
