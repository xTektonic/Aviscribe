# Aviscribe

Aviscribe is a cross-platform Talkatoo run assistant for Super Mario Odyssey.
It watches a camera, capture card, or virtual camera, recognizes relevant
gameplay text, and maintains pending, counted, and uncounted moon state.

The maintained application is one Avalonia desktop program. Capture is provided
by FlashCap through DirectShow on Windows, AVFoundation on macOS, and V4L2 on
Linux. Aviscribe uses the same shared FlashCap capture path on every platform,
including DirectShow virtual cameras that do not expose a `DevicePath`.

## Supported platforms

- Windows 10/11 x64
- macOS 14 or newer on Apple Silicon
- Ubuntu 22.04 and 24.04 x64
- Best effort on other modern glibc-based x64 Linux distributions

Release automation produces a Windows x64 installer, a macOS Apple Silicon
DMG containing `Aviscribe.app`, a Linux x64 `.deb`, and a Linux x64 AppImage.
NixOS-specific packaging is not provided.

## Develop

Install the .NET 10 SDK. The repository pins the expected SDK feature band in
`global.json`.

Restore also requires the private `FlashCap` and `FlashCap.Core` 1.11.9
packages in `../../LocalNuGet` relative to the repository. `NuGet.Config` maps
those two package IDs exclusively to that local feed so they cannot silently
fall back to the public FlashCap packages. CI keeps the packages out of public
feeds and source control by building them from the pinned fork commit in a
bootstrap job, then passing the resulting local feed to each platform job as a
short-lived workflow artifact.

On Windows, open `Aviscribe.sln` with Visual Studio 2026 18.x or another
Visual Studio release that supports .NET 10. Select the shared
**Aviscribe Desktop** launch profile and press F5. If shared solution launch
profiles are disabled, set `Aviscribe.Desktop` as the startup project instead.
`Aviscribe.Core`, `Aviscribe.Core.Capture`, `Aviscribe.Capture`, and
`Aviscribe.UI` are libraries and are debugged through the desktop process.

After switching from an older checkout that used the pre-normalization project
paths, close Visual Studio and remove the ignored `.vs` directory once so the
IDE does not restore retired projects such as `Core\Core.csproj`.

```text
dotnet restore Aviscribe.sln
dotnet build Aviscribe.sln --configuration Release --no-restore
dotnet test Aviscribe.sln --configuration Release --no-build
dotnet run --project src/Aviscribe.Desktop
```

The solution contains:

- `src/Aviscribe.Core`: game state, matching, OCR, persistence, and frame processing
- `src/Aviscribe.Core.Capture`: capture contracts, owned frames, and crop models
- `src/Aviscribe.Capture`: the shared FlashCap implementation
- `src/Aviscribe.UI`: platform-neutral Avalonia views and controls
- `src/Aviscribe.Desktop`: the single GUI entry point and dependency composition
- `tools/Aviscribe.Classifier`: audits, experiments, and smoke/regression commands
- `tests/Aviscribe.Core.Tests` and `tests/Aviscribe.Capture.Tests`: hardware-independent tests

The legacy Windows-only application and Accord capture projects are retained
temporarily for parity reference but are not part of the maintained solution or
release path.

### Smoke and regression commands

Run these after the automated tests:

```text
dotnet run --project tools/Aviscribe.Classifier --configuration Release --no-build -- state-smoke
dotnet run --project tools/Aviscribe.Classifier --configuration Release --no-build -- capture-crop-smoke
dotnet run --project tools/Aviscribe.Classifier --configuration Release --no-build -- frameprocessor-smoke
dotnet run --project tools/Aviscribe.Classifier --configuration Release --no-build -- talkatoo-confirmation-smoke
dotnet run --project tools/Aviscribe.Classifier --configuration Release --no-build -- matcher-smoke
```

`frameprocessor-smoke` includes the collection-confirmation scenarios. Video
regression and audit commands remain available through
`dotnet run --project tools/Aviscribe.Classifier -- --help`; they require local
test footage and are intentionally not part of hardware-independent CI.

## Capture and crop behavior

Choose a source under **Settings → Capture Source**, refresh the list if a
device was attached after startup, then start capture. Aviscribe selects a
reported format closest to 1920×1080 at 16:9, preferring higher frame rates.

Device crop keys have this form:

```text
platform:backend:96-bit-sha256-fingerprint
```

The fingerprint is derived from FlashCap's native device identity, falling back
to its normalized display name. This is stable enough for a device on one
machine without writing the native identifier to settings. Mappings from a
different operating system or a disconnected device are ignored harmlessly.

**Crop Gameplay** waits for a raw, uncropped source frame. The saved per-device
selection is always 16:9. When source resolution changes, it scales the saved
selection; invalid or legacy data falls back to the largest centered 16:9
region. The runtime has one normalization path:

```text
raw source → selected 16:9 crop → 1920×1080 BGR → OCR/detection
```

The crop preview is cropped but calibration snapshots are raw. Snapshot
requests cancel when superseded, capture stops, a device disconnects, the crop
window closes, the application exits, or the five-second timeout expires.

FlashCap is configured with a single queued frame and a non-scattering callback.
When processing is busy it drops newly arriving frames instead of building
latency. Confirmation intervals are counted against delivered frames, and the
existing stability, retry, rearm, overlap, and deduplication behavior is covered
by both unit and smoke tests.

## Camera permissions and troubleshooting

### Windows

