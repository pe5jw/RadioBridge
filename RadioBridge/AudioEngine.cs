using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RadioBridge;

// ── AudioPreset ───────────────────────────────────────────────────────────────

public record AudioPreset(
    string Name,
    int    SampleRate,
    int    BitsPerSample,
    int    Channels,
    int    BufferMs,
    int    PlaybackLatencyMs,
    bool   UseOpus    = false,
    int    OpusBitrate = 0)
{
    public override string ToString() => Name;
}

// ── AudioPresets ──────────────────────────────────────────────────────────────

public static class AudioPresets
{
    public static readonly AudioPreset[] All =
    [
        // ── Lage latency presets (10ms) — beste voor lokaal netwerk ─────────
        new("⚡ Opus Spraak LL  — 16kHz mono   10ms   16 kbps",  16000, 16, 1,  10,  20, true, 16000),
        new("⚡ Opus SSB LL     — 48kHz mono   10ms   32 kbps",  48000, 16, 1,  10,  20, true, 32000),
        new("⚡ PCM Spraak LL   — 16kHz 16bit mono   10ms ~256 kbps", 16000, 16, 1, 10, 20),
        new("⚡ PCM SSB LL      — 48kHz 16bit mono   10ms ~768 kbps", 48000, 16, 1, 10, 20),

        // ── Standaard presets (20ms) ─────────────────────────────────────────
        new("PCM  Telefoon    —  8kHz 16bit mono   20ms  ~128 kbps",   8000, 16, 1,  20,  40),
        new("PCM  Spraak      — 16kHz 16bit mono   20ms  ~256 kbps",  16000, 16, 1,  20,  40),
        new("PCM  SSB mono    — 48kHz 16bit mono   20ms  ~768 kbps",  48000, 16, 1,  20,  40),
        new("PCM  SSB stereo  — 48kHz 16bit stereo 20ms  ~1.5 Mbps",  48000, 16, 2,  20,  40),

        // ── Opus presets ─────────────────────────────────────────────────────
        new("Opus Telefoon    —  8kHz mono   20ms    6 kbps",   8000, 16, 1,  20,  40, true,  6000),
        new("Opus Telefoon    —  8kHz mono   20ms   12 kbps",   8000, 16, 1,  20,  40, true, 12000),
        new("Opus Spraak      — 16kHz mono   20ms   12 kbps",  16000, 16, 1,  20,  40, true, 12000),
        new("Opus Spraak      — 16kHz mono   20ms   24 kbps",  16000, 16, 1,  20,  40, true, 24000),
        new("Opus SSB mono    — 48kHz mono   20ms   24 kbps",  48000, 16, 1,  20,  40, true, 24000),
        new("Opus SSB mono    — 48kHz mono   20ms   32 kbps",  48000, 16, 1,  20,  40, true, 32000),
        new("Opus SSB mono    — 48kHz mono   20ms   64 kbps",  48000, 16, 1,  20,  40, true, 64000),
        new("Opus SSB stereo  — 48kHz stereo 20ms   64 kbps",  48000, 16, 2,  20,  40, true, 64000),
        new("Opus SSB stereo  — 48kHz stereo 20ms  128 kbps",  48000, 16, 2,  20,  40, true,128000),
        new("Opus RNNoise     — 48kHz mono   20ms   32 kbps",  48000, 16, 1,  20,  40, true, 32000),
        new("Opus RNNoise     — 48kHz mono   10ms   32 kbps",  48000, 16, 1,  10,  20, true, 32000),
    ];

    public static AudioPreset Default => All[0]; // ⚡ Opus Spraak LL 16kHz 10ms

