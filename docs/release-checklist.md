# Release checklist

Use this checklist after CI has completed. CI builds the solution, runs tests
and smoke checks, publishes the supported runtimes, and inspects the generated
packages. The remaining checks require release hardware or credentials.

## Hardware

- Install and uninstall the Windows package.
- Open the macOS app from the DMG and test granted and denied camera and screen-recording permissions.
- Install and remove the Debian package.
- Run the AppImage normally and through its extraction fallback when FUSE is unavailable.
- Test a physical camera, USB capture card, and OBS Virtual Camera on each supported OS.
- Disconnect and reconnect a running device, refresh sources, and restart capture.
- Test gameplay crops from 16:9, 4:3, and ultrawide sources.
- Verify MoonGet, StoryMoon, and Talkatoo behavior during a representative run.
- Inspect diagnostics and confirm packages contain the required native OpenCV and ONNX Runtime libraries.
- Test a user profile and checkout path containing non-ASCII characters.

## Signing and branding

- Replace temporary package icons with final branded assets.
- Sign Windows installers and release binaries.
- Sign and notarize the macOS application with hardened-runtime entitlements.
- Keep certificates and notarization credentials in protected release secrets,
  never in the repository.
