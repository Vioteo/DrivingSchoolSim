using System;

namespace DrivingSchool.Audio
{
    /// <summary>Source of mono samples rendered on the audio thread. Parameters are set from the main thread.</summary>
    public interface ISampleSource
    {
        /// <summary>Writes <paramref name="count"/> mono samples (−1…1) into <paramref name="mono"/> starting at 0.</summary>
        void Render(float[] mono, int count);
    }

    /// <summary>Deterministic xorshift noise: the same seed gives the same sound (tests, previews).</summary>
    public struct AudioRng
    {
        uint s;
        public AudioRng(uint seed) { s = seed == 0 ? 0x9E3779B9u : seed; }
        /// <summary>Uniform value in [−1, 1).</summary>
        public float Next()
        {
            if (s == 0) s = 0x9E3779B9u;
            s ^= s << 13; s ^= s >> 17; s ^= s << 5;
            return (s & 0xFFFFFF) / 8388608f - 1f;
        }
        /// <summary>Uniform value in [0, 1).</summary>
        public float Next01() => 0.5f * (Next() + 1f);
    }

    /// <summary>One-pole low-pass; High() is the complementary high-pass.</summary>
    public struct OnePole
    {
        float a, z;
        public void SetLowpass(float hz, int sampleRate)
        {
            float f = Math.Min(Math.Max(hz, 1f), sampleRate * 0.45f);
            a = 1f - MathF.Exp(-2f * MathF.PI * f / sampleRate);
        }
        public float Low(float x) { z += a * (x - z); return z; }
        public float High(float x) => x - Low(x);
        public void Reset() { z = 0f; }
    }

    /// <summary>RBJ biquad (direct form I): band-pass with 0 dB peak or resonant low-pass.</summary>
    public struct Biquad
    {
        float b0, b1, b2, a1, a2, x1, x2, y1, y2;

        public void SetBandpass(float hz, float q, int sampleRate)
        {
            float w = 2f * MathF.PI * Math.Min(Math.Max(hz, 5f), sampleRate * 0.45f) / sampleRate;
            float al = MathF.Sin(w) / (2f * Math.Max(q, 0.05f)), a0 = 1f + al;
            b0 = al / a0; b1 = 0f; b2 = -al / a0; a1 = -2f * MathF.Cos(w) / a0; a2 = (1f - al) / a0;
        }

        public void SetLowpass(float hz, float q, int sampleRate)
        {
            float w = 2f * MathF.PI * Math.Min(Math.Max(hz, 5f), sampleRate * 0.45f) / sampleRate;
            float al = MathF.Sin(w) / (2f * Math.Max(q, 0.05f)), c = MathF.Cos(w), a0 = 1f + al;
            b0 = (1f - c) * 0.5f / a0; b1 = (1f - c) / a0; b2 = b0; a1 = -2f * c / a0; a2 = (1f - al) / a0;
        }

        public float Process(float x)
        {
            float y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
            x2 = x1; x1 = x; y2 = y1; y1 = y;
            if (float.IsNaN(y) || float.IsInfinity(y)) { x1 = x2 = y1 = y2 = 0f; return 0f; }
            return y;
        }
    }

    public static class Dsp
    {
        /// <summary>Per-sample coefficient of an exponential smoother with time constant <paramref name="seconds"/>.</summary>
        public static float Coef(float seconds, int sampleRate) => seconds <= 0f ? 1f : 1f - MathF.Exp(-1f / (seconds * sampleRate));
        /// <summary>Soft limiter: linear near zero, never exceeds ±1.</summary>
        public static float Soft(float x) => x / (1f + Math.Abs(x));
        public static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        /// <summary>NaN/∞ → fallback, then clamp.</summary>
        public static float Safe(float v, float lo, float hi, float fallback = 0f) => !Finite(v) ? fallback : v < lo ? lo : v > hi ? hi : v;
    }
}
