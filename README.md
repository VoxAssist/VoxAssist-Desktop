# VoxAssist Desktop

VoxAssist is a high-performance voice assistant and dictation tool designed for desktop environments. It combines streaming speech-to-text, customizable AI post-processing, and native hardware integration to provide a seamless voice-driven workflow.

> **Note:** This application has currently only been tested and verified on **Linux**. While the codebase contains foundations for Windows and macOS, Linux is the primary supported platform.

## 🚀 Key Features

- **Streaming Speech-to-Text**: Real-time voice transcription using Grok (xAI) with support for multiple languages and audio compression (FLAC/G.711).
- **AI Post-Processing**: Automatically process transcribed text through LLMs with customizable system prompts. Supports "Append to Last Reply" for conversational context.
- **Native Keyboard Injection**: High-speed, low-latency text typing directly into any active application.
- **Global Hotkeys**: Trigger actions via system-wide keyboard shortcuts.
- **Text-to-Speech (TTS)**: Audible feedback for AI responses using high-quality neural voices.
- **Ticking Feedback**: Subtle audio cues during AI processing to indicate the system is "thinking."
- **Automatic Updates**: Built-in update system that pulls the latest releases directly from GitHub.

## 🎙️ ReSpeaker Hardware Support

VoxAssist features deep integration with Seeed Studio ReSpeaker Mic Array hardware (v1.0, v2.0, and Lite):

- **Visual Feedback**: Automatic LED pattern control (Listening, Thinking, Speaking, etc.).
- **DOA Tracking**: Direction of Arrival tracking to visualize where the sound is coming from.
- **Hardware Controls**: Toggle Hardware-level AEC (Acoustic Echo Cancellation), AGC (Automatic Gain Control), and NS (Noise Suppression) directly from the UI.
- **LED Customization**: Control LED brightness and patterns.

## 🐧 Linux Optimization & Security

VoxAssist uses a specialized security model for Linux to handle `/dev/uinput` access securely:

- **Surgical Native Launcher**: Uses a tiny C-based gatekeeper to handle keyboard permissions without requiring broad system-wide group changes or root privileges.
- **Wayland Compatibility**: The native uinput implementation ensures keyboard injection works even on restricted Wayland sessions.
- **AppImage Support**: Can be built as a single, portable AppImage containing all necessary .NET and native dependencies.

## 🛠️ Installation (Linux)

### 1-Line Terminal Installer (Recommended)
You can automatically download, install, and integrate VoxAssist into your desktop application menu with a single command:
```bash
curl -sSL https://raw.githubusercontent.com/VoxAssist/VoxAssist-Desktop/master/install.sh | bash
```

### Manual AppImage Installation
1. Download the latest `VoxAssist-x86_64.AppImage` from the [GitHub Releases](https://github.com/VoxAssist/VoxAssist-Desktop/releases) page.
2. Make it executable:
   ```bash
   chmod +x VoxAssist-x86_64.AppImage
   ```
3. Run the AppImage. If your Linux distribution does not have FUSE 2 installed (getting a `dlopen(): error loading libfuse.so.2` error), run the AppImage from the terminal once with the `--appimage-extract-and-run` flag:
   ```bash
   ./VoxAssist-x86_64.AppImage --appimage-extract-and-run
   ```
   *This initial run will automatically add VoxAssist to your desktop menu. You can launch it directly from your KDE/GNOME application launcher for all subsequent runs!*

### Build Requirements
- .NET 10 SDK
- Build essentials (`gcc`, `make` - required for the native launcher setup)

### Local Build
```bash
# Build the standalone binary
dotnet publish -r linux-x64 -c Release --self-contained true -o publish

# Run the application
./publish/VoxAssist.Desktop
```

## 📄 License
This project is licensed under the [MIT License](LICENSE).
