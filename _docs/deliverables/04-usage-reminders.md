# Deliverable 4: Usage reminders

## Status

Implemented.

## Goal

Provide nonintrusive but difficult-to-miss reminders that help the user wrap up
active work and finish PC usage at designated local times. The module contains
no alarm sound playback.

"Wrap up" and "Sign off" are supplied as editable default profiles. They use
the same scheduling and presentation system as user-created reminders.

## User capabilities

- Create, edit, enable, disable, and delete multiple reminder profiles.
- Configure local time, active weekdays, title, message, snooze duration, and a
  missed-reminder grace period.
- Select a persistent corner card, a Windows notification, or both.
- Preview a card and notification without changing delivery history.
- Acknowledge a card with **Done** or create a persisted one-time occurrence
  with **Snooze**.
- See the next occurrence for every profile and the latest 20 delivery results.

## Presentation

The WPF corner card appears in the lower-right corner of the monitor containing
the mouse pointer. It is topmost but initially shown without activating or
stealing keyboard focus. Multiple simultaneous cards stack upward.

The expanded card shows the title, message, scheduled time, **Done**, and the
configured snooze action. After 20 seconds it collapses into a narrow edge card
and remains present until acknowledged or snoozed. Expanding the edge card does
not change reminder state.

The overlay is held back while the foreground window occupies the entire
monitor. It appears when full-screen mode ends if the profile's grace period is
still active; otherwise the queued overlay is discarded. Windows notifications
continue to follow Windows notification and Focus Assist policy. Clicking one
opens MultiTool on the Usage reminders page.

## Scheduling and persistence

`UsageReminderService` evaluates enabled local wall-clock rules using the
current Windows time zone. It recalculates after restart, resume, time-zone or
clock changes, and each scheduler tick. Invalid daylight-saving times are
skipped; an ambiguous time uses the earlier of the two possible instants.

Profiles, pending snoozes, and recent delivery history share the normal
MultiTool settings file without replacing other module settings. A deterministic
delivery identifier prevents the same recurring or snoozed occurrence from
being delivered more than once. An occurrence predating its profile is ignored.

A recently missed occurrence is delivered during its configured grace period.
An older occurrence is recorded as missed and is not presented. Snoozing does
not modify the recurring profile. Closing the main window to the tray keeps the
scheduler running; explicit application exit stops it without deleting profiles
or pending snoozes.

## Safety and privacy

- MultiTool does not play or bundle an alarm sound. Windows remains responsible
  for any sound associated with its own notifications.
- No application is blocked, closed, or controlled.
- The card never accepts typing or takes initial focus.
- No cloud service or network communication is used.
- Delivery continues to respect the user's enabled/disabled profile state.

## Automated coverage

- Local recurrence and weekday selection
- Invalid and ambiguous daylight-saving times
- Default profile and validation behavior
- Grace-period delivery and missed occurrences
- Duplicate prevention after clock/resume events
- Acknowledge and snooze persistence
- Profile deletion and pending-snooze cleanup
- Preview isolation from history
- Settings round-trip and preservation of other modules

Automated tests use fake clock, presentation, notification, settings, and system
event adapters. They do not display real windows or notifications.

## Manual Windows checklist

- Preview a corner card and confirm it does not interrupt typing in another app.
- Confirm the expanded card collapses after about 20 seconds and can be reopened.
- Confirm **Done** closes the card and records Acknowledged.
- Confirm **Snooze** closes the card and presents one new occurrence at the
  selected delay without changing the recurring time.
- Schedule two profiles for the same minute and confirm their cards stack.
- Confirm cards use the monitor containing the mouse and stay inside its work
  area without covering the taskbar.
- Enter and leave a full-screen video or presentation and verify grace-period
  behavior.
- Confirm the Windows notification is shown according to Windows settings and
  opens the Usage reminders module when clicked.
- Close the main window to the tray and confirm reminders continue to run.
- Restart MultiTool and confirm profiles, pending snoozes, and history restore.

## Out of scope

- Sounds or repeating audio alarms
- Forced logout, application blocking, or parental controls
- Usage tracking or screen-time analytics
- Cloud synchronization or reminders on other devices
- Arbitrary automation triggered by a reminder
