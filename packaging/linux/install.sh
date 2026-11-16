#!/bin/bash
# Adds TasksBar (from this unpacked folder) to your app menu, for your user only. No root needed.
# Run it again after moving the folder. To remove: ./install.sh --uninstall
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
DATA="${XDG_DATA_HOME:-$HOME/.local/share}"
APPS="$DATA/applications"
ICONS="$DATA/icons/hicolor/256x256/apps"
BIN="$HOME/.local/bin"

if [ "${1:-}" = "--uninstall" ]; then
  rm -f "$APPS/tasksbar.desktop" "$ICONS/tasksbar.png" "$BIN/tasksbar"
  echo "TasksBar was removed from your app menu. Your tasks and notes stay in ~/.config/TasksBar."
  exit 0
fi

mkdir -p "$APPS" "$ICONS" "$BIN"
install -m 644 "$HERE/tasksbar.png" "$ICONS/tasksbar.png"
ln -sf "$HERE/tasksbar" "$BIN/tasksbar"

EXEC="\"$HERE/tasksbar\""
cat > "$APPS/tasksbar.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=TasksBar
GenericName=Tasks and Sticky Notes
Comment=Your Google Tasks / Tasks.org tasks and sticky notes, one click away
Exec=$EXEC
Icon=tasksbar
Terminal=false
Categories=Utility;Office;
StartupNotify=false
Actions=NewNote;OpenNotes;

[Desktop Action NewNote]
Name=New Sticky Note
Exec=$EXEC --new-note

[Desktop Action OpenNotes]
Name=Open All Sticky Notes
Exec=$EXEC --open-notes
EOF

command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database -q "$APPS" || true
echo "TasksBar is in your app menu now (and runs as 'tasksbar' if ~/.local/bin is on your PATH)."
