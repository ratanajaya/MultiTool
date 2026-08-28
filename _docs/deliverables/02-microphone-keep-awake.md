# Deliverable 2: Microphone keep-awake

## Goal

Keep a selected microphone endpoint active by continuously opening a capture
stream and immediately discarding all captured buffers.

## User capabilities

- Enable or disable the module.
- Select an input endpoint or follow the Windows default endpoint.
- Start and stop capture manually for testing.
- See capture state, selected device, start time, and last error.
- Open Windows microphone privacy settings when access is denied.
- Choose whether the module restarts automatically after a device change.

## Privacy and safety

The module does not save, analyze, log, or transmit audio samples. This behavior
must be stated next to the enable control. Captured buffers are discarded in
memory as soon as the audio API supplies them.

Windows may display a microphone-in-use indicator for the entire time the module
is running. Permission denial is a supported state, not an application failure.
The user always has a visible way to stop capture.

## Design

`MicrophoneKeepAwakeService` controls an `IAudioCaptureService` implemented by
the Windows platform layer. The platform adapter owns the native/NAudio capture
object and exposes state and endpoint-change events without exposing sample data
to the application layer.

Unexpected stream termination causes a bounded retry. Endpoint removal changes
the module to a recoverable unavailable state. No retry loop should repeatedly
trigger permission prompts.

## Out of scope

- Recording, transcription, metering, voice detection, or streaming
- Suppressing Windows privacy indicators
- Changing microphone gain, enhancement, or exclusive-mode settings
- Guaranteeing compatibility with every microphone or driver

## Acceptance boundary

The software acceptance boundary is met: capture runs continuously through the
selected or default multimedia endpoint, sample callbacks discard their
buffers without reading them, start and stop are cancellation-aware, permission
denial is actionable, and device loss is isolated to this module.

Automated tests cover lifecycle, cancellation, permission denial, endpoint-loss
policy, bounded recovery, idempotency, settings validation, navigation, and
cross-module settings persistence. The following hardware and privacy checks
remain release validation:

1. Grant microphone access, start the module, and confirm Windows shows the
   expected microphone-in-use indicator while capture remains active.
2. Run for an extended period and confirm buffers are not written to disk or
   retained in memory and resource use does not grow without bound.
3. Disable microphone privacy access and confirm the module faults once without
   repeated prompts; confirm the privacy-settings button opens the correct page.
4. Unplug an explicitly selected microphone with automatic restart both enabled
   and disabled, and confirm the configured behavior.
5. Follow the default microphone, change that Windows default,
   and confirm capture restarts against the new endpoint.
6. Confirm the target device stays awake on representative USB, Bluetooth, and
   built-in microphone hardware.

## Implementation

Production code is in `MultiTool/Modules/MicrophoneKeepAwake`. The NAudio/WASAPI
adapter and Windows privacy-settings launcher are in `MultiTool/Platform/Windows`.
The adapter registers for endpoint notifications, immediately discards every
`DataAvailable` buffer, and never exposes sample data to the module layer.

Recovery is deliberately bounded to three capture attempts with a three-second
delay. Access denial never retries. When automatic restart after device changes
is disabled, endpoint removal immediately remains faulted until the user starts
the module again. Settings share `%LOCALAPPDATA%\MultiTool\settings.json` and are
merged atomically with settings belonging to other modules.
