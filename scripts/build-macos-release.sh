#!/bin/zsh

set -euo pipefail

VERSION="${1:-0.1.0}"
SCRIPT_DIR="${0:A:h}"
REPO_ROOT="${SCRIPT_DIR:h}"
PROJECT_PATH="${REPO_ROOT}/src/PowerGauge/PowerGauge.csproj"
PUBLISH_PROFILE="MacOsArm64"
APP_BUNDLE_PATH="${REPO_ROOT}/artifacts/package/macos/PowerGauge.app"
RELEASE_DIR="${REPO_ROOT}/artifacts/release"
RELEASE_ZIP="${RELEASE_DIR}/PowerGauge-macos-arm64-${VERSION}.zip"

print "Publishing macOS arm64 build ${VERSION}..."
dotnet publish "${PROJECT_PATH}" -p:PublishProfile="${PUBLISH_PROFILE}" -p:Version="${VERSION}" -p:FileVersion="${VERSION}" -p:InformationalVersion="${VERSION}"

print "Assembling PowerGauge.app..."
zsh "${SCRIPT_DIR}/build-macos-app.sh" "${VERSION}"

print "Removing extended attributes from app bundle..."
xattr -cr "${APP_BUNDLE_PATH}"

mkdir -p "${RELEASE_DIR}"
rm -f "${RELEASE_ZIP}"

print "Creating release zip ${RELEASE_ZIP}..."
ditto -c -k --norsrc --keepParent "${APP_BUNDLE_PATH}" "${RELEASE_ZIP}"

print "Created macOS release artifact at ${RELEASE_ZIP}"
