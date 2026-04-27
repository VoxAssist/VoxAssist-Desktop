#!/bin/bash
set -e

LAUNCHER_SRC="$1"
LAUNCHER_BIN="$2"
APP_BIN="$3"
DESKTOP_FILE="$HOME/.local/share/applications/voxassist.desktop"

echo "Compiling launcher..."
gcc "$LAUNCHER_SRC" -o "$LAUNCHER_BIN"

echo "Setting capabilities..."
setcap cap_sys_admin,cap_sys_rawio,cap_dac_override+ep "$LAUNCHER_BIN"

if [ -f "$DESKTOP_FILE" ]; then
    echo "Updating desktop entry..."
    # Use | as separator to avoid issues with / in paths
    sed -i "s|Exec=.*|Exec=$LAUNCHER_BIN $APP_BIN|" "$DESKTOP_FILE"
fi

echo "Setup complete."
