using MultiTool.Modules.BatchFileOrganizer;

namespace MultiTool.Tests.Modules.BatchFileOrganizer;

[TestClass]
public sealed class BatchFileRenameServiceTests
{
    [TestMethod]
    public async Task Rename_SafelyHandlesANameSwap()
    {
        var fileSystem = new FakeFileSystem();
        var folder = fileSystem.AddFolder("photos");
        var first = fileSystem.AddFile(folder, "A.txt", "first");
        var second = fileSystem.AddFile(folder, "B.txt", "second");
        var plan = ValidPlan(
            new(first, second),
            new(second, first));

        var result = await new BatchFileRenameService(fileSystem).RenameAsync(folder, plan);

        Assert.IsTrue(result.Succeeded, result.Message);
        Assert.AreEqual("second", fileSystem.ReadFile(first));
        Assert.AreEqual("first", fileSystem.ReadFile(second));
    }

    [TestMethod]
    public async Task Rename_WhenFinalMoveFails_RestoresEverySource()
    {
        var fileSystem = new FakeFileSystem { FailingMoveNumbers = [3] };
        var folder = fileSystem.AddFolder("photos");
        var first = fileSystem.AddFile(folder, "A.txt", "first");
        var second = fileSystem.AddFile(folder, "B.txt", "second");
        var plan = ValidPlan(
            new(first, Path.Combine(folder, "One.txt")),
            new(second, Path.Combine(folder, "Two.txt")));

        var result = await new BatchFileRenameService(fileSystem).RenameAsync(folder, plan);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.RolledBack, result.Message);
        Assert.AreEqual("first", fileSystem.ReadFile(first));
        Assert.AreEqual("second", fileSystem.ReadFile(second));
        Assert.IsFalse(fileSystem.Files.Keys.Any(path => path.Contains(".multitool-rename-")));
    }

    [TestMethod]
    public async Task Rename_WhenRollbackAlsoFails_ReportsUnrestoredPaths()
    {
        var fileSystem = new FakeFileSystem { FailingMoveNumbers = [3, 4] };
        var folder = fileSystem.AddFolder("photos");
        var first = fileSystem.AddFile(folder, "A.txt", "first");
        var second = fileSystem.AddFile(folder, "B.txt", "second");
        var plan = ValidPlan(
            new(first, Path.Combine(folder, "One.txt")),
            new(second, Path.Combine(folder, "Two.txt")));

        var result = await new BatchFileRenameService(fileSystem).RenameAsync(folder, plan);

        Assert.IsFalse(result.Succeeded);
        Assert.IsFalse(result.RolledBack);
        Assert.IsTrue(result.UnrestoredPaths.Count > 0);
        StringAssert.Contains(result.Message, "could not be restored");
    }

    [TestMethod]
    public async Task Rename_WhenFolderSnapshotChanged_BlocksBeforeMovingAnything()
    {
        var fileSystem = new FakeFileSystem();
        var folder = fileSystem.AddFolder("photos");
        var first = fileSystem.AddFile(folder, "A.txt", "first");
        fileSystem.AddFile(folder, "unexpected.txt", "new");
        var plan = ValidPlan(new BatchRenameRequest(first, Path.Combine(folder, "One.txt")));

        var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            new BatchFileRenameService(fileSystem).RenameAsync(folder, plan));

        StringAssert.Contains(exception.Message, "contents changed");
        Assert.AreEqual(0, fileSystem.MoveCount);
    }

    [TestMethod]
    public void ValidateCurrentConflicts_BlocksExistingDirectoryName()
    {
        var fileSystem = new FakeFileSystem();
        var folder = fileSystem.AddFolder("photos");
        var source = fileSystem.AddFile(folder, "A.txt", "first");
        var destination = fileSystem.AddFolder(Path.Combine(folder, "File_001.txt"));
        var plan = ValidPlan(new BatchRenameRequest(source, destination));

        var error = new BatchFileRenameService(fileSystem).ValidateCurrentConflicts(plan);

        StringAssert.Contains(error, "directory already uses");
    }

    [TestMethod]
    public async Task Rename_RejectsDestinationOutsideSelectedFolder()
    {
        var fileSystem = new FakeFileSystem();
        var folder = fileSystem.AddFolder("photos");
        var otherFolder = fileSystem.AddFolder("other");
        var source = fileSystem.AddFile(folder, "A.txt", "first");
        var plan = ValidPlan(new BatchRenameRequest(
            source,
            Path.Combine(otherFolder, "File_001.txt")));

        var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            new BatchFileRenameService(fileSystem).RenameAsync(folder, plan));

        StringAssert.Contains(exception.Message, "selected folder");
        Assert.AreEqual(0, fileSystem.MoveCount);
    }

    private static BatchRenamePlan ValidPlan(params BatchRenameRequest[] requests) => new()
    {
        Requests = requests,
        ProposedFileNames = requests.Select(request => Path.GetFileName(request.DestinationPath)).ToArray()
    };

    private sealed class FakeFileSystem : IBatchFileSystem
    {
        private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<int> FailingMoveNumbers { get; init; } = [];

        public int MoveCount { get; private set; }

        public string AddFolder(string path)
        {
            var fullPath = Path.GetFullPath(path);
            _directories.Add(fullPath);
            return fullPath;
        }

        public string AddFile(string folder, string name, string contents)
        {
            var path = Path.GetFullPath(Path.Combine(folder, name));
            Files.Add(path, contents);
            return path;
        }

        public string ReadFile(string path) => Files[Path.GetFullPath(path)];

        public bool DirectoryExists(string path) => _directories.Contains(Path.GetFullPath(path));

        public bool FileExists(string path) => Files.ContainsKey(Path.GetFullPath(path));

        public IReadOnlyList<string> EnumerateTopLevelFiles(string folderPath)
        {
            var fullFolder = Path.GetFullPath(folderPath);
            return Files.Keys
                .Where(path => string.Equals(
                    Path.GetDirectoryName(path),
                    fullFolder,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        public void MoveFile(string sourcePath, string destinationPath)
        {
            MoveCount++;
            if (FailingMoveNumbers.Contains(MoveCount))
            {
                throw new IOException($"Injected move failure {MoveCount}.");
            }

            var source = Path.GetFullPath(sourcePath);
            var destination = Path.GetFullPath(destinationPath);
            if (!Files.Remove(source, out var contents))
            {
                throw new FileNotFoundException("Source does not exist.", source);
            }

            if (Files.ContainsKey(destination))
            {
                Files.Add(source, contents);
                throw new IOException("Destination exists.");
            }

            Files.Add(destination, contents);
        }

        public void DeleteFile(string path) => Files.Remove(Path.GetFullPath(path));
    }
}
