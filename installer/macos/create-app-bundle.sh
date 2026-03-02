#!/usr/bin/env bash
# create-app-bundle.sh — Package macOS .app (built by the .NET macOS workload) into .dmg and .tar.gz
#
# The .NET 8 macOS workload (Xamarin.Shared.Sdk.targets) auto-creates a .app bundle
# and .pkg installer in the publish output directory when PublishTrimmed=true.
# This script locates that .app, creates a .dmg with a drag-to-Applications layout,
# and creates a portable .tar.gz.
#
# Usage:
#   bash installer/macos/create-app-bundle.sh <rid> <version> [output-dir]
#
#   rid         : osx-x64 or osx-arm64
#   version     : semver string, e.g. 0.1.0
#   output-dir  : directory for .dmg and .tar.gz (default: dist/)
#
# Requires (CI: macos-latest runner):
#   brew install create-dmg

set -euo pipefail

RID="${1:?Usage: $0 <rid> <version> [output-dir]}"
VERSION="${2:?Version is required}"
OUTDIR="${3:-dist}"

PUBLISH_DIR="src/NexusMonitor.UI/publish/${RID}"
APP_NAME="NexusMonitor"
DMG_NAME="${APP_NAME}-${VERSION}-${RID}.dmg"
DMG_PATH="${OUTDIR}/${DMG_NAME}"
TARBALL="${OUTDIR}/${APP_NAME}-${VERSION}-${RID}.tar.gz"

# ── Locate the .app bundle produced by the macOS workload ────────────────────
# The SDK places the .app in the PublishDir alongside the .pkg installer.
# Try alternate locations as fallback.
APP_SRC=""
if [[ -d "${PUBLISH_DIR}/${APP_NAME}.app" ]]; then
  APP_SRC="${PUBLISH_DIR}/${APP_NAME}.app"
elif [[ -d "src/NexusMonitor.UI/bin/Release/net8.0-macos/${RID}/${APP_NAME}.app" ]]; then
  APP_SRC="src/NexusMonitor.UI/bin/Release/net8.0-macos/${RID}/${APP_NAME}.app"
else
  echo "Error: ${APP_NAME}.app not found."
  echo "Searched:"
  echo "  ${PUBLISH_DIR}/${APP_NAME}.app"
  echo "  src/NexusMonitor.UI/bin/Release/net8.0-macos/${RID}/${APP_NAME}.app"
  echo ""
  echo "Actual contents of ${PUBLISH_DIR}:"
  find "${PUBLISH_DIR}" -maxdepth 3 2>/dev/null || echo "(directory not found)"
  exit 1
fi

mkdir -p "${OUTDIR}"

echo "→ Found app bundle: ${APP_SRC}"

# ── Create .dmg with drag-to-Applications layout ─────────────────────────────
echo "→ Creating ${DMG_PATH} ..."
rm -f "${DMG_PATH}"

create-dmg \
  --volname "Nexus System Monitor ${VERSION}" \
  --volicon "src/NexusMonitor.UI/Assets/nexus-icon.icns" \
  --window-pos 200 120 \
  --window-size 580 380 \
  --icon-size 100 \
  --icon "${APP_NAME}.app" 145 190 \
  --hide-extension "${APP_NAME}.app" \
  --app-drop-link 430 190 \
  "${DMG_PATH}" \
  "${APP_SRC}"

echo "  ✓ DMG created: ${DMG_PATH}"

# ── Create portable tar.gz of the .app bundle ────────────────────────────────
echo "→ Creating ${TARBALL} ..."
tar -czf "${TARBALL}" -C "$(dirname "${APP_SRC}")" "${APP_NAME}.app"
echo "  ✓ Portable archive: ${TARBALL}"
