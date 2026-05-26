#!/bin/bash
# VoxAssist Installer for Linux

set -e

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

echo -e "${BLUE}=======================================${NC}"
echo -e "${BLUE}     VoxAssist Installer for Linux     ${NC}"
echo -e "${BLUE}=======================================${NC}"

# Check for curl
if ! command -v curl &> /dev/null; then
    echo -e "${RED}Error: curl is not installed. Please install curl and try again.${NC}"
    exit 1
fi

INSTALL_DIR="$HOME/.local/share/voxassist"
mkdir -p "$INSTALL_DIR"

echo -e "Fetching latest release information from GitHub..."
RELEASE_JSON=$(curl -s https://api.github.com/repos/VoxAssist/VoxAssist-Desktop/releases/latest)

# Find AppImage download link
DOWNLOAD_URL=$(echo "$RELEASE_JSON" | grep "browser_download_url" | grep -i "AppImage" | cut -d '"' -f 4 | head -n 1)

if [ -z "$DOWNLOAD_URL" ]; then
    echo -e "${RED}Error: Could not find AppImage in the latest release assets.${NC}"
    exit 1
fi

APPIMAGE_PATH="$INSTALL_DIR/VoxAssist.AppImage"

echo -e "Downloading latest release..."
curl -L "$DOWNLOAD_URL" -o "$APPIMAGE_PATH"

echo -e "Making AppImage executable..."
chmod +x "$APPIMAGE_PATH"

# Check if FUSE 2 is missing
FUSE_WORKAROUND=""
if ! ldconfig -p 2>/dev/null | grep -q "libfuse.so.2"; then
    echo -e "${YELLOW}Notice: libfuse.so.2 is not detected on your system. Bypassing FUSE requirement...${NC}"
    FUSE_WORKAROUND="--appimage-extract-and-run"
fi

echo -e "${GREEN}Starting VoxAssist to register desktop shortcut and menu integration...${NC}"
# Launch in the background and disown to allow installer script to finish
"$APPIMAGE_PATH" $FUSE_WORKAROUND >/dev/null 2>&1 &
disown

echo -e "Waiting for initial integration setup..."
sleep 3

echo -e "${GREEN}=======================================${NC}"
echo -e "${GREEN}   VoxAssist Installed Successfully!   ${NC}"
echo -e "${GREEN}=======================================${NC}"
echo -e "- App Location: $APPIMAGE_PATH"
echo -e "- Desktop Menu Shortcut created at ~/.local/share/applications/voxassist.desktop"
echo -e "- You can launch VoxAssist from your Application Menu (KDE / GNOME)"
echo -e "======================================="
