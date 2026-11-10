using System;

namespace RadioBridge;

/// <summary>
/// Genereert een sinusgolf als 16-bit PCM op de achtergrond.
/// Thread-safe: alle properties zijn atomair leesbaar, phase loopt door.
/// </summary>
internal sealed class ToneGenerator
{
    // ── Parameters (schrijf vanuit UI-thread, lees vanuit audio-thread) ────────
    private volatile bool  _enabled;
    private volatile float _frequency = 1000f;  // Hz
    private volatile float _amplitude = 0.25f;  // 0.0 – 1.0

    public bool  Enabled   { get => _enabled;   set => _enabled   = value; }
    public float Frequency { get => _frequency; set => _frequency = Math.Clamp(value, 50f, 8000f); }
    public float Amplitude { get => _amplitude; set => _amplitude = Math.Clamp(value, 0f, 1f); }

    // ── Phase accumulator ──────────────────────────────────────────────────────
    private double _phase;     // 0 .. 2π
    private int    _sampleRate = 48000;
    private int    _channels   = 1;

    public void Configure(int sampleRate, int channels)
    {
        _sampleRate = sampleRate;
        _channels   = channels;
        _phase      = 0;
    }

    /// <summary>
    /// Mixt de testtoon door de gegeven PCM buffer (in-place, 16-bit LE).
    /// Werkt op elk buffer-formaat dat de AudioEngine gebruikt.
    /// </summary>
    public void Mix(byte[] buf, int byteLen)
    {
        if (!_enabled) return;

        float freq = _frequency;
        float amp  = _amplitude;
        int   sr   = _sampleRate;
        int   ch   = _channels;

        double phaseStep = 2.0 * Math.PI * freq / sr;
        int    frames    = byteLen / (2 * ch);   // 2 bytes per sample

        for (int f = 0; f < frames; f++)
        {
            short tone = (short)(Math.Sin(_phase) * amp * short.MaxValue);
            _phase += phaseStep;
            if (_phase >= 2 * Math.PI) _phase -= 2 * Math.PI;

            for (int c = 0; c < ch; c++)
            {
                int offset = (f * ch + c) * 2;
                if (offset + 1 >= byteLen) break;

                // Lees bestaande sample
                short existing = (short)(buf[offset] | (buf[offset + 1] << 8));

                // Mix (clip naar short range)
                int mixed = existing + tone;
                if (mixed >  short.MaxValue) mixed =  short.MaxValue;
                if (mixed <  short.MinValue) mixed =  short.MinValue;

                buf[offset]     = (byte)(mixed & 0xFF);
                buf[offset + 1] = (byte)((mixed >> 8) & 0xFF);
            }
        }
    }
}
