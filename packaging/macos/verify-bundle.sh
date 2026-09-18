#!/usr/bin/env bash
set -euo pipefail

app="${1:?Usage: verify-bundle.sh path/to/Aviscribe.app}"
plist="${app}/Contents/Info.plist"
plutil -lint "${plist}"
test -n "$(plutil -extract NSCameraUsageDescription raw "${plist}")"
test "$(plutil -extract CFBundleIconFile raw "${plist}")" = "Aviscribe.icns"
test -s "${app}/Contents/Resources/Aviscribe.icns"

# Inspect the final executable's signature after Velopack has re-signed it.
# A valid signature alone does not imply permission to open a camera.
signature="$(mktemp)"
trap 'rm -f "${signature}"' EXIT
codesign --display --entitlements :- "${app}/Contents/MacOS/Aviscribe" > "${signature}" 2>/dev/null
for key in com.apple.security.device.camera com.apple.security.cs.allow-jit; do
  test "$(/usr/libexec/PlistBuddy -c "Print :${key}" "${signature}")" = "true"
done
bridge="${app}/Contents/MacOS/libAviscribeScreenCapture.dylib"
test -s "${bridge}"
file "${bridge}" | grep -q arm64
for symbol in aviscribe_screen_open aviscribe_screen_read aviscribe_screen_close aviscribe_screen_free; do
  nm -gU "${bridge}" | grep -q "_${symbol}$"
done
codesign --verify --deep --strict "${app}"
