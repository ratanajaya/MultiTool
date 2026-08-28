using MultiTool.Modules.MongoDbBackup;

namespace MultiTool.Tests.Modules.MongoDbBackup;

[TestClass]
public sealed class MongoDbOutputRedactorTests
{
    [TestMethod]
    public void Redact_RemovesUriCredentialsAndTemporaryPath()
    {
        const string uri = "mongodb+srv://backup-user:p%40ssword@cluster.example.net/?authSource=admin";
        const string configPath = @"C:\private\run-1\mongodump.yml";
        var output = $"failed for {uri}; user backup-user; password p%40ssword / p@ssword; config {configPath}";

        var redacted = MongoDbOutputRedactor.Redact(output, uri, configPath);

        Assert.IsFalse(redacted.Contains(uri, StringComparison.Ordinal));
        Assert.IsFalse(redacted.Contains("backup-user", StringComparison.Ordinal));
        Assert.IsFalse(redacted.Contains("p%40ssword", StringComparison.Ordinal));
        Assert.IsFalse(redacted.Contains("p@ssword", StringComparison.Ordinal));
        Assert.IsFalse(redacted.Contains(configPath, StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(redacted, "[REDACTED_MONGODB_URI]");
    }

    [TestMethod]
    public void Redact_RemovesAnUnexpectedMongoDbUriFromToolOutput()
    {
        var redacted = MongoDbOutputRedactor.Redact(
            "server replied for mongodb://another:secret@host/admin?x=1",
            "mongodb://localhost");

        Assert.IsFalse(redacted.Contains("secret", StringComparison.Ordinal));
        Assert.IsFalse(redacted.Contains("mongodb://", StringComparison.OrdinalIgnoreCase));
    }
}
