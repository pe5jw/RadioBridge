# RadioBridge

**Audio + serieel over TCP — remote radio bediening**

RadioBridge verbindt twee computers via een netwerk (LAN of internet) zodat je een radio op afstand kunt bedienen. Je hoort de radio alsof je er naast zit, én je kunt hem aansturen via CAT en PTT — alsof de USB-kabel gewoon doorloopt naar de andere kant.

```
[Radio] ── USB ── [Server-PC]  ══ TCP ══  [Client-PC] ── [Jij / Logger]
                  microfoon/speaker         koptelefoon/microfoon
                  CAT / PTT poort           virtuele COM-poort
```

---

## Wat doet het

| Functie | Details |
|---|---|
| **2-weg audio** | Volledige duplex, 48 kHz / 16-bit PCM |
| **Opus compressie** | Configureerbaar (2,4 – 510 kbps), presets voor SSB/FM/CW |
| **PCM modus** | Ruwe audio zonder compressie, minimale latency |
| **Ruisonderdrukking** | RNNoise neuraal filter op capturekanaal |
| **Serieel over TCP** | CAT- en PTT-poort als virtuele COM op de client |
| **Firewall helper** | Poort 5000 TCP automatisch openen (Windows: netsh, Linux: ufw) |
| **Cross-platform** | Windows x64, Linux x64, Linux arm64 (Raspberry Pi 4/5) |

---

## Architectuur

```
┌─────────────────────────────────────────────────────────────┐
│  RadioBridge.exe / RadioBridge                              │
│                                                             │
│  MainWindow (Avalonia UI)                                   │
│       │                                                     │
│       ├── AudioEngine.cs          TCP audio stream          │
│       │      └── MiniAudioEngine  P/Invoke → native .so/.dll│
│       │              └── miniaudio (C, full-duplex)         │
│       │                                                     │
│       ├── OpusCodec.cs            Concentus (pure C#)       │
│       ├── NoiseFilter.cs          RNNoise.Net                │
│       └── SerialBridge.cs         System.IO.Ports + TCP     │
└─────────────────────────────────────────────────────────────┘
```

