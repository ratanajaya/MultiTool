namespace MultiTool.Modules.BatchFileOrganizer;

public interface IBatchFileSystem
{
    bool DirectoryExists(string path);

    bool FileExists(string path);

    IReadOnlyList<string> EnumerateTopLevelFiles(string folderPath);

    void MoveFile(string sourcePath, string destinationPath);
}
