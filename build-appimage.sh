#!/bin/bash
set -e

# 1. Define variables
APP_NAME="VoxAssist"
BINARY_NAME="VoxAssist.Desktop"
APP_DIR="AppDir"
OUT_DIR="publish_appimage"

# 2. Clean up previous builds
rm -rf $APP_DIR
rm -rf $OUT_DIR
rm -f ${APP_NAME}-x86_64.AppImage

# 3. Publish the .NET application
# We don't use PublishSingleFile=true here because AppImage handles the bundling better
dotnet publish -c Release -r linux-x64 --self-contained true -o $OUT_DIR

# 4. Create AppDir structure
mkdir -p $APP_DIR/usr/bin
mkdir -p $APP_DIR/usr/share/icons/hicolor/256x256/apps
mkdir -p $APP_DIR/usr/share/applications

# 5. Copy files
cp -r $OUT_DIR/* $APP_DIR/usr/bin/
cp Assets/avalonia-logo.ico $APP_DIR/voxassist.ico # Fallback
cp Assets/avalonia-logo.ico $APP_DIR/voxassist.png # Fake it as png for tool compatibility

# 6. Create Desktop file
cat > $APP_DIR/voxassist.desktop <<EOF
[Desktop Entry]
Name=${APP_NAME}
Exec=voxassist
Icon=voxassist
Type=Application
Categories=Utility;
Terminal=false
Comment=Voice Assistant with AI capabilities
EOF

# 7. Create AppRun script (required by AppImage)
cat > $APP_DIR/AppRun <<EOF
#!/bin/sh
HERE="\$(dirname "\$(readlink -f "\$0")")"
export LD_LIBRARY_PATH="\$HERE/usr/bin:\$LD_LIBRARY_PATH"
exec "\$HERE/usr/bin/${BINARY_NAME}" "\$@"
EOF
chmod +x $APP_DIR/AppRun

# 8. Download appimagetool if not present
if [ ! -f appimagetool ]; then
    curl -L -o appimagetool https://github.com/AppImage/AppImageKit/releases/download/continuous/appimagetool-x86_64.AppImage
    chmod +x appimagetool
fi

# 9. Build the AppImage
# ARCH=x86_64 is required for appimagetool
ARCH=x86_64 ./appimagetool --appimage-extract-and-run $APP_DIR ${APP_NAME}-x86_64.AppImage

echo "AppImage created: ${APP_NAME}-x86_64.AppImage"