Allow desktop applications to access cameras under Windows privacy settings.
Close other applications that may have opened the device exclusively, then use
**Refresh**. The installer creates Start menu integration under Program Files
and can be removed through Installed Apps without retaining program files.

OBS Virtual Camera is a DirectShow source and may omit the optional `DevicePath`
property. Aviscribe's FlashCap build locates those filters by their DirectShow
moniker and captures them through the same FlashCap backend as physical cameras.
Start the OBS virtual camera, select it, and start capture normally.

### macOS

The app bundle contains `NSCameraUsageDescription`. On first use, allow camera
access. If access was denied, enable Aviscribe under **System Settings → Privacy
& Security → Camera**, restart the app, and refresh the source list.

Development and CI bundles use an ad-hoc signature. Public distribution still
requires an Apple Developer ID certificate, hardened-runtime signing, and
notarization.

### Linux

Capture devices normally appear as `/dev/video*`. If a device is listed but
cannot be opened, inspect its permissions and add the current user to the
distribution's `video` group, then sign out and back in. Also close software
that may be holding the device.

Aviscribe supports Avalonia's X11 path, including XWayland sessions. Native
Wayland support is experimental and is not a release target yet.

AppImage execution commonly requires FUSE 2. If FUSE mounting is unavailable:

```text
./Aviscribe-0.1.0-x86_64.AppImage --appimage-extract
./squashfs-root/AppRun
```

On NixOS, use `appimage-run` or extract the AppImage. A native Nix package may
be added later.

## Diagnostics and privacy

Enable debug logging under **Settings → Diagnostics**, then open the diagnostics
window to see:

- Application, runtime, operating system, and architecture
- Selected capture device, backend, format, and state
- Source dimensions, active crop, and normalization target
- Recent errors and diagnostic log entries
- The log directory and an action to open it

Informational logging is the default. Debug logging is an explicit persisted
opt-in. OCR enqueue, recognized text, match decisions, and collection outcomes
are included only while debug logging is enabled. The diagnostics window is
modeless, remains open while the main window is used, and refreshes recent
entries automatically. Logs rotate at 5 MiB with ten files retained. Aviscribe
never automatically saves captured frames, and logs avoid native device
identifiers, stack traces, secrets, and unnecessary personal paths.

| Platform | Settings and run state | Logs |
| --- | --- | --- |
| Windows | `%LOCALAPPDATA%\Aviscribe` | `%LOCALAPPDATA%\Aviscribe\logs` |
| macOS | `~/Library/Application Support/Aviscribe` | `~/Library/Logs/Aviscribe` |
| Linux | `$XDG_CONFIG_HOME/aviscribe` or `~/.config/aviscribe` | `$XDG_STATE_HOME/aviscribe/logs` or `~/.local/state/aviscribe/logs` |

All application paths use runtime path APIs and have automated coverage for
non-ASCII and target-OS path forms. Avalonia resource identifiers use exact
case for case-sensitive filesystems.

## Publish and package locally

Raw self-contained publishing:

```text
dotnet publish src/Aviscribe.Desktop/Aviscribe.Desktop.csproj -c Release -r win-x64 --self-contained true
dotnet publish src/Aviscribe.Desktop/Aviscribe.Desktop.csproj -c Release -r osx-arm64 --self-contained true
dotnet publish src/Aviscribe.Desktop/Aviscribe.Desktop.csproj -c Release -r linux-x64 --self-contained true
```

Platform packages must be built on their corresponding operating system:

```text
# Windows PowerShell; WiX is restored as an SDK package
./packaging/windows/package.ps1 -Version 0.1.0

# macOS Apple Silicon; uses codesign, plutil, and hdiutil
bash packaging/macos/package.sh 0.1.0

# Ubuntu x64; requires dpkg-deb, curl, and AppImage tooling
bash packaging/linux/package.sh 0.1.0
```

Outputs are written to `artifacts/packages`. The Debian and AppImage builds copy
the same `linux-x64` publish payload. CI restores, builds, tests, runs smoke
suites, publishes each RID, checks the native OpenCV and ONNX Runtime assets,
inspects or launches the package as appropriate, and uploads versioned
artifacts.

## Manual hardware release checklist

Complete this checklist on release hardware:

1. Install and cleanly uninstall the Windows package; confirm Start menu and optional desktop shortcuts.
2. Open the macOS app from the DMG, grant and deny camera permission, and verify the resulting messages.
3. Install and remove the `.deb`; confirm application-menu registration.
4. Run the AppImage with FUSE and by extraction fallback.
5. Test at least one physical camera, USB capture card, and OBS Virtual Camera on every OS.
6. Disconnect and reconnect a running device, refresh, restart capture, and confirm no stale frames.
7. Calibrate 16:9 crops from 16:9, 4:3, and ultrawide source formats; change crop during active capture.
8. Confirm MoonGet, StoryMoon, and Talkatoo behavior over a representative run and inspect diagnostics.
9. Verify all packages contain the expected native OpenCV and ONNX Runtime libraries.
10. Test a user profile and checkout path containing non-ASCII characters.

## Deferred production release work

- Replace the temporary Linux vector mark and add final branded Windows,
  macOS, and Linux icon sets.
- Obtain Windows and Apple signing certificates.
- Add macOS hardened-runtime entitlements and notarization credentials.
- Sign the Windows installer and release binaries.

Unsigned/ad-hoc packages produced today are suitable for development and CI
validation, not a polished public release.
