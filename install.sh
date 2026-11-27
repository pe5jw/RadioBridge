#!/bin/bash
# RadioBridge — installatie voor Linux (x64 en arm64/Raspberry Pi 4/5)
#
#   curl -fsSL https://raw.githubusercontent.com/pe5jw/RadioBridge/main/install.sh | bash
#
# Installeert naar ~/RadioBridge, zet een 'radiobridge' commando in ~/.local/bin,
# een snelkoppeling in het applicatiemenu, en update.sh voor latere updates.
set -e
REPO="pe5jw/RadioBridge"
DEST="${RADIOBRIDGE_DIR:-$HOME/RadioBridge}"
BIN_DIR="$HOME/.local/bin"
APPS_DIR="$HOME/.local/share/applications"

GREEN='\033[0;32m'; YELLOW='\033[1;33m'; RED='\033[0;31m'; NC='\033[0m'
info()    { echo -e "${GREEN}[RadioBridge]${NC} $*"; }
warning() { echo -e "${YELLOW}[let op]${NC} $*"; }
error()   { echo -e "${RED}[fout]${NC} $*"; exit 1; }

for cmd in curl unzip; do
    command -v "$cmd" >/dev/null 2>&1 || error "'$cmd' niet gevonden. Installeer met: sudo apt install $cmd"
done

case "$(uname -m)" in
    x86_64)        RID=linux-x64 ;;
    aarch64|arm64) RID=linux-arm64
                   warning "Raspberry Pi / arm64-versie is nog niet getest. Meld problemen graag via GitHub issues." ;;
    *) error "Niet ondersteunde processor: $(uname -m). Alleen x86_64 en aarch64 (64-bit OS)." ;;
esac

ASSET="RadioBridge-$RID.zip"
URL="https://github.com/$REPO/releases/latest/download/$ASSET"
TMP=$(mktemp -d); trap 'rm -rf "$TMP"' EXIT

info "Downloaden: $URL"
curl -fL --progress-bar -o "$TMP/rb.zip" "$URL" || error "Download mislukt. Controleer je internetverbinding."

info "Installeren naar $DEST ..."
pkill -x RadioBridge 2>/dev/null && sleep 1 || true
unzip -q "$TMP/rb.zip" -d "$TMP"
mkdir -p "$DEST"
cp -a "$TMP/RadioBridge/." "$DEST/"
chmod +x "$DEST/RadioBridge"

# update.sh meeleveren voor latere updates
curl -fsSL -o "$DEST/update.sh" "https://raw.githubusercontent.com/$REPO/main/update.sh" && chmod +x "$DEST/update.sh" || true

# 'radiobridge' commando
mkdir -p "$BIN_DIR"
cat > "$BIN_DIR/radiobridge" <<LAUNCHER
#!/bin/bash
exec "$DEST/RadioBridge" "\$@"
LAUNCHER
chmod +x "$BIN_DIR/radiobridge"

# Snelkoppeling in het applicatiemenu
mkdir -p "$APPS_DIR"
cat > "$APPS_DIR/radiobridge.desktop" <<DESKTOP
[Desktop Entry]
Name=RadioBridge
Comment=Audio en CAT over netwerk voor remote radio bediening
Exec=$DEST/RadioBridge
Icon=audio-headset
Terminal=false
Type=Application
Categories=HamRadio;Audio;Network;
DESKTOP
update-desktop-database "$APPS_DIR" 2>/dev/null || true

# Seriële poort: gebruiker moet in groep dialout zitten
if ! id -nG | grep -qw dialout; then
    warning "Je zit niet in de groep 'dialout' — nodig voor de seriële (CAT) poort:"
    echo "    sudo usermod -aG dialout \$USER   (daarna opnieuw inloggen)"
fi
case ":$PATH:" in *":$BIN_DIR:"*) ;; *)
    warning "'$BIN_DIR' staat niet in je PATH. Voeg toe aan ~/.bashrc:"
    echo "    export PATH=\"\$HOME/.local/bin:\$PATH\"" ;;
esac

echo ""
info "Geïnstalleerd: $("$DEST/RadioBridge" --version 2>/dev/null || echo RadioBridge)"
info "Starten:  radiobridge      (of via het applicatiemenu)"
info "Updaten:  $DEST/update.sh"
