using System;

namespace DrivingSchool.Audio
{
    public struct RoadSoundInput
    {
        public float speedMps;
        /// <summary>Tyre squeal, 0…1 (see <see cref="TyreSound.SquealIntensity"/>).</summary>
        public float squeal01;
        /// <summary>Water on the road: hiss and spray.</summary>
        public float wet01;
        /// <summary>Snow on the road: crunch.</summary>
        public float snow01;
        public float cabin01;
    }

    /// <summary>Tyre and air noise of the player car: rolling roar, wet hiss, snow crunch, wind and tyre squeal.</summary>
    public sealed class RoadSoundSynth : ISampleSource
    {
        const int Block = 32;
        readonly int sr;
        AudioRng rng;
        float tSpeed, tSqueal, tWet, tSnow, tCabin;
        float speed, squeal, wet, snow, cabin, grain, lfo;
        OnePole roll, rumble; Biquad wetBand, crunch, wind, sq1, sq2, sq3, cabinLp;

        public RoadSoundSynth(int sampleRate, uint seed = 3)
        {
            if (sampleRate < 8000) throw new ArgumentOutOfRangeException(nameof(sampleRate));
            sr = sampleRate; rng = new AudioRng(seed);
            rumble.SetLowpass(90f, sr);
            wetBand.SetBandpass(3500f, 0.8f, sr);
            crunch.SetBandpass(1500f, 1f, sr);
            cabinLp.SetLowpass(12000f, 0.7f, sr);
        }

        public void Set(RoadSoundInput i)
        {
            tSpeed = Dsp.Safe(Math.Abs(i.speedMps), 0f, 80f);
            tSqueal = Dsp.Safe(i.squeal01, 0f, 1f);
            tWet = Dsp.Safe(i.wet01, 0f, 1f);
            tSnow = Dsp.Safe(i.snow01, 0f, 1f);
            tCabin = Dsp.Safe(i.cabin01, 0f, 1f);
        }

        public void Render(float[] mono, int count)
        {
            if (mono == null) return;
            count = Math.Min(count, mono.Length);
            for (int i = 0; i < count; i += Block)
            {
                int m = Math.Min(Block, count - i);
                float v = speed;
                roll.SetLowpass(150f + 40f * v, sr);
                wind.SetBandpass(250f + 25f * v, 0.6f, sr);
                float f = 900f * (1f + 0.04f * lfo);         // squeal wanders a little in pitch
                sq1.SetBandpass(f, 30f, sr); sq2.SetBandpass(f * 1.47f, 30f, sr); sq3.SetBandpass(f * 2.2f, 30f, sr);
                cabinLp.SetLowpass(Dsp.Lerp(12000f, 2500f, cabin), 0.7f, sr);
                float k = Dsp.Coef(0.05f, sr), ks = Dsp.Coef(0.03f, sr);
                float rollA = MathF.Pow(Math.Min(v / 25f, 1.3f), 1.2f);
                float windA = (v / 35f) * (v / 35f) * 0.45f * (1f - 0.45f * cabin);
                float wetA = Math.Min(1f, v / 15f) * 0.35f, snowA = Math.Min(1f, v / 2f) * 0.6f;
                float grainRate = (25f + 6f * v) / sr, grainDecay = MathF.Exp(-1f / (0.008f * sr));

                for (int j = 0; j < m; j++)
                {
                    speed += (tSpeed - speed) * k; squeal += (tSqueal - squeal) * ks;
                    wet += (tWet - wet) * k; snow += (tSnow - snow) * k; cabin += (tCabin - cabin) * k;
                    float n = rng.Next();
                    float x = (roll.Low(n) * 0.5f + rumble.Low(n) * 1.2f) * rollA;
                    x += wetBand.Process(n) * wet * wetA;
                    if (rng.Next01() < grainRate) grain = 0.5f + 0.5f * rng.Next01();
                    grain *= grainDecay;
                    x += crunch.Process(n) * grain * snow * snowA;
                    x += wind.Process(n) * windA;
                    if (squeal > 0.002f)
                        x += (sq1.Process(n) + 0.6f * sq2.Process(n) + 0.35f * sq3.Process(n)) * MathF.Pow(squeal, 1.2f) * 9f;
                    mono[i + j] = Dsp.Soft(cabinLp.Process(x));
                }
                lfo = (lfo + rng.Next() * 0.15f) * 0.98f;
            }
        }
    }

    public static class TyreSound
    {
        /// <summary>
        /// How loudly a tyre squeals, 0…1: longitudinal slip past ≈8 % (wheelspin, locked wheel) or a slip angle past
        /// ≈4° (cornering at the limit) — where TireModel starts to saturate (x = 1 at κ ≈ 1/16, α ≈ μ/12). <paramref name="slidingSpeedMps"/> — how fast the rubber slides over the road
        /// (no sound when both car and wheel stand still). <paramref name="squealFactor"/> — the surface: 1 dry asphalt,
        /// ≈0.3 wet, 0 snow and ice (rubber on snow does not squeal).
        /// </summary>
        public static float SquealIntensity(float slipRatio, float slipAngleRad, float slidingSpeedMps, float squealFactor)
        {
            if (!Dsp.Finite(slipRatio) || !Dsp.Finite(slipAngleRad) || !Dsp.Finite(slidingSpeedMps)) return 0f;
            float lon = Math.Max(0f, Math.Abs(slipRatio) - 0.08f) / 0.2f;
            float lat = Math.Max(0f, Math.Abs(slipAngleRad) - 0.07f) / 0.15f;
            float s = Dsp.Clamp01(lon + lat) * Dsp.Clamp01(Math.Abs(slidingSpeedMps) / 3f);
            return s * Dsp.Clamp01(squealFactor);
        }
    }
}
