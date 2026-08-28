using MultiTool.Core.Notifications;
using MultiTool.Core.Scheduling;
using MultiTool.Modules.PowerTimer;
using MultiTool.Modules.UsageReminders;

namespace MultiTool.Tests.Modules.UsageReminders;

[TestClass]
public sealed class UsageReminderServiceTests
{
    [TestMethod]
    public async Task DueOccurrence_IsPresentedAndNotifiedOnlyOnce()
    {
        var fixture = new Fixture();
        await using var service = fixture.CreateService();

        await service.InitializeAsync(fixture.CreateDueSettings());
        await WaitUntilAsync(() => fixture.Presenter.ShowCount == 1);
        fixture.SystemEvents.RaiseTimeChanged();
        fixture.SystemEvents.RaiseResume();
        await Task.Delay(30);

        Assert.AreEqual(1, fixture.Presenter.ShowCount);
        Assert.AreEqual(1, fixture.Notifications.ShowCount);
        Assert.AreEqual("UsageReminders", fixture.Notifications.LastContext);
        Assert.AreEqual(ReminderDeliveryStatus.Delivered, service.Settings.RecentDeliveries[0].Status);
    }

    [TestMethod]
    public async Task RecentlyMissedOccurrence_IsDeliveredWithinGracePeriod()
    {
        var fixture = new Fixture();
        fixture.Clock.Advance(TimeSpan.FromMinutes(4));
        await using var service = fixture.CreateService();

        await service.InitializeAsync(fixture.CreateDueSettings(graceMinutes: 5));

        Assert.AreEqual(1, fixture.Presenter.ShowCount);
        Assert.AreEqual(ReminderDeliveryStatus.Delivered, service.Settings.RecentDeliveries[0].Status);
    }

    [TestMethod]
    public async Task OldOccurrence_IsRecordedAsMissedWithoutPresentation()
    {
        var fixture = new Fixture();
        fixture.Clock.Advance(TimeSpan.FromMinutes(6));
        await using var service = fixture.CreateService();

        await service.InitializeAsync(fixture.CreateDueSettings(graceMinutes: 5));

        Assert.AreEqual(0, fixture.Presenter.ShowCount);
        Assert.AreEqual(0, fixture.Notifications.ShowCount);
        Assert.AreEqual(ReminderDeliveryStatus.Missed, service.Settings.RecentDeliveries[0].Status);
    }

    [TestMethod]
    public async Task OccurrenceBeforeProfileCreation_IsIgnored()
    {
        var fixture = new Fixture();
        var profile = fixture.CreateProfile() with
        {
            CreatedUtc = fixture.Clock.UtcNow.AddMinutes(1)
        };
        await using var service = fixture.CreateService();

        await service.InitializeAsync(new UsageReminderSettings { Profiles = [profile] });

        Assert.AreEqual(0, fixture.Presenter.ShowCount);
        Assert.AreEqual(0, service.Settings.RecentDeliveries.Count);
    }

    [TestMethod]
    public async Task Snooze_PersistsOneTimeOccurrenceAndDeliversItOnce()
    {
        var fixture = new Fixture();
        await using var service = fixture.CreateService();
        await service.InitializeAsync(fixture.CreateDueSettings());
        var first = fixture.Presenter.LastPresentation!;

        fixture.Presenter.Respond(first.OccurrenceId, ReminderResponse.Snooze);
        await WaitUntilAsync(() => service.Settings.PendingSnoozes.Count == 1);
        Assert.AreEqual(ReminderDeliveryStatus.Snoozed, service.Settings.RecentDeliveries[0].Status);

        fixture.Clock.Advance(TimeSpan.FromMinutes(10));
        fixture.SystemEvents.RaiseResume();
        await WaitUntilAsync(() => fixture.Presenter.ShowCount == 2);
        fixture.SystemEvents.RaiseTimeChanged();
        await Task.Delay(30);

        Assert.AreEqual(2, fixture.Presenter.ShowCount);
        Assert.AreEqual(0, service.Settings.PendingSnoozes.Count);
        Assert.AreEqual(2, service.Settings.RecentDeliveries.Count);
    }

    [TestMethod]
    public async Task Acknowledge_UpdatesDeliveryHistory()
    {
        var fixture = new Fixture();
        await using var service = fixture.CreateService();
        await service.InitializeAsync(fixture.CreateDueSettings());
        var presentation = fixture.Presenter.LastPresentation!;

        fixture.Presenter.Respond(presentation.OccurrenceId, ReminderResponse.Acknowledge);
        await WaitUntilAsync(() =>
            service.Settings.RecentDeliveries[0].Status == ReminderDeliveryStatus.Acknowledged);

        Assert.AreEqual(presentation.OccurrenceId, fixture.Presenter.LastClosedId);
    }

