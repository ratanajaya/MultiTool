using System.Collections.Concurrent;
using MultiTool.Core.Notifications;
using MultiTool.Core.Scheduling;
using MultiTool.Modules.PowerTimer;

namespace MultiTool.Modules.UsageReminders;

public sealed class UsageReminderService : IAsyncDisposable
{
    internal static readonly TimeSpan SchedulerInterval = TimeSpan.FromSeconds(1);
    internal const int MaximumHistoryEntries = 20;
    private const string NotificationContext = "UsageReminders";

    private readonly IUsageReminderSettingsStore _settingsStore;
    private readonly IReminderPresentationService _presenter;
    private readonly INotificationService _notifications;
    private readonly IClock _clock;
    private readonly IAsyncDelay _delay;
    private readonly ISystemTimeEvents _systemTimeEvents;
    private readonly TimeZoneInfo _timeZone;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly ConcurrentDictionary<Guid, ActivePresentation> _activePresentations = [];
    private CancellationTokenSource? _runCancellation;
    private Task? _runTask;
    private UsageReminderSettings _settings = new();
    private string? _lastError;
    private bool _disposed;

    public UsageReminderService(
        IUsageReminderSettingsStore settingsStore,
        IReminderPresentationService presenter,
        INotificationService notifications,
        IClock clock,
        IAsyncDelay delay,
        ISystemTimeEvents systemTimeEvents,
        TimeZoneInfo? timeZone = null)
    {
        _settingsStore = settingsStore;
        _presenter = presenter;
        _notifications = notifications;
        _clock = clock;
        _delay = delay;
        _systemTimeEvents = systemTimeEvents;
        _timeZone = timeZone ?? TimeZoneInfo.Local;
        _presenter.ResponseRequested += HandleResponseRequested;
        _systemTimeEvents.Resumed += HandleScheduleChanged;
        _systemTimeEvents.TimeChanged += HandleScheduleChanged;
    }

    public event EventHandler? StatusChanged;

    public UsageReminderSettings Settings => _settings;

    public string? LastError => _lastError;

    public async Task InitializeAsync(
        UsageReminderSettings settings,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var errors = settings.Validate();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", errors));
        }

