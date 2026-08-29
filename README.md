# MultiTool

MultiTool is a Windows tray application for small background utilities. The
completed modules can periodically play a near-silent output burst, hold a
microphone capture stream open to prevent device power saving, schedule one
shutdown, sleep, or hibernate action, show configurable usage reminders, and
create compressed whole-deployment MongoDB backups with `mongodump`. It also
includes a batch file organizer with a multi-select, drag-reorderable thumbnail
grid, contained image previews, and safe Prefix+Number renames configured in a
modal.

## Requirements

- Windows 10 or Windows 11
- .NET 10 SDK
- MongoDB Database Tools 100.3.0 or newer for Module 5

## Build and run

```powershell
dotnet restore MultiTool.slnx
dotnet build MultiTool.slnx --no-restore
dotnet test MultiTool.Tests/MultiTool.Tests.csproj --no-build
dotnet run --project MultiTool/MultiTool.csproj
```

Closing the window hides MultiTool in the notification area. Use the tray menu
to reopen or exit it. Settings are stored in
`%LOCALAPPDATA%\MultiTool\settings.json`.

MongoDB connection strings are required for every backup and are never saved in
MultiTool settings or passed to `mongodump` on its command line.

Design documents and module acceptance notes are in [`_docs`](./_docs/README.md).
