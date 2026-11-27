#!/bin/bash
# Haalt RadioBridge voor Linux (x64 of arm64/Raspberry Pi) van GitHub en zet hem in ~/RadioBridge.
#
# Gebruik:  ./update.sh            nieuwste stabiele versie
#           ./update.sh --dev      nieuwste ontwikkelversie (automatische build van main)
#           ./update.sh --start    en daarna starten (combineerbaar met --dev)
set -e
REPO="pe5jw/RadioBridge"
DEST="${RADIOBRIDGE_DIR:-$HOME/RadioBridge}"
DEV=0; START=0
for a in "$@"; do
    case "$a" in
        --dev)   DEV=1 ;;
        --start) START=1 ;;
        *) echo "Onbekende optie: $a (gebruik --dev en/of --start)"; exit 1 ;;
    esac
done

case "$(uname -m)" in
    x86_64)        RID=linux-x64 ;;
    aarch64|arm64) RID=linux-arm64 ;;
    *) echo "Niet ondersteunde processor: $(uname -m) (alleen x86_64 en aarch64)"; exit 1 ;;
esac
ASSET="RadioBridge-$RID.zip"
if [ $DEV = 1 ]; then URL="https://github.com/$REPO/releases/download/dev/$ASSET"; KIND="ontwikkelversie"
else                  URL="https://github.com/$REPO/releases/latest/download/$ASSET"; KIND="stabiel"; fi

TMP=$(mktemp -d); trap 'rm -rf "$TMP"' EXIT

echo "→ RadioBridge afsluiten (indien actief)..."
pkill -x RadioBridge 2>/dev/null && sleep 1 || true

echo "→ Downloaden ($KIND, $RID): $URL"
wget -q --show-progress -O "$TMP/rb.zip" "$URL"

echo "→ Uitpakken naar $DEST ..."
unzip -q "$TMP/rb.zip" -d "$TMP"
mkdir -p "$DEST"
cp -a "$TMP/RadioBridge/." "$DEST/"
chmod +x "$DEST/RadioBridge"
echo "✅ $("$DEST/RadioBridge" --version 2>/dev/null || echo RadioBridge) → $DEST"

if [ $START = 1 ]; then
    nohup "$DEST/RadioBridge" >/dev/null 2>&1 &
fi