    public static AudioPreset FindOrCreate(int sampleRate, int bits, int channels,
        int bufferMs, int latencyMs, bool useOpus, int opusBitrate)
    {
        var match = System.Linq.Enumerable.FirstOrDefault(All, p =>
            p.SampleRate    == sampleRate &&
            p.BitsPerSample == bits &&
            p.Channels      == channels &&
            p.BufferMs      == bufferMs &&
            p.UseOpus       == useOpus &&
            p.OpusBitrate   == opusBitrate);
        return match ?? new AudioPreset(
            $"Custom {(useOpus ? $"Opus {opusBitrate/1000}kbps" : "PCM")} " +
            $"{sampleRate}Hz {bits}bit {channels}ch {bufferMs}ms",
            sampleRate, bits, channels, bufferMs, latencyMs, useOpus, opusBitrate);
    }
}

// ── Config types ──────────────────────────────────────────────────────────────

public class SerialPortConfig
{
    public bool   Enabled { get; set; }
    public string ComPort { get; set; } = "";
    public int    Baud    { get; set; } = 9600;
    public int    TcpPort { get; set; }
}

public class BridgeConfig
{
    public int    SampleRate        { get; set; }
    public int    BitsPerSample     { get; set; }
    public int    Channels          { get; set; }
    public int    BufferMs          { get; set; }
    public int    PlaybackLatencyMs { get; set; }
    public bool   NoiseSuppress     { get; set; }
    public string PresetName        { get; set; } = "";
    public bool   UseOpus           { get; set; }
    public int    OpusBitrate       { get; set; }
    public SerialPortConfig Port1   { get; set; } = new();
    public SerialPortConfig Port2   { get; set; } = new();
}

// ── AudioEngine (v0.9 — miniaudio cross-platform) ────────────────────────────

public class AudioEngine : IDisposable
{
    // ── TCP / stream state ────────────────────────────────────────────────────
    private TcpListener?   _listener;
    private TcpClient?     _tcpClient;
    private NetworkStream? _stream;

    // ── Virtuele seriële poorten (client kant) ────────────────────────────────
    private VirtualSerialBridge? _vSerial1;
    private VirtualSerialBridge? _vSerial2;

    /// <summary>Welk com0com "inside" poort voor CAT (Windows). Leeg = auto PTY (Linux).</summary>
    public string VirtualComPort1 { get; set; } = "";
    /// <summary>Welk com0com "inside" poort voor PTT (Windows). Leeg = auto PTY (Linux).</summary>
    public string VirtualComPort2 { get; set; } = "";

    private CancellationTokenSource _cts = new();
    private bool _running;
    private bool _disconnectFired;

    // ── Audio state ───────────────────────────────────────────────────────────
    private AudioPreset          _preset  = AudioPresets.Default;
    private readonly NoiseFilter _filter  = new();
    private readonly OpusCodec   _codec   = new();
    private readonly ToneGenerator _tone  = new();

    // Native callbacks — must be kept alive (GC root) while audio is running
    private MiniAudioEngine.CaptureCallback?  _captureDelegate;
    private MiniAudioEngine.PlaybackCallback? _playbackDelegate;

    // Playback ring-buffer (lock-free SPSC approximation via byte queue)
    private readonly object _playLock  = new();
    private readonly Queue<byte[]> _playQueue = new();
    // Max queue: 3 frames à 20ms = 60ms buffer — daarboven droppen om latency te beperken
    private int _maxPlayQueueBytes = 1; // wordt ingesteld in StartAudio op 3 × frameBytes
    private int _playQueueBytes;

    // ── Gain (volume) ─────────────────────────────────────────────────────────
    /// <summary>Opname-gain: 0.0 = stil, 1.0 = origineel, >1.0 = versterken (max 4.0)</summary>
    public float CaptureGain  { get; set; } = 1.0f;
    /// <summary>Afspeel-gain: 0.0 = stil, 1.0 = origineel, >1.0 = versterken (max 4.0)</summary>
    public float PlaybackGain { get; set; } = 1.0f;

    // VU-meter: peak level (0.0–1.0) van het laatste audio-frame
    private float _captureLevel;
    private float _playbackLevel;

