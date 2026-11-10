#!/bin/bash
# RadioBridge v0.9 — Linux installatiescript
# Gebruik: curl -fsSL https://raw.githubusercontent.com/pe5jw/RadioBridge/main/install.sh | bash

set -e

REPO="pe5jw/RadioBridge"
VERSION="v0.9"
INSTALL_DIR="$HOME/.local/share/radiobridge"
BIN_DIR="$HOME/.local/bin"
DESKTOP_DIR="$HOME/.local/share/applications"
ASSET="RadioBridge-v0.9-linux-x64.zip"
DOWNLOAD_URL="https://github.com/$REPO/releases/download/$VERSION/$ASSET"

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

info()    { echo -e "${GREEN}[RadioBridge]${NC} $*"; }
warning() { echo -e "${YELLOW}[waarschuwing]${NC} $*"; }
error()   { echo -e "${RED}[fout]${NC} $*"; exit 1; }

# ── Vereisten checken ────────────────────────────────────────────────────────

info "Vereisten controleren..."

for cmd in curl unzip; do
    command -v "$cmd" >/dev/null 2>&1 || error "'$cmd' niet gevonden. Installeer met: sudo apt install $cmd"
done

# ── Downloaden ───────────────────────────────────────────────────────────────

info "RadioBridge $VERSION downloaden van GitHub..."
mkdir -p "$INSTALL_DIR"
TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT

curl -fsSL --progress-bar -o "$TMP/$ASSET" "$DOWNLOAD_URL" \
    || error "Download mislukt. Controleer je internetverbinding."

# ── Uitpakken ────────────────────────────────────────────────────────────────

info "Uitpakken naar $INSTALL_DIR..."
rm -rf "${INSTALL_DIR:?}"/*
unzip -q "$TMP/$ASSET" -d "$INSTALL_DIR"
chmod +x "$INSTALL_DIR/RadioBridge"
chmod +x "$INSTALL_DIR/radiobridge_audio.so" 2>/dev/null || true

# ── Launcher in PATH ─────────────────────────────────────────────────────────

mkdir -p "$BIN_DIR"
cat > "$BIN_DIR/radiobridge" << 'LAUNCHER'
#!/bin/bash
DIR="$HOME/.local/share/radiobridge"
exec "$DIR/RadioBridge" "$@"
LAUNCHER
chmod +x "$BIN_DIR/radiobridge"

# ── Desktop entry (optioneel) ────────────────────────────────────────────────

if [ -d "$DESKTOP_DIR" ]; then
    mkdir -p "$DESKTOP_DIR"
    cat > "$DESKTOP_DIR/radiobridge.desktop" << DESKTOP
[Desktop Entry]
Name=RadioBridge
Comment=Audio over netwerk voor remote radio bediening
Exec=$BIN_DIR/radiobridge
Icon=audio-headset
Terminal=false
Type=Application
Categories=HamRadio;Audio;Network;
DESKTOP
    update-desktop-database "$DESKTOP_DIR" 2>/dev/null || true
    info "Desktop icoon aangemaakt."
fi

# ── PATH hint ────────────────────────────────────────────────────────────────

if ! echo "$PATH" | grep -q "$BIN_DIR"; then
    warning "'$BIN_DIR' staat niet in je PATH."
    warning "Voeg toe aan ~/.bashrc of ~/.zshrc:"
    echo "    export PATH=\"\$HOME/.local/bin:\$PATH\""
fi

# ── Klaar ────────────────────────────────────────────────────────────────────

echo ""
info "Installatie voltooid!"
info "Starten: radiobridge"
info "Of direct: $INSTALL_DIR/RadioBridge"
echo ""
info "Vereist: X11 of Wayland display (GUI app)"
