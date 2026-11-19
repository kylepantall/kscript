#!/bin/sh
# Installs the kscript CLI from the latest GitHub release.
#
#   curl -fsSL https://raw.githubusercontent.com/kylepantall/kscript/master/install.sh | sh
#
# Options (environment variables):
#   KSCRIPT_VERSION=v0.1.0    install a specific release instead of the latest
#   KSCRIPT_INSTALL=~/.kscript where to install (the binary goes in $KSCRIPT_INSTALL/bin)
#   KSCRIPT_NO_MODIFY_PATH=1  don't add the bin folder to your shell profile
set -eu

REPO="${KSCRIPT_REPO:-kylepantall/kscript}"
INSTALL_DIR="${KSCRIPT_INSTALL:-$HOME/.kscript}"
BIN_DIR="$INSTALL_DIR/bin"
VERSION="${KSCRIPT_VERSION:-latest}"

say() { printf '%s\n' "$*"; }
fail() { printf 'kscript install: %s\n' "$*" >&2; exit 1; }

case "$(uname -s)" in
  Linux) os=linux ;;
  Darwin) os=osx ;;
  MINGW*|MSYS*|CYGWIN*) fail "on Windows, run in PowerShell: irm https://raw.githubusercontent.com/$REPO/master/install.ps1 | iex" ;;
  *) fail "unsupported OS: $(uname -s)" ;;
esac

case "$(uname -m)" in
  x86_64|amd64) arch=x64 ;;
  arm64|aarch64) arch=arm64 ;;
  *) fail "unsupported CPU: $(uname -m)" ;;
esac

# An x64 shell under Rosetta still reports x86_64; prefer the native Apple Silicon build.
if [ "$os" = osx ] && [ "$arch" = x64 ] && [ "$(sysctl -n sysctl.proc_translated 2>/dev/null || echo 0)" = 1 ]; then
  arch=arm64
fi

rid="$os-$arch"
asset="kscript-$rid.tar.gz"
if [ "$VERSION" = latest ]; then
  url="https://github.com/$REPO/releases/latest/download/$asset"
else
  case "$VERSION" in v*) ;; *) VERSION="v$VERSION" ;; esac
  url="https://github.com/$REPO/releases/download/$VERSION/$asset"
fi

if command -v curl >/dev/null 2>&1; then
  download() { curl -fsSL "$1" -o "$2"; }
elif command -v wget >/dev/null 2>&1; then
  download() { wget -qO "$2" "$1"; }
else
  fail "curl or wget is needed"
fi

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT INT TERM

say "Downloading $asset ($VERSION)..."
download "$url" "$tmp/$asset" || fail "could not download $url"
tar -xzf "$tmp/$asset" -C "$tmp" || fail "could not unpack $asset"
[ -f "$tmp/kscript" ] || fail "$asset has no kscript binary inside"

mkdir -p "$BIN_DIR"
chmod +x "$tmp/kscript"
if [ "$os" = osx ]; then
  # The binary isn't notarized: clear the quarantine flag so Gatekeeper lets it run.
  xattr -d com.apple.quarantine "$tmp/kscript" 2>/dev/null || true
fi
# Rename into place so a running kscript isn't disturbed.
mv -f "$tmp/kscript" "$BIN_DIR/kscript"

installed="$("$BIN_DIR/kscript" --version 2>/dev/null)" || fail "installed $BIN_DIR/kscript but it didn't run"
say "Installed kscript $installed to $BIN_DIR/kscript"

case ":$PATH:" in
  *":$BIN_DIR:"*) say "Run: kscript --help"; exit 0 ;;
esac

if [ "${KSCRIPT_NO_MODIFY_PATH:-}" = 1 ]; then
  say "Add $BIN_DIR to your PATH to run kscript from anywhere."
  exit 0
fi

line="export PATH=\"$BIN_DIR:\$PATH\""
profiles=""
case "${SHELL:-}" in
  */zsh) profiles="${ZDOTDIR:-$HOME}/.zshrc" ;;
  */bash) if [ "$os" = osx ]; then profiles="$HOME/.bash_profile"; else profiles="$HOME/.bashrc"; fi ;;
  */fish) profiles="fish" ;;
  *) profiles="$HOME/.profile" ;;
esac

if [ "$profiles" = fish ]; then
  mkdir -p "$HOME/.config/fish/conf.d"
  printf 'fish_add_path "%s"\n' "$BIN_DIR" > "$HOME/.config/fish/conf.d/kscript.fish"
  say "Added $BIN_DIR to PATH in ~/.config/fish/conf.d/kscript.fish"
else
  if ! grep -qsF "$BIN_DIR" "$profiles"; then
    printf '\n# kscript\n%s\n' "$line" >> "$profiles"
  fi
  say "Added $BIN_DIR to PATH in $profiles"
fi
say "Open a new terminal (or run: $line), then: kscript --help"