    // Level events zijn gethrottled: max 20× per seconde (elke 50 ms)
    private long _lastLevelTick = 0;
    private const long LevelIntervalTicks = 10_000_000 / 20; // 50 ms in ticks

    // ── Events ────────────────────────────────────────────────────────────────
    public event Action<string>?       StatusChanged;
    public event Action<string>?       Error;
    public event Action?               ClientDisconnected;
    public event Action<BridgeConfig>? ConfigReceived;
    /// <summary>VU-meter update: (captureLevel 0–1, playbackLevel 0–1) — max 20×/s</summary>
    public event Action<float, float>? LevelChanged;

    public SerialPortConfig SerialConfig1 { get; set; } = new();
    public SerialPortConfig SerialConfig2 { get; set; } = new();

    private void Log(string msg) => StatusChanged?.Invoke(msg);

    // ── Server ────────────────────────────────────────────────────────────────

    public async Task StartServerAsync(int port, int captureIdx, int playbackIdx)
    {
        try
        {
            Log($"Luisteren op TCP poort {port}...");
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Server.SetSocketOption(SocketOptionLevel.Socket,
                SocketOptionName.ReuseAddress, true);
            _listener.Start();
            _tcpClient = await _listener.AcceptTcpClientAsync(_cts.Token);
            _tcpClient.NoDelay = true;
            _stream = _tcpClient.GetStream();
            _disconnectFired = false;
            Log($"Verbinding van {_tcpClient.Client.RemoteEndPoint}");

            var cfg = await ReceiveConfigAsync(_cts.Token);
            if (cfg == null) { Error?.Invoke("Geen configuratie ontvangen van client."); return; }

            _preset = AudioPresets.FindOrCreate(
                cfg.SampleRate, cfg.BitsPerSample, cfg.Channels,
                cfg.BufferMs, cfg.PlaybackLatencyMs, cfg.UseOpus, cfg.OpusBitrate);

            Log($"Client preset: {cfg.PresetName}");
            Log($"Codec: {(cfg.UseOpus ? $"Opus {cfg.OpusBitrate / 1000}kbps" : "PCM (geen compressie)")}");
            Log($"Ruisonderdrukking: {(cfg.NoiseSuppress ? "AAN" : "UIT")}");

            cfg.Port1 = SerialConfig1;
            cfg.Port2 = SerialConfig2;
            await SendConfigAsync(cfg, _cts.Token);

            ConfigReceived?.Invoke(cfg);
            StartAudio(captureIdx, playbackIdx, cfg.NoiseSuppress, cfg.UseOpus, cfg.OpusBitrate);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Error?.Invoke($"Server fout: {ex.Message}"); }
    }

    // ── Client ────────────────────────────────────────────────────────────────

    public async Task StartClientAsync(string host, int port,
        int captureIdx, int playbackIdx,
        AudioPreset preset, bool noiseSuppress)
    {
        _preset = preset;
        try
        {
            Log($"Verbinden met {host}:{port}...");
            _tcpClient = new TcpClient { NoDelay = true };
            await _tcpClient.ConnectAsync(host, port, _cts.Token);
            _stream = _tcpClient.GetStream();
            Log($"Verbonden met {host}:{port}");

            var cfg = new BridgeConfig
            {
                SampleRate        = preset.SampleRate,
                BitsPerSample     = preset.BitsPerSample,
                Channels          = preset.Channels,
                BufferMs          = preset.BufferMs,
                PlaybackLatencyMs = preset.PlaybackLatencyMs,
                NoiseSuppress     = noiseSuppress,
                PresetName        = preset.Name,
                UseOpus           = preset.UseOpus,
                OpusBitrate       = preset.OpusBitrate,
            };
            await SendConfigAsync(cfg, _cts.Token);
            Log($"Configuratie verstuurd: {(preset.UseOpus ? $"Opus {preset.OpusBitrate / 1000}kbps" : "PCM")}");

            var cfgBack = await ReceiveConfigAsync(_cts.Token);
            if (cfgBack != null)
            {
                ConfigReceived?.Invoke(cfgBack);
                await StartVirtualSerialAsync(host, cfgBack);
            }

            StartAudio(captureIdx, playbackIdx, noiseSuppress, preset.UseOpus, preset.OpusBitrate);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Error?.Invoke($"Verbinding mislukt: {ex.Message}"); }
    }

