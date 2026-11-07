# RadioBridge v0.8 — Audio + Serieel over netwerk

2-way audio streaming én seriële poort bridging tussen twee pc's via TCP.
Bedoeld voor remote radio bediening: audio (microfoon/speaker of VAC) én CAT/PTT over het netwerk.

---

## Functies

- **Opus compressie** — factor 10–20 minder bandbreedte t.o.v. ruwe PCM
- **Audio bridge** — volledig duplex, instelbare kwaliteit/latency
- **Seriële poort bridge** — 2 poorten (CAT + PTT), instelbaar baud/TCP-poort
- **Ruisonderdrukking** — RNNoise (neuraal netwerk) bij 48kHz mono, anders noise gate
- **PCM fallback** — ruwe PCM presets nog steeds beschikbaar
- **Automatische firewall** — UAC-prompt voor firewall regels
- **Server herstart automatisch** na verbroken verbinding

---

## Vereisten

- Windows 10/11
- [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) → "Run desktop apps" → Windows x64

---

## Bouwen

```
cd RadioBridge
dotnet restore
dotnet build -c Release
```

---

## Audio presets

### Opus (aanbevolen — lage bandbreedte)

| Preset | Sample rate | Bitrate | Geschikt voor |
|--------|-------------|---------|---------------|
| Opus Telefoon | 8 kHz mono | 6–12 kbps | Noodverbinding, zeer trage link |
| Opus Spraak | 16 kHz mono | 12–24 kbps | SSB spraak, normale verbinding |
| Opus SSB mono | 48 kHz mono | 24–64 kbps | SSB/digimode, hoge kwaliteit |
| Opus SSB stereo | 48 kHz stereo | 64–128 kbps | Stereo ontvangst |
| Opus RNNoise | 48 kHz mono | 32 kbps | Met neuraal netwerk ruisfilter |

### PCM (geen compressie)

| Preset | Sample rate | Bitrate |
|--------|-------------|---------|
| PCM Telefoon | 8 kHz mono | ~128 kbps |
| PCM Spraak | 16 kHz mono | ~256 kbps |
| PCM SSB mono | 48 kHz mono | ~768 kbps |
| PCM SSB stereo | 48 kHz stereo | ~1.5 Mbps |

> Opus gebruikt een factor 10–20 minder bandbreedte bij vergelijkbare kwaliteit.
> RNNoise (neuraal netwerk ruisfilter) werkt alleen bij 48kHz mono presets.

---

## Verbinding

### Remote pc (server)
- Modus: **Server**
- TCP Poort: `7000`
- Opname/Afspelen: audio apparaten of VB-Audio VAC
- Optioneel: seriële poorten voor CAT/PTT
- Klik **Verbinden**

### Local pc (client)
- Modus: **Client**
- IP-adres: IP van server (LAN / Tailscale / VPN)
- TCP Poort: `7000`
- Kies preset (Opus aanbevolen)
- Klik **Verbinden**

> De client kiest de codec, kwaliteit en ruisonderdrukking.
> De server past zich automatisch aan.

---

## Seriële poorten

| Instelling | Beschrijving |
|------------|-------------|
| COM poort | Lokale seriële poort |
| Baud | 300–115200, beide kanten gelijk |
| TCP poort | Aparte TCP verbinding per poort |

---

## Afhankelijkheden

| Pakket | Versie | Gebruik |
|--------|--------|---------|
| [NAudio](https://github.com/naudio/NAudio) | 2.2.1 | Audio opname/afspelen |
| [Concentus](https://github.com/lostromb/concentus) | 2.2.2 | Opus codec (pure C#) |
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
| v0.7 | Firewall helper, RNNoise neuraal netwerk filter |
| v0.8 | **Opus compressie** via Concentus — 6–128 kbps |
