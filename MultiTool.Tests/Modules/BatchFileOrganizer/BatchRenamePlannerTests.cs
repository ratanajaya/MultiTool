using MultiTool.Modules.BatchFileOrganizer;

namespace MultiTool.Tests.Modules.BatchFileOrganizer;

[TestClass]
public sealed class BatchRenamePlannerTests
{
    [TestMethod]
    public void Create_UsesPrefixStartPaddingAndPreservesExtensions()
    {
        var folder = Path.Combine(Path.GetTempPath(), "photos");
        var sources = new[]
        {
            Path.Combine(folder, "first.JPG"),
            Path.Combine(folder, "archive.tar.gz"),
            Path.Combine(folder, "README")
        };

        var plan = BatchRenamePlanner.Create(folder, sources, "Trip_", "7", "3");

        Assert.IsTrue(plan.IsValid, plan.Error);
        CollectionAssert.AreEqual(
            new[] { "Trip_007.JPG", "Trip_008.gz", "Trip_009" },
            plan.ProposedFileNames.ToArray());
    }

    [TestMethod]
    public void Create_AllowsAnEmptyPrefixAndDoesNotTruncateWideNumbers()
    {
        var folder = Path.Combine(Path.GetTempPath(), "photos");
        var source = Path.Combine(folder, "photo.png");

        var plan = BatchRenamePlanner.Create(folder, [source], string.Empty, "123", "2");

        Assert.IsTrue(plan.IsValid, plan.Error);
        Assert.AreEqual("123.png", plan.ProposedFileNames.Single());
    }

    [DataTestMethod]
    [DataRow("-1", "3", "Starting number")]
    [DataRow("abc", "3", "Starting number")]
    [DataRow("1", "0", "Padding")]
    [DataRow("1", "10", "Padding")]
    public void Create_RejectsInvalidNumberInputs(
        string start,
        string padding,
        string expectedMessage)
    {
        var folder = Path.Combine(Path.GetTempPath(), "photos");

        var plan = BatchRenamePlanner.Create(
            folder,
            [Path.Combine(folder, "photo.png")],
            "File_",
            start,
            padding);

        Assert.IsFalse(plan.IsValid);
        StringAssert.Contains(plan.Error, expectedMessage);
    }

    [TestMethod]
    public void Create_RejectsInvalidPrefixCharacters()
    {
        var folder = Path.Combine(Path.GetTempPath(), "photos");

        var plan = BatchRenamePlanner.Create(
            folder,
            [Path.Combine(folder, "photo.png")],
            "bad:name",
            "1",
            "3");

        Assert.IsFalse(plan.IsValid);
        StringAssert.Contains(plan.Error, "does not allow");
    }

    [TestMethod]
    public void Create_RejectsNumberOverflow()
    {
        var folder = Path.Combine(Path.GetTempPath(), "photos");

        var plan = BatchRenamePlanner.Create(
            folder,
            [Path.Combine(folder, "one.txt"), Path.Combine(folder, "two.txt")],
            "File_",
            int.MaxValue.ToString(),
            "1");

        Assert.IsFalse(plan.IsValid);
        StringAssert.Contains(plan.Error, "exceed");
    }
}
