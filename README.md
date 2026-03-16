# universal-cam

Use your iPhone as a Windows webcam and microphone over USB or Wi-Fi.

> **Status:** Early development — Phase 1 (Foundation)

---

## Requirements

- Windows 10 version 2004 (build 19041) or later
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Windows App Runtime 1.8](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads)
- Apple Devices (formerly iTunes) — for USB pairing

## Building

Requires .NET 8 SDK and VS 2022 Build Tools with `ManagedDesktopBuildTools` + `VCTools` workloads.

```powershell
# Restore packages
dotnet restore universal-cam.sln

# Build (Debug)
dotnet build universal-cam.sln --arch x64

# Run
dotnet run --project src\UniversalCam\UniversalCam.csproj --arch x64

# Test
dotnet test universal-cam.sln
```

## Project Structure

```
universal-cam/
  src/
    UniversalCam/          # WinUI 3 app — UI layer, main window, settings
    UniversalCam.Core/     # Transport (MsQuic), video pipeline (MF/D3D11),
                           # device logic (libimobiledevice P/Invoke)
  tests/
    UniversalCam.Tests/    # xUnit unit tests
  docs/
    architecture/          # Design docs, diagrams, ADRs
  tools/
    scripts/               # Build helpers, release scripts
  ROADMAP.md               # 4-phase product roadmap
```

## Tech Stack

| Concern | Technology |
|---|---|
| UI | WinUI 3 / Windows App SDK 1.8 |
| Language | C# / .NET 8 |
| Transport | MsQuic (QUIC over USB or Wi-Fi) |
| Video | Windows Media Foundation + DXVA2 hardware decode |
| Audio | WASAPI virtual audio device |
| Rendering | Direct3D 11 + WIC |
| USB | libimobiledevice / Apple Devices driver |

## License

TBD
