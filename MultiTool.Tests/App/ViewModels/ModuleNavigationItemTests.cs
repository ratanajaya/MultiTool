using MultiTool.Presentation.ViewModels;

namespace MultiTool.Tests.App.ViewModels;

[TestClass]
public sealed class ModuleNavigationItemTests
{
    [TestMethod]
    public void DefaultNavigation_ContainsAllModulesInDeliveryOrder()
    {
        var navigation = ModuleNavigationItem.CreateDefaultNavigation();

        CollectionAssert.AreEqual(
            new[]
            {
                ModulePage.AudioOutputKeepAlive,
                ModulePage.MicrophoneKeepAwake,
                ModulePage.PowerTimer,
                ModulePage.UsageReminders,
                ModulePage.MongoDbBackup,
                ModulePage.BatchFileOrganizer
            },
            navigation.Select(item => item.Page).ToArray());
        Assert.AreEqual("Available", navigation[0].Availability);
        Assert.AreEqual("Available", navigation[1].Availability);
        Assert.AreEqual("Available", navigation[2].Availability);
        Assert.AreEqual("Available", navigation[3].Availability);
        Assert.AreEqual("Available", navigation[4].Availability);
        Assert.AreEqual("Available", navigation[5].Availability);
    }
}
