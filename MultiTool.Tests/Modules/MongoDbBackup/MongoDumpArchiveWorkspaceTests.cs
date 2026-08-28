using System.Security.AccessControl;
using System.Security.Principal;
using MultiTool.Platform.Windows;

namespace MultiTool.Tests.Modules.MongoDbBackup;

[TestClass]
public sealed class MongoDumpArchiveWorkspaceTests
{
    [TestMethod]
    public void Complete_PromotesPartialArchiveAndRemovesProtectedConfig()
    {
        var testDirectory = CreateTestDirectory();
        var temporaryRoot = Path.Combine(testDirectory, "temporary");
        var finalPath = Path.Combine(testDirectory, "backup.archive.gz");
        const string uri = "mongodb://user:password@localhost";

        try
        {
            string runDirectory;
            using (var workspace = MongoDumpArchiveWorkspace.Create(
                       temporaryRoot,
                       finalPath,
                       overwriteExisting: false,
                       uri))
            {
                runDirectory = Path.GetDirectoryName(workspace.ConfigPath)!;
                StringAssert.Contains(File.ReadAllText(workspace.ConfigPath), uri);
                AssertCurrentUserOnly(runDirectory);
                File.WriteAllBytes(workspace.PartialArchivePath, [1, 2, 3, 4]);

                Assert.AreEqual(4, workspace.Complete());
                Assert.IsTrue(File.Exists(finalPath));
            }

            Assert.IsFalse(Directory.Exists(runDirectory));
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(finalPath));
        }
        finally
        {
            DeleteTestDirectory(testDirectory);
        }
    }

    [TestMethod]
    public void Dispose_RemovesIncompleteArchiveAndConfigAfterFailure()
    {
        var testDirectory = CreateTestDirectory();
        var temporaryRoot = Path.Combine(testDirectory, "temporary");
        var finalPath = Path.Combine(testDirectory, "backup.archive.gz");
        string partialPath;
        string configPath;

        try
        {
            using (var workspace = MongoDumpArchiveWorkspace.Create(
                       temporaryRoot,
                       finalPath,
                       overwriteExisting: false,
                       "mongodb://localhost"))
            {
                partialPath = workspace.PartialArchivePath;
                configPath = workspace.ConfigPath;
                File.WriteAllText(partialPath, "incomplete");
            }

            Assert.IsFalse(File.Exists(partialPath));
            Assert.IsFalse(File.Exists(configPath));
            Assert.IsFalse(File.Exists(finalPath));
        }
        finally
        {
            DeleteTestDirectory(testDirectory);
        }
    }

    [TestMethod]
    public void Complete_PreservesExistingArchiveUntilReplacementSucceeds()
    {
        var testDirectory = CreateTestDirectory();
        var temporaryRoot = Path.Combine(testDirectory, "temporary");
        var finalPath = Path.Combine(testDirectory, "backup.archive.gz");

        try
        {
            File.WriteAllText(finalPath, "old archive");
            using var workspace = MongoDumpArchiveWorkspace.Create(
                temporaryRoot,
                finalPath,
                overwriteExisting: true,
                "mongodb://localhost");

            Assert.AreEqual("old archive", File.ReadAllText(finalPath));
            File.WriteAllText(workspace.PartialArchivePath, "new archive");
            workspace.Complete();
            Assert.AreEqual("new archive", File.ReadAllText(finalPath));
        }
        finally
        {
            DeleteTestDirectory(testDirectory);
        }
    }

    [TestMethod]
    public async Task CleanupStaleFiles_DeletesOnlyOwnedRunDirectories()
    {
        var testDirectory = CreateTestDirectory();
        var temporaryRoot = Path.Combine(testDirectory, "temporary");
        var stale = Path.Combine(temporaryRoot, "run-stale");
        var unrelated = Path.Combine(temporaryRoot, "keep-this");

        try
        {
            Directory.CreateDirectory(stale);
            Directory.CreateDirectory(unrelated);
            File.WriteAllText(Path.Combine(stale, "mongodump.yml"), "secret");

            await MongoDumpArchiveWorkspace.CleanupStaleFilesAsync(temporaryRoot);

            Assert.IsFalse(Directory.Exists(stale));
            Assert.IsTrue(Directory.Exists(unrelated));
        }
        finally
        {
            DeleteTestDirectory(testDirectory);
        }
    }

    private static void AssertCurrentUserOnly(string directory)
    {
        var security = new DirectoryInfo(directory).GetAccessControl();
        Assert.IsTrue(security.AreAccessRulesProtected);
        var currentUser = WindowsIdentity.GetCurrent().User;
        var rules = security.GetAccessRules(
            includeExplicit: true,
            includeInherited: false,
            typeof(SecurityIdentifier));
        Assert.IsTrue(rules.Cast<FileSystemAccessRule>().Any(rule =>
            rule.AccessControlType == AccessControlType.Allow
            && Equals(rule.IdentityReference, currentUser)
            && rule.FileSystemRights.HasFlag(FileSystemRights.FullControl)));
    }

    private static string CreateTestDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "MultiTool.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTestDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
