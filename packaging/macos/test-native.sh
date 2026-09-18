#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
test_dir="$(mktemp -d)"
trap 'rm -rf "${test_dir}"' EXIT
xcrun clang -fobjc-arc -fblocks -O2 -Wall -Wextra -Werror -Wno-unused-parameter \
  -mmacosx-version-min=14.0 \
  -framework Foundation -framework ScreenCaptureKit -framework CoreMedia -framework CoreVideo \
  "${repo_root}/tests/Aviscribe.Capture.Native.Tests/ScreenCaptureTests.m" \
  -o "${test_dir}/screen-capture-tests"
"${test_dir}/screen-capture-tests"
