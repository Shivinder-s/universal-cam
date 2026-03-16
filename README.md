# universal-cam

Use your iPhone as a camera and microphone source for a Windows app over Wi-Fi (QUIC) or USB tunnel (TCP).

> Status: Active prototype (Phase 1).

---

## What Is In The Repo

This repository contains both sides of the system:

- iOS app (SwiftUI): camera/audio capture, H.264 + AAC encoding, Bonjour discovery, QUIC/TCP transport.
- Windows app (WPF): stream session orchestration, transport listeners, protocol parsing, preview UI.

Core transport/protocol implementation currently lives in `src/UniversalCam.Core`.

---

## Current Features

- iPhone app advertises/discovers with Bonjour (`_universalcam._tcp`).
- Wi-Fi transport via QUIC on port `7779`.
- USB path via localhost TCP tunnel on port `7780`.
- Unified control protocol (JSON control messages + stream-type multiplexing).
- Video and audio frame parsing on Windows.
- Basic Windows preview window with connection state and transport badge.
- Camera/microphone permission flow and settings entry points on iOS.

---

## Known Prototype Limitations

- Windows `H264Decoder` is currently a Phase 1 stub and emits placeholder frames.
- Audio playback/rendering pipeline on Windows is not complete yet.
- USB requires a local forwarding setup and Apple mobile device support on Windows.
- Protocol and app UX are still evolving.

---

## Requirements

### Windows

- Windows 10 version 2004 (build `19041`) or newer
- .NET 8 SDK
- Visual Studio 2022 Build Tools (Desktop .NET workloads)

### iOS

- Xcode 15+
- iOS 15+
- Physical iPhone for camera/mic testing

---

## Build And Run

### Windows

```powershell
# restore
dotnet restore universal-cam.sln

# build
dotnet build universal-cam.sln --configuration Debug

# run the Windows app
dotnet run --project src\UniversalCam\UniversalCam.csproj

# tests
dotnet test universal-cam.sln
```

### iOS

1. Open `ios/UniversalCamPhone.xcodeproj` in Xcode.
2. Select a real iPhone target.
3. Build and run.
4. Grant camera/microphone/local-network permissions.

---

## Project Structure

```text
universal-cam/
  src/
    UniversalCam/              # Windows WPF app UI
    UniversalCam.Core/         # Shared Windows core logic
      Discovery/               # Bonjour advertisement
      Protocol/                # Control messages + frame header model
      Transport/               # QUIC server, TCP USB transport, parser
      Video/                   # H.264 decode pipeline (stub in Phase 1)
  ios/
    UniversalCamPhone/         # iOS app (capture, encode, transport, UI)
  tests/
    UniversalCam.Tests/
  docs/
  ROADMAP.md
```

---

## Wire Protocol Summary

- Stream type byte first:
  - `0x00` control (JSON + newline)
  - `0x01` video frame
  - `0x02` audio frame
- Binary media frame header is 14 bytes:
  - stream type (1)
  - payload length (4, little-endian)
  - PTS in microseconds (8, little-endian)
  - flags (1)

See `ios/UniversalCamPhone/Transport/PROTOCOL.md` for full details.

---

## Tech Stack

| Area | Technology |
|---|---|
| Windows UI | WPF (.NET 8, x64) |
| Windows transport | `System.Net.Quic`, `TcpClient` |
| Windows discovery | `Makaretu.Dns.Multicast` |
| iOS UI | SwiftUI |
| iOS media | AVFoundation, VideoToolbox |
| iOS networking | Network.framework (`NWConnection`, `NWListener`, `NWBrowser`) |
| Serialization | `System.Text.Json` (Windows), `Codable` (iOS) |

---

## Contributing

Contributions are welcome.

- Start with `CONTRIBUTING.md` for setup, workflow, and pull request checklist.
- Please follow `CODE_OF_CONDUCT.md` in all project interactions.
- For major protocol or architecture changes, open an issue first to align on direction.

---

## License

This project is open source under the MIT License.

See `LICENSE` for full text.

---

## Third-Party Notices

For open-source dependency attributions and framework notices, see
`THIRD_PARTY_NOTICES.md`.
