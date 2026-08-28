using MultiTool.Platform.Windows;

namespace MultiTool.Tests.Modules.MongoDbBackup;

[TestClass]
public sealed class WindowsMongoDumpRunnerTests
{
    [TestMethod]
    public void BuildBackupStartInfo_ContainsOnlyConfigArchiveAndCompressionArguments()
    {
        const string uri = "mongodb://user:top-secret@localhost";
        var startInfo = WindowsMongoDumpRunner.BuildBackupStartInfo(
            @"C:\tools\mongodump.exe",
            @"C:\protected\mongodump.yml",
            @"C:\backups\backup.partial");

        CollectionAssert.AreEqual(
            new[]
            {
                @"--config=C:\protected\mongodump.yml",
                @"--archive=C:\backups\backup.partial",
                "--gzip"
            },
            startInfo.ArgumentList.ToArray());
        Assert.IsFalse(startInfo.ArgumentList.Any(argument =>
            argument.Contains(uri, StringComparison.Ordinal)
            || argument.Contains("top-secret", StringComparison.Ordinal)));
        Assert.IsFalse(startInfo.UseShellExecute);
        Assert.IsTrue(startInfo.CreateNoWindow);
        Assert.IsTrue(startInfo.RedirectStandardOutput);
        Assert.IsTrue(startInfo.RedirectStandardError);
    }
}
