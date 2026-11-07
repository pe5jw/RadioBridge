using System.Runtime.InteropServices;

namespace RadioBridge;

/// <summary>
/// Noise suppression filter for captured audio.
/// Uses RNNoise (neural network) when available, falls back to noise gate.
///
/// RNNoise requirements:
///   - Input: 16-bit PCM, mono, 48kHz ONLY
///   - Frame size: exactly 480 samples (10ms at 48kHz)
///   - Output: float array, same size
///
/// For other sample rates we use the noise gate fallback.
/// </summary>
public class NoiseFilter : IDisposable
{
    public enum FilterMode { Off, NoiseGate, RNNoise }

    private FilterMode _mode = FilterMode.Off;
    private object? _rnnoiseDenoiser;   // RNNoise.Net.Denoiser — late bound to avoid hard dependency
    private bool _rnnoiseAvailable;

    // Noise gate state
    private float _noiseFloor    = 0.01f;   // energy threshold below which audio is gated
    private float _attackCoef    = 0.9f;    // how fast gate opens
    private float _releaseCoef   = 0.995f;  // how fast gate closes
    private float _gateGain      = 0f;      // current gate gain (0=closed, 1=open)

    // RNNoise frame buffer (480 samples at 48kHz = 10ms)
    private const int RNNoiseFrameSamples = 480;
    private readonly short[] _rnInputBuf  = new short[RNNoiseFrameSamples];
    private readonly float[] _rnInputF    = new float[RNNoiseFrameSamples];
    private readonly float[] _rnOutputF   = new float[RNNoiseFrameSamples];
    private int _rnBufPos = 0;

    public bool IsEnabled => _mode != FilterMode.Off;
    public FilterMode Mode => _mode;

    // ── Init ──────────────────────────────────────────────────────────────────

    public void Initialize(bool enabled, int sampleRate, int channels)
    {
        Cleanup();

        if (!enabled)
        {
            _mode = FilterMode.Off;
            return;
        }

        // RNNoise only works at 48kHz mono
        if (sampleRate == 48000 && channels == 1)
        {
            _rnnoiseAvailable = TryInitRNNoise();
        }

        _mode = _rnnoiseAvailable ? FilterMode.RNNoise : FilterMode.NoiseGate;
        _gateGain = 0f;
        _rnBufPos = 0;
    }

    private bool TryInitRNNoise()
    {
        try
        {
            // Late binding so app doesn't crash if RNNoise.Net is missing
            var type = Type.GetType("RNNoise.Net.Denoiser, RNNoise.Net");
            if (type == null) return false;
            _rnnoiseDenoiser = Activator.CreateInstance(type);
            return _rnnoiseDenoiser != null;
        }
        catch { return false; }
    }

    // ── Process ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Process a buffer of 16-bit PCM samples in-place.
    /// Buffer contains interleaved samples if stereo.
    /// </summary>
    public void Process(byte[] buffer, int bytesRecorded, int channels)
    {
        if (_mode == FilterMode.Off) return;

        if (_mode == FilterMode.RNNoise && channels == 1)
            ProcessRNNoise(buffer, bytesRecorded);
        else
            ProcessNoiseGate(buffer, bytesRecorded);
    }

    // ── RNNoise ───────────────────────────────────────────────────────────────

    private void ProcessRNNoise(byte[] buffer, int bytesRecorded)
    {
        if (_rnnoiseDenoiser == null) return;

        int sampleCount = bytesRecorded / 2; // 16-bit = 2 bytes per sample
        int bufPos = 0;

        // Convert input bytes to short samples and process in 480-sample frames
        for (int i = 0; i < sampleCount; i++)
        {
            short s = (short)(buffer[bufPos] | (buffer[bufPos + 1] << 8));
            bufPos += 2;
            _rnInputBuf[_rnBufPos++] = s;

            if (_rnBufPos == RNNoiseFrameSamples)
            {
                // Convert to float [-1, 1]
                for (int j = 0; j < RNNoiseFrameSamples; j++)
                    _rnInputF[j] = _rnInputBuf[j] / 32768f;

                // Call RNNoise via reflection
                try
                {
                    var method = _rnnoiseDenoiser!.GetType().GetMethod("Denoise",
                        new[] { typeof(float[]), typeof(float[]) });
                    method?.Invoke(_rnnoiseDenoiser, new object[] { _rnInputF, _rnOutputF });
                }
                catch
                {
                    // If RNNoise fails, pass through unchanged
                    Array.Copy(_rnInputF, _rnOutputF, RNNoiseFrameSamples);
                }

                // Convert back to 16-bit PCM and write to buffer
                // Find where in buffer this frame started
                int frameStart = (i - RNNoiseFrameSamples + 1) * 2;
                if (frameStart >= 0)
                {
                    for (int j = 0; j < RNNoiseFrameSamples; j++)
                    {
                        short outSample = (short)Math.Clamp(_rnOutputF[j] * 32768f, -32768f, 32767f);
                        int outPos = frameStart + j * 2;
                        buffer[outPos]     = (byte)(outSample & 0xFF);
                        buffer[outPos + 1] = (byte)((outSample >> 8) & 0xFF);
                    }
                }
                _rnBufPos = 0;
            }
        }
    }

    // ── Noise Gate ────────────────────────────────────────────────────────────

    private void ProcessNoiseGate(byte[] buffer, int bytesRecorded)
    {
        int sampleCount = bytesRecorded / 2;

        // Calculate RMS energy of this buffer
        double sumSq = 0;
        for (int i = 0; i < bytesRecorded; i += 2)
        {
            float s = (short)(buffer[i] | (buffer[i + 1] << 8)) / 32768f;
            sumSq += s * s;
        }
        float rms = (float)Math.Sqrt(sumSq / sampleCount);

        // Update gate gain with attack/release envelope
        float targetGain = rms > _noiseFloor ? 1.0f : 0.0f;
        float coef = targetGain > _gateGain ? _attackCoef : _releaseCoef;
        _gateGain = _gateGain * coef + targetGain * (1f - coef);

        // Apply gain to samples
        for (int i = 0; i < bytesRecorded; i += 2)
        {
            short s = (short)(buffer[i] | (buffer[i + 1] << 8));
            short gated = (short)Math.Clamp(s * _gateGain, -32768f, 32767f);
            buffer[i]     = (byte)(gated & 0xFF);
            buffer[i + 1] = (byte)((gated >> 8) & 0xFF);
        }
    }

    // ── Settings ──────────────────────────────────────────────────────────────

    /// <summary>Set noise gate threshold (0.0 - 0.1, default 0.01)</summary>
    public void SetGateThreshold(float threshold)
    {
        _noiseFloor = Math.Clamp(threshold, 0.001f, 0.2f);
    }

    private void Cleanup()
    {
        if (_rnnoiseDenoiser is IDisposable d) d.Dispose();
        _rnnoiseDenoiser  = null;
        _rnnoiseAvailable = false;
    }

    public void Dispose() => Cleanup();
}
