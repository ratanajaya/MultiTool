# MultiTool

MultiTool is a Windows tray application for small background utilities. The
completed modules can periodically play a near-silent output burst, hold a
microphone capture stream open to prevent device power saving, and schedule one
shutdown, sleep, or hibernate action.

## Requirements

- Windows 10 or Windows 11
- .NET 10 SDK

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

Design documents and module acceptance notes are in [`_docs`](./_docs/README.md).
