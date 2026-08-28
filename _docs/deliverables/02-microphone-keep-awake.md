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

The deliverable is complete when capture can run continuously, all sample data
is demonstrably discarded, start/stop is reliable, denied permission is clearly
reported, and device disconnection does not crash the application.

Automated tests cover lifecycle, permission states, cancellation, and endpoint
changes using a fake capture adapter. Privacy behavior, power-saving prevention,
and long-running resource use require documented manual tests.

