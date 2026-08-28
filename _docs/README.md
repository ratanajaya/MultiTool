# MultiTool design documentation

MultiTool is a Windows tray application that hosts a set of independently
deliverable desktop utilities. The chosen application stack is C# on .NET 10
with WPF.

## Documents

- [High-level design](high-level-design.md)
- [Deliverable 1: Audio output keep-alive](deliverables/01-audio-output-keep-alive.md)
- [Deliverable 2: Microphone keep-awake](deliverables/02-microphone-keep-awake.md)
- [Deliverable 3: Power timer](deliverables/03-power-timer.md)
- [Deliverable 4: Usage reminders](deliverables/04-usage-reminders.md)
- [Deliverable 5: MongoDB backup](deliverables/05-mongodb-backup.md)

Each module is a separate deliverable. It must be possible to enable, disable,
test, and release a module without requiring unfinished modules to be present.
The shared shell is developed first because every module depends on its module
lifecycle, settings, status, and tray integration.

