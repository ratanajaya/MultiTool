using System.Windows.Media;
using MultiTool.Core.Settings;
using MultiTool.Modules.BatchFileOrganizer;
using MultiTool.Presentation.ViewModels;

namespace MultiTool.Tests.App.ViewModels;

[TestClass]
public sealed class BatchFileOrganizerViewModelTests
{
    [TestMethod]
    public async Task LoadFolder_NaturallySortsAndReorderUpdatesProposedNames()
    {
        var fixture = new Fixture("file10.txt", "file2.txt", "file1.txt");

        await fixture.ViewModel.LoadFolderAsync(fixture.Folder);

        CollectionAssert.AreEqual(
            new[] { "file1.txt", "file2.txt", "file10.txt" },
            fixture.ViewModel.Files.Select(file => file.CurrentName).ToArray());
        fixture.ViewModel.MoveFile(fixture.ViewModel.Files[2], 0);
        CollectionAssert.AreEqual(
            new[] { "file10.txt", "file1.txt", "file2.txt" },
            fixture.ViewModel.Files.Select(file => file.CurrentName).ToArray());
        CollectionAssert.AreEqual(
            new[] { "File_001.txt", "File_002.txt", "File_003.txt" },
            fixture.ViewModel.Files.Select(file => file.ProposedName).ToArray());
    }

    [TestMethod]
    public async Task LeaveModule_ClearsEveryFolderDependentValue()
    {
        var fixture = new Fixture("photo.jpg", "notes.txt");
        await fixture.ViewModel.LoadFolderAsync(fixture.Folder);

        fixture.ViewModel.LeaveModule();

        Assert.IsFalse(fixture.ViewModel.HasFolder);
        Assert.AreEqual(string.Empty, fixture.ViewModel.FolderPath);
        Assert.AreEqual(0, fixture.ViewModel.Files.Count);
        Assert.IsNull(fixture.ViewModel.SelectedFile);
        Assert.IsNull(fixture.ViewModel.PreviewSource);
        Assert.AreEqual("Drop a folder here or choose Browse.", fixture.ViewModel.Status);
    }

    [TestMethod]
    public async Task SelectNonImage_ShowsUnsupportedStateWithoutCallingLoader()
    {
        var fixture = new Fixture("notes.txt");

        await fixture.ViewModel.LoadFolderAsync(fixture.Folder);

        Assert.AreEqual(0, fixture.PreviewLoader.LoadCount);
        StringAssert.Contains(fixture.ViewModel.PreviewMessage, "not available");
    }

    [TestMethod]
    public void MultiToolSettings_DoesNotPersistAnOrganizerFolder()
    {
        Assert.IsFalse(typeof(MultiToolSettings).GetProperties().Any(property =>
            property.Name.Contains("Organizer", StringComparison.OrdinalIgnoreCase)));
    }

    private sealed class Fixture
    {
        public Fixture(params string[] fileNames)
        {
            Folder = Path.GetFullPath(Path.Combine("test-data", Guid.NewGuid().ToString("N")));
            FileSystem.Folders.Add(Folder);
            foreach (var name in fileNames)
            {
                FileSystem.Files.Add(Path.Combine(Folder, name));
            }

            ViewModel = new BatchFileOrganizerViewModel(
                FileSystem,
                new BatchFileRenameService(FileSystem),
                new FakeFolderPicker(),
                PreviewLoader);
        }

        public string Folder { get; }

        public FakeFileSystem FileSystem { get; } = new();

        public FakePreviewLoader PreviewLoader { get; } = new();

        public BatchFileOrganizerViewModel ViewModel { get; }
    }

    private sealed class FakeFileSystem : IBatchFileSystem
    {
        public HashSet<string> Folders { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool DirectoryExists(string path) => Folders.Contains(Path.GetFullPath(path));

        public bool FileExists(string path) => Files.Contains(Path.GetFullPath(path));

        public IReadOnlyList<string> EnumerateTopLevelFiles(string folderPath) =>
            Files.Where(path => string.Equals(
                    Path.GetDirectoryName(path),
                    Path.GetFullPath(folderPath),
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();

        public void MoveFile(string sourcePath, string destinationPath)
        {
            Files.Remove(Path.GetFullPath(sourcePath));
            Files.Add(Path.GetFullPath(destinationPath));
        }
    }

    private sealed class FakeFolderPicker : IBatchFileOrganizerFolderPicker
    {
        public string? SelectFolder() => null;
    }

    private sealed class FakePreviewLoader : IImagePreviewLoader
    {
        public int LoadCount { get; private set; }

        public Task<ImageSource> LoadAsync(
            string filePath,
            CancellationToken cancellationToken = default)
        {
            LoadCount++;
            var image = new DrawingImage();
            image.Freeze();
            return Task.FromResult<ImageSource>(image);
        }
    }
}
