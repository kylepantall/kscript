#!/bin/sh
# Builds "Life Lab.app": a native macOS window running the KScript version of Life Lab.
#
#   ./make-app.sh                      build kscript from this repository (needs the .NET 8 SDK) and bundle it
#   ./make-app.sh --kscript ~/.kscript/bin/kscript   bundle an installed kscript instead
#   ./make-app.sh --out ~/Applications put the app somewhere else (default: this folder)
#
# Needs the Xcode command line tools for swiftc (xcode-select --install).
set -eu

here=$(cd "$(dirname "$0")" && pwd)
site_src=$(cd "$here/.." && pwd)
repo=$(cd "$site_src/../.." && pwd)
out="$here"
kscript=""

while [ $# -gt 0 ]; do
  case "$1" in
    --kscript) kscript=$2; shift 2 ;;
    --out) out=$2; shift 2 ;;
    -h|--help) sed -n '2,9p' "$0"; exit 0 ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
done

[ "$(uname -s)" = "Darwin" ] || { echo "make-app.sh builds a macOS app; run it on a Mac." >&2; exit 1; }
command -v swiftc >/dev/null 2>&1 || { echo "swiftc not found. Install the Xcode command line tools: xcode-select --install" >&2; exit 1; }

case "$(uname -m)" in
  arm64) rid=osx-arm64 ;;
  *) rid=osx-x64 ;;
esac

app="$out/Life Lab.app"
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

if [ -z "$kscript" ]; then
  command -v dotnet >/dev/null 2>&1 || {
    echo "dotnet not found. Install the .NET 8 SDK, or bundle an installed kscript with --kscript ~/.kscript/bin/kscript" >&2
    exit 1
  }
  echo "Building kscript ($rid) from $repo ..."
  dotnet publish "$repo/KScriptApp" -c Release -r "$rid" --self-contained \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "$work/kscript" >/dev/null
  kscript="$work/kscript/kscript"
fi
[ -x "$kscript" ] || { echo "No kscript at $kscript" >&2; exit 1; }

echo "Compiling the app shell ..."
swiftc -O "$here/LifeLab.swift" -o "$work/LifeLab" -framework Cocoa -framework WebKit

echo "Assembling $app ..."
rm -rf "$app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources/site"
cp "$work/LifeLab" "$app/Contents/MacOS/LifeLab"
cp "$kscript" "$app/Contents/MacOS/kscript"
cp "$site_src/index.html" "$site_src/site.css" "$site_src/kscript.json" "$app/Contents/Resources/site/"
cp -R "$site_src/lib" "$app/Contents/Resources/site/lib"

cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Life Lab</string>
  <key>CFBundleDisplayName</key><string>Life Lab</string>
  <key>CFBundleIdentifier</key><string>io.github.kylepantall.kscript.lifelab</string>
  <key>CFBundleExecutable</key><string>LifeLab</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>1.0</string>
  <key>CFBundleVersion</key><string>1</string>
  <key>LSMinimumSystemVersion</key><string>11.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSAppTransportSecurity</key>
  <dict><key>NSAllowsLocalNetworking</key><true/></dict>
</dict>
</plist>
PLIST

# Ad-hoc sign so Apple Silicon runs it; it isn't notarized, so this is for your own Mac.
codesign --force --deep --sign - "$app" >/dev/null 2>&1 || true

echo "Done: $app"
echo "Open it with: open \"$app\""
