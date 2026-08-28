using MultiTool.Core.Notifications;
using MultiTool.Core.Scheduling;
using MultiTool.Modules.PowerTimer;

namespace MultiTool.Tests.Modules.PowerTimer;

[TestClass]
public sealed class PowerTimerServiceTests
{
    [TestMethod]
    public async Task Schedule_SendsOneWarningAndExecutesOnceAtTheDeadline()
    {
        var fixture = new Fixture(advancingDelay: true);
        await using var service = fixture.CreateService();

        await service.ScheduleAsync(PowerAction.Shutdown, fixture.Clock.UtcNow.AddSeconds(62));
        await WaitUntilAsync(() => fixture.PowerActions.ExecutionCount == 1);

        Assert.AreEqual(1, fixture.Notifications.ShowCount);
        Assert.AreEqual(PowerAction.Shutdown, fixture.PowerActions.LastAction);
        Assert.AreEqual(PowerTimerPhase.Idle, service.Phase);
        Assert.IsNull(service.ActiveSchedule);
        Assert.IsNull(fixture.Store.Schedule);
    }

    [TestMethod]
    public async Task Cancel_PreventsThePowerActionAndClearsPersistence()
    {
        var fixture = new Fixture();
        await using var service = fixture.CreateService();

        await service.ScheduleAsync(PowerAction.Sleep, fixture.Clock.UtcNow.AddMinutes(5));
        await WaitUntilAsync(() => fixture.ControlledDelay!.DelayCount == 1);
        await service.CancelAsync();
        fixture.Clock.Advance(TimeSpan.FromMinutes(10));
        fixture.ControlledDelay!.Advance();

        await Task.Delay(20);
        Assert.AreEqual(0, fixture.PowerActions.ExecutionCount);
        Assert.AreEqual(PowerTimerPhase.Idle, service.Phase);
        Assert.IsNull(fixture.Store.Schedule);
    }

    [TestMethod]
    public async Task OneMinuteTimer_SendsTheWarningImmediately()
    {
        var fixture = new Fixture();
        await using var service = fixture.CreateService();

        await service.ScheduleAsync(PowerAction.Shutdown, fixture.Clock.UtcNow.AddMinutes(1));
        await WaitUntilAsync(() => fixture.Notifications.ShowCount == 1);

        Assert.AreEqual(PowerTimerPhase.WarningSent, service.Phase);
        Assert.AreEqual(true, fixture.Store.Schedule?.WarningSent);
        Assert.AreEqual(0, fixture.PowerActions.ExecutionCount);
    }

    [TestMethod]
    public async Task Initialize_WithMissedSchedule_ExpiresWithoutExecuting()
    {
        var fixture = new Fixture();
        await using var service = fixture.CreateService();
        var saved = new PowerTimerSchedule
        {
            Action = PowerAction.Hibernate,
            TargetUtc = fixture.Clock.UtcNow.AddMinutes(-1)
        };

        await service.InitializeAsync(saved);

        Assert.AreEqual(PowerTimerPhase.Expired, service.Phase);
        Assert.AreEqual(0, fixture.PowerActions.ExecutionCount);
        Assert.IsNull(fixture.Store.Schedule);
    }

    [TestMethod]
    public async Task ResumeAcrossDeadline_ExpiresWithoutExecuting()
    {
        var fixture = new Fixture();
        await using var service = fixture.CreateService();

        await service.ScheduleAsync(PowerAction.Shutdown, fixture.Clock.UtcNow.AddMinutes(2));
        await WaitUntilAsync(() => fixture.ControlledDelay!.DelayCount == 1);
        fixture.Clock.Advance(TimeSpan.FromMinutes(3));
        fixture.SystemEvents.RaiseResume();
        fixture.ControlledDelay!.Advance();
        await WaitUntilAsync(() => service.Phase == PowerTimerPhase.Expired);

        Assert.AreEqual(0, fixture.PowerActions.ExecutionCount);
        Assert.IsNull(fixture.Store.Schedule);
    }

    [TestMethod]
    public async Task ForwardClockJumpAcrossDeadline_ExpiresWithoutExecuting()
    {
        var fixture = new Fixture();
        await using var service = fixture.CreateService();

        await service.ScheduleAsync(PowerAction.Shutdown, fixture.Clock.UtcNow.AddMinutes(2));
        await WaitUntilAsync(() => fixture.ControlledDelay!.DelayCount == 1);
        fixture.Clock.Advance(TimeSpan.FromMinutes(3));
        fixture.SystemEvents.RaiseTimeChanged();
        fixture.ControlledDelay!.Advance();
        await WaitUntilAsync(() => service.Phase == PowerTimerPhase.Expired);

        Assert.AreEqual(0, fixture.PowerActions.ExecutionCount);
    }

