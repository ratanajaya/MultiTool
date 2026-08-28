# Deliverable 4: Usage reminders

## Goal

Provide nonintrusive, configurable reminders that help the user wrap up active
work and finish PC usage at designated times.

The module is generic: “Wrap up” and “Sign off” are two default reminder
profiles rather than two separate implementations.

## User capabilities

- Create, edit, enable, disable, and delete reminder profiles.
- Configure local time, active weekdays, title, and message.
- Select notification, short sound, or both.
- Preview a reminder without waiting for its schedule.
- Snooze or dismiss a reminder where supported.
- See the next occurrence and recent delivery history.

## Design

`UsageReminderService` calculates the next occurrence of every enabled profile
and delegates presentation to `INotificationService` and `ISoundService`.
Schedules are stored as local wall-clock rules with a time-zone identifier;
calculated occurrences are represented as instants.

The scheduler recalculates after restart, resume, time-zone change, daylight-
saving transition, and system clock adjustment. A delivery identifier prevents
the same occurrence from appearing twice. A configurable grace period determines
whether a recently missed reminder should still be shown.

Notification failure can fall back to sound only when the user enabled sound.
The module remains functional without requiring a cloud service.

## Out of scope

- Forced logout or application blocking
- Parental controls, usage tracking, or screen-time analytics
- Cloud synchronization or reminders on other devices
- Arbitrary automation triggered by a reminder

## Acceptance boundary

The deliverable is complete when multiple independent profiles can be managed,
the two default profiles are supplied, occurrences are delivered at most once,
and schedules behave according to specification across restart and resume.

Automated tests cover recurrence, weekday selection, snooze, grace periods,
clock changes, and duplicate prevention using a fake clock and notification
service. Notification appearance and sound levels require manual Windows tests.

