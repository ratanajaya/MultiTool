using MultiTool.Modules.BatchFileOrganizer;

namespace MultiTool.Tests.Modules.BatchFileOrganizer;

[TestClass]
public sealed class NaturalFileNameComparerTests
{
    [TestMethod]
    public void Sort_PutsNumericRunsInHumanOrder()
    {
        string[] names = ["file10.jpg", "File2.jpg", "file1.jpg", "file02.jpg"];

        Array.Sort(names, NaturalFileNameComparer.Instance);

        CollectionAssert.AreEqual(
            new[] { "file1.jpg", "File2.jpg", "file02.jpg", "file10.jpg" },
            names);
    }
}
