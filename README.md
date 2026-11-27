# RadioBridge

**Audio + serieel over TCP — remote radio bediening**

RadioBridge verbindt twee computers via het netwerk zodat je een radio op afstand kunt bedienen. Je hoort de radio alsof je ernaast zit, en je stuurt hem aan via CAT/PTT — alsof de USB-kabel gewoon doorloopt naar de andere kant.

```
[Radio] ── USB ── [Server]  ══ TCP ══  [Client] ── [Jij / logger / CAT-app]
                  audio in/uit          koptelefoon / microfoon
                  COM-poort(en)         virtuele COM-poort(en)
```

De seriële brug is **protocol-onafhankelijk**: RadioBridge stuurt alleen bytes door. Elke transceiver met een CAT-poort en elke besturingsapp met een COM-poort werkt (DXLab Commander, flrig, PCC950, loggers…), net als andere apparaten met een seriële poort.

---

## Wat doet het

| Functie | Details |
|---|---|
| **2-weg audio** | Full-duplex, PCM of Opus; presets van 6 kbps tot 1,5 Mbps, lage-latency presets met 10 ms blokken |
| **Ruisfilter** | Noise gate / RNNoise op het opnamekanaal |
| **2 seriële poorten** | Server: echte COM-poort of `/dev/ttyUSB*`. Client: virtuele poort (com0com op Windows, automatisch een PTY op Linux) |
| **Instellingen onthouden** | Alles wat je instelt komt terug na een herstart |
| **Zonder venster** | `--startserver --headless`, bijv. als systemd-dienst |
| **Firewall-knop** | Opent de audiopoort en de TCP-poorten van de seriële bruggen |
| **Platforms** | Windows x64, Linux x64, Raspberry Pi 4/5 (arm64, ⚠️ nog niet getest) |

---

## Installatie