    // ── Config handshake ──────────────────────────────────────────────────────

    private async Task SendConfigAsync(BridgeConfig cfg, CancellationToken ct)
    {
        var json  = JsonSerializer.Serialize(cfg);
        var bytes = Encoding.UTF8.GetBytes(json);
        var len   = BitConverter.GetBytes(bytes.Length);
        await _stream!.WriteAsync(len, ct);
        await _stream!.WriteAsync(bytes, ct);
        await _stream!.FlushAsync(ct);
    }

    private async Task<BridgeConfig?> ReceiveConfigAsync(CancellationToken ct)
    {
        var lenBuf = new byte[4];
        int r = 0;
        while (r < 4)
        {
            int n = await _stream!.ReadAsync(lenBuf.AsMemory(r, 4 - r), ct);
            if (n == 0) return null;
            r += n;
        }
        int len = BitConverter.ToInt32(lenBuf, 0);
        if (len <= 0 || len > 4096) return null;

        var buf = new byte[len];
        r = 0;
        while (r < len)
        {
            int n = await _stream!.ReadAsync(buf.AsMemory(r, len - r), ct);
            if (n == 0) return null;
            r += n;
        }
        return JsonSerializer.Deserialize<BridgeConfig>(Encoding.UTF8.GetString(buf));
    }

    // ── Virtuele seriële poorten (client) ────────────────────────────────────

    private async Task StartVirtualSerialAsync(string host, BridgeConfig cfg)
    {
        if (cfg.Port1.Enabled)
        {
            _vSerial1 = new VirtualSerialBridge();
            _vSerial1.StatusChanged += msg => Log(msg);
            _vSerial1.Error         += msg => Log($"⚠ {msg}");
            await _vSerial1.StartAsync(host, cfg.Port1.TcpPort, VirtualComPort1);
            if (!string.IsNullOrEmpty(_vSerial1.VirtualPortName))
                Log($"CAT virtuele poort: {_vSerial1.VirtualPortName}");
        }

        if (cfg.Port2.Enabled)
        {
            _vSerial2 = new VirtualSerialBridge();
            _vSerial2.StatusChanged += msg => Log(msg);
            _vSerial2.Error         += msg => Log($"⚠ {msg}");
            await _vSerial2.StartAsync(host, cfg.Port2.TcpPort, VirtualComPort2);
            if (!string.IsNullOrEmpty(_vSerial2.VirtualPortName))
                Log($"PTT virtuele poort: {_vSerial2.VirtualPortName}");
        }
    }

    // ── Tone injection ────────────────────────────────────────────────────────

    /// <summary>
    /// Schakel de testtoon in of uit.
    /// frequency: Hz (50–8000), amplitude: 0.0–1.0
    /// </summary>
    public void SetTone(bool enabled, float frequency = 1000f, float amplitude = 0.25f)
    {
        _tone.Enabled   = enabled;
        _tone.Frequency = frequency;
        _tone.Amplitude = amplitude;
        if (_running)
            Log(enabled
                ? $"Testtoon: AAN  {frequency:F0} Hz  vol {amplitude * 100:F0}%"
                : "Testtoon: UIT");
    }

    // ── Noise toggle ─────────────────────────────────────────────────────────

    public void SetNoiseSuppression(bool enabled)
    {
        _filter.Initialize(enabled, _preset.SampleRate, _preset.Channels);
        if (_running)
            Log($"Ruisonderdrukking: {(enabled ? $"AAN ({_filter.Mode})" : "UIT")}");
    }

