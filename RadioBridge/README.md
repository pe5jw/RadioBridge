# RadioBridge v0.7 — Audio + Serieel over netwerk

2-way audio streaming én seriële poort bridging tussen twee pc's via TCP.
Bedoeld voor remote radio bediening: audio (microfoon/speaker of VAC) én CAT/PTT over het netwerk.

---

## Functies

- **Audio bridge** — volledig duplex, instelbare kwaliteit/latency
- **Seriële poort bridge** — 2 poorten (CAT + PTT), instelbaar baud/TCP-poort
- **Ruisonderdrukking** — RNNoise (neuraal netwerk) bij 48kHz mono, anders noise gate
- **Audio presets** — van 8kHz telefoon tot 48kHz stereo (~1.5 Mbps)
- **Automatische firewall** — UAC-prompt voor firewall regels
- **Server herstart automatisch** na verbroken verbinding

---

## Vereisten

- Windows 10/11
- .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0
- (Optioneel) VB-Audio Virtual Cable voor koppeling met SDR/radio software

---

## Bouwen

Dubbelklik op `build.bat` of voer uit in CMD:

```
cd RadioBridge
dotnet restore
dotnet build -c Release
```

De executable verschijnt in:
```
bin\Release\net10.0-windows\RadioBridge.exe
```

---

## Testen op één pc (loopback test)

### Stap 1 — Start de server

Open `RadioBridge.exe`, stel in:
- Modus: **Server**
- TCP Poort: `7000`
- Opname: microfoon of VAC CABLE Output
- Afspelen: speakers of VAC CABLE Input
- Klik **Verbinden**

### Stap 2 — Start een tweede instantie als client

Open een tweede `RadioBridge.exe`, stel in:
- Modus: **Client**
- IP-adres: `127.0.0.1`
- TCP Poort: `7000`
- Opname: een **ander** opname-apparaat dan de server
- Afspelen: een **ander** afspeel-apparaat dan de server
- Klik **Verbinden**

### Stap 3 — Verifieer

In het Log-venster zie je:
```
Server:  [HH:MM:SS]  Verbinding ontvangen van 127.0.0.1:xxxxx
         [HH:MM:SS]  Audio actief  |  16000Hz 16bit 1ch  buf 20ms  ~256 kbps

Client:  [HH:MM:SS]  Verbonden met 127.0.0.1:7000
         [HH:MM:SS]  Configuratie verstuurd naar server
         [HH:MM:SS]  Audio actief  |  16000Hz 16bit 1ch  buf 20ms  ~256 kbps
```

---

## Testen op twee pc's via LAN of Tailscale

### Remote pc (server)
- Modus: **Server**
- TCP Poort: `7000`
- Opname: CABLE Output (audio van radio software)
- Afspelen: CABLE Input (audio naar radio software)
- Serieel: Poort 1 → COM-poort van radio (CAT), baud instellen, TCP poort `5000`
- Klik **Verbinden**

### Local pc (client)
- Modus: **Client**
- IP-adres: IP van de Remote pc (LAN: `192.168.x.x` / Tailscale: `100.x.x.x`)
- TCP Poort: `7000`
- Opname: microfoon (TX audio)
- Afspelen: speakers of hoofdtelefoon (RX audio)
- Serieel: Poort 1 → virtuele COM-poort van logging software, zelfde TCP poort `5000`
- Klik **Verbinden**

> De client kiest de audio preset en ruisonderdrukking; de server past zich automatisch aan.
> De server stuurt de seriële poort configuratie naar de client bij verbinding.

### Firewall openen (eenmalig op de server)

Klik op de **🛡 Firewall** knop in RadioBridge — dit vraagt UAC toestemming en voegt de juiste regels toe voor alle geconfigureerde TCP poorten.

Of handmatig via PowerShell als Administrator:
```powershell
New-NetFirewallRule -DisplayName "RadioBridge Audio" `
  -Direction Inbound -Protocol TCP -LocalPort 7000 -Action Allow
New-NetFirewallRule -DisplayName "RadioBridge Serial 1" `
  -Direction Inbound -Protocol TCP -LocalPort 5000 -Action Allow
```

---

## Audio presets

| Preset           | Sample rate | Kanalen | Buffer | Bitrate    |
|------------------|-------------|---------|--------|------------|
| Telefoon         | 8 kHz       | mono    | 10–100ms | ~128 kbps |
| Spraak smal      | 16 kHz      | mono    | 10–100ms | ~256 kbps |
| Spraak stereo    | 16 kHz      | stereo  | 20–50ms  | ~512 kbps |
| SSB mono         | 48 kHz      | mono    | 10–50ms  | ~768 kbps |
| SSB stereo       | 48 kHz      | stereo  | 10–50ms  | ~1.5 Mbps |
| RNNoise ready    | 48 kHz      | mono    | 10–50ms  | ~768 kbps |

> RNNoise (neuraal netwerk ruisfilter) werkt alleen bij 48kHz mono. Bij andere presets valt de ruisonderdrukking terug op een noise gate.

---

## Seriële poorten

Twee seriële bruggen zijn beschikbaar (bijv. CAT + PTT):

| Instelling | Beschrijving |
|------------|-------------|
| COM poort  | Lokale seriële poort |
| Baud       | 300 – 115200, moet aan beide kanten gelijk zijn |
| TCP poort  | Aparte TCP verbinding per seriële brug |

---

## Afhankelijkheden

| Pakket | Versie | Gebruik |
|--------|--------|---------|
| [NAudio](https://github.com/naudio/NAudio) | 2.2.1 | Audio opname/afspelen |
| [RNNoise.Net](https://github.com/mozilla/rnnoise) | 1.0.0 | Neuraal netwerk ruisonderdrukking |
| System.IO.Ports | 10.0.0 | Seriële poort communicatie |

---

## Versiehistorie

| Versie | Wat is nieuw |
|--------|-------------|
| v0.1 | Audio over TCP (basis) |
| v0.2 | Ruisonderdrukking (noise gate) |
| v0.3 | Audio presets |
| v0.5 | Seriële poort bridge |
| v0.6 | Automatische server herstart na disconnect |
| v0.7 | Firewall helper, RNNoise neuraal netwerk filter, server stuurt serieel config naar client |
