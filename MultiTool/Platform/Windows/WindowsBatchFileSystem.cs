using System.IO;
using MultiTool.Modules.BatchFileOrganizer;

namespace MultiTool.Platform.Windows;

public sealed class WindowsBatchFileSystem : IBatchFileSystem
{
    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool FileExists(string path) => File.Exists(path);

    public IReadOnlyList<string> EnumerateTopLevelFiles(string folderPath) =>
        Directory.EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly).ToArray();

    public void MoveFile(string sourcePath, string destinationPath) =>
        File.Move(sourcePath, destinationPath, overwrite: false);

    public void DeleteFile(string path) => File.Delete(path);
}
