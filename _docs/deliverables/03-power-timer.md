# Deliverable 3: Power timer

## Goal

Allow the user to schedule one shutdown, sleep, or hibernate action using either
a countdown or an exact local date and time.

## User capabilities

- Select shutdown, sleep, or hibernate.
- Enter a countdown or exact execution time.
- Start, inspect, reschedule, or cancel the active timer.
- See the remaining time in the application and tray menu.
- Configure one or more warnings before execution.
- Choose defined behavior when a deadline is missed during sleep or app exit.

## Design

`PowerTimerService` persists an absolute target instant and requested action.
The scheduler uses the clock only to decide when the target is due; the actual
Windows operation is delegated to `IPowerActionService`.

On resume or restart, the service compares the stored target with the current
time. The safe default for a missed deadline is to prompt or cancel, never to
immediately shut down without warning. The service prevents duplicate execution
if multiple timer callbacks occur.

The UI checks platform capabilities before enabling hibernate. A final warning
offers cancellation unless the user has explicitly disabled that warning.

## Out of scope

- Recurring power schedules
- Remote shutdown of another computer
- Bypassing Windows policy or forcing unavailable sleep states
- Waking a powered-off computer
- Guaranteed execution while MultiTool is not running

## Acceptance boundary

The deliverable is complete when each supported action can be scheduled and
cancelled, state survives a normal app restart, warnings appear once, and missed
deadlines follow the configured policy.

Automated tests use fake clock, notification, and power adapters and must never
invoke real machine power actions. Real sleep, hibernate, shutdown, resume, and
policy behavior require an explicit manual test checklist.

