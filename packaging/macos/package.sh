#!/usr/bin/env bash
set -euo pipefail

version="${1:-1.1.0}"
configuration="${CONFIGURATION:-Release}"
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
artifacts_root="${ARTIFACTS_DIR:-${repo_root}/artifacts}"
publish_dir="${artifacts_root}/publish/osx-arm64"
staging_dir="${artifacts_root}/staging/macos"
app_dir="${staging_dir}/Aviscribe.app"
contents_dir="${app_dir}/Contents"
package_dir="${artifacts_root}/packages"
portable_dir="${staging_dir}/portable"

rm -rf "${publish_dir}" "${staging_dir}"
mkdir -p "${publish_dir}" "${contents_dir}/MacOS" "${package_dir}"
find "${package_dir}" -maxdepth 1 -type f \( \
  -name "*${version}*" -o \
  -name '*.dmg' -o \
  -name '*-Portable.zip' -o \
  -name 'releases.osx.json' -o \
  -name 'assets.osx.json' \
\) -delete

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

dotnet tool restore
dotnet tool run vpk -- pack \
  --packId io.github.xtektonic.aviscribe \
  --packVersion "${version}" \
  --packDir "${app_dir}" \
  --mainExe Aviscribe \
  --packTitle Aviscribe \
  --packAuthors xTektonic \
  --runtime osx-arm64 \
  --channel osx \
  --outputDir "${package_dir}" \
  --signAppIdentity "${signing_identity}" \
  --noInst

find "${package_dir}" -maxdepth 1 -name '*.nupkg' ! -name "*${version}*" -delete

portable_zip="$(find "${package_dir}" -maxdepth 1 -name '*-Portable.zip' -print -quit)"
test -n "${portable_zip}"
mkdir -p "${portable_dir}"
ditto -x -k "${portable_zip}" "${portable_dir}"
packaged_app="$(find "${portable_dir}" -maxdepth 2 -name 'Aviscribe.app' -type d -print -quit)"
test -n "${packaged_app}"
codesign --verify --deep --strict "${packaged_app}"

dmg_root="${staging_dir}/dmg"
mkdir -p "${dmg_root}"
cp -R "${packaged_app}" "${dmg_root}/Aviscribe.app"
ln -s /Applications "${dmg_root}/Applications"
hdiutil create \
  -volname "Aviscribe" \
  -srcfolder "${dmg_root}" \
  -ov \
  -format UDZO \
  "${package_dir}/Aviscribe-${version}-osx-arm64.dmg"