    [TestMethod]
    public async Task UpdatingProfiles_PreservesHistoryAndRemovesDeletedProfileSnoozes()
    {
        var fixture = new Fixture();
        await using var service = fixture.CreateService();
        await service.InitializeAsync(fixture.CreateDueSettings());
        fixture.Presenter.Respond(
            fixture.Presenter.LastPresentation!.OccurrenceId,
            ReminderResponse.Snooze);
        await WaitUntilAsync(() => service.Settings.PendingSnoozes.Count == 1);

        await service.UpdateProfilesAsync([]);

        Assert.AreEqual(0, service.Settings.PendingSnoozes.Count);
        Assert.AreEqual(1, service.Settings.RecentDeliveries.Count);
    }

    [TestMethod]
    public async Task Preview_DoesNotChangeHistoryOrCreateSnooze()
    {
        var fixture = new Fixture();
        await using var service = fixture.CreateService();
        await service.InitializeAsync(new UsageReminderSettings { Profiles = [] });
        var profile = fixture.CreateProfile();

        service.Preview(profile);
        var preview = fixture.Presenter.LastPresentation!;
        fixture.Presenter.Respond(preview.OccurrenceId, ReminderResponse.Acknowledge);
        await Task.Delay(30);

        Assert.IsTrue(preview.IsPreview);
        Assert.AreEqual(0, service.Settings.RecentDeliveries.Count);
        Assert.AreEqual(0, service.Settings.PendingSnoozes.Count);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Clock = new FakeClock(new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.Zero));
        }

        public FakeClock Clock { get; }

        public FakePresenter Presenter { get; } = new();

        public FakeNotificationService Notifications { get; } = new();

        public FakeSettingsStore Store { get; } = new();

        public FakeSystemTimeEvents SystemEvents { get; } = new();

        public UsageReminderService CreateService() => new(
            Store,
            Presenter,
            Notifications,
            Clock,
            new BlockingDelay(),
            SystemEvents,
            TimeZoneInfo.Utc);

        public UsageReminderSettings CreateDueSettings(int graceMinutes = 5) => new()
        {
            Profiles = [CreateProfile(graceMinutes)]
        };

        public UsageReminderProfile CreateProfile(int graceMinutes = 5) => new()
        {
            CreatedUtc = Clock.UtcNow.AddDays(-1),
            Name = "Wrap up",
            Hour = 10,
            Minute = 0,
            ActiveDays = ReminderDays.EveryDay,
            Title = "Time to wrap up",
            Message = "Save your work.",
            SnoozeMinutes = 10,
            GracePeriodMinutes = graceMinutes
        };
    }

    private sealed class FakeClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = utcNow;

        public void Advance(TimeSpan duration) => UtcNow += duration;
    }

    private sealed class BlockingDelay : IAsyncDelay
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
            Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    private sealed class FakePresenter : IReminderPresentationService
    {
        public event EventHandler<ReminderResponseEventArgs>? ResponseRequested;

        public int ShowCount { get; private set; }

        public ReminderPresentation? LastPresentation { get; private set; }

        public Guid? LastClosedId { get; private set; }

        public void Show(ReminderPresentation presentation)
        {
            ShowCount++;
            LastPresentation = presentation;
        }

        public void Close(Guid occurrenceId) => LastClosedId = occurrenceId;

        public void Respond(Guid occurrenceId, ReminderResponse response) =>
            ResponseRequested?.Invoke(this, new ReminderResponseEventArgs(occurrenceId, response));

        public void Dispose()
        {
        }
    }

    private sealed class FakeNotificationService : INotificationService
    {
        public int ShowCount { get; private set; }

        public string? LastContext { get; private set; }

        public void Show(string title, string message) => Show(title, message, null);

        public void Show(string title, string message, string? activationContext)
        {
            ShowCount++;
            LastContext = activationContext;
        }
    }

    private sealed class FakeSettingsStore : IUsageReminderSettingsStore
    {
        public UsageReminderSettings? Saved { get; private set; }

        public Task SaveAsync(
            UsageReminderSettings settings,
            CancellationToken cancellationToken = default)
        {
            Saved = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSystemTimeEvents : ISystemTimeEvents
    {
        public event EventHandler? Resumed;

        public event EventHandler? TimeChanged;

        public void RaiseResume() => Resumed?.Invoke(this, EventArgs.Empty);

        public void RaiseTimeChanged() => TimeChanged?.Invoke(this, EventArgs.Empty);

        public void Dispose()
        {
        }
    }
}
