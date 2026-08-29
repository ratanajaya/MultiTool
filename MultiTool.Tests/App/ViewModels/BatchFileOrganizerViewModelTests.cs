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
    public async Task MoveFiles_MovesNonContiguousFilesAsAnOrderedGroup()
    {
        var fixture = new Fixture("a.txt", "b.txt", "c.txt", "d.txt", "e.txt");
        await fixture.ViewModel.LoadFolderAsync(fixture.Folder);
        var movedFiles = new[]
        {
            fixture.ViewModel.Files[1],
            fixture.ViewModel.Files[3]
        };

        fixture.ViewModel.MoveFiles(movedFiles, fixture.ViewModel.Files.Count);

        CollectionAssert.AreEqual(
            new[] { "a.txt", "c.txt", "e.txt", "b.txt", "d.txt" },
            fixture.ViewModel.Files.Select(file => file.CurrentName).ToArray());
        CollectionAssert.AreEqual(
            new[] { 1, 2, 3, 4, 5 },
            fixture.ViewModel.Files.Select(file => file.Position).ToArray());
        Assert.AreEqual("Moved 2 files. Files will be numbered in tile order.", fixture.ViewModel.Status);
    }

    [TestMethod]
    public async Task RequestDeleteFiles_OpensConfirmationWithoutDeletingFiles()
    {
        var fixture = new Fixture("a.txt", "b.txt");
        await fixture.ViewModel.LoadFolderAsync(fixture.Folder);

        fixture.ViewModel.RequestDeleteFiles(fixture.ViewModel.Files);

        Assert.IsTrue(fixture.ViewModel.IsDeleteConfirmationOpen);
        Assert.IsFalse(fixture.ViewModel.CanEdit);
        Assert.AreEqual("Delete 2 files?", fixture.ViewModel.DeleteConfirmationHeading);
        StringAssert.Contains(fixture.ViewModel.DeleteConfirmationMessage, "a.txt");
        StringAssert.Contains(fixture.ViewModel.DeleteConfirmationMessage, "cannot be undone");
        Assert.AreEqual(2, fixture.FileSystem.Files.Count);
    }

    [TestMethod]
    public async Task CancelDelete_ClosesConfirmationWithoutDeletingFiles()
    {
        var fixture = new Fixture("a.txt");
        await fixture.ViewModel.LoadFolderAsync(fixture.Folder);
        fixture.ViewModel.RequestDeleteFiles(fixture.ViewModel.Files);

        fixture.ViewModel.CancelDeleteCommand.Execute(null);

        Assert.IsFalse(fixture.ViewModel.IsDeleteConfirmationOpen);
        Assert.IsTrue(fixture.ViewModel.CanEdit);
        Assert.AreEqual(1, fixture.FileSystem.Files.Count);
    }

    [TestMethod]
    public async Task ConfirmDelete_DeletesSelectionAndRecomputesPositions()
    {
        var fixture = new Fixture("a.txt", "b.txt", "c.txt");
        await fixture.ViewModel.LoadFolderAsync(fixture.Folder);
        fixture.ViewModel.RequestDeleteFiles(
            new[] { fixture.ViewModel.Files[0], fixture.ViewModel.Files[2] });

        fixture.ViewModel.ConfirmDeleteCommand.Execute(null);
        await WaitUntilAsync(() => !fixture.ViewModel.IsBusy);

        Assert.IsFalse(fixture.ViewModel.IsDeleteConfirmationOpen);
        CollectionAssert.AreEqual(
            new[] { "b.txt" },
            fixture.ViewModel.Files.Select(file => file.CurrentName).ToArray());
        Assert.AreEqual(1, fixture.ViewModel.Files[0].Position);
        Assert.AreEqual("Deleted 2 files.", fixture.ViewModel.Status);
        Assert.AreEqual(1, fixture.FileSystem.Files.Count);
    }

    [TestMethod]
    public async Task ConfirmDelete_PartialFailureKeepsFailedFileAndReportsIt()
    {
        var fixture = new Fixture("a.txt", "b.txt");
        await fixture.ViewModel.LoadFolderAsync(fixture.Folder);
        fixture.FileSystem.FailingDeletes.Add(fixture.ViewModel.Files[1].CurrentPath);
        fixture.ViewModel.RequestDeleteFiles(fixture.ViewModel.Files);

        fixture.ViewModel.ConfirmDeleteCommand.Execute(null);
        await WaitUntilAsync(() => !fixture.ViewModel.IsBusy);

        CollectionAssert.AreEqual(
            new[] { "b.txt" },
            fixture.ViewModel.Files.Select(file => file.CurrentName).ToArray());
        StringAssert.Contains(fixture.ViewModel.Status, "1 failed");
        StringAssert.Contains(fixture.ViewModel.Status, "b.txt");
    }

    [TestMethod]
    public async Task ConfirmDelete_AllFilesShowsEmptyFolderState()
    {
        var fixture = new Fixture("a.txt");
        await fixture.ViewModel.LoadFolderAsync(fixture.Folder);
        fixture.ViewModel.RequestDeleteFiles(fixture.ViewModel.Files);

        fixture.ViewModel.ConfirmDeleteCommand.Execute(null);
        await WaitUntilAsync(() => !fixture.ViewModel.IsBusy);

        Assert.AreEqual(0, fixture.ViewModel.Files.Count);
        Assert.IsNull(fixture.ViewModel.SelectedFile);
        Assert.AreEqual("This folder contains no files.", fixture.ViewModel.PreviewMessage);
    }

    [TestMethod]
    public async Task LeaveModule_ClearsEveryFolderDependentValue()
    {
        var fixture = new Fixture("photo.jpg", "notes.txt");
        await fixture.ViewModel.LoadFolderAsync(fixture.Folder);
        fixture.ViewModel.OpenRenameDialogCommand.Execute(null);

        fixture.ViewModel.LeaveModule();

        Assert.IsFalse(fixture.ViewModel.HasFolder);
        Assert.AreEqual(string.Empty, fixture.ViewModel.FolderPath);
        Assert.AreEqual(0, fixture.ViewModel.Files.Count);
        Assert.IsNull(fixture.ViewModel.SelectedFile);
        Assert.IsNull(fixture.ViewModel.PreviewSource);
        Assert.IsFalse(fixture.ViewModel.IsRenameDialogOpen);
        Assert.AreEqual("Drop a folder here or choose Browse.", fixture.ViewModel.Status);
    }

    [TestMethod]
    public async Task LeaveModule_ClosesDeleteConfirmationWithoutDeletingFiles()
    {
        var fixture = new Fixture("notes.txt");
        await fixture.ViewModel.LoadFolderAsync(fixture.Folder);
        fixture.ViewModel.RequestDeleteFiles(fixture.ViewModel.Files);

        fixture.ViewModel.LeaveModule();

        Assert.IsFalse(fixture.ViewModel.IsDeleteConfirmationOpen);
        Assert.AreEqual(1, fixture.FileSystem.Files.Count);
    }

    [TestMethod]
    public async Task RenameDialog_OpenAndCloseControlsBackgroundEditing()
    {
        var fixture = new Fixture("notes.txt");

        Assert.IsFalse(fixture.ViewModel.OpenRenameDialogCommand.CanExecute(null));
        await fixture.ViewModel.LoadFolderAsync(fixture.Folder);

        fixture.ViewModel.OpenRenameDialogCommand.Execute(null);

        Assert.IsTrue(fixture.ViewModel.IsRenameDialogOpen);
        Assert.IsFalse(fixture.ViewModel.CanEdit);
        Assert.IsFalse(fixture.ViewModel.BrowseCommand.CanExecute(null));
        Assert.IsFalse(fixture.ViewModel.ReloadCommand.CanExecute(null));
        Assert.IsTrue(fixture.ViewModel.CloseRenameDialogCommand.CanExecute(null));

        fixture.ViewModel.CloseRenameDialogCommand.Execute(null);

        Assert.IsFalse(fixture.ViewModel.IsRenameDialogOpen);
        Assert.IsTrue(fixture.ViewModel.CanEdit);
    }

    [TestMethod]
    public async Task RenameDialog_SuccessClosesAndUpdatesCurrentNames()
    {
        var fixture = new Fixture("notes.txt", "photo.jpg");
        await fixture.ViewModel.LoadFolderAsync(fixture.Folder);
        fixture.ViewModel.OpenRenameDialogCommand.Execute(null);

        fixture.ViewModel.RenameCommand.Execute(null);
        await WaitUntilAsync(() => !fixture.ViewModel.IsBusy);

        Assert.IsFalse(fixture.ViewModel.IsRenameDialogOpen);
        CollectionAssert.AreEqual(
            new[] { "File_001.txt", "File_002.jpg" },
            fixture.ViewModel.Files.Select(file => file.CurrentName).ToArray());
    }

    [TestMethod]
    public async Task RenameDialog_MoveFailureStaysOpenAndReportsFailure()
    {
        var fixture = new Fixture("notes.txt");
        await fixture.ViewModel.LoadFolderAsync(fixture.Folder);
        fixture.FileSystem.FailMoves = true;
        fixture.ViewModel.OpenRenameDialogCommand.Execute(null);

        fixture.ViewModel.RenameCommand.Execute(null);
        await WaitUntilAsync(() => !fixture.ViewModel.IsBusy);

        Assert.IsTrue(fixture.ViewModel.IsRenameDialogOpen);
        StringAssert.Contains(fixture.ViewModel.RenameOperationMessage, "failed");
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
    public async Task LoadFolder_RequestsALargerBoundedThumbnailForImages()
    {
        var fixture = new Fixture("photo.jpg");

        await fixture.ViewModel.LoadFolderAsync(fixture.Folder);

        CollectionAssert.Contains(fixture.PreviewLoader.MaximumDimensions, 192);
        Assert.IsNotNull(fixture.ViewModel.Files[0].ThumbnailSource);
    }

    [TestMethod]
    public void MultiToolSettings_DoesNotPersistAnOrganizerFolder()
    {
        Assert.IsFalse(typeof(MultiToolSettings).GetProperties().Any(property =>
            property.Name.Contains("Organizer", StringComparison.OrdinalIgnoreCase)));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
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

        public bool FailMoves { get; set; }

        public HashSet<string> FailingDeletes { get; } = new(StringComparer.OrdinalIgnoreCase);

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
            if (FailMoves)
            {
                throw new IOException("Injected move failure.");
            }

            Files.Remove(Path.GetFullPath(sourcePath));
            Files.Add(Path.GetFullPath(destinationPath));
        }

        public void DeleteFile(string path)
        {
            var fullPath = Path.GetFullPath(path);
            if (FailingDeletes.Contains(fullPath))
            {
                throw new IOException("Injected delete failure.");
            }

            Files.Remove(fullPath);
        }
    }

    private sealed class FakeFolderPicker : IBatchFileOrganizerFolderPicker
    {
        public string? SelectFolder() => null;
    }

    private sealed class FakePreviewLoader : IImagePreviewLoader
    {
        public int LoadCount { get; private set; }

        public List<int?> MaximumDimensions { get; } = [];

        public Task<ImageSource> LoadAsync(
            string filePath,
            int? maximumPixelDimension = null,
            CancellationToken cancellationToken = default)
        {
            LoadCount++;
            MaximumDimensions.Add(maximumPixelDimension);
            var image = new DrawingImage();
            image.Freeze();
            return Task.FromResult<ImageSource>(image);
        }
    }
}
