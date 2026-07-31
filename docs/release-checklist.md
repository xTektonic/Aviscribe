# Release checklist

The canonical development, packaging, platform troubleshooting, and manual
hardware checklist is in the repository `README.md`. This file records the
release gate that CI cannot complete without hardware or credentials.

## Automated gate

- Build all maintained projects with .NET 10.
- Run both xUnit test projects.
- Run state, crop, frame-processor, Talkatoo-confirmation, collection-confirmation,
  and matcher smoke coverage.
- Publish `win-x64`, `osx-arm64`, and `linux-x64` as self-contained payloads.
- Verify OpenCV and ONNX Runtime native libraries in every payload.
- Build and inspect the installer, DMG/app bundle, Debian package, and AppImage.

## Hardware gate

- Follow the ten-step manual hardware checklist in `README.md`.
- Record tested device names, native backend, selected format, OS version, and
  whether device loss/restart succeeded.
- Exercise camera permission granted and denied states on macOS and Windows.
- Exercise a Linux user with and without `/dev/video*` permission.

## Credential and branding gate

Public releases remain blocked until final icons and signing assets exist.
Never store certificates or notarization credentials in the repository. CI
should receive them through protected release-environment secrets.
