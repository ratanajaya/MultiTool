# MultiTool

MultiTool is a Windows tray application for small background utilities.

- Audio keep-alive: Periodically plays near-silent audio to keep output devices awake.
- Microphone keep-awake: Keeps a capture stream open to prevent device power saving.
- Timed power actions: Schedules a shutdown, sleep, or hibernate action.
- Usage reminders: Shows configurable reminders to take breaks.
- MongoDB backups: Creates compressed whole-deployment backups with `mongodump`.
- Batch file organizer: Previews, reorders, and batch-renames files, with confirmed permanent deletion.

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

Release builds write the app directly to `C:\Utils\MultiTool`:

```powershell
dotnet build MultiTool/MultiTool.csproj --configuration Release --no-restore
```
