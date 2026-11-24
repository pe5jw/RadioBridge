#!/bin/bash
# Haalt de nieuwste Linux-build van RadioBridge van GitHub en zet hem in ~/RadioBridge.
# Gebruik:  ./update.sh          (downloaden + uitpakken)
#           ./update.sh --start  (en daarna starten)
set -e
VERSION="v0.9"
URL="https://github.com/pe5jw/RadioBridge/releases/download/$VERSION/RadioBridge-$VERSION-linux-x64.zip"
TMP=$(mktemp -d); trap 'rm -rf "$TMP"' EXIT

echo "→ RadioBridge afsluiten (indien actief)..."
pkill -x RadioBridge 2>/dev/null && sleep 1 || true

echo "→ Downloaden: $URL"
wget -q --show-progress -O "$TMP/rb.zip" "$URL"

echo "→ Uitpakken naar $HOME/RadioBridge ..."
unzip -oq "$TMP/rb.zip" -d "$HOME"
chmod +x "$HOME/RadioBridge/RadioBridge"
echo "✅ Klaar: $HOME/RadioBridge/RadioBridge ($(date -r "$HOME/RadioBridge/RadioBridge" '+%F %T'))"

if [ "$1" = "--start" ]; then
    nohup "$HOME/RadioBridge/RadioBridge" >/dev/null 2>&1 &
fi
