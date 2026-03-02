#!/usr/bin/env bash
# create-appimage.sh — Build a Linux x64 AppImage from dotnet publish output
#
# Usage:
#   bash installer/linux/create-appimage.sh <version> [output-dir]
#
#   version    : semver string, e.g. 0.1.0
#   output-dir : directory for the .AppImage (default: dist/)
#
# Requires: running on a Linux x64 host (CI: ubuntu-latest)
#
# Note: arm64 AppImage is not produced — arm64 Linux users use the tar.gz portable.

set -euo pipefail

VERSION="${1:?Usage: $0 <version> [output-dir]}"
OUTDIR="${2:-dist}"

PUBLISH_DIR="src/NexusMonitor.UI/publish/linux-x64"
APPDIR="${OUTDIR}/NexusMonitor.AppDir"
APPIMAGE_OUT="${OUTDIR}/NexusMonitor-${VERSION}-linux-x64.AppImage"
APPIMAGETOOL_URL="https://github.com/AppImage/AppImageKit/releases/download/continuous/appimagetool-x86_64.AppImage"
APPIMAGETOOL="${OUTDIR}/appimagetool-x86_64.AppImage"

# ── Validate publish output ──────────────────────────────────────────────────
if [[ ! -f "${PUBLISH_DIR}/NexusMonitor" ]]; then
  echo "Error: publish output not found at ${PUBLISH_DIR}/NexusMonitor"
  echo "Run: dotnet publish src/NexusMonitor.UI/NexusMonitor.UI.csproj /p:PublishProfile=linux-x64"
  exit 1
fi

mkdir -p "${OUTDIR}"

# ── Download appimagetool if not present ──────────────────────────────────────
if [[ ! -f "${APPIMAGETOOL}" ]]; then
  echo "→ Downloading appimagetool ..."
  curl -fsSL -o "${APPIMAGETOOL}" "${APPIMAGETOOL_URL}"
  chmod +x "${APPIMAGETOOL}"
fi

# Allow running AppImage without FUSE (extract-and-run fallback)
export APPIMAGE_EXTRACT_AND_RUN=1

# ── Build AppDir structure ───────────────────────────────────────────────────
echo "→ Building AppDir at ${APPDIR} ..."
rm -rf "${APPDIR}"
mkdir -p "${APPDIR}/usr/bin"
mkdir -p "${APPDIR}/usr/share/applications"
mkdir -p "${APPDIR}/usr/share/icons/hicolor/256x256/apps"

# Copy all publish output into usr/bin/
cp -R "${PUBLISH_DIR}/." "${APPDIR}/usr/bin/"

# Ensure main executable is marked executable
chmod +x "${APPDIR}/usr/bin/NexusMonitor"

# AppImage required files at root
cp installer/linux/AppRun "${APPDIR}/AppRun"
chmod +x "${APPDIR}/AppRun"

# .desktop file (AppImageKit requires one at the AppDir root)
cp installer/linux/NexusMonitor.desktop "${APPDIR}/NexusMonitor.desktop"
cp installer/linux/NexusMonitor.desktop "${APPDIR}/usr/share/applications/"

# Icon (AppImageKit looks for <icon-name>.png at root, same name as the Icon= in .desktop)
cp src/NexusMonitor.UI/Assets/nexus-icon-256.png "${APPDIR}/nexus-monitor.png"
cp src/NexusMonitor.UI/Assets/nexus-icon-256.png \
  "${APPDIR}/usr/share/icons/hicolor/256x256/apps/nexus-monitor.png"

# ── Build AppImage ────────────────────────────────────────────────────────────
echo "→ Building ${APPIMAGE_OUT} ..."
rm -f "${APPIMAGE_OUT}"
"${APPIMAGETOOL}" "${APPDIR}" "${APPIMAGE_OUT}"
chmod +x "${APPIMAGE_OUT}"

echo "  ✓ AppImage: ${APPIMAGE_OUT}"

# ── Clean up build intermediates ──────────────────────────────────────────────
rm -rf "${APPDIR}"
rm -f "${APPIMAGETOOL}"
