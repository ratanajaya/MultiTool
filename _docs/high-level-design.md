# MultiTool high-level design

## 1. Purpose

MultiTool is an extensible Windows utility application. It runs in the signed-in
user's session, remains available from the notification area, and hosts small
tools that can run in the background.

The initial modules are:

1. Audio output keep-alive
2. Microphone keep-awake
3. Shutdown, sleep, and hibernate timer
4. Configurable usage reminders
5. MongoDB backup

The application is a modular monolith. Modules are isolated behind contracts
but are compiled and shipped with the application. A runtime plug-in system is
not part of the initial scope; modules can be extracted later if a real plug-in
distribution requirement appears.

## 2. Chosen technology stack

| Concern | Decision |
| --- | --- |
| Operating system | Windows 10/11, x64 initially |
| Runtime and language | C# on .NET 10 LTS |
| Desktop UI | WPF with MVVM |
| Process model | One per-user desktop/tray process |
| Background lifecycle | .NET Generic Host and cancellation-aware services |
| Audio | NAudio with Windows audio endpoints/WASAPI where appropriate |
| Notifications | Windows App SDK app notifications behind an abstraction |
| Configuration | Strongly typed settings persisted as local JSON initially |
| Secrets | Module-specific; MongoDB URIs are never persisted and use an ACL-protected one-run config |
| Logging | Structured local logs with redaction and retention |
| Tests | Unit tests plus focused Windows integration tests |
| Distribution | Unpackaged during development; MSIX evaluated for releases |

WPF is selected because this is primarily a tray and background utility. It
provides mature data binding and direct access to Windows desktop integration
while allowing the legacy C#/NAudio proof of concept in `_archive` to be reused.

## 3. Process model

MultiTool runs as a normal per-user desktop application. Closing the main window
hides it to the notification area unless the user explicitly selects Exit.
Modules that are enabled start after application initialization and stop cleanly
when the application exits.

A Windows Service is deliberately excluded from the initial design. Audio
devices, microphone privacy, and notifications belong to the interactive user
session. If a future backup or timer must run when no user is signed in, it
should use a separate headless executable registered with Windows Task Scheduler
or an optional service. That component must communicate with the desktop app
through a narrow authenticated interface.

Only one MultiTool instance should run per Windows user session. A second launch
should activate the existing instance.

## 4. Logical architecture

```text
+---------------------------------------------------------+
| MultiTool (one WPF production project)                  |
|                                                         |
|  App shell and tray                                     |
|          |                                              |
|  Core lifecycle and shared contracts                    |
|          |                                              |
|  Feature modules ------> Windows platform implementations|
+---------------------------------------------------------+

+---------------------------------------------------------+
| MultiTool.Tests (one test project)                      |
| Core, module, and platform-facing tests                 |
+---------------------------------------------------------+
```

The solution contains exactly two projects. Architectural boundaries are
represented by folders, namespaces, and dependency direction rather than by
additional assemblies.

Suggested layout:

```text
MultiTool/
  App/
    Shell/
    Tray/
    ViewModels/
  Core/
    Modules/
    Scheduling/
    Settings/
  Modules/
    AudioOutputKeepAlive/
    MicrophoneKeepAwake/
    PowerTimer/
    UsageReminders/
    MongoDbBackup/
  Platform/
    Windows/
  MultiTool.csproj

MultiTool.Tests/
  Core/
  Modules/
  Platform/
  MultiTool.Tests.csproj
```

Each module remains a separate deliverable through its feature folder, module
registration, settings, acceptance criteria, and tests. Separate deliverability
means features can be implemented and accepted incrementally; it does not mean
that every feature produces a separate binary. Additional production or test
projects are outside the initial design and should require an explicit future
architecture decision.

## 5. Shared module contract

Every module has stable identity, metadata, settings, validation, commands, and
observable runtime status. Long-running modules support asynchronous start and
stop with cancellation.

Common runtime states are:

```text
Disabled -> Stopped -> Starting -> Running -> Stopping -> Stopped
                              \-> Faulted -> Starting/Stopped
```

The host owns module creation and lifecycle. Modules must not directly create UI
windows, write arbitrary configuration files, or terminate the application.
They request platform behavior through interfaces such as:

- `IAudioOutputService`
- `IAudioCaptureService`
- `INotificationService`
- `IPowerActionService`
- `ISecretStore`
- `IExternalProcessRunner`
- `IClock`

These boundaries make scheduling and module behavior testable without operating
real hardware, shutting down a test machine, or contacting a database.

## 6. Shell and user experience

The shared application shell is a foundation deliverable and includes:

- Dashboard showing every installed module and its current state
- Navigation to module-specific settings and history
- Global start-at-login setting
- Tray menu with open, module status, quick actions, and exit
- Close-to-tray behavior and single-instance activation
- Consistent validation, error display, and confirmation dialogs
- Local logs and a user-facing diagnostics view
- Version and update information

Each module can be enabled independently. A module fault is isolated: it changes
that module to `Faulted`, records a redacted error, and does not terminate other
modules or the shell.

## 7. Scheduling and persistence

Short recurring work uses cancellation-aware in-process timers. Absolute
deadlines and daily schedules store their intended time, not only an in-memory
timer duration. The scheduler recalculates after application restart, system
resume, time-zone change, and clock adjustment.

Settings are written atomically and versioned so migrations can be introduced.
Modules that require durable secrets must reference a separate Windows-protected
store. Module 5 deliberately has no durable secret: its URI is entered for every
run and exists on disk only in an ACL-protected temporary `mongodump` config.
Logs must never include microphone samples, MongoDB passwords, full connection
strings, or secret configuration-file contents.

The first version does not promise execution while MultiTool is exited or no
user is signed in. A module that needs that guarantee must explicitly add a
Task Scheduler or service deliverable.

## 8. Packaging and privileges

Normal operation should not require MultiTool itself to run elevated. Platform
operations report capability or policy failures to the user. Shutdown actions
must provide warning and cancellation; hibernate must be hidden or disabled
when Windows reports it unavailable.

Development builds are self-contained, unpackaged x64 builds. Before general
distribution, evaluate MSIX for clean install/uninstall, application identity,
notifications, startup registration, and update delivery. Packaging work is
kept separate from module implementation.

## 9. Delivery strategy

Deliverables are incremental and independently acceptable:

0. **Application foundation:** solution, WPF shell, tray behavior, module host,
   settings, logging, diagnostics, and test infrastructure.
1. **Audio output keep-alive**
2. **Microphone keep-awake**
3. **Power timer**
4. **Usage reminders**
5. **MongoDB backup**

Deliverables 1-5 depend only on the accepted application foundation. They do not
depend on one another and may be reordered. Every module delivery includes its
UI, settings, lifecycle behavior, diagnostics, automated tests where practical,
and module-specific documentation.

## 10. Cross-cutting quality requirements

- Module start and stop operations are idempotent.
- Application shutdown does not leave audio or child processes running.
- Hardware removal and permission denial produce recoverable module errors.
- User-visible messages are actionable and do not expose secrets.
- Potentially disruptive actions require explicit configuration and warning.
- Scheduling behavior is defined for sleep/resume and missed deadlines.
- Modules have no hidden network communication.
- Settings remain compatible across application upgrades or are migrated.
