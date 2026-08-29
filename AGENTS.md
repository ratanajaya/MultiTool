# AGENTS.md

## Project overview

MultiTool is a Windows 10/11 tray application built with .NET 10, WPF, and a
small amount of Windows Forms integration for the notification-area icon. Its
modules provide audio device keep-alive, microphone keep-awake, timed power
actions, usage reminders, and MongoDB backups.

## Repository layout

- `MultiTool/` contains the application.
  - `App/` contains the WPF shell, view models, commands, dialogs, and tray UI.
  - `Core/` contains shared settings, scheduling, notification, and module-state
    abstractions.
  - `Modules/` contains feature-specific domain logic and views.
  - `Platform/Windows/` contains Windows and third-party integrations.
- `MultiTool.Tests/` contains MSTest tests arranged to mirror application
  features.
- `_archive/` contains historical/reference code. Do not modify it unless the
  task explicitly calls for archival work.
- `_docs/` contains design and acceptance documentation when present.

## Build, test, and run

Run commands from the repository root with the .NET 10 SDK installed:

```powershell
dotnet restore MultiTool.slnx
dotnet build MultiTool.slnx --no-restore
dotnet test MultiTool.Tests/MultiTool.Tests.csproj --no-build
dotnet run --project MultiTool/MultiTool.csproj
```

After changing code, run the narrowest relevant tests first, then run the full
test project. Build the solution when changes affect application wiring, XAML,
project files, or platform integrations.

## Implementation conventions

- Keep nullable reference types enabled and do not suppress warnings without a
  concrete reason.
- Follow the existing C# style: file-scoped namespaces, four-space indentation,
  PascalCase public members, camelCase locals and parameters, and one primary
  type per file.
- Preserve the current dependency direction: presentation code depends on
  module/core abstractions, while operating-system and external-process details
  stay under `Platform/Windows`.
- Keep feature logic out of XAML code-behind when it can live in a view model or
  service.
- Keep long-running and scheduled work asynchronous and cancellation-aware.
  Treat start/stop and initialization paths as potentially repeatable.
- Use the existing abstractions such as `IClock`, `IAsyncDelay`, settings stores,
  and platform interfaces so behavior remains deterministic and testable.
- When adding or changing persisted settings, preserve backward-compatible
  defaults and ensure one module's update does not reset another module.
- Add or update MSTest coverage for behavior changes. Name tests in the existing
  `Action_ExpectedBehavior` style and prefer fakes over real devices, clocks,
  dialogs, power actions, or external processes.

## Safety and privacy

- Never commit credentials, MongoDB connection strings, local settings, or
  generated backup archives.
- MongoDB connection strings must remain transient: do not persist them and do
  not place them on the `mongodump` command line. Redact secrets from results,
  logs, notifications, and exception messages.
- Do not invoke real shutdown, sleep, hibernate, audio-device, microphone, or
  MongoDB backup operations in automated tests.
- Preserve the tray lifecycle: closing the main window hides the application;
  explicit exit performs cleanup and terminates it.

## Change discipline

- Keep changes focused and avoid unrelated formatting or refactors.
- Preserve existing user changes and generated-file exclusions.
- Update `README.md` or relevant design documentation when setup, behavior, or
  user-visible workflows change.
