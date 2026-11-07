using NAudio.Wave;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace RadioBridge;

public record AudioPreset(string Name, int SampleRate, int BitsPerSample, int Channels, int BufferMs, int PlaybackLatencyMs, bool UseOpus = false, int OpusBitrate = 0)
{
    public WaveFormat ToWaveFormat() => new WaveFormat(SampleRate, BitsPerSample, Channels);
    public override string ToString() => Name;
}

public static class AudioPresets
{
    public static readonly AudioPreset[] All =
    [
        // ── PCM presets (geen compressie) ────────────────────────────────────
        new("PCM  Telefoon    —  8kHz 16bit mono   20ms  ~128 kbps",   8000, 16, 1,  20,  40),
        new("PCM  Spraak      — 16kHz 16bit mono   20ms  ~256 kbps",  16000, 16, 1,  20,  40),
        new("PCM  SSB mono    — 48kHz 16bit mono   20ms  ~768 kbps",  48000, 16, 1,  20,  40),
        new("PCM  SSB stereo  — 48kHz 16bit stereo 20ms  ~1.5 Mbps",  48000, 16, 2,  20,  40),

        // ── Opus presets ─────────────────────────────────────────────────────
        // 8kHz mono (narrowband) — smalste bandbreedte
        new("Opus Telefoon    —  8kHz mono   20ms    6 kbps",   8000, 16, 1,  20,  40, true,  6000),
        new("Opus Telefoon    —  8kHz mono   20ms   12 kbps",   8000, 16, 1,  20,  40, true, 12000),

        // 16kHz mono (wideband) — goed voor SSB spraak
        new("Opus Spraak      — 16kHz mono   20ms   12 kbps",  16000, 16, 1,  20,  40, true, 12000),
        new("Opus Spraak      — 16kHz mono   20ms   24 kbps",  16000, 16, 1,  20,  40, true, 24000),

        // 48kHz mono — SSB/digimode
        new("Opus SSB mono    — 48kHz mono   20ms   24 kbps",  48000, 16, 1,  20,  40, true, 24000),
        new("Opus SSB mono    — 48kHz mono   20ms   32 kbps",  48000, 16, 1,  20,  40, true, 32000),
        new("Opus SSB mono    — 48kHz mono   20ms   64 kbps",  48000, 16, 1,  20,  40, true, 64000),
        new("Opus SSB mono    — 48kHz mono   10ms   32 kbps",  48000, 16, 1,  10,  20, true, 32000),

        // 48kHz stereo
        new("Opus SSB stereo  — 48kHz stereo 20ms   64 kbps",  48000, 16, 2,  20,  40, true, 64000),
        new("Opus SSB stereo  — 48kHz stereo 20ms  128 kbps",  48000, 16, 2,  20,  40, true,128000),

        // RNNoise compatible (48kHz mono) — met neurale ruisfilter
        new("Opus RNNoise     — 48kHz mono   20ms   32 kbps",  48000, 16, 1,  20,  40, true, 32000),
        new("Opus RNNoise     — 48kHz mono   10ms   32 kbps",  48000, 16, 1,  10,  20, true, 32000),
    ];

    public static AudioPreset Default => All[6]; // Opus Spraak 16kHz mono 20ms 12kbps

    public static AudioPreset FindOrCreate(int sampleRate, int bits, int channels, int bufferMs, int latencyMs, bool useOpus, int opusBitrate)
    {
        var match = All.FirstOrDefault(p =>
            p.SampleRate    == sampleRate &&
            p.BitsPerSample == bits &&
            p.Channels      == channels &&
            p.BufferMs      == bufferMs &&
            p.UseOpus       == useOpus &&
            p.OpusBitrate   == opusBitrate);
        return match ?? new AudioPreset(
            $"Custom {(useOpus ? $"Opus {opusBitrate/1000}kbps" : "PCM")} {sampleRate}Hz {bits}bit {channels}ch {bufferMs}ms",
            sampleRate, bits, channels, bufferMs, latencyMs, useOpus, opusBitrate);
    }
}

// ── Config packet sent by client to server on connect ────────────────────────