Download de nieuwste versie van de [Releases](https://github.com/pe5jw/RadioBridge/releases/latest)-pagina. Geen .NET-installatie nodig.

| Platform | Bestand |
|---|---|
| Windows x64 | `RadioBridge-win-x64.zip` |
| Linux x64 | `RadioBridge-linux-x64.zip` |
| Raspberry Pi 4/5 (64-bit OS) | `RadioBridge-linux-arm64.zip` — ⚠️ nog niet getest |

### Windows

1. Pak `RadioBridge-win-x64.zip` uit, bijv. naar `C:\ham\` — je krijgt een map `RadioBridge`
2. Start `RadioBridge\RadioBridge.exe`
3. **Als client met CAT**: installeer [com0com](https://sourceforge.net/projects/com0com/) voor een virtueel COM-paar (bijv. COM7 ↔ COM8). RadioBridge gebruikt de ene kant, je CAT-app de andere.

### Linux x64 en Raspberry Pi

```bash
curl -fsSL https://raw.githubusercontent.com/pe5jw/RadioBridge/main/install.sh | bash
```

Installeert naar `~/RadioBridge`, met een `radiobridge` commando en een snelkoppeling in het applicatiemenu. Kiest automatisch x64 of arm64.

Voor de seriële poort moet je in de groep `dialout` zitten:
```bash
sudo usermod -aG dialout $USER   # daarna opnieuw inloggen
```

### Updaten

| | Stabiele versie | Ontwikkelversie (laatste build van `main`) |
|---|---|---|
| Linux | `~/RadioBridge/update.sh` | `~/RadioBridge/update.sh --dev` |
| Windows (vanuit de repo) | `.\update.ps1` | `.\update.ps1 -Dev` |

Voeg `--start` / `-Start` toe om RadioBridge daarna meteen te starten.

---

## Gebruik

### Server (bij de radio)

1. Kies **Server**
2. Kies het **opname**-apparaat (audio uit de radio) en **afspeel**-apparaat (audio naar de radio)
3. Schakel **Poort 1** in: COM-poort / `/dev/ttyUSB0` van de radio, baud gelijk aan de radio, TCP-poort (standaard 5001)
4. Klik **🔥 Firewall** (eenmalig)
5. Klik **Server starten**

### Client (jouw werkplek)

1. Kies **Client**, vul het IP-adres van de server in
2. Kies je audio-apparaten en een codec-preset
3. Windows: klik **com0com detecteren** en kies het paar voor Poort 1. Je CAT-app verbindt met de *andere* kant (het log zegt welke)
4. Linux: RadioBridge maakt zelf een virtuele poort (`/dev/pts/N`) — het log zegt welke
5. Klik **Verbinden**

De codec kies je op de client; de server neemt hem automatisch over.

### Opstartparameters

| Parameter | Werking |
|---|---|
| `--startserver` | Start direct als server met de laatst opgeslagen instellingen |
| `--startclient` | Start direct als client met de laatst opgeslagen instellingen |
| `--headless` | Zonder venster, alleen samen met `--startserver` of `--startclient`. Logt naar de console |
| `--version` | Versienummer |
| `--help` | Uitleg |

Instellingen worden automatisch bewaard zodra je ze wijzigt:

- Windows: `%APPDATA%\RadioBridge\settings.json`
- Linux: `~/.config/RadioBridge/settings.json`

Stel dus eerst alles één keer in met het venster; daarna kan RadioBridge zonder venster draaien. Een server zonder venster luistert opnieuw na het verbreken van een client; een client zonder venster probeert elke 5 seconden opnieuw te verbinden.

### Linux: server automatisch starten (systemd)

```bash
mkdir -p ~/.config/systemd/user
cat > ~/.config/systemd/user/radiobridge.service <<'UNIT'
[Unit]
Description=RadioBridge server
After=network-online.target sound.target

[Service]
ExecStart=%h/RadioBridge/RadioBridge --startserver --headless
Restart=on-failure
RestartSec=5

[Install]
WantedBy=default.target
UNIT

systemctl --user daemon-reload
systemctl --user enable --now radiobridge
loginctl enable-linger $USER          # ook starten zonder dat je inlogt
journalctl --user -u radiobridge -f   # log bekijken
```

Stop de dienst (`systemctl --user stop radiobridge`) voordat je de versie met venster start, anders zijn poorten en audio al bezet.

### Tips

- **Via RDP / zwakke computer**: minimaliseer het venster — dan tekent RadioBridge niets bij en kost het veel minder CPU. VU-meters kun je ook uitzetten.
- **CAT werkt niet?** Controleer baud en poort op de server, en of de firewall de TCP-poort van de seriële brug (5001) doorlaat.

---

## Bekende beperkingen

- Raspberry Pi (arm64): wordt gebouwd, maar is nog niet getest
- Windows-client heeft com0com nodig voor een virtuele COM-poort
- **Geen wachtwoord of versleuteling** — gebruik RadioBridge op je eigen netwerk of via een VPN, zet de poorten niet direct open op internet
- Eén client tegelijk
- `Tmds.DBus.Protocol` security-waarschuwing bij het bouwen komt van een indirecte Avalonia-dependency

Problemen of ervaringen graag via [GitHub issues](https://github.com/pe5jw/RadioBridge/issues): vermeld platform, radio, besturingsapp en het log.

---

## Architectuur

```
RadioBridge (Avalonia UI of --headless)
 ├── AudioEngine          TCP audio-stream, presets, gain, VU
 │    └── MiniAudioEngine  P/Invoke → radiobridge_audio.dll/.so (miniaudio, full-duplex)
 ├── OpusCodec            Concentus (pure C#)
 ├── NoiseFilter          noise gate / RNNoise.Net
 ├── SerialBridge         server: COM-poort ↔ TCP
 ├── VirtualSerialBridge  client: com0com / PTY ↔ TCP
 └── AppSettings          settings.json
```

---

## Zelf bouwen

Releases worden automatisch gebouwd door GitHub Actions (`.github/workflows/build-release.yml`):

- **push naar `main`** → release `dev` wordt vervangen door de nieuwste build
- **tag `vX.Y.Z` pushen** → stabiele release, met notes uit `.github/release-notes/vX.Y.Z.md`

Nieuwe versie uitbrengen: zet `<Version>` in `RadioBridge/RadioBridge.csproj`, schrijf de release-notes, en:
```bash
git tag v1.0.1 && git push origin v1.0.1
```

Lokaal bouwen (vereist [.NET 10 SDK](https://dotnet.microsoft.com/download)):
```bash
dotnet publish RadioBridge -r win-x64     --self-contained -c Release -o dist/win-x64
dotnet publish RadioBridge -r linux-x64   --self-contained -c Release -o dist/linux-x64
dotnet publish RadioBridge -r linux-arm64 --self-contained -c Release -o dist/linux-arm64
```

De native audiobibliotheek staat voorgebouwd in `native/`. Opnieuw bouwen:
```bash
# Windows (MSVC, MinGW of clang):  native\build_windows.bat
# Linux x64:
gcc -O2 -shared -fPIC -Inative native/radiobridge_audio.c -o native/linux-x64/radiobridge_audio.so -ldl -lpthread -lm
# Linux arm64 (cross-compile):
aarch64-linux-gnu-gcc -O2 -shared -fPIC -Inative native/radiobridge_audio.c -o native/linux-arm64/radiobridge_audio.so -ldl -lpthread -lm
```

---

## Versiegeschiedenis

| Versie | Wat veranderd |
|---|---|
| v0.1 – v0.6 | Proof of concept: WinForms, NAudio, PCM over TCP; daarna Opus, presets en seriële brug |
| v0.7 | RNNoise ruisfilter, firewall helper, betere buffering |
| v0.8 | NAudio vervangen door miniaudio C-shim |
| v0.9 | WinForms vervangen door Avalonia 11 → cross-platform |
| **v1.0.0** | Betrouwbare seriële brug, instellingen onthouden, `--headless`, minder CPU, Raspberry Pi build (ongetest), automatische builds |

---

## Licentie

MIT — zie [LICENSE](LICENSE)

Gebruikt:
- [miniaudio](https://miniaud.io) — MIT-0 / public domain, David Reid
- [Concentus](https://github.com/lostromb/concentus) — BSD, Opus/Xiph.Org
- [RNNoise.Net](https://github.com/Yellow-Dog-Man/RNNoise.Net) — BSD
- [Avalonia UI](https://avaloniaui.net) — MIT
