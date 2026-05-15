#!/bin/bash
set -e

LAUNCHER_SRC="$1"
LAUNCHER_BIN="$2"
APP_BIN="$3"
# Resolve the actual user who invoked pkexec
REAL_USER=${PKEXEC_UID:-$(id -u)}
USER_HOME=$(getent passwd "$REAL_USER" | cut -d: -f6)
DESKTOP_FILE="$USER_HOME/.local/share/applications/voxassist.desktop"

echo "Compiling launcher..."
gcc "$LAUNCHER_SRC" -o "$LAUNCHER_BIN"

echo "Setting capabilities..."
setcap cap_sys_admin,cap_sys_rawio,cap_dac_override+ep "$LAUNCHER_BIN"

if [ -f "$DESKTOP_FILE" ]; then
    echo "Updating desktop entry at $DESKTOP_FILE..."
    # Update Name to include version and Exec to use launcher
    sed -i "s|^Name=.*|Name=VoxAssist|" "$DESKTOP_FILE"
    sed -i "s|^Exec=.*|Exec=$LAUNCHER_BIN $APP_BIN|" "$DESKTOP_FILE"
fi

echo "Setup complete."
