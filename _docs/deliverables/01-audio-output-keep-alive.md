# Deliverable 1: Audio output keep-alive

## Goal

Prevent a selected audio output device from entering its device-specific power
saving mode by periodically playing a near-silent audio burst.

The legacy implementation in `_archive/SilentSound` is the behavioral proof of
concept. Its 60-second interval, 250-millisecond burst, 48 kHz stereo format, and
0.00008 amplitude are retained as defaults. Its console UI, blocking loop, fixed
settings, and default-device-only behavior are replaced.

## User capabilities

- Enable or disable the module.
- Select an active output endpoint or follow the Windows default endpoint.
- Configure burst interval, duration, and amplitude within safe limits.
- Play a test burst immediately.
- See running state, last burst, and the latest recoverable error.
- Keep the process running from the Windows notification area.

## Design

`AudioOutputKeepAliveService` owns the recurring lifecycle. It uses an
`IAudioOutputService` Windows adapter to enumerate endpoints and play a generated
sample provider. Timing is asynchronous and cancellation-aware.

The adapter resolves the selected endpoint for every burst. This naturally
follows Windows default-device changes when the default option is selected.
When an explicitly configured endpoint is unavailable, the module enters a
recoverable fault state and retries at the configured interval. It does not
silently switch devices.

## Out of scope

- Playing user media or arbitrary audio files
- Audio enhancement or volume control
- Guaranteeing compatibility with every vendor's power-saving firmware
- Operation while no user is signed in

## Acceptance boundary

The software acceptance boundary is met: the module is integrated into the WPF
shell, configured independently, can be started and stopped repeatedly, follows
default-device changes, persists settings, and exposes status and errors.

Automated tests cover settings validation, recurrence, cancellation, recovery
after device errors, audio sample bounds, and settings persistence. The following
hardware checks remain release validation because they cannot be automated
reliably without representative devices:

1. Verify the default burst is inaudible on speakers, headphones, and USB or
   Bluetooth outputs.
2. Verify the target device remains awake during an extended run without
   unbounded resource growth.
3. Change the Windows default endpoint and confirm the next burst uses it.
4. Unplug an explicitly selected endpoint and confirm an error is shown without
   changing the selection; reconnect it and confirm recovery.

## Implementation

Production code is in `MultiTool/Modules/AudioOutputKeepAlive` with the Windows
adapter in `MultiTool/Platform/Windows`. Automated tests are in
`MultiTool.Tests/Modules/AudioOutputKeepAlive`. User settings are stored in
`%LOCALAPPDATA%\MultiTool\settings.json`.
