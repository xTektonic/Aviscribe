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
icon_source="${repo_root}/src/Aviscribe.UI/Assets/aviscribe-icon.png"
iconset_dir="${staging_dir}/Aviscribe.iconset"
icon_file="${contents_dir}/Resources/Aviscribe.icns"

rm -rf "${publish_dir}" "${staging_dir}"
mkdir -p \
  "${publish_dir}" \
  "${contents_dir}/MacOS" \
  "${contents_dir}/Resources" \
  "${package_dir}"
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

mkdir -p "${iconset_dir}"
while read -r pixels filename; do
  sips --resampleHeightWidth "${pixels}" "${pixels}" \
    "${icon_source}" \
    --out "${iconset_dir}/${filename}" \
    >/dev/null
done <<'EOF'
16 icon_16x16.png
32 icon_16x16@2x.png
32 icon_32x32.png
64 icon_32x32@2x.png
128 icon_128x128.png
256 icon_128x128@2x.png
256 icon_256x256.png
512 icon_256x256@2x.png
512 icon_512x512.png
1024 icon_512x512@2x.png
EOF
iconutil --convert icns --output "${icon_file}" "${iconset_dir}"

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
