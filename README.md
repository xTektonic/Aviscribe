# Aviscribe

Aviscribe is a cross-platform Talkatoo run assistant for Super Mario Odyssey.
It watches a camera, capture card, virtual camera, or application window, recognizes relevant
gameplay text, and maintains pending, counted, and uncounted moon state.

The maintained application is one Avalonia desktop program. Video-device capture
is provided by FlashCap through DirectShow on Windows, AVFoundation on macOS,
and V4L2 on Linux. Window capture uses an in-process platform adapter: Win32
window capture on Windows, CoreGraphics on macOS, X11 for X11/XWayland windows,
and the XDG ScreenCast portal with PipeWire for native Wayland windows.
Both paths deliver the same owned BGR frame contract to crop, OCR, and preview.

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

Restore also requires the private `FlashCap` and `FlashCap.Core` 1.11.9 packages
and `PipeWire.NET` 0.2.1-alpha-aviscribe.1 in `../../LocalNuGet` relative to the
repository. `NuGet.Config` maps those package IDs to that local feed. CI builds
FlashCap from its pinned fork commit. It builds PipeWire.NET from the pinned
`xTektonic/PipeWire.NET` fork commit
`263081ab3d5117c487cf8174548d98c38f4d32e8`, which adds portal-FD connection
and a CPU-readable buffer policy. The packages stay out of source control. CI
rebuilds the local feed inside each job that consumes it, so dependency packages
do not use workflow artifact storage.

Build all three pinned packages for local development with:

```powershell
./tools/build-local-nuget.ps1 -OutputPath ../../LocalNuGet
```

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
- `src/Aviscribe.Capture`: shared video-device and platform window capture
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

OCR uses CPU by default. **Settings → Diagnostics → OCR processor** offers an
opt-in **GPU (WebGPU)** mode that is persisted and recreates the OCR session.
Supported graph nodes run on a high-performance WebGPU adapter while remaining
shape nodes use CPU. Initialization or inference failures recreate a CPU-only
session and appear with the requested and active providers in Diagnostics.
The first GPU inference may spend about two seconds warming shaders. See
[docs/gpu-ocr-research.md](docs/gpu-ocr-research.md) for benchmarks and package
details. This is cross-vendor acceleration, not a CUDA-specific fast path.

## Capture and crop behavior

Choose **Video Device** or **Window** under **Settings → Capture Source**, then
choose a source and start capture. Refresh after attaching a device, opening a
window, or changing permissions. Aviscribe remembers the last selection for each
source type and keeps crop settings independently for every device and window.
For video devices, Aviscribe selects a reported format closest to 1920×1080 at
16:9, preferring higher frame rates.

Capture-source crop keys have this form:

```text
platform:backend:96-bit-sha256-fingerprint
```

For devices, the fingerprint is derived from FlashCap's native identity, falling
back to its normalized display name. For windows it is derived from the owning
application, class where available, and title. This avoids storing native handles
and lets recurring application windows recover their crop after restart. Mappings
from another operating system or a source that is not open are ignored harmlessly.

Window capture runs at 10 fps, which is sufficient for Aviscribe's OCR cadence.
Covered windows work on supported compositor/application combinations. Minimized
windows and protected or GPU-only surfaces are best effort; restore the window if
the preview is blank or capture reports repeated failures.

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

Window capture uses `PrintWindow` with a display-copy fallback. Most covered
desktop windows work. Minimized, hardware-overlay, and protected-content windows
may return no usable image; restore the source window in that case.

### macOS

The app bundle contains `NSCameraUsageDescription`. On first use, allow camera
access. If access was denied, enable Aviscribe under **System Settings → Privacy
& Security → Camera**, restart the app, and refresh the source list.

Development and CI bundles use an ad-hoc signature. Public distribution still
requires an Apple Developer ID certificate, hardened-runtime signing, and
notarization.

Window capture requires **Screen & System Audio Recording** permission. If the
Window list shows a permission message, enable Aviscribe under **System Settings
→ Privacy & Security**, restart it, and refresh. CoreGraphics captures covered
windows, but minimized or protected windows may be unavailable. macOS builds and
permission behavior must be validated on a signed app bundle; Windows CI cannot
exercise those APIs.

### Linux

Capture devices normally appear as `/dev/video*`. If a device is listed but
cannot be opened, inspect its permissions and add the current user to the
distribution's `video` group, then sign out and back in. Also close software
that may be holding the device.

Choose **Choose Capture Source…** for a unified list of video devices and
windows. X11 and XWayland windows are listed directly through `libX11`. In a
Wayland session, choose **Choose a Wayland window…**. The source dialog remains
open while the compositor's secure window picker runs and closes only after a
capture session has been prepared. Aviscribe receives the selected window through
the XDG ScreenCast portal's authorized PipeWire connection; native Wayland
applications do not expose a window list directly to Aviscribe.

Covered-window behavior depends on the compositor. Minimized windows generally
cannot be captured through X11. Linux packaging already depends on `libx11-6`;
Wayland capture requires a working `xdg-desktop-portal` ScreenCast backend,
PipeWire, and `libpipewire-0.3.so.0`. GNOME and KDE normally supply capable
backends. Some wlroots backends expose monitor capture but not individual windows;
Aviscribe detects that capability and disables the Wayland window entry instead
of silently offering a monitor. No external capture executable is used.

On NixOS, enable PipeWire and `xdg.portal`, then select the portal backend intended
for the active desktop or compositor. A running portal service alone is not
sufficient: its ScreenCast interface must advertise the `WINDOW` source type.
The Diagnostics window and Linux log distinguish a missing service, a backend
without window support, user cancellation, portal rejection, PipeWire connection
failure, and a stream that produces no readable frames.

AppImage execution commonly requires FUSE 2. If FUSE mounting is unavailable:

```text
./Aviscribe-0.3.2-x86_64.AppImage --appimage-extract
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
./packaging/windows/package.ps1 -Version 0.3.2

# macOS Apple Silicon; uses codesign, plutil, and hdiutil
bash packaging/macos/package.sh 0.3.2

# Ubuntu x64; requires dpkg-deb, curl, and AppImage tooling
bash packaging/linux/package.sh 0.3.2
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
