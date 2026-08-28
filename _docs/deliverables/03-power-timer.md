# Deliverable 3: Power timer

## Goal

Allow the user to schedule one shutdown, sleep, or hibernate action using either
a countdown or an exact local date and time.

## User capabilities

- Select shutdown, sleep, or hibernate.
- Enter a countdown or exact execution time.
- Schedule, inspect, replace, cancel, or immediately run the active timer.
- See the remaining time and execution target in the application.
- Receive one Windows notification when one minute remains.
- Open the Power Timer page by clicking the notification.

## Design

`PowerTimerService` persists a schedule identifier, absolute target instant,
requested action, and whether the warning was sent. The actual Windows operation
is delegated to `IPowerActionService`; notification delivery is delegated to
`INotificationService` and uses the application's existing notification-area
icon.

The scheduler observes Windows resume and clock-change events and also detects
long gaps between observations. If a deadline is crossed during sleep, app
downtime, or a forward clock change, the timer becomes `Expired` and never runs
the delayed action. A future schedule is restored after an unexpected process
termination. The service prevents duplicate execution when callbacks or Run now
requests race.

The main page is the only place that cancels an active timer. The notification
has no action buttons and Windows controls its sound and Focus Assist behavior.
Closing the window to the tray keeps the timer running. Explicitly exiting the
application warns that the timer will be cancelled and clears it after
confirmation.

Shutdown uses the Windows shutdown executable without its force option. Sleep
and hibernate use `SetSuspendState` with forced suspension disabled. Unavailable
sleep states are disabled in the UI, and MultiTool does not request elevation.

## Out of scope

- Recurring power schedules
- Remote shutdown of another computer
- Bypassing Windows policy or forcing unavailable sleep states
- Waking a powered-off computer
- Guaranteed execution while MultiTool is not running

## Acceptance boundary

The deliverable is complete when each supported action can be scheduled and
cancelled, a future schedule survives an unexpected restart, the one-minute
notification is persisted and appears at most once, and missed deadlines expire
without invoking a power action.

Automated tests use fake clock, notification, and power adapters and must never
invoke real machine power actions. Real sleep, hibernate, shutdown, resume, and
policy behavior require an explicit manual test checklist.

## Manual Windows validation

Perform these checks only on a test computer after saving all work:

1. Schedule a timer for each action and verify the remaining time updates on the
   Power Timer page.
2. Verify the Windows notification appears once at one minute remaining and has
   no action buttons. Confirm its sound follows Windows notification settings.
3. Click the notification and verify MultiTool opens directly to Power Timer
   without cancelling or changing the schedule.
4. Cancel from the Power Timer page and verify no power action occurs.
5. Close the main window and confirm the timer continues from the tray. Select
   tray Exit and verify declining preserves the timer while confirming cancels
   it and exits.
6. On a disposable test machine, verify shutdown does not force-close an app
   that blocks shutdown, and verify sleep and hibernate resume normally.
7. Put the computer to sleep across a timer deadline, then resume and verify the
   timer is marked `Expired` without executing.
8. Disable hibernation in Windows and verify Hibernate is shown as unavailable.
