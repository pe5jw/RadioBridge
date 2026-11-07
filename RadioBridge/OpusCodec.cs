using Concentus;
using Concentus.Enums;

namespace RadioBridge;

/// <summary>
/// Opus encoder/decoder wrapper using Concentus (pure C#).
///
/// Opus requirements:
///   - Sample rates: 8000, 12000, 16000, 24000, 48000 Hz
///   - Frame sizes: 2.5, 5, 10, 20, 40, 60 ms
///   - Channels: 1 (mono) or 2 (stereo)
///   - Input: 16-bit PCM interleaved
///
/// We always encode at the preset's frame size (BufferMs).
/// If BufferMs is not a valid Opus frame size we round up to the nearest valid one.
/// </summary>
public class OpusCodec : IDisposable
{
    // Valid Opus frame sizes in ms
    private static readonly int[] ValidFrameMs = { 10, 20, 40, 60 };

    private IOpusEncoder? _encoder;
    private IOpusDecoder? _decoder;

    public bool   IsEnabled    { get; private set; }
    public int    SampleRate   { get; private set; }
    public int    Channels     { get; private set; }
    public int    FrameMs      { get; private set; }
    public int    FrameSamples { get; private set; }  // samples per channel per frame
    public int    Bitrate      { get; private set; }

    // Leftover PCM from previous encode call (incomplete frame)
    private short[] _inputBuf  = Array.Empty<short>();
    private int     _inputPos  = 0;

    // Output buffer for encoded packets
    private byte[]  _encodeBuf = new byte[4000];

    // ── Init ──────────────────────────────────────────────────────────────────

    public void Initialize(bool enabled, int sampleRate, int channels, int bufferMs, int bitrate = 0)
    {
        Dispose();
        IsEnabled  = false;
        _inputPos  = 0;

        if (!enabled) return;

        // Validate sample rate
        int[] validRates = { 8000, 12000, 16000, 24000, 48000 };
        if (!validRates.Contains(sampleRate))
        {
            // Fallback: find nearest valid rate
            sampleRate = validRates.OrderBy(r => Math.Abs(r - sampleRate)).First();
        }

        // Round up to nearest valid Opus frame size
        int frameMs = ValidFrameMs.FirstOrDefault(f => f >= bufferMs, 60);

        SampleRate   = sampleRate;
        Channels     = channels;
        FrameMs      = frameMs;
        FrameSamples = sampleRate * frameMs / 1000;

        // Default bitrate: scale with sample rate and channels
        // 8kHz mono ~8kbps, 16kHz mono ~16kbps, 48kHz mono ~32kbps, 48kHz stereo ~64kbps
        Bitrate = bitrate > 0 ? bitrate : Math.Max(8000, sampleRate * channels / 24);

        _inputBuf = new short[FrameSamples * channels * 4]; // buffer 4 frames

        _encoder = OpusCodecFactory.CreateEncoder(sampleRate, channels, OpusApplication.OPUS_APPLICATION_VOIP);
        _encoder.Bitrate = Bitrate;
        _encoder.SignalType = OpusSignalType.OPUS_SIGNAL_VOICE;
        _encoder.Complexity = 5;

        _decoder = OpusCodecFactory.CreateDecoder(sampleRate, channels);

        IsEnabled = true;
    }

    // ── Encode ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Encode a buffer of raw 16-bit PCM bytes.
    /// Returns a list of encoded Opus packets (one per frame).
    /// Leftover samples are buffered for the next call.
    /// </summary>
    public List<byte[]> Encode(byte[] pcmBytes, int byteCount)
    {
        var packets = new List<byte[]>();
        if (_encoder == null || !IsEnabled) return packets;

        int totalSamples = byteCount / 2; // 16-bit = 2 bytes per sample
        int samplesPerFrame = FrameSamples * Channels;

        // Copy new samples into buffer
        for (int i = 0; i < totalSamples; i++)
        {
            if (_inputPos >= _inputBuf.Length)
            {
                // Buffer overflow — encode what we have and reset
                _inputPos = 0;
            }
            _inputBuf[_inputPos++] = (short)(pcmBytes[i * 2] | (pcmBytes[i * 2 + 1] << 8));
        }

        // Encode complete frames
        int pos = 0;
        while (pos + samplesPerFrame <= _inputPos)
        {
            int encoded = _encoder.Encode(_inputBuf, pos, FrameSamples, _encodeBuf, 0, _encodeBuf.Length);
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
        var pcmOut = new short[samplesPerFrame];

        int decoded = _decoder.Decode(packet, 0, packetLen, pcmOut, 0, FrameSamples);
        if (decoded <= 0) return Array.Empty<byte>();

        int outBytes = decoded * Channels * 2;
        var result = new byte[outBytes];
        for (int i = 0; i < decoded * Channels; i++)
        {
            result[i * 2]     = (byte)(pcmOut[i] & 0xFF);
            result[i * 2 + 1] = (byte)((pcmOut[i] >> 8) & 0xFF);
        }
        return result;
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    public void Dispose()
    {
        _encoder = null;
        _decoder = null;
        IsEnabled = false;
        _inputPos = 0;
    }

    // ── Info ──────────────────────────────────────────────────────────────────

    public string InfoString()
    {
        if (!IsEnabled) return "PCM (geen compressie)";
        return $"Opus  {Bitrate / 1000}kbps  {FrameMs}ms frames";
    }
}
