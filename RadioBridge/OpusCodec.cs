using Concentus;
using Concentus.Enums;

namespace RadioBridge;

/// <summary>
/// Opus encoder/decoder wrapper using Concentus (pure C#, no native dependencies).
///
/// Opus requirements:
///   - Sample rates: 8000, 12000, 16000, 24000, 48000 Hz
///   - Frame sizes: 10, 20, 40, 60 ms
///   - Channels: 1 (mono) or 2 (stereo)
///   - Input/output: 16-bit PCM interleaved shorts
/// </summary>
public class OpusCodec : IDisposable
{
    private static readonly int[] ValidFrameMs = { 10, 20, 40, 60 };

    private IOpusEncoder? _encoder;
    private IOpusDecoder? _decoder;

    public bool IsEnabled    { get; private set; }
    public int  SampleRate   { get; private set; }
    public int  Channels     { get; private set; }
    public int  FrameMs      { get; private set; }
    public int  FrameSamples { get; private set; }  // samples per channel per frame
    public int  Bitrate      { get; private set; }

    // Leftover input samples between DataAvailable callbacks
    private short[] _inputBuf = Array.Empty<short>();
    private int     _inputPos = 0;

    // Reusable encode output buffer (max Opus packet = 1275 bytes)
    private readonly byte[] _encodeBuf = new byte[1275];

    // ── Init ──────────────────────────────────────────────────────────────────

    public void Initialize(bool enabled, int sampleRate, int channels, int bufferMs, int bitrate = 0)
    {
        Dispose();
        IsEnabled = false;
        _inputPos = 0;

        if (!enabled) return;

        // Clamp to nearest valid Opus frame size
        int frameMs = ValidFrameMs.FirstOrDefault(f => f >= bufferMs, 60);

        SampleRate   = sampleRate;
        Channels     = channels;
        FrameMs      = frameMs;
        FrameSamples = sampleRate * frameMs / 1000;

        // Default bitrate: scale with sample rate and channels
        Bitrate = bitrate > 0 ? bitrate : Math.Max(8000, sampleRate * channels / 24);

        // Buffer holds up to 4 frames worth of leftover samples
        _inputBuf = new short[FrameSamples * channels * 4];

        _encoder = OpusCodecFactory.CreateEncoder(sampleRate, channels, OpusApplication.OPUS_APPLICATION_VOIP);
        _encoder.Bitrate    = Bitrate;
        _encoder.SignalType = OpusSignal.OPUS_SIGNAL_VOICE;
        _encoder.Complexity = 5;

        _decoder = OpusCodecFactory.CreateDecoder(sampleRate, channels);

        IsEnabled = true;
    }

    // ── Encode ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Encode a buffer of raw 16-bit PCM bytes.
    /// Returns a list of encoded Opus packets (one per complete frame).
    /// Leftover samples are buffered for the next call.
    /// </summary>
    public List<byte[]> Encode(byte[] pcmBytes, int byteCount)
    {
        var packets = new List<byte[]>();
        if (_encoder == null || !IsEnabled) return packets;

        int sampleCount      = byteCount / 2;          // 2 bytes per sample
        int samplesPerFrame  = FrameSamples * Channels;

        // Append new samples to the leftover buffer
        for (int i = 0; i < sampleCount; i++)
        {
            if (_inputPos >= _inputBuf.Length) _inputPos = 0; // safety
            _inputBuf[_inputPos++] = (short)(pcmBytes[i * 2] | (pcmBytes[i * 2 + 1] << 8));
        }

        // Encode all complete frames
        int pos = 0;
        while (pos + samplesPerFrame <= _inputPos)
        {
            var frameSpan = new ReadOnlySpan<short>(_inputBuf, pos, samplesPerFrame);
            int encoded   = _encoder.Encode(frameSpan, FrameSamples, _encodeBuf, _encodeBuf.Length);
            if (encoded > 0)
            {
                var packet = new byte[encoded];
                Array.Copy(_encodeBuf, packet, encoded);
                packets.Add(packet);
            }
            pos += samplesPerFrame;
        }

        // Keep leftover samples at start of buffer
        int leftover = _inputPos - pos;
        if (leftover > 0 && pos > 0)
            Array.Copy(_inputBuf, pos, _inputBuf, 0, leftover);
        _inputPos = leftover;

        return packets;
    }

    // ── Decode ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Decode a single Opus packet to 16-bit PCM bytes.
    /// </summary>
    public byte[] Decode(byte[] packet, int packetLen)
    {
        if (_decoder == null || !IsEnabled) return Array.Empty<byte>();

        int samplesPerFrame = FrameSamples * Channels;
        var pcmOut          = new short[samplesPerFrame];

        int decoded = _decoder.Decode(
            new ReadOnlySpan<byte>(packet, 0, packetLen),
            pcmOut,
            FrameSamples);

        if (decoded <= 0) return Array.Empty<byte>();

        int outBytes = decoded * Channels * 2;
        var result   = new byte[outBytes];
        for (int i = 0; i < decoded * Channels; i++)
        {
            result[i * 2]     = (byte)(pcmOut[i] & 0xFF);
            result[i * 2 + 1] = (byte)((pcmOut[i] >> 8) & 0xFF);
        }
        return result;
    }

    // ── Info ──────────────────────────────────────────────────────────────────

    public string InfoString() =>
        IsEnabled ? $"Opus {Bitrate / 1000}kbps  {FrameMs}ms frames" : "PCM (geen compressie)";

    // ── Dispose ───────────────────────────────────────────────────────────────

    public void Dispose()
    {
        _encoder?.Dispose();
        _decoder?.Dispose();
        _encoder  = null;
        _decoder  = null;
        IsEnabled = false;
        _inputPos = 0;
    }
}
