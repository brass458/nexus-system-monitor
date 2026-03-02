#!/usr/bin/env bash
# create-app-bundle.sh — Assemble a macOS .app bundle and .dmg from dotnet publish output
#
# Usage:
#   bash installer/macos/create-app-bundle.sh <rid> <version> [output-dir]
#
#   rid         : osx-x64 or osx-arm64
#   version     : semver string, e.g. 0.1.0
#   output-dir  : directory for .app and .dmg (default: dist/)
#
# Requires (CI: macos-latest runner):
#   brew install create-dmg
#
# Example:
#   bash installer/macos/create-app-bundle.sh osx-arm64 0.1.0 dist

set -euo pipefail

RID="${1:?Usage: $0 <rid> <version> [output-dir]}"
VERSION="${2:?Version is required}"
OUTDIR="${3:-dist}"

# Derive arch label for output filenames (osx-arm64 → arm64, osx-x64 → x64)
ARCH="${RID#osx-}"

PUBLISH_DIR="src/NexusMonitor.UI/publish/${RID}"
APP_NAME="NexusMonitor"
APP_BUNDLE="${OUTDIR}/${APP_NAME}.app"
DMG_NAME="${APP_NAME}-${VERSION}-${RID}.dmg"
DMG_PATH="${OUTDIR}/${DMG_NAME}"

# ── Validate publish output exists ──────────────────────────────────────────
if [[ ! -f "${PUBLISH_DIR}/NexusMonitor" ]]; then
  echo "Error: publish output not found at ${PUBLISH_DIR}/NexusMonitor"
  echo "Run: dotnet publish src/NexusMonitor.UI/NexusMonitor.UI.csproj /p:PublishProfile=${RID}"
  exit 1
fi

mkdir -p "${OUTDIR}"

# ── Remove previous .app if present ─────────────────────────────────────────
rm -rf "${APP_BUNDLE}"

# ── Build .app bundle structure ──────────────────────────────────────────────
echo "→ Building ${APP_BUNDLE} ..."
mkdir -p "${APP_BUNDLE}/Contents/MacOS"
mkdir -p "${APP_BUNDLE}/Contents/Resources"

# Copy all publish output into MacOS/
cp -R "${PUBLISH_DIR}/." "${APP_BUNDLE}/Contents/MacOS/"

# Substitute version into Info.plist
sed "s/VERSION_PLACEHOLDER/${VERSION}/g" \
  installer/macos/Info.plist > "${APP_BUNDLE}/Contents/Info.plist"

# Copy .icns icon
cp src/NexusMonitor.UI/Assets/nexus-icon.icns \
  "${APP_BUNDLE}/Contents/Resources/nexus-icon.icns"

# Ensure main executable is marked executable
chmod +x "${APP_BUNDLE}/Contents/MacOS/NexusMonitor"

echo "  ✓ App bundle created: ${APP_BUNDLE}"

# ── Create .dmg with drag-to-Applications layout ─────────────────────────────
echo "→ Creating ${DMG_PATH} ..."

# Remove previous dmg if present
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
  "${APP_BUNDLE}"

echo "  ✓ DMG created: ${DMG_PATH}"

# ── Create portable tar.gz as well ───────────────────────────────────────────
TARBALL="${OUTDIR}/${APP_NAME}-${VERSION}-${RID}.tar.gz"
echo "→ Creating ${TARBALL} ..."
tar -czf "${TARBALL}" -C "${PUBLISH_DIR}" .
echo "  ✓ Portable archive: ${TARBALL}"