public class SerialPortConfig
{
    public bool   Enabled  { get; set; }
    public string ComPort  { get; set; } = "";
    public int    Baud     { get; set; } = 9600;
    public int    TcpPort  { get; set; }
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
    // v0.8: Opus
    public bool   UseOpus           { get; set; }
    public int    OpusBitrate       { get; set; }
    // Serial config from server
    public SerialPortConfig Port1   { get; set; } = new();
    public SerialPortConfig Port2   { get; set; } = new();
}

// ── Engine ───────────────────────────────────────────────────────────────────

public class AudioEngine : IDisposable
{
    private WaveInEvent?          _waveIn;
    private WaveOutEvent?         _waveOut;
    private BufferedWaveProvider? _playbackBuffer;
    private TcpListener?          _listener;
    private TcpClient?            _tcpClient;
    private NetworkStream?        _stream;
    private CancellationTokenSource _cts = new();
    private bool _running;
    private bool _disconnectFired;
    private AudioPreset _preset = AudioPresets.Default;
    private readonly NoiseFilter _filter = new();
    private readonly OpusCodec   _codec  = new();

    public event Action<string>?          StatusChanged;
    public event Action<string>?          Error;
    public event Action?                  ClientDisconnected;
    public event Action<BridgeConfig>?    ConfigReceived;

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
            _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
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

    public async Task StartClientAsync(string host, int port, int captureIdx, int playbackIdx,
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
            if (cfgBack != null) ConfigReceived?.Invoke(cfgBack);

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

    // ── Noise toggle (live) ───────────────────────────────────────────────────

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

        try { _waveIn?.StopRecording(); } catch { }
        try { _waveOut?.Stop(); }         catch { }
        try { _stream?.Close(); }         catch { }
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

        try { _waveIn?.Dispose(); }  catch { }
        try { _waveOut?.Dispose(); } catch { }
        try { _filter.Dispose(); }   catch { }
        try { _codec.Dispose(); }    catch { }
        _waveIn  = null;
        _waveOut = null;

        _cts = new CancellationTokenSource();
        Log("Gestopt.");
    }

    // ── Audio setup ───────────────────────────────────────────────────────────

    private void StartAudio(int captureIdx, int playbackIdx, bool noiseSuppress, bool useOpus, int opusBitrate)
    {
        var fmt = _preset.ToWaveFormat();
        _filter.Initialize(noiseSuppress, _preset.SampleRate, _preset.Channels);
        _codec.Initialize(useOpus, _preset.SampleRate, _preset.Channels, _preset.BufferMs, opusBitrate);

        _playbackBuffer = new BufferedWaveProvider(fmt)
        {
            BufferDuration          = TimeSpan.FromMilliseconds(_preset.PlaybackLatencyMs * 4),
            DiscardOnBufferOverflow = true,
        };
        _waveOut = new WaveOutEvent
        {
            DeviceNumber   = playbackIdx,
            DesiredLatency = _preset.PlaybackLatencyMs,
        };
        _waveOut.Init(_playbackBuffer);
        _waveOut.Play();

        _waveIn = new WaveInEvent
        {
            DeviceNumber       = captureIdx,
            WaveFormat         = fmt,
            BufferMilliseconds = _preset.BufferMs,
        };
        _waveIn.DataAvailable    += OnDataAvailable;
        _waveIn.RecordingStopped += (s, e) =>
        {
            if (e.Exception != null) Error?.Invoke($"Opname gestopt: {e.Exception.Message}");
        };
        _waveIn.StartRecording();
        _running = true;

        int kbps = useOpus
            ? opusBitrate / 1000
            : (fmt.SampleRate * fmt.BitsPerSample * fmt.Channels) / 1000;

        string codecInfo  = _codec.InfoString();
        string filterInfo = noiseSuppress ? $"  ruis: {_filter.Mode}" : "  ruis: uit";
        Log($"Audio actief  |  {fmt.SampleRate}Hz {fmt.BitsPerSample}bit {fmt.Channels}ch  " +
            $"buf {_preset.BufferMs}ms  ~{kbps} kbps  [{codecInfo}]{filterInfo}");

        _ = Task.Run(() => ReceiveLoopAsync(_cts.Token));
    }

