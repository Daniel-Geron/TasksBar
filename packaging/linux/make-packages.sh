#!/bin/bash
# Builds the Linux release packages from a self-contained build of TasksBar.Linux:
#   tasksbar_<version>_amd64.deb       for Ubuntu, Debian, Mint, Pop!_OS and KDE neon
#   tasksbar-<version>-linux-x64.tar.gz for every other distribution
#
# Usage: make-packages.sh <publish folder> <version> [output folder]
# The publish folder comes from (on Windows or Linux):
#   dotnet publish TasksBar.Linux -c Release -r linux-x64 --self-contained -o publish/linux-x64
# Needs dpkg-deb for the .deb (any Debian/Ubuntu system or WSL has it).
set -euo pipefail

PUBLISH="${1:?publish folder}"
VERSION="${2:?version, e.g. 1.3.0}"
OUT="${3:-.}"
HERE="$(cd "$(dirname "$0")" && pwd)"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
mkdir -p "$OUT"
OUT="$(cd "$OUT" && pwd)"

# Never ship personal data that may sit next to a development build
EXCLUDES=(--exclude='config.json' --exclude='token.json' --exclude='credentials.json' --exclude='local_*.json'
          --exclude='note_images' --exclude='*.pdb' --exclude='*.dbg')

# Builds copied from a Windows drive mark every file executable; only the programs should be
fix_permissions() {
  find "$1" -type d -exec chmod 755 {} +
  find "$1" -type f -exec chmod 644 {} +
  chmod 755 "$1/tasksbar" "$1/createdump" 2>/dev/null || true
}

# ------------------------------------------------------------------ .deb
PKG="$WORK/deb"
mkdir -p "$PKG/DEBIAN" "$PKG/opt/tasksbar" "$PKG/usr/bin" "$PKG/usr/share/applications"
tar -C "$PUBLISH" "${EXCLUDES[@]}" -cf - . | tar -C "$PKG/opt/tasksbar" -xf -
fix_permissions "$PKG/opt/tasksbar"
ln -s /opt/tasksbar/tasksbar "$PKG/usr/bin/tasksbar"
install -m 644 "$HERE/tasksbar.desktop" "$PKG/usr/share/applications/tasksbar.desktop"
for size in 48 64 128 256; do
  install -D -m 644 "$HERE/icons/tasksbar-$size.png" "$PKG/usr/share/icons/hicolor/${size}x${size}/apps/tasksbar.png"
done

SIZE_KB=$(du -sk "$PKG" | cut -f1)
cat > "$PKG/DEBIAN/control" <<EOF
Package: tasksbar
Version: $VERSION
Architecture: amd64
Maintainer: Daniel Geron <Daniel-Geron@users.noreply.github.com>
Installed-Size: $SIZE_KB
Depends: libc6, libx11-6, libice6, libsm6, libfontconfig1
Recommends: pipewire-bin | pulseaudio-utils
Suggests: gnome-shell-extension-appindicator
Section: utils
Priority: optional
Homepage: https://github.com/Daniel-Geron/TasksBar
Description: Tasks and sticky notes in your system tray
 TasksBar is a lightweight tasks widget that lives in the system tray, with
 two-way sync for Google Tasks, Tasks.org and other CalDAV servers, and sticky
 notes with drawings and images that sync between Linux and Windows.
EOF

# Refresh the app menu and icons after installing or removing
for script in postinst postrm; do
  cat > "$PKG/DEBIAN/$script" <<'EOF'
#!/bin/sh
set -e
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database -q /usr/share/applications || true
command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -q -t /usr/share/icons/hicolor || true
exit 0
EOF
  chmod 755 "$PKG/DEBIAN/$script"
done

if command -v dpkg-deb >/dev/null 2>&1; then
  dpkg-deb --root-owner-group --build "$PKG" "$OUT/tasksbar_${VERSION}_amd64.deb" >/dev/null
  echo "built $OUT/tasksbar_${VERSION}_amd64.deb"
else
  echo "dpkg-deb not found: skipping the .deb" >&2
fi

# ------------------------------------------------------------------ .tar.gz
TAR="$WORK/tar/tasksbar"
mkdir -p "$TAR"
tar -C "$PUBLISH" "${EXCLUDES[@]}" -cf - . | tar -C "$TAR" -xf -
fix_permissions "$TAR"
install -m 644 "$HERE/icons/tasksbar-256.png" "$TAR/tasksbar.png"
install -m 755 "$HERE/install.sh" "$TAR/install.sh"
tar -C "$WORK/tar" --owner=0 --group=0 -czf "$OUT/tasksbar-${VERSION}-linux-x64.tar.gz" tasksbar
echo "built $OUT/tasksbar-${VERSION}-linux-x64.tar.gz"
