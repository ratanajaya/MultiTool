# Deliverable 5: MongoDB deployment backup

## Delivered behavior

Module 5 creates an on-demand, compressed, single-file backup of every database
visible to a supplied MongoDB account. It invokes the official `mongodump`
Database Tool with `--archive` and `--gzip`; MultiTool does not implement or
interpret MongoDB's dump format.

The module page provides:

- automatic discovery, manual selection, and validation of `mongodump.exe`;
- a connection-string password field that must be filled for every run;
- a Save As dialog for the `*.archive.gz` destination;
- indeterminate progress, elapsed time, sanitized activity, and Cancel;
- the latest result for the current application session; and
- a completion or failure notification that opens this module.

The minimum supported Database Tools version is 100.3.0 because that version
introduced `mongodump --config`. Closing MultiTool to the notification area
allows a backup to continue. Explicit application exit warns before cancelling
an active backup.

## Connection-string boundary

The connection string is intentionally not part of `MongoDbBackupSettings` and
is never stored in settings, profiles, backup history, logs, notifications, or
process arguments. The WPF page uses a non-bound `PasswordBox` and clears it as
soon as the child process has started. The only persisted Module 5 values are
the validated executable path and last destination folder.

For the duration of a run, the URI is written to a randomly named YAML file in
an ACL-protected directory accessible only to the current Windows user. The
command line contains the config path rather than the URI. MultiTool removes
the config on every normal completion, failure, cancellation, and process-start
failure, and removes stale module-owned run directories at startup. Captured
output is redacted before it reaches service state or the UI.

Only deployment-level `mongodb://` and `mongodb+srv://` URIs are accepted. A URI
with a database path is rejected rather than silently changing its
authentication or backup scope.

## Execution and files

`MongoDbBackupService` enforces one active backup and delegates the Windows
process work to `IMongoDumpRunner`. The Windows runner starts without a shell or
console, asynchronously captures output, and assigns the child to a
kill-on-close Windows job object.

`mongodump` writes to a unique partial file in the destination directory. Exit
code zero and a non-empty partial file are required before it is moved to the
selected final name. An existing archive approved for replacement remains
untouched until the new archive succeeds. Cancellation terminates the complete
child process tree and removes incomplete output.

The phases are `Idle`, `Validating`, `Running`, `Cancelling`, `Succeeded`,
`Cancelled`, and `Faulted`. Numeric progress is not shown because `mongodump`
does not expose a dependable total-progress stream.

## Out of scope

- Saved connection profiles or credentials
- Selecting an individual database or collection
- Scheduling and execution while MultiTool is exited
- Retention rules or persistent backup history
- Restore, cloud upload, or continuous point-in-time backup
- Downloading or bundling MongoDB Database Tools

## Automated acceptance

Tests cover deployment-URI validation, settings isolation, command-line secrecy,
output redaction, protected temporary workspaces, stale cleanup, partial-file
promotion and removal, replacement safety, concurrent-start rejection,
cancellation, notification routing, and navigation availability. Automated
tests use fake runners and never invoke a real power action, `mongodump`, or
MongoDB deployment.

## Manual Windows checklist

- Validate a Database Tools 100.3.0-or-newer installation found through `PATH`,
  a conventional installation folder, and manual browsing.
- Back up a disposable local deployment and an Atlas deployment using
  deployment-level URIs.
- Restore each result into a disposable target with
  `mongorestore --archive=<file> --gzip` and inspect the restored databases.
- Cancel a large backup and confirm that no partial archive or protected config
  remains.
- Replace an existing archive; also force a failed replacement and confirm the
  original archive remains readable.
- Close the window during a backup, confirm it continues in the tray, and verify
  the completion notification opens Module 5.
- Select explicit Exit during a backup, decline once, then confirm and verify
  cancellation and cleanup.
- Exercise missing, renamed, and obsolete tools, an unwritable destination,
  insufficient MongoDB permissions, and a network interruption.
- Inspect the child process command line and confirm it contains only the
  executable, temporary config path, partial archive path, and `--gzip`.