    // ── Send ─────────────────────────────────────────────────────────────────

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (!_running || _stream == null || e.BytesRecorded == 0) return;
        try
        {
            // Apply noise filter first (in-place on PCM)
            if (_filter.IsEnabled)
                _filter.Process(e.Buffer, e.BytesRecorded, _preset.Channels);

            if (_codec.IsEnabled)
            {
                // Opus: encode to packets, send each with 2-byte length prefix
                var packets = _codec.Encode(e.Buffer, e.BytesRecorded);
                foreach (var packet in packets)
                {
                    // Use 2-byte length for Opus packets (max ~4000 bytes)
                    _stream.Write(BitConverter.GetBytes((ushort)packet.Length), 0, 2);
                    _stream.Write(packet, 0, packet.Length);
                }
            }
            else
            {
                // PCM: 4-byte length prefix + raw samples
                _stream.Write(BitConverter.GetBytes(e.BytesRecorded), 0, 4);
                _stream.Write(e.Buffer, 0, e.BytesRecorded);
            }
        }
        catch (ObjectDisposedException) { _running = false; }
        catch (IOException)             { _running = false; }
        catch { }
    }

    // ── Receive ──────────────────────────────────────────────────────────────

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
        // 2-byte length prefix for Opus packets
        var hdr = new byte[2];
        int r = 0;
        while (r < 2)
        {
            int n = await _stream!.ReadAsync(hdr.AsMemory(r, 2 - r), ct);
            if (n == 0) { FireDisconnect(); throw new EndOfStreamException("Verbinding verbroken"); }
            r += n;
        }
        int len = BitConverter.ToUInt16(hdr, 0);
        if (len <= 0 || len > 4000) throw new Exception($"Ongeldig Opus packet: {len}");

        var packet = new byte[len];
        r = 0;
        while (r < len)
        {
            int n = await _stream!.ReadAsync(packet.AsMemory(r, len - r), ct);
            if (n == 0) throw new EndOfStreamException("Verbinding verbroken");
            r += n;
        }

        var pcm = _codec.Decode(packet, len);
        if (pcm.Length > 0)
            _playbackBuffer?.AddSamples(pcm, 0, pcm.Length);
    }

    private async Task ReceivePcmFrameAsync(CancellationToken ct)
    {
        // 4-byte length prefix for PCM frames
        var hdr = new byte[4];
        int r = 0;
        while (r < 4)
        {
            int n = await _stream!.ReadAsync(hdr.AsMemory(r, 4 - r), ct);
            if (n == 0) { FireDisconnect(); throw new EndOfStreamException("Verbinding verbroken"); }
            r += n;
        }
        int len = BitConverter.ToInt32(hdr, 0);
        if (len <= 0 || len > 65536) throw new Exception($"Ongeldige PCM frame: {len}");

        var buf = new byte[len];
        r = 0;
        while (r < len)
        {
            int n = await _stream!.ReadAsync(buf.AsMemory(r, len - r), ct);
            if (n == 0) throw new EndOfStreamException("Verbinding verbroken");
            r += n;
        }
        _playbackBuffer?.AddSamples(buf, 0, len);
    }

    private void FireDisconnect()
    {
        if (!_disconnectFired) { _disconnectFired = true; ClientDisconnected?.Invoke(); }
    }

    // ── Device enumeration ────────────────────────────────────────────────────

    public static List<(int Index, string Name)> GetCaptureDevices()
    {
        var list = new List<(int, string)>();
        list.Add((-1, "⭐ Windows standaard opname-apparaat"));
        for (int i = 0; i < WaveInEvent.DeviceCount; i++)
            list.Add((i, WaveInEvent.GetCapabilities(i).ProductName));
        return list;
    }

    public static List<(int Index, string Name)> GetPlaybackDevices()
    {
        var list = new List<(int, string)>();
        list.Add((-1, "⭐ Windows standaard afspeel-apparaat"));
        for (int i = 0; i < WaveOut.DeviceCount; i++)
            list.Add((i, WaveOut.GetCapabilities(i).ProductName));
        return list;
    }

    public void Dispose() => Stop();
}
