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
                ModulePage.MongoDbBackup
            },
            navigation.Select(item => item.Page).ToArray());
        Assert.AreEqual("Available", navigation[0].Availability);
        Assert.IsTrue(navigation.Skip(1).All(item => item.Availability == "Planned"));
    }
}
