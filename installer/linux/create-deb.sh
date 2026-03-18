#!/usr/bin/env bash
# create-deb.sh — Build a Debian/Ubuntu .deb package from dotnet publish output
#
# Usage:
#   bash installer/linux/create-deb.sh <version> [output-dir]
#
#   version    : semver string, e.g. 0.1.0
#   output-dir : directory for the .deb (default: dist/)
#
# Requires: dpkg-deb (available on Ubuntu/Debian runners natively)
# Note: Only x64 (amd64) — arm64 users use the tar.gz portable.

set -euo pipefail

VERSION="${1:?Usage: $0 <version> [output-dir]}"
OUTDIR="${2:-dist}"

PUBLISH_DIR="src/NexusMonitor.UI/publish/linux-x64"
PKG_NAME="nexus-monitor"
PKG_DIR="${OUTDIR}/${PKG_NAME}_${VERSION}_amd64"
DEB_OUT="${OUTDIR}/NexusMonitor-Linux-${VERSION}.deb"
INSTALL_DIR="${PKG_DIR}/usr/lib/nexus-monitor"

# ── Validate publish output ──────────────────────────────────────────────────
if [[ ! -f "${PUBLISH_DIR}/NexusMonitor" ]]; then
  echo "Error: publish output not found at ${PUBLISH_DIR}/NexusMonitor"
  echo "Run: dotnet publish src/NexusMonitor.UI/NexusMonitor.UI.csproj /p:PublishProfile=linux-x64"
  exit 1
fi

mkdir -p "${OUTDIR}"

# ── Build package directory structure ────────────────────────────────────────
echo "→ Building .deb package structure at ${PKG_DIR} ..."
rm -rf "${PKG_DIR}"

mkdir -p "${PKG_DIR}/DEBIAN"
mkdir -p "${INSTALL_DIR}"
mkdir -p "${PKG_DIR}/usr/bin"
mkdir -p "${PKG_DIR}/usr/share/applications"
mkdir -p "${PKG_DIR}/usr/share/icons/hicolor/16x16/apps"
mkdir -p "${PKG_DIR}/usr/share/icons/hicolor/32x32/apps"
mkdir -p "${PKG_DIR}/usr/share/icons/hicolor/48x48/apps"
mkdir -p "${PKG_DIR}/usr/share/icons/hicolor/64x64/apps"
mkdir -p "${PKG_DIR}/usr/share/icons/hicolor/128x128/apps"
mkdir -p "${PKG_DIR}/usr/share/icons/hicolor/256x256/apps"
mkdir -p "${PKG_DIR}/usr/share/icons/hicolor/512x512/apps"

# Copy publish output
cp -R "${PUBLISH_DIR}/." "${INSTALL_DIR}/"
chmod +x "${INSTALL_DIR}/NexusMonitor"

# Symlink in /usr/bin
ln -s "/usr/lib/nexus-monitor/NexusMonitor" "${PKG_DIR}/usr/bin/nexus-monitor"

# .desktop file
cat > "${PKG_DIR}/usr/share/applications/nexus-monitor.desktop" << 'EOF'
[Desktop Entry]
Version=1.0
Type=Application
Name=Nexus System Monitor
GenericName=System Monitor
Comment=Cross-platform system monitoring and process management
Exec=/usr/lib/nexus-monitor/NexusMonitor
Icon=nexus-monitor
Terminal=false
Categories=System;Monitor;
Keywords=system;monitor;process;cpu;memory;disk;network;gpu;
StartupWMClass=NexusMonitor
StartupNotify=true
EOF

# Icons at multiple resolutions
for RES in 16 32 48 64 128 256 512; do
  SRC="src/NexusMonitor.UI/Assets/nexus-icon-${RES}.png"
  DST="${PKG_DIR}/usr/share/icons/hicolor/${RES}x${RES}/apps/nexus-monitor.png"
  if [[ -f "${SRC}" ]]; then
    cp "${SRC}" "${DST}"
  fi
done

# ── DEBIAN/control ────────────────────────────────────────────────────────────
INSTALLED_SIZE=$(du -sk "${INSTALL_DIR}" | cut -f1)

cat > "${PKG_DIR}/DEBIAN/control" << EOF
Package: ${PKG_NAME}
Version: ${VERSION}
Section: utils
Priority: optional
Architecture: amd64
Installed-Size: ${INSTALLED_SIZE}
Depends: libx11-6, libfontconfig1, libfreetype6, libicu-dev | libicu72 | libicu74
Maintainer: TheBlackSwordsman <brass458@users.noreply.github.com>
Homepage: https://github.com/brass458/nexus-system-monitor
Description: Cross-platform system monitor
 Nexus System Monitor provides real-time system insight across
 Windows, macOS, and Linux. Features include CPU, memory, disk,
 network and GPU metrics, process management, service control,
 startup management, anomaly detection, Prometheus metrics export,
 and a built-in Grafana integration guide.
EOF

# ── DEBIAN/postinst — update icon cache ──────────────────────────────────────
cat > "${PKG_DIR}/DEBIAN/postinst" << 'EOF'
#!/bin/sh
set -e
if command -v gtk-update-icon-cache >/dev/null 2>&1; then
  gtk-update-icon-cache -f -t /usr/share/icons/hicolor || true
fi
if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database /usr/share/applications || true
fi
EOF
chmod 755 "${PKG_DIR}/DEBIAN/postinst"

# ── DEBIAN/postrm — clean up icon cache ──────────────────────────────────────
cat > "${PKG_DIR}/DEBIAN/postrm" << 'EOF'
#!/bin/sh
set -e
if command -v gtk-update-icon-cache >/dev/null 2>&1; then
  gtk-update-icon-cache -f -t /usr/share/icons/hicolor || true
fi
if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database /usr/share/applications || true
fi
EOF
chmod 755 "${PKG_DIR}/DEBIAN/postrm"

# ── Build .deb ────────────────────────────────────────────────────────────────
echo "→ Building ${DEB_OUT} ..."
rm -f "${DEB_OUT}"
dpkg-deb --build --root-owner-group "${PKG_DIR}" "${DEB_OUT}"
echo "  ✓ .deb: ${DEB_OUT}"

# Cleanup staging directory
rm -rf "${PKG_DIR}"
