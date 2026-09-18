# Aviscribe

Aviscribe is a cross-platform Talkatoo translator for Super Mario Odyssey. Currently only Traditional Chinese gameplay is supported, with output being available in all native Super Mario Odyssey languages. 

Aviscribe works by capturing gameplay from a camera, capture card, virtual camera, or application window, recognizing Talkatoo and moon collection text, and displaying that info for the user to see.

Aviscribe can also synchronize a run between players through a compatible SMOO+ server. See [Multiplayer with SMOO+](docs/online-runs.md) for setup and usage.

For support, feedback, and to be notified of new builds, join the [Discord server](https://discord.gg/ADDAuJVxjn).

![Aviscribe run interface](docs/images/aviscribe-ui.png)

## Supported platforms

- Windows 10/11 x64
- macOS 14+ on Apple Silicon
- Ubuntu 22.04/24.04 x64
- Other modern glibc-based x64 Linux distributions may work

## Installation

Aviscribe is self-contained, so you do not need to install .NET separately.

Download the package for your operating system from the [latest release](https://github.com/xTektonic/Aviscribe/releases/latest).

### Windows

1. Download `Aviscribe-*-win-x64.msi`.
2. Open the installer and follow the prompts. Optionally select **Create a desktop shortcut (all users)** before installing; it is unchecked by default.
3. Leave **Launch Aviscribe** selected on the final page to open it immediately. Aviscribe is also added to the Start menu.

### macOS

1. Download `Aviscribe-*-osx-arm64.dmg`.
2. Open the disk image and drag **Aviscribe** into your Applications folder.
3. Open Aviscribe from Applications.

Because Aviscribe is not currently notarized, macOS may block the first launch. If that happens, open **System Settings > Privacy & Security** and select **Open Anyway** for Aviscribe.

### Linux

Download the AppImage. On most Linux distributions, you can double-click the file to run it. If it does not open, mark it as executable in the file’s **Properties** window, or run:

~~~bash
chmod +x Aviscribe-*-x86_64.AppImage
./Aviscribe-*-x86_64.AppImage
~~~

## Using the application

1. Start Aviscribe and open **Settings > Capture Source**.
2. Select a video device or application window, then start capture.
3. Use **Crop Gameplay** to select the gameplay area when prompted.
4. Configure the run and language, then use the **Run** screen to review pending,
   counted, and uncounted results.

OCR uses WebGPU by default when it is available, which can significantly decrease text and moon recognition times. If WebGPU cannot initialize or fails during processing, Aviscribe automatically falls back to CPU processing. You can explicitly select CPU under **Settings > Setup > OCR processor**.

Capture permissions depend on the platform:

- **Windows:** Allow desktop applications to access cameras.
- **macOS:** Allow Camera and Screen & System Audio Recording access as needed.
- **Linux:** Allow access to `/dev/video*` devices. Capturing Wayland windows
  also requires PipeWire and a working `xdg-desktop-portal` ScreenCast backend.

If a source is not listed after changing permissions or connecting hardware, refresh the source list and restart Aviscribe if necessary.

## Development setup

Aviscribe requires the .NET 10 SDK. The required SDK version is pinned in [global.json](global.json).

The project uses three private packages built from pinned repositories. From the repository root, create the local package feed before restoring:

~~~powershell
./tools/build-local-nuget.ps1 -OutputPath ../../LocalNuGet
dotnet restore Aviscribe.sln
~~~

Build, test, and run the desktop application with:

~~~text
dotnet build Aviscribe.sln --configuration Release --no-restore
dotnet test Aviscribe.sln --configuration Release --no-build
dotnet run --project src/Aviscribe.Desktop
~~~

On Windows, open Aviscribe.sln in Visual Studio and use Aviscribe.Desktop as the startup project if you prefer an IDE workflow.

## Validation

The automated tests are hardware-independent. The classifier tool also provides smoke checks for the state, capture crop, frame processor, Talkatoo confirmation, and matcher paths:

~~~text
dotnet run --project tools/Aviscribe.Classifier --configuration Release --no-build -- state-smoke
dotnet run --project tools/Aviscribe.Classifier --configuration Release --no-build -- capture-crop-smoke
dotnet run --project tools/Aviscribe.Classifier --configuration Release --no-build -- frameprocessor-smoke
dotnet run --project tools/Aviscribe.Classifier --configuration Release --no-build -- talkatoo-confirmation-smoke
dotnet run --project tools/Aviscribe.Classifier --configuration Release --no-build -- matcher-smoke
~~~

## Packaging

The packaging scripts use Velopack to produce a Windows x64 MSI, a macOS Apple Silicon DMG, and a Linux x64 AppImage together with the update packages and platform release feeds. Set the version explicitly before packaging:

~~~powershell
./tools/set-version.ps1 1.1.0
~~~

Run the package script on its target operating system:

macOS builds also require Xcode Command Line Tools (`xcode-select --install`) to compile the ScreenCaptureKit bridge. The bridge is built automatically for local runs and included in published applications.

~~~powershell
./packaging/windows/package.ps1 -Version 1.1.0
~~~

~~~text
bash packaging/macos/package.sh 1.1.0
bash packaging/linux/package.sh 1.1.0
~~~

Packages and publish outputs are written to `artifacts/`. Restore the repository's pinned Velopack tool before running a packaging script directly:

~~~text
dotnet tool restore
~~~

Windows packaging adds an optional desktop-shortcut feature to Velopack's generated MSI. CI checks its default and selected states without installing the application. For unattended installations, pass `AVISCRIBE_DESKTOP_SHORTCUT=1` to `msiexec` to opt in; otherwise no desktop shortcut is created. The Start menu shortcut is always installed. macOS and Linux packaging do not add desktop shortcuts.

macOS signing uses `packaging/macos/Aviscribe.entitlements` for both the initial bundle and Velopack's final signature. It includes camera access as well as the .NET runtime permissions. Packaging verifies the final executable's entitlements and the ScreenCaptureKit library. CI also tests native frame ownership, padded rows, idle frames, and stream errors without requesting screen access. Before publishing a macOS release, run the [live capture checks](docs/macos-capture-validation.md) against the installed DMG.

Installed copies check the stable GitHub Releases feed whenever Aviscribe starts. Draft and prerelease releases are not offered. When an update is available, Aviscribe asks before downloading it and restarts only after the download completes.

Pull requests build and validate all three packages. A push to `main` creates or refreshes a draft GitHub Release for the version in `Directory.Build.props`; publishing that draft makes the update available. Published releases cannot be replaced; bump the version before creating a new release.

Windows and macOS artifacts are not signed with trusted developer certificates. Windows SmartScreen and macOS Gatekeeper may therefore display warnings.

## Repository layout

- src/Aviscribe.Core: run state, moon matching, OCR, and frame processing
- src/Aviscribe.Core.Capture: capture contracts and crop models
- src/Aviscribe.Capture: video-device and platform window capture
- src/Aviscribe.UI: Avalonia views and controls
- src/Aviscribe.Desktop: application entry point
- tools/Aviscribe.Classifier: dataset, detector, OCR, and video analysis tools
- tests: automated tests

The classifier tool has its own usage notes in
[tools/Aviscribe.Classifier/README.md](tools/Aviscribe.Classifier/README.md).

## License

Aviscribe is licensed under the MIT License. See [LICENSE](LICENSE).