    // ── Stop ─────────────────────────────────────────────────────────────────

    public void Stop()
    {
        if (!_running && _tcpClient == null && _listener == null) return;
        _cts.Cancel();
        _running = false;

        // Stop native audio first
        try { MiniAudioEngine.rb_audio_stop();   } catch { }
        try { MiniAudioEngine.rb_audio_uninit(); } catch { }
        _captureDelegate  = null;
        _playbackDelegate = null;

        lock (_playLock) { _playQueue.Clear(); _playQueueBytes = 0; }

        try { _vSerial1?.Stop(); _vSerial1?.Dispose(); } catch { }
        try { _vSerial2?.Stop(); _vSerial2?.Dispose(); } catch { }
        _vSerial1 = null; _vSerial2 = null;

        try { _stream?.Close(); } catch { }
        _stream = null;

        try
        {
            if (_tcpClient?.Client != null && _tcpClient.Client.Connected)
                _tcpClient.Client.SetSocketOption(
                    SocketOptionLevel.Socket, SocketOptionName.Linger,
                    new LingerOption(true, 0));
            _tcpClient?.Close();
        }
        catch { }
        _tcpClient = null;

        try { _listener?.Stop(); } catch { }
        _listener = null;

        try { _filter.Dispose(); } catch { }
        try { _codec.Dispose();  } catch { }

        _cts = new CancellationTokenSource();
        Log("Gestopt.");
    }

    // ── Audio setup ───────────────────────────────────────────────────────────

    private void StartAudio(int captureIdx, int playbackIdx,
        bool noiseSuppress, bool useOpus, int opusBitrate)
    {
        _filter.Initialize(noiseSuppress, _preset.SampleRate, _preset.Channels);
        _codec.Initialize(useOpus, _preset.SampleRate, _preset.Channels,
            _preset.BufferMs, opusBitrate);
        _tone.Configure(_preset.SampleRate, _preset.Channels);

        // Buffer size in frames: BufferMs × SampleRate / 1000
        int bufFrames = (_preset.SampleRate * _preset.BufferMs) / 1000;

        // Max playback queue: 3 frames — daarboven droppen om latency klein te houden
        int frameBytes = bufFrames * _preset.Channels * 2; // 16-bit PCM
        _maxPlayQueueBytes = frameBytes * 3;

        // Keep delegates alive (GC root) for the duration of audio streaming
        _captureDelegate  = OnCapture;
        _playbackDelegate = OnPlayback;

        int rc = MiniAudioEngine.rb_audio_init(
            captureIdx, playbackIdx,
            _preset.SampleRate, _preset.Channels, bufFrames,
            _captureDelegate, _playbackDelegate);

        if (rc != 0)
        {
            Error?.Invoke($"Audio init mislukt (code {rc}): {MiniAudioEngine.GetLastError()}");
            return;
        }

        rc = MiniAudioEngine.rb_audio_start();
        if (rc != 0)
        {
            Error?.Invoke($"Audio start mislukt (code {rc}): {MiniAudioEngine.GetLastError()}");
            MiniAudioEngine.rb_audio_uninit();
            return;
        }

        _running = true;

        int kbps = useOpus
            ? opusBitrate / 1000
            : (_preset.SampleRate * _preset.BitsPerSample * _preset.Channels) / 1000;

        Log($"Audio actief  |  {_preset.SampleRate}Hz {_preset.BitsPerSample}bit " +
            $"{_preset.Channels}ch  buf {_preset.BufferMs}ms  ~{kbps} kbps  " +
            $"[{_codec.InfoString()}]" +
            (noiseSuppress ? $"  ruis: {_filter.Mode}" : "  ruis: uit"));

        _ = Task.Run(() => ReceiveLoopAsync(_cts.Token));
    }

    // ── Capture callback (called by native audio thread) ─────────────────────

