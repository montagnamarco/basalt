#!/bin/sh
# Builds Basalt.app for macOS.
#
# A .app is a folder with a fixed shape, and macOS reads Info.plist inside it
# to find the name and the icon. Published first, then arranged: dotnet has no
# idea what a bundle is.
set -e

root=$(cd "$(dirname "$0")/.." && pwd)
out=${1:-"$root/artifacts"}
runtime=${2:-osx-arm64}

app="$out/Basalt.app"

echo "Publishing for $runtime…"

dotnet publish "$root/src/Basalt.Shell/Basalt.Shell.csproj" \
  -c Release -r "$runtime" --self-contained true \
  -o "$out/publish" --nologo

echo "Assembling $app…"

rm -rf "$app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"

cp "$root/src/Basalt.Shell/Info.plist" "$app/Contents/Info.plist"
cp "$root/src/Basalt.Shell/Assets/basalt.icns" "$app/Contents/Resources/basalt.icns"
cp -R "$out/publish/." "$app/Contents/MacOS/"

chmod +x "$app/Contents/MacOS/Basalt.Shell"

# Signed last: anything written into the bundle afterwards breaks the
# signature, which is why this is the final step.
echo "Signing…"
codesign --force --deep --sign - "$app"

codesign -v "$app" && echo "The signature checks out."

echo "Done: $app"
