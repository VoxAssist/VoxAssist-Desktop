#!/bin/bash
set -e

LAUNCHER_SRC="$1"
LAUNCHER_BIN="$2"
APP_BIN="$3"

# Resolve the actual user who invoked pkexec
REAL_USER=${PKEXEC_UID:-$(id -u)}
USER_HOME=$(getent passwd "$REAL_USER" | cut -d: -f6)

VOXASSIST_DIR="$USER_HOME/.local/share/voxassist"
DESKTOP_FILE="$USER_HOME/.local/share/applications/voxassist.desktop"

echo "Creating directory $VOXASSIST_DIR..."
mkdir -p "$VOXASSIST_DIR"

echo "Compiling launcher..."
gcc "$LAUNCHER_SRC" -o "$LAUNCHER_BIN"

echo "Setting ownership to user $REAL_USER..."
chown -R "$REAL_USER:$REAL_USER" "$VOXASSIST_DIR"
chmod 755 "$LAUNCHER_BIN"

echo "Setting capabilities..."
setcap cap_sys_admin,cap_sys_rawio,cap_dac_override+ep "$LAUNCHER_BIN"

# If a desktop file exists, update it to point to the new launcher
if [ -f "$DESKTOP_FILE" ]; then
    echo "Updating desktop entry at $DESKTOP_FILE..."
    # Update Exec to use the new launcher
    sed -i "s|^Exec=.*|Exec=$LAUNCHER_BIN $APP_BIN|" "$DESKTOP_FILE"
    chown "$REAL_USER:$REAL_USER" "$DESKTOP_FILE"
fi

echo "Setup complete."