    private void OnCapture(IntPtr buffer, int frames)
    {
        if (!_running || _stream == null) return;
        try
        {
            int samples = frames * _preset.Channels;
            int byteLen = samples * 2; // 16-bit = 2 bytes per sample
            var pcm = new short[samples];
            Marshal.Copy(buffer, pcm, 0, samples);

            // Convert to byte[] for existing filter / codec APIs
            var bytes = new byte[byteLen];
            Buffer.BlockCopy(pcm, 0, bytes, 0, byteLen);

            // Testtoon injecteren vóór filter en codec
            _tone.Mix(bytes, byteLen);

            // Capture gain toepassen + VU meten
            ApplyGainAndMeasure(pcm, samples, CaptureGain, ref _captureLevel, isCap: true);
            Buffer.BlockCopy(pcm, 0, bytes, 0, byteLen);

            if (_filter.IsEnabled)
                _filter.Process(bytes, byteLen, _preset.Channels);

            if (_codec.IsEnabled)
            {
                var packets = _codec.Encode(bytes, byteLen);
                foreach (var packet in packets)
                {
                    _stream.Write(BitConverter.GetBytes((ushort)packet.Length), 0, 2);
                    _stream.Write(packet, 0, packet.Length);
                }
            }
            else
            {
                _stream.Write(BitConverter.GetBytes(byteLen), 0, 4);
                _stream.Write(bytes, 0, byteLen);
            }
        }
        catch (ObjectDisposedException) { _running = false; }
        catch (System.IO.IOException)   { _running = false; }
        catch { }
    }

    // ── Playback callback (called by native audio thread) ─────────────────────

    private void OnPlayback(IntPtr buffer, int frames)
    {
        int samples  = frames * _preset.Channels;
        int byteLen  = samples * 2;

        byte[]? chunk = null;
        lock (_playLock)
        {
            if (_playQueue.Count > 0)
            {
                chunk = _playQueue.Dequeue();
                _playQueueBytes -= chunk.Length;
            }
        }

        if (chunk != null && chunk.Length == byteLen)
        {
            var pcm = new short[samples];
            Buffer.BlockCopy(chunk, 0, pcm, 0, byteLen);
            ApplyGainAndMeasure(pcm, samples, PlaybackGain, ref _playbackLevel, isCap: false);
            Marshal.Copy(pcm, 0, buffer, samples);
        }
        else
        {
            // Silence
            _playbackLevel = 0f;
            for (int i = 0; i < byteLen; i++)
                Marshal.WriteByte(buffer + i, 0);
        }
    }

    // ── Gain + VU meting ─────────────────────────────────────────────────────

    /// <summary>
    /// Past gain toe op PCM samples (in-place) en meet de peak als fraction 0–1.
    /// Vuurt LevelChanged event als het interval verstreken is.
    /// </summary>
    private void ApplyGainAndMeasure(short[] pcm, int count, float gain, ref float level, bool isCap)
    {
        float peak = 0f;
        if (Math.Abs(gain - 1.0f) > 0.001f)
        {
            for (int i = 0; i < count; i++)
            {
                float s = pcm[i] * gain;
                // Soft-clip bij ±32767
                if (s >  32767f) s =  32767f;
                if (s < -32768f) s = -32768f;
                pcm[i] = (short)s;
                float a = s < 0 ? -s : s;
                if (a > peak) peak = a;
            }
        }
        else
        {
            for (int i = 0; i < count; i++)
            {
                float a = pcm[i] < 0 ? -pcm[i] : pcm[i];
                if (a > peak) peak = a;
            }
        }

        level = peak / 32768f;

        // Throttle: stuur event max 20× per seconde
        long now = DateTime.UtcNow.Ticks;
        long last = System.Threading.Interlocked.Read(ref _lastLevelTick);
        if (now - last >= LevelIntervalTicks)
        {
            System.Threading.Interlocked.Exchange(ref _lastLevelTick, now);
            LevelChanged?.Invoke(_captureLevel, _playbackLevel);
        }
    }

