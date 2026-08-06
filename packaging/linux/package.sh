#!/usr/bin/env bash
set -euo pipefail

version="${1:-0.5.0}"
configuration="${CONFIGURATION:-Release}"
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
artifacts_root="${ARTIFACTS_DIR:-${repo_root}/artifacts}"
publish_dir="${artifacts_root}/publish/linux-x64"
staging_dir="${artifacts_root}/staging/linux"
deb_root="${staging_dir}/deb"
app_dir="${staging_dir}/Aviscribe.AppDir"
package_dir="${artifacts_root}/packages"

rm -rf "${publish_dir}" "${staging_dir}"
mkdir -p "${publish_dir}" "${package_dir}"

dotnet publish "${repo_root}/src/Aviscribe.Desktop/Aviscribe.Desktop.csproj" \
  --configuration "${configuration}" \
  --runtime linux-x64 \
  --self-contained true \
  --output "${publish_dir}" \
  -p:Version="${version}"

mkdir -p \
  "${deb_root}/DEBIAN" \
  "${deb_root}/usr/bin" \
  "${deb_root}/usr/lib/aviscribe" \
  "${deb_root}/usr/share/applications" \
  "${deb_root}/usr/share/icons/hicolor/256x256/apps"
cp -a "${publish_dir}/." "${deb_root}/usr/lib/aviscribe/"
ln -s "../lib/aviscribe/Aviscribe" "${deb_root}/usr/bin/Aviscribe"
ln -s "Aviscribe" "${deb_root}/usr/bin/aviscribe"
cp "${repo_root}/packaging/linux/io.github.xtektonic.aviscribe.desktop" \
  "${deb_root}/usr/share/applications/"
cp "${repo_root}/packaging/linux/aviscribe.png" \
  "${deb_root}/usr/share/icons/hicolor/256x256/apps/aviscribe.png"

installed_size="$(du -sk "${deb_root}/usr" | cut -f1)"
sed \
  -e "s/@VERSION@/${version}/g" \
  -e "s/@INSTALLED_SIZE@/${installed_size}/g" \
  "${repo_root}/packaging/linux/control" \
  > "${deb_root}/DEBIAN/control"
chmod 0755 "${deb_root}/usr/lib/aviscribe/Aviscribe"
dpkg-deb --root-owner-group --build \
  "${deb_root}" \
  "${package_dir}/aviscribe_${version}_amd64.deb"

mkdir -p "${app_dir}/usr/lib/aviscribe"
cp -a "${publish_dir}/." "${app_dir}/usr/lib/aviscribe/"
cp "${repo_root}/packaging/linux/AppRun" "${app_dir}/AppRun"
cp "${repo_root}/packaging/linux/io.github.xtektonic.aviscribe.desktop" \
  "${app_dir}/io.github.xtektonic.aviscribe.desktop"
cp "${repo_root}/packaging/linux/aviscribe.png" "${app_dir}/aviscribe.png"
chmod 0755 "${app_dir}/AppRun" "${app_dir}/usr/lib/aviscribe/Aviscribe"

appimage_tool="${APPIMAGETOOL:-${artifacts_root}/tools/appimagetool-x86_64.AppImage}"
if [[ ! -x "${appimage_tool}" ]]; then
  mkdir -p "$(dirname "${appimage_tool}")"
  curl --fail --location \
    "https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage" \
    --output "${appimage_tool}"
  chmod 0755 "${appimage_tool}"
fi

ARCH=x86_64 APPIMAGE_EXTRACT_AND_RUN=1 "${appimage_tool}" \
  "${app_dir}" \
  "${package_dir}/Aviscribe-${version}-x86_64.AppImage"
