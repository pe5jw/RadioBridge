# RadioBridge v0.9

**Audio over netwerk voor amateur radio** — volledige duplex TCP audio streaming  
met Opus compressie, RNNoise ruisonderdrukking en seriële poort bridging (CAT/PTT).

---

## Nieuw in v0.9

| Versie | Wat veranderd |
|--------|---------------|
| v0.7   | Eerste release — PCM audio, WinForms, Windows only |
| v0.8   | Opus compressie via Concentus (pure C#) |
| **v0.9** | **Cross-platform (Windows + Linux) — miniaudio native shim, Avalonia UI** |

---

## Architectuur

```
┌─────────────────────────────────────────────────────────┐
│  C# / .NET 10 (net10.0 — geen -windows)                 │
│  ┌────────────┐  ┌──────────────┐  ┌─────────────────┐  │
│  │ AudioEngine│  │  OpusCodec   │  │  SerialBridge   │  │
│  │  (TCP I/O) │  │ (Concentus)  │  │ (System.IO.Ports│  │
│  └─────┬──────┘  └──────────────┘  └─────────────────┘  │
│        │ P/Invoke                                        │
│  ┌─────▼──────────────────────────────────────────────┐  │
│  │  MiniAudioEngine.cs  (P/Invoke wrapper)            │  │
│  └─────┬──────────────────────────────────────────────┘  │
│        │                                                 │
│  ┌─────▼──────────────────────────────────────────────┐  │
│  │  radiobridge_audio.dll / .so  (C, miniaudio v0.11) │  │
│  └────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────┘
```

**Audio libraries:**
- **miniaudio v0.11.26** — single-header C library, geen externe systeemafhankelijkheden
- **Concentus 2.2.2** — pure C# Opus codec
- **RNNoise.Net 1.0.0** — neurale ruisonderdrukking (48kHz mono)
- **Avalonia 11.2.5** — cross-platform GUI

---

## Presets

| Naam | Sample rate | Codec | Bitrate | Toepassing |
|------|-------------|-------|---------|------------|
| PCM Telefoon | 8 kHz mono | PCM | ~128 kbps | Lokaal netwerk |
| PCM Spraak | 16 kHz mono | PCM | ~256 kbps | Lokaal netwerk |
| PCM SSB mono | 48 kHz mono | PCM | ~768 kbps | Lokaal netwerk |
| **Opus Telefoon** | 8 kHz mono | Opus | 6–12 kbps | Lage bandbreedte |
| **Opus Spraak** | 16 kHz mono | Opus | 12–24 kbps | Standaard SSB |
| **Opus SSB mono** | 48 kHz mono | Opus | 24–64 kbps | Hoge kwaliteit |
| **Opus SSB stereo** | 48 kHz stereo | Opus | 64–128 kbps | Stereo ontvangst |
| **Opus RNNoise** | 48 kHz mono | Opus + filter | 32 kbps | Ruisonderdrukking |

---

## Bouwen

### Windows

**Vereiste:** .NET 10 SDK + een C compiler voor de native DLL.

```powershell
# 1. Bouw native DLL (kies één optie):
#    a) Visual Studio Developer Command Prompt:
cd C:\dev\RadioBridge\native
build_windows.bat

#    b) MinGW/MSYS2 (gcc moet in PATH staan):
gcc -O2 -shared -IC:\dev\RadioBridge\native \
    C:\dev\RadioBridge\native\radiobridge_audio.c \
    -o C:\dev\RadioBridge\native\win-x64\radiobridge_audio.dll \
    -lole32 -lwinmm

# 2. Bouw en start de applicatie:
cd C:\dev\RadioBridge\RadioBridge
dotnet run
```

### Linux

```bash
# 1. Bouw native .so (gcc vereist):
cd /pad/naar/RadioBridge/native
gcc -O2 -shared -fPIC -I. radiobridge_audio.c \
    -o linux-x64/radiobridge_audio.so -ldl -lpthread -lm

# 2. Bouw en start:
cd ../RadioBridge
dotnet run
```

---

## Gebruik

### Server (bij de radio)

1. Start RadioBridge, kies **Server**
2. Stel audio-apparaten in (microfoon / luidspreker van de radio)
3. Optioneel: seriële poorten voor CAT en PTT
4. Klik **Server starten** — de applicatie wacht op een client
5. Klik **🔥 Firewall openen** als de Windows firewall TCP blokkeert

### Client (op afstand)

1. Start RadioBridge, kies **Client**
2. Vul het IP-adres van de server in
3. Kies een preset (codec/kwaliteit)
4. Optioneel: ruisfilter inschakelen
5. Klik **Verbinden**

---

## Seriële poort bridging

| Poort | Gebruik | Standaard TCP |
|-------|---------|---------------|
| Poort 1 | CAT (transceiver control, bijv. CI-V) | 5001 |
| Poort 2 | PTT (keying) | 5002 |

Op de client verbindt u uw CAT/PTT software met `localhost:5001` / `localhost:5002`.

---

## Native audio shim

De `radiobridge_audio` shim zit in `native/`:

```
native/
├── radiobridge_audio.h       # C API header
├── radiobridge_audio.c       # miniaudio implementatie
├── miniaudio.h               # miniaudio v0.11.26 (single-header)
├── win-x64/
│   └── radiobridge_audio.dll # Windows x64 binary
├── linux-x64/
│   └── radiobridge_audio.so  # Linux x64 binary
└── build_windows.bat         # Windows bouw-script
```

---

## Afhankelijkheden

| Pakket | Versie | Doel |
|--------|--------|------|
| Concentus | 2.2.2 | Opus codec (pure C#) |
| RNNoise.Net | 1.0.0 | Neurale ruisonderdrukking |
| System.IO.Ports | 10.0.0 | Seriële poorten |
| Avalonia | 11.2.5 | Cross-platform GUI |
| miniaudio | 0.11.26 | Native audio (C, single-header) |

---

## Licentie

MIT — zie LICENSE
