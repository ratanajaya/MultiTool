using MultiTool.Modules.MongoDbBackup;

namespace MultiTool.Tests.Modules.MongoDbBackup;

[TestClass]
public sealed class MongoDbConnectionStringValidatorTests
{
    [DataTestMethod]
    [DataRow("mongodb://localhost:27017")]
    [DataRow("mongodb://user:password@host1:27017,host2:27017/")]
    [DataRow("mongodb+srv://user:password@cluster.example.net/?retryWrites=true&authSource=admin")]
    public void Validate_AcceptsDeploymentUris(string connectionString)
    {
        Assert.AreEqual(0, MongoDbConnectionStringValidator.Validate(connectionString).Count);
    }

    [DataTestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("https://localhost")]
    [DataRow("mongodb://")]
    [DataRow("mongodb://localhost/database")]
    [DataRow("mongodb+srv://cluster.example.net/admin?authSource=admin")]
    [DataRow("mongodb://localhost/\npassword: leaked")]
    [DataRow(" mongodb://localhost")]
    public void Validate_RejectsInvalidOrDatabaseScopedUris(string? connectionString)
    {
        Assert.IsTrue(MongoDbConnectionStringValidator.Validate(connectionString).Count > 0);
    }
}