        _settings = Normalize(settings);
        await _settingsStore.SaveAsync(_settings, cancellationToken);
        StartRunLoop();
        await CheckDueAsync(cancellationToken);
        RaiseStatusChanged();
    }

    public async Task UpdateProfilesAsync(
        IReadOnlyList<UsageReminderProfile> profiles,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var candidate = _settings with { Profiles = profiles.ToArray() };
        var errors = candidate.Validate();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", errors));
        }

        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            var profileIds = profiles.Select(profile => profile.Id).ToHashSet();
            candidate = candidate with
            {
                PendingSnoozes = candidate.PendingSnoozes
                    .Where(snooze => profileIds.Contains(snooze.ProfileId))
                    .ToArray()
            };
            await SaveSettingsAsync(candidate, cancellationToken);
        }
        finally
        {
            _operationGate.Release();
        }

        await CheckDueAsync(cancellationToken);
        RaiseStatusChanged();
    }

    public DateTimeOffset? GetNextOccurrence(UsageReminderProfile profile) =>
        profile.Enabled
            ? UsageReminderRecurrence.GetNextOccurrence(profile, _clock.UtcNow, _timeZone)
            : null;

    public void Preview(UsageReminderProfile profile)
    {
        ThrowIfDisposed();
        var errors = profile.Validate();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", errors));
        }

        var now = _clock.UtcNow;
        var presentation = new ReminderPresentation
        {
            ProfileName = profile.Name,
            Title = $"Preview: {profile.Title}",
            Message = profile.Message,
            ScheduledUtc = now,
            ExpiresUtc = now.AddMinutes(profile.GracePeriodMinutes),
            SnoozeMinutes = 0,
            IsPreview = true
        };
        _activePresentations[presentation.OccurrenceId] = new ActivePresentation(null, true);

        if (profile.ShowCornerCard)
        {
            _presenter.Show(presentation);
        }

        if (profile.ShowWindowsNotification)
        {
            _notifications.Show(presentation.Title, presentation.Message, NotificationContext);
        }
    }

    private void StartRunLoop()
    {
        _runCancellation?.Cancel();
        _runCancellation?.Dispose();
        _runCancellation = new CancellationTokenSource();
        _runTask = Task.Run(() => RunSafelyAsync(_runCancellation.Token));
    }

    private async Task RunSafelyAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await CheckDueAsync(cancellationToken);
                await _delay.DelayAsync(SchedulerInterval, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _lastError = exception.Message;
                RaiseStatusChanged();
                try
                {
                    await _delay.DelayAsync(SchedulerInterval, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }

    private async Task CheckDueAsync(CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            var now = _clock.UtcNow;
            foreach (var snooze in _settings.PendingSnoozes
                         .Where(item => item.DueUtc <= now)
                         .OrderBy(item => item.DueUtc)
                         .ToArray())
            {
                var profile = _settings.Profiles.FirstOrDefault(item => item.Id == snooze.ProfileId);
                if (profile is null || !profile.Enabled)
                {
                    await RemoveSnoozeAsync(snooze.Id, cancellationToken);
                    continue;
                }

                await ProcessOccurrenceAsync(
                    profile,
                    snooze.DueUtc,
                    $"snooze:{snooze.Id:N}",
                    snooze.Id,
                    now,
                    cancellationToken);
            }

            foreach (var profile in _settings.Profiles.Where(item => item.Enabled).ToArray())
            {
                var scheduledUtc = UsageReminderRecurrence.GetMostRecentOccurrence(
                    profile,
                    now,
                    _timeZone);
                if (scheduledUtc is null)
                {
                    continue;
                }

                await ProcessOccurrenceAsync(
                    profile,
                    scheduledUtc.Value,
                    BuildDeliveryId(profile.Id, scheduledUtc.Value),
                    null,
                    now,
                    cancellationToken);
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task ProcessOccurrenceAsync(
        UsageReminderProfile profile,
        DateTimeOffset scheduledUtc,
        string deliveryId,
        Guid? snoozeId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (scheduledUtc < profile.CreatedUtc)
        {
            return;
        }

        if (_settings.RecentDeliveries.Any(item => item.DeliveryId == deliveryId))
        {
            if (snoozeId is not null)
            {
                await RemoveSnoozeAsync(snoozeId.Value, cancellationToken);
            }

            return;
        }

        var withinGrace = now - scheduledUtc <= TimeSpan.FromMinutes(profile.GracePeriodMinutes);
        var delivery = new ReminderDelivery
        {
            DeliveryId = deliveryId,
            ProfileId = profile.Id,
            ProfileName = profile.Name,
            ScheduledUtc = scheduledUtc,
            RecordedUtc = now,
            Status = withinGrace ? ReminderDeliveryStatus.Delivered : ReminderDeliveryStatus.Missed
        };
        var pending = snoozeId is null
            ? _settings.PendingSnoozes
            : _settings.PendingSnoozes.Where(item => item.Id != snoozeId.Value).ToArray();
        await SaveSettingsAsync(
            _settings with
            {
                PendingSnoozes = pending,
                RecentDeliveries = AddHistory(_settings.RecentDeliveries, delivery)
            },
            cancellationToken);

        if (!withinGrace)
        {
            RaiseStatusChanged();
            return;
        }

        var presentation = new ReminderPresentation
        {
            ProfileName = profile.Name,
            Title = profile.Title,
            Message = profile.Message,
            ScheduledUtc = scheduledUtc,
            ExpiresUtc = scheduledUtc.AddMinutes(profile.GracePeriodMinutes),
            SnoozeMinutes = profile.SnoozeMinutes
        };
        _activePresentations[presentation.OccurrenceId] = new ActivePresentation(deliveryId, false);

        try
        {
            if (profile.ShowCornerCard)
            {
                _presenter.Show(presentation);
            }

            if (profile.ShowWindowsNotification)
            {
                _notifications.Show(profile.Title, profile.Message, NotificationContext);
            }

            _lastError = null;
        }
        catch (Exception exception)
        {
            _lastError = exception.Message;
        }

        RaiseStatusChanged();
    }

    private async void HandleResponseRequested(object? sender, ReminderResponseEventArgs eventArgs)
    {
        try
        {
            await HandleResponseAsync(eventArgs);
        }
        catch (Exception exception)
        {
            _lastError = exception.Message;
            RaiseStatusChanged();
        }
    }

    private async Task HandleResponseAsync(ReminderResponseEventArgs eventArgs)
    {
        await _operationGate.WaitAsync();
        try
        {
            if (!_activePresentations.TryRemove(eventArgs.OccurrenceId, out var active))
            {
                return;
            }

            _presenter.Close(eventArgs.OccurrenceId);
            if (active.IsPreview || active.DeliveryId is null)
            {
                return;
            }

            var history = _settings.RecentDeliveries.ToArray();
            var index = Array.FindIndex(history, item => item.DeliveryId == active.DeliveryId);
            if (index < 0)
            {
                return;
            }

            var original = history[index];
            var pending = _settings.PendingSnoozes;
            var status = ReminderDeliveryStatus.Acknowledged;
            if (eventArgs.Response == ReminderResponse.Snooze)
            {
                var profile = _settings.Profiles.FirstOrDefault(item => item.Id == original.ProfileId);
                if (profile is not null && profile.Enabled)
                {
                    pending = pending.Append(new PendingReminderSnooze
                    {
                        ProfileId = profile.Id,
                        DueUtc = _clock.UtcNow.AddMinutes(profile.SnoozeMinutes)
                    }).ToArray();
                    status = ReminderDeliveryStatus.Snoozed;
                }
            }

            history[index] = original with
            {
                Status = status,
                RecordedUtc = _clock.UtcNow
            };
            await SaveSettingsAsync(
                _settings with { PendingSnoozes = pending, RecentDeliveries = history });
            RaiseStatusChanged();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private Task RemoveSnoozeAsync(Guid snoozeId, CancellationToken cancellationToken) =>
        SaveSettingsAsync(
            _settings with
            {
                PendingSnoozes = _settings.PendingSnoozes
                    .Where(item => item.Id != snoozeId)
                    .ToArray()
            },
            cancellationToken);

    private async Task SaveSettingsAsync(
        UsageReminderSettings settings,
        CancellationToken cancellationToken = default)
    {
        await _settingsStore.SaveAsync(settings, cancellationToken);
        _settings = settings;
    }

    private void HandleScheduleChanged(object? sender, EventArgs eventArgs) =>
        _ = RunCheckSafelyAsync();

    private async Task RunCheckSafelyAsync()
    {
        try
        {
            await CheckDueAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            _lastError = exception.Message;
            RaiseStatusChanged();
        }
    }

    private static UsageReminderSettings Normalize(UsageReminderSettings settings)
    {
        var profileIds = settings.Profiles.Select(profile => profile.Id).ToHashSet();
        return settings with
        {
            PendingSnoozes = settings.PendingSnoozes
                .Where(item => profileIds.Contains(item.ProfileId))
                .ToArray(),
            RecentDeliveries = settings.RecentDeliveries
                .OrderByDescending(item => item.RecordedUtc)
                .Take(MaximumHistoryEntries)
                .ToArray()
        };
    }

    private static IReadOnlyList<ReminderDelivery> AddHistory(
        IReadOnlyList<ReminderDelivery> history,
        ReminderDelivery delivery) =>
        history.Prepend(delivery).Take(MaximumHistoryEntries).ToArray();

    private static string BuildDeliveryId(Guid profileId, DateTimeOffset scheduledUtc) =>
        $"{profileId:N}:{scheduledUtc.UtcTicks}";

    private void RaiseStatusChanged() => StatusChanged?.Invoke(this, EventArgs.Empty);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _runCancellation?.Cancel();
        if (_runTask is not null)
        {
            try
            {
                await _runTask;
            }
            catch (OperationCanceledException)
            {
                // Expected during application shutdown.
            }
        }

        _runCancellation?.Dispose();
        _presenter.ResponseRequested -= HandleResponseRequested;
        _systemTimeEvents.Resumed -= HandleScheduleChanged;
        _systemTimeEvents.TimeChanged -= HandleScheduleChanged;
        _systemTimeEvents.Dispose();
        foreach (var occurrenceId in _activePresentations.Keys.ToArray())
        {
            _presenter.Close(occurrenceId);
        }

        _activePresentations.Clear();
        _presenter.Dispose();
        _operationGate.Dispose();
    }

    private sealed record ActivePresentation(string? DeliveryId, bool IsPreview);
}
