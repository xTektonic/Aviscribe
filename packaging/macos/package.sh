#!/usr/bin/env bash
set -euo pipefail

version="${1:-0.3.2}"
configuration="${CONFIGURATION:-Release}"
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
artifacts_root="${ARTIFACTS_DIR:-${repo_root}/artifacts}"
publish_dir="${artifacts_root}/publish/osx-arm64"
staging_dir="${artifacts_root}/staging/macos"
app_dir="${staging_dir}/Aviscribe.app"
contents_dir="${app_dir}/Contents"
package_dir="${artifacts_root}/packages"

rm -rf "${publish_dir}" "${staging_dir}"
mkdir -p "${publish_dir}" "${contents_dir}/MacOS" "${package_dir}"

dotnet publish "${repo_root}/src/Aviscribe.Desktop/Aviscribe.Desktop.csproj" \
  --configuration "${configuration}" \
  --runtime osx-arm64 \
  --self-contained true \
  --output "${publish_dir}" \
  -p:Version="${version}"

cp -R "${publish_dir}/." "${contents_dir}/MacOS/"
chmod +x "${contents_dir}/MacOS/Aviscribe"
sed "s/@VERSION@/${version}/g" \
  "${repo_root}/packaging/macos/Info.plist" \
  > "${contents_dir}/Info.plist"

signing_identity="${CODESIGN_IDENTITY:--}"
codesign --force --deep --sign "${signing_identity}" "${app_dir}"
codesign --verify --deep --strict "${app_dir}"
plutil -lint "${contents_dir}/Info.plist"

hdiutil create \
  -volname "Aviscribe" \
  -srcfolder "${app_dir}" \
  -ov \
  -format UDZO \
  "${package_dir}/Aviscribe-${version}-osx-arm64.dmg"
