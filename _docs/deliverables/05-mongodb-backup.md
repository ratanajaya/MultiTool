# Deliverable 5: MongoDB backup

## Goal

Create a logical MongoDB backup archive from a user-supplied connection profile
and destination by invoking the official `mongodump` Database Tool.

## User capabilities

- Create and edit named MongoDB connection profiles.
- Select a database or configured namespace scope.
- Select a destination folder and filename template.
- Run a backup now and cancel an active backup.
- Choose compression and an optional retention rule.
- See start time, duration, result, archive path, and redacted error details.
- Locate or validate the installed `mongodump` executable.

Scheduling recurring backups is a later enhancement unless explicitly added to
this deliverable. The initial module runs backups on demand while MultiTool is
running.

## Design

`MongoDbBackupService` builds a validated backup request and delegates execution
to `IMongoDumpRunner`. The Windows implementation starts `mongodump` without a
shell, captures output asynchronously, supports cancellation, and reports the
exit code.

The normal output is a compressed archive produced with `--archive` and
`--gzip`. A backup is first written with a temporary/incomplete name and moved
to its final name only after successful process completion. Existing successful
archives are not overwritten unless the user explicitly permits it.

Connection secrets are stored in the Windows secret store. They are never
written to logs or passed in a command line that can be exposed by process
inspection. If `mongodump` needs a configuration file, the module creates a
short-lived file readable only by the current user and removes it after process
completion. All displayed process output is redacted.

Retention runs only after a successful backup and can delete only files that
match the module's validated destination and naming policy.

## Out of scope

- Implementing MongoDB's dump format with the .NET driver
- Physical storage snapshots or continuous point-in-time backup
- Restore execution in the initial deliverable
- Uploading archives to cloud storage
- Running while no user is signed in

## Acceptance boundary

The deliverable is complete when it detects a compatible `mongodump`, creates a
restorable compressed archive, supports cancellation without presenting a
partial file as successful, protects credentials, and reports redacted failures.

Automated tests cover command construction, secret redaction, filename safety,
cancellation, incomplete-file behavior, and retention boundaries using a fake
process runner. An integration test uses a disposable MongoDB database and
verifies the resulting archive with the official restore tooling.