    // ── Receive loop ─────────────────────────────────────────────────────────

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _stream != null)
            {
                if (_codec.IsEnabled)
                    await ReceiveOpusFrameAsync(ct);
                else
                    await ReceivePcmFrameAsync(ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException)    { }
        catch (Exception ex)
        {
            if (_running)
            {
                Error?.Invoke($"Verbinding verbroken: {ex.Message}");
                if (!_disconnectFired) { _disconnectFired = true; ClientDisconnected?.Invoke(); }
            }
        }
    }

    private async Task ReceiveOpusFrameAsync(CancellationToken ct)
    {
        var hdr = new byte[2];
        int r = 0;
        while (r < 2)
        {
            int n = await _stream!.ReadAsync(hdr.AsMemory(r, 2 - r), ct);
            if (n == 0) { FireDisconnect(); throw new System.IO.EndOfStreamException("Verbinding verbroken"); }
            r += n;
        }
        int len = BitConverter.ToUInt16(hdr, 0);
        if (len <= 0 || len > 4000) throw new Exception($"Ongeldig Opus packet: {len}");

        var packet = new byte[len];
        r = 0;
        while (r < len)
        {
            int n = await _stream!.ReadAsync(packet.AsMemory(r, len - r), ct);
            if (n == 0) throw new System.IO.EndOfStreamException("Verbinding verbroken");
            r += n;
        }

        var pcm = _codec.Decode(packet, len);
        if (pcm.Length > 0) EnqueuePlayback(pcm);
    }

    private async Task ReceivePcmFrameAsync(CancellationToken ct)
    {
        var hdr = new byte[4];
        int r = 0;
        while (r < 4)
        {
            int n = await _stream!.ReadAsync(hdr.AsMemory(r, 4 - r), ct);
            if (n == 0) { FireDisconnect(); throw new System.IO.EndOfStreamException("Verbinding verbroken"); }
            r += n;
        }
        int len = BitConverter.ToInt32(hdr, 0);
        if (len <= 0 || len > 65536) throw new Exception($"Ongeldige PCM frame: {len}");

        var buf = new byte[len];
        r = 0;
        while (r < len)
        {
            int n = await _stream!.ReadAsync(buf.AsMemory(r, len - r), ct);
            if (n == 0) throw new System.IO.EndOfStreamException("Verbinding verbroken");
            r += n;
        }
        EnqueuePlayback(buf);
    }

    private void EnqueuePlayback(byte[] data)
    {
        lock (_playLock)
        {
            // Drop frames tot de queue weer klein genoeg is
            while (_playQueueBytes + data.Length > _maxPlayQueueBytes && _playQueue.Count > 0)
            {
                var old = _playQueue.Dequeue();
                _playQueueBytes -= old.Length;
            }
            _playQueue.Enqueue(data);
            _playQueueBytes += data.Length;
        }
    }

    private void FireDisconnect()
    {
        if (!_disconnectFired) { _disconnectFired = true; ClientDisconnected?.Invoke(); }
    }

    // ── Device enumeration ────────────────────────────────────────────────────

    public static List<(int Index, string Name)> GetCaptureDevices()
    {
        var list = new List<(int, string)>();
        list.Add((-1, "⭐ Standaard opname-apparaat"));
        var names = MiniAudioEngine.GetDeviceNames(true);
        for (int i = 0; i < names.Length; i++)
            list.Add((i, names[i]));
        return list;
    }

    public static List<(int Index, string Name)> GetPlaybackDevices()
    {
        var list = new List<(int, string)>();
        list.Add((-1, "⭐ Standaard afspeel-apparaat"));
        var names = MiniAudioEngine.GetDeviceNames(false);
        for (int i = 0; i < names.Length; i++)
            list.Add((i, names[i]));
        return list;
    }

    public void Dispose() => Stop();
}
