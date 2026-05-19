# VoxAssist-Desktop: AI Developer Notes

This document serves as a reference for AI assistants and developers working on the VoxAssist-Desktop codebase. It outlines the architecture, key design decisions, and platform-specific workarounds implemented in the project.

## Core Architecture
- **Framework**: .NET 10 utilizing Avalonia UI for cross-platform desktop support.
- **State Management**: ReactiveUI is used extensively within the `ViewModels` for property change notification and reactive programming.
- **Configuration**: JSON-based settings (e.g., `settings.json`, `actions.json`, `ai_providers.json`) are stored in the user's local application data directory (e.g., `~/.config/VoxAssist` on Linux). If these files are missing or empty on startup, the application extracts factory defaults embedded within the assembly.

## Key Features & Services
- **Speech-to-Text (STT)**: Streaming audio transcription primarily utilizing the Grok API (`GrokService.cs`).
- **LLM Post-Processing**: Transcriptions can be passed through LLMs. Features a "Smarter Append Mode" that scans conversation history for the "root" question and rolls context forward to provide full conversational awareness without unrelated history.
- **Text-to-Speech (TTS)**: Audible AI responses (`GrokTtsService.cs`).
- **Global Hotkeys**: System-wide hotkey interception powered by `SharpHook`.
- **Keyboard Injection**: Types out the AI's response or raw dictation directly into the active application.

## Hardware Integration: ReSpeaker
The application features deep, native integration with Seeed Studio ReSpeaker mic arrays (v1.0, v2.0, and Lite) using `LibUsbDotNet` (`RespeakerService.cs`):
- **Controls**: Hardware-level Acoustic Echo Cancellation (AEC), Automatic Gain Control (AGC), and Noise Suppression (NS).
- **Telemetry**: Direction of Arrival (DOA) tracking.
- **Visual Feedback**: Direct control over the LED ring (brightness, colors, and patterns like "Think", "Listen", "Spin").

## Linux-Specific Security & Workarounds (CRITICAL)
Typing text via simulated keystrokes on Linux (especially under Wayland) requires strict security permissions to access `/dev/uinput`.

### The .NET Capability-Dropping Problem
Standard Linux capabilities (e.g., `setcap`) fail with .NET applications because the CoreCLR runtime drops "Effective" capabilities during its thread initialization and stack hardening phases, long before C# code can open `/dev/uinput`.

### The "Surgical Native Launcher" Solution
To bypass the .NET runtime restriction without requiring the user to run the app as `root` or join the `input` group (which presents keylogging risks):
1. **`vox-launch.c`**: A tiny native C program acts as a gatekeeper.
2. **Permissions**: The `setcap` command (`cap_sys_admin,cap_sys_rawio,cap_dac_override+ep`) is applied *only* to this C launcher.
3. **File Descriptor Inheritance**: The launcher opens `/dev/uinput`, initializes the virtual keyboard device, and then uses `execv()` to launch the .NET `VoxAssist.Desktop` binary. It passes the open file descriptor via the `VOXASSIST_UINPUT_FD` environment variable.
4. **C# Consumption**: `KeyboardService.cs` detects this inherited file descriptor and uses it directly, bypassing the need to open the device itself.

### AppImage & CI/CD
- **Deployment**: Linux builds are packaged as AppImages via the `.github/workflows/build.yml` and `build-appimage.sh` scripts.
- **Self-Integration**: On first run from an AppImage, VoxAssist automatically creates a `voxassist.desktop` file in `~/.local/share/applications/` to add itself to the system menu (`MainWindowViewModel.Settings.cs`).
- **Permission Elevation**: If launched without permissions, the app prompts the user and uses `pkexec` to run `setup-launcher.sh`, which compiles the C launcher locally (ensuring perfect system compatibility) and applies the necessary capabilities.