#!/usr/bin/env bash
set -euo pipefail

version="${1:-1.1.0}"
configuration="${CONFIGURATION:-Release}"
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
artifacts_root="${ARTIFACTS_DIR:-${repo_root}/artifacts}"
publish_dir="${artifacts_root}/publish/linux-x64"
package_dir="${artifacts_root}/packages"

rm -rf "${publish_dir}"
mkdir -p "${publish_dir}" "${package_dir}"
find "${package_dir}" -maxdepth 1 -type f \( \
  -name "*${version}*" -o \
  -name '*.AppImage' -o \
  -name 'releases.linux.json' -o \
  -name 'assets.linux.json' \
\) -delete

dotnet publish "${repo_root}/src/Aviscribe.Desktop/Aviscribe.Desktop.csproj" \
  --configuration "${configuration}" \
  --runtime linux-x64 \
  --self-contained true \
  --output "${publish_dir}" \
  -p:Version="${version}"

chmod 0755 "${publish_dir}/Aviscribe"
dotnet tool restore
dotnet tool run vpk -- pack \
  --packId io.github.xtektonic.aviscribe \
  --packVersion "${version}" \
  --packDir "${publish_dir}" \
  --mainExe Aviscribe \
  --packTitle Aviscribe \
  --packAuthors xTektonic \
  --runtime linux-x64 \
  --channel linux \
  --icon "${repo_root}/packaging/linux/aviscribe.png" \
  --categories Utility \
  --outputDir "${package_dir}"

find "${package_dir}" -maxdepth 1 -name '*.nupkg' ! -name "*${version}*" -delete
generated_appimage="$(find "${package_dir}" -maxdepth 1 -name '*.AppImage' -print -quit)"
test -n "${generated_appimage}"
release_appimage="${package_dir}/Aviscribe-${version}-x86_64.AppImage"
if [[ "${generated_appimage}" != "${release_appimage}" ]]; then
  mv -f "${generated_appimage}" "${release_appimage}"
fi