**Native audio laag** — `radiobridge_audio.c` is een dunne C-shim bovenop [miniaudio](https://miniaud.io) (single-header, geen externe dependencies). De shim wordt als `.dll` (Windows) of `.so` (Linux) naast de EXE geplaatst en aangeroepen via P/Invoke.

---

## Ontwikkelgeschiedenis

| Versie | Wat veranderd |
|---|---|
| **v0.1 – v0.3** | Proof of concept: WinForms UI, NAudio voor audio, ruwe PCM over TCP |
| **v0.4 – v0.6** | Opus compressie toegevoegd (Concentus), presets voor SSB/FM/CW, serieel bridging |
| **v0.7** | RNNoise ruisfilter, firewall helper, betere buffering |
| **v0.8** | NAudio vervangen door miniaudio C-shim (native full-duplex, lagere latency) |
| **v0.9** | WinForms vervangen door Avalonia 11 → cross-platform; Linux x64 + arm64 builds |
| **v1.0 (gepland)** | Kant-en-klare Raspberry Pi release, automatische latency-compensatie |

---

## Zelf bouwen

### Vereisten

| Component | Versie | Waarvoor |
|---|---|---|
| [.NET SDK](https://dotnet.microsoft.com/download) | 10.0 | C# applicatie builden |
| [Visual Studio Build Tools](https://visualstudio.microsoft.com/downloads/) of MinGW/GCC | — | Native audio DLL compileren (Windows) |
| GCC | — | Native audio SO compileren (Linux) |

### 1 — Native audio library bouwen

**Windows** (vanuit `native/`):
```bat
build_windows.bat
```
Detecteert automatisch MSVC (`cl.exe`), MinGW (`gcc`) of LLVM (`clang`).
Output: `native/win-x64/radiobridge_audio.dll`

**Linux x64** (vanuit `native/`):
```bash
gcc -O2 -shared -fPIC -I. radiobridge_audio.c \
    -o linux-x64/radiobridge_audio.so \
    -ldl -lpthread -lm
```

**Linux arm64** (cross-compile vanuit Linux x64, of native op Pi):
```bash
# Cross-compile op Ubuntu/Debian x64:
sudo apt install gcc-aarch64-linux-gnu

aarch64-linux-gnu-gcc -O2 -shared -fPIC -I. radiobridge_audio.c \
    -o linux-arm64/radiobridge_audio.so \
    -ldl -lpthread -lm
```

### 2 — C# applicatie bouwen

```bash
cd RadioBridge

# Debug build (snel testen):
dotnet build

# Release publish voor Windows:
dotnet publish -r win-x64 --self-contained -c Release -o dist/win-x64

# Release publish voor Linux x64:
dotnet publish -r linux-x64 --self-contained -c Release -o dist/linux-x64

# Release publish voor Linux arm64 (Raspberry Pi 4/5):
dotnet publish -r linux-arm64 --self-contained -c Release -o dist/linux-arm64
```

> **Let op**: de native `.dll` / `.so` wordt automatisch naast de EXE geplaatst via de `CopyToOutputDirectory`-instelling in het `.csproj`, mits de juiste `-r` RID is opgegeven.

### 3 — Zip voor distributie

```powershell
# Windows
Compress-Archive dist/win-x64/* RadioBridge-v0.9-win-x64.zip

# Linux (vanuit Linux)
cd dist/linux-x64 && zip -r ../../RadioBridge-v0.9-linux-x64.zip .
```

---

## Installatie

### Windows

1. Download `RadioBridge-v0.9-win-x64.zip` van de [Releases](https://github.com/pe5jw/RadioBridge/releases) pagina
2. Pak uit naar een map naar keuze, bijv. `C:\ham\RadioBridge\`
3. Dubbelklik `RadioBridge.exe`
4. Eerste keer: klik **🔥 Firewall** om poort 5000 TCP automatisch te openen

> Geen .NET installatie nodig — alles zit ingebakken in de EXE.

---

### Linux (x64 — PC / laptop)

**Automatisch via installatiescript:**
```bash
curl -fsSL https://raw.githubusercontent.com/pe5jw/RadioBridge/main/install.sh | bash
```

Installeert naar `~/.local/share/radiobridge/` en maakt automatisch:
- Een snelkoppeling in het applicatiemenu
- Een snelkoppeling op het bureaublad
- Een `radiobridge` commando in de terminal

**Handmatig:**
```bash
wget https://github.com/pe5jw/RadioBridge/releases/download/v0.9/RadioBridge-v0.9-linux-x64.zip
unzip RadioBridge-v0.9-linux-x64.zip -d ~/radiobridge
chmod +x ~/radiobridge/RadioBridge
~/radiobridge/RadioBridge
```

> Vereist een X11 of Wayland desktop. Geen .NET installatie nodig.

---

### Raspberry Pi (arm64 — Pi 4 / Pi 5)

> **Pi 4 en Pi 5** draaien een 64-bit OS (aarch64) — gebruik de `linux-arm64` build.  
> **Pi 3** draait standaard 32-bit — gebruik de `linux-arm` build (nog niet beschikbaar; zie hieronder).

#### Optie A — Kant-en-klare arm64 build (aanbevolen)

Zodra de `linux-arm64` release beschikbaar is:
```bash
wget https://github.com/pe5jw/RadioBridge/releases/download/v0.9/RadioBridge-v0.9-linux-arm64.zip
unzip RadioBridge-v0.9-linux-arm64.zip -d ~/radiobridge
chmod +x ~/radiobridge/RadioBridge
~/radiobridge/RadioBridge
```

#### Optie B — Zelf compileren op de Pi

```bash
# 1. .NET 10 installeren op de Pi
curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0
echo 'export PATH=$PATH:~/.dotnet' >> ~/.bashrc
source ~/.bashrc

# 2. Repo clonen
git clone https://github.com/pe5jw/RadioBridge.git
cd RadioBridge

# 3. Native audio library compileren (native op de Pi)
cd native
gcc -O2 -shared -fPIC -I. radiobridge_audio.c \
    -o linux-arm64/radiobridge_audio.so \
    -ldl -lpthread -lm

# 4. App builden
cd ../RadioBridge
dotnet publish -r linux-arm64 --self-contained -c Release -o ~/radiobridge
cp ../native/linux-arm64/radiobridge_audio.so ~/radiobridge/

# 5. Starten
~/radiobridge/RadioBridge
```

> Op een Pi 3 (32-bit OS): vervang `linux-arm64` door `linux-arm` in stap 3 en 4.

---

## Gebruik

### Server instellen (bij de radio)

1. Selecteer **Server** modus
2. Kies het **capture** apparaat (microfoon / radio-audio in)
3. Kies het **playback** apparaat (speaker / radio-audio uit)
4. Stel de seriële poort in voor CAT/PTT (optioneel)
5. Klik **Verbinden** — wacht op een client

### Client instellen (jouw werkplek)

1. Selecteer **Client** modus
2. Vul het IP-adres van de server in
3. Kies je lokale audio apparaten
4. Klik **Verbinden**

### Presets

| Preset | Bitrate | Geschikt voor |
|---|---|---|
| SSB Smal | 16 kbps | HF SSB, smalle bandbreedte |
| SSB Breed | 32 kbps | HF SSB, betere kwaliteit |
| FM | 64 kbps | VHF/UHF FM |
| Hifi | 128 kbps | Breed spectrum ontvangst |
| PCM | — | Geen compressie, laagste latency |

---

## Bekende beperkingen

- Audio init mislukt op systemen zonder audio apparaten (servers) — gebruik een USB audio adapter of virtueel audio apparaat
- Pi 3 (32-bit) vereist handmatige compilatie met `linux-arm` RID
- `Tmds.DBus.Protocol` security warning is een indirecte Avalonia dependency — niet functioneel

---

## Licentie

MIT — zie [LICENSE](LICENSE)

Gebruikt:
- [miniaudio](https://miniaud.io) — MIT, David Reid
- [Concentus](https://github.com/lostromb/concentus) — BSD, Opus/Xiph.Org
- [RNNoise.Net](https://github.com/Yellow-Dog-Man/RNNoise.Net) — BSD
- [Avalonia UI](https://avaloniaui.net) — MIT