    [TestMethod]
    public async Task RestoreAfterWarning_DoesNotSendDuplicateNotification()
    {
        var fixture = new Fixture();
        await using var service = fixture.CreateService();
        var saved = new PowerTimerSchedule
        {
            Action = PowerAction.Shutdown,
            TargetUtc = fixture.Clock.UtcNow.AddMinutes(2),
            WarningSent = true
        };

        await service.InitializeAsync(saved);
        await WaitUntilAsync(() => fixture.ControlledDelay!.DelayCount == 1);

        Assert.AreEqual(PowerTimerPhase.WarningSent, service.Phase);
        Assert.AreEqual(0, fixture.Notifications.ShowCount);
    }

    [TestMethod]
    public async Task RunNow_ConcurrentRequestsExecuteOnlyOnce()
    {
        var fixture = new Fixture();
        await using var service = fixture.CreateService();
        await service.ScheduleAsync(PowerAction.Sleep, fixture.Clock.UtcNow.AddMinutes(5));

        await Task.WhenAll(service.RunNowAsync(), service.RunNowAsync());

        Assert.AreEqual(1, fixture.PowerActions.ExecutionCount);
        Assert.IsNull(service.ActiveSchedule);
    }

    [TestMethod]
    public async Task Schedule_RejectsUnavailableActionAndShortDeadline()
    {
        var fixture = new Fixture();
        fixture.PowerActions.UnavailableAction = PowerAction.Hibernate;
        await using var service = fixture.CreateService();

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            service.ScheduleAsync(PowerAction.Hibernate, fixture.Clock.UtcNow.AddMinutes(5)));
        await Assert.ThrowsExceptionAsync<ArgumentOutOfRangeException>(() =>
            service.ScheduleAsync(PowerAction.Shutdown, fixture.Clock.UtcNow.AddSeconds(58)));
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
        private readonly IAsyncDelay _delay;

        public Fixture(bool advancingDelay = false)
        {
            Clock = new FakeClock(new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.Zero));
            if (advancingDelay)
            {
                _delay = new AdvancingDelay(Clock);
            }
            else
            {
                ControlledDelay = new ControlledDelay();
                _delay = ControlledDelay;
            }
        }

        public FakeClock Clock { get; }

        public ControlledDelay? ControlledDelay { get; }

        public FakePowerActionService PowerActions { get; } = new();

        public FakeNotificationService Notifications { get; } = new();

        public FakeScheduleStore Store { get; } = new();

        public FakeSystemTimeEvents SystemEvents { get; } = new();

        public PowerTimerService CreateService() => new(
            PowerActions,
            Notifications,
            Store,
            Clock,
            _delay,
            SystemEvents);
    }

    private sealed class FakeClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = utcNow;

        public void Advance(TimeSpan duration) => UtcNow += duration;
    }

    private sealed class AdvancingDelay(FakeClock clock) : IAsyncDelay
    {
        public async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            clock.Advance(delay);
            await Task.Yield();
        }
    }

    private sealed class ControlledDelay : IAsyncDelay
    {
        private TaskCompletionSource _nextDelay = CreateCompletionSource();

        public int DelayCount { get; private set; }

        public async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            DelayCount++;
            var current = _nextDelay;
            await current.Task.WaitAsync(cancellationToken);
        }

        public void Advance()
        {
            var current = _nextDelay;
            _nextDelay = CreateCompletionSource();
            current.TrySetResult();
        }

        private static TaskCompletionSource CreateCompletionSource() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class FakePowerActionService : IPowerActionService
    {
        public int ExecutionCount { get; private set; }

        public PowerAction? LastAction { get; private set; }

        public PowerAction? UnavailableAction { get; set; }

        public bool IsAvailable(PowerAction action) => action != UnavailableAction;

        public Task ExecuteAsync(PowerAction action, CancellationToken cancellationToken)
        {
            ExecutionCount++;
            LastAction = action;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeNotificationService : INotificationService
    {
        public int ShowCount { get; private set; }

        public void Show(string title, string message) => ShowCount++;
    }

    private sealed class FakeScheduleStore : IPowerTimerScheduleStore
    {
        public PowerTimerSchedule? Schedule { get; private set; }

        public Task SaveAsync(
            PowerTimerSchedule? schedule,
            CancellationToken cancellationToken = default)
        {
            Schedule = schedule;
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
