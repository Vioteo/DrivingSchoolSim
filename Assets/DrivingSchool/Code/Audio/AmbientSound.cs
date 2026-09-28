using System;

namespace DrivingSchool.Audio
{
    public struct AmbientSoundInput
    {
        public float rain01, snow01;
        /// <summary>Daylight 0 (night) … 1 (day): birds by day, a quieter city at night.</summary>
        public float daylight01;
        /// <summary>Distant city hum: 1 in the city, less on a closed site.</summary>
        public float city01;
        /// <summary>Listener in the car: rain drums on the roof, outside noise is muffled.</summary>
        public float cabin01;
    }

    /// <summary>Non-positional background: distant city hum, wind gusts, rain (hiss and drops, or the roof in the cabin), birds by day.</summary>
    public sealed class AmbientSoundSynth : ISampleSource
    {
        const int Block = 32;
        readonly int sr;
        AudioRng rng;
        float tRain, tSnow, tDay, tCity, tCabin;
        float rain, snow, day, city, cabin;
        float brown, gust, gustTarget, gustTimer;
        OnePole humLp, rainHp, windLp; Biquad rainBand, dropBand, roofBand, cabinLp;
        float dropEnv, roofEnv;
        // Birds: a phrase of a few chirps.
        float birdTimer = 2f, noteT, noteLen, noteGap, noteF0, noteF1, notePhase; int notesLeft;

        public AmbientSoundSynth(int sampleRate, uint seed = 11)
        {
            if (sampleRate < 8000) throw new ArgumentOutOfRangeException(nameof(sampleRate));
            sr = sampleRate; rng = new AudioRng(seed);
            humLp.SetLowpass(180f, sr); rainHp.SetLowpass(1500f, sr); windLp.SetLowpass(350f, sr);
            rainBand.SetBandpass(5000f, 0.5f, sr); dropBand.SetBandpass(3000f, 4f, sr); roofBand.SetLowpass(1100f, 1.2f, sr);
            cabinLp.SetLowpass(12000f, 0.7f, sr);
        }

        public void Set(AmbientSoundInput i)
        {
            tRain = Dsp.Safe(i.rain01, 0f, 1f); tSnow = Dsp.Safe(i.snow01, 0f, 1f);
            tDay = Dsp.Safe(i.daylight01, 0f, 1f, 1f); tCity = Dsp.Safe(i.city01, 0f, 1f); tCabin = Dsp.Safe(i.cabin01, 0f, 1f);
        }

        /// <summary>True while a bird phrase is sounding (tests).</summary>
        public bool BirdSinging => notesLeft > 0;

        public void Render(float[] mono, int count)
        {
            if (mono == null) return;
            count = Math.Min(count, mono.Length);
            float dt = 1f / sr;
            for (int i = 0; i < count; i += Block)
            {
                int m = Math.Min(Block, count - i);
                float k = Dsp.Coef(0.5f, sr);
                cabinLp.SetLowpass(Dsp.Lerp(12000f, 1800f, cabin), 0.7f, sr);
                // Gusts: a new target every 1.5–5 s, approached slowly.
                gustTimer -= m * dt;
                if (gustTimer <= 0f) { gustTimer = 1.5f + 3.5f * rng.Next01(); gustTarget = rng.Next01(); }
                float humA = 0.25f * city * (0.6f + 0.4f * day) * (1f - 0.5f * cabin);
                float windA = (0.04f + 0.12f * snow + 0.06f * rain) * (1f - 0.6f * cabin);
                float dropRate = rain * Dsp.Lerp(80f, 250f, cabin) / sr;
                float dropDecay = MathF.Exp(-1f / (0.004f * sr)), roofDecay = MathF.Exp(-1f / (0.012f * sr));
                bool birdsAllowed = day > 0.6f && rain < 0.05f && snow < 0.05f && city > 0.05f;
                if (!birdsAllowed) notesLeft = 0;
                else if (notesLeft == 0)
                {
                    birdTimer -= m * dt;
                    if (birdTimer <= 0f) { birdTimer = 1.5f + 4.5f * rng.Next01(); notesLeft = 2 + (int)(rng.Next01() * 4f); NextNote(); }
                }

                for (int j = 0; j < m; j++)
                {
                    rain += (tRain - rain) * k; snow += (tSnow - snow) * k; day += (tDay - day) * k;
                    city += (tCity - city) * k; cabin += (tCabin - cabin) * k;
                    gust += (gustTarget - gust) * 0.00005f;
                    float n = rng.Next();
                    brown = brown * 0.995f + n * 0.05f;
                    float x = humLp.Low(brown) * 0.6f * humA;
                    x += windLp.Low(n) * windA * (0.3f + gust) * 4f;

                    // Rain: hiss outside, drops; in the cabin the drops hit the roof (lower, louder).
                    if (rain > 0.002f)
                    {
                        x += rainBand.Process(rainHp.High(n)) * rain * 0.2f * (1f - 0.7f * cabin);
                        if (rng.Next01() < dropRate)
                        {
                            if (rng.Next01() < cabin) roofEnv = 0.4f + 0.6f * rng.Next01();
                            else { dropEnv = 0.4f + 0.6f * rng.Next01(); dropBand.SetBandpass(2500f + 2000f * rng.Next01(), 4f, sr); }
                        }
                        dropEnv *= dropDecay; roofEnv *= roofDecay;
                        x += dropBand.Process(n) * dropEnv * 0.5f + roofBand.Process(n) * roofEnv * 0.9f;
                    }

                    if (notesLeft > 0)
                    {
                        if (noteT < noteLen)
                        {
                            float p = noteT / noteLen;
                            notePhase += (noteF0 + (noteF1 - noteF0) * p) * dt;
                            notePhase -= (float)Math.Floor(notePhase);
                            x += MathF.Sin(2f * MathF.PI * notePhase) * MathF.Sin(MathF.PI * p) * 0.05f * (1f - 0.7f * cabin);
                        }
                        noteT += dt;
                        if (noteT >= noteLen + noteGap) { notesLeft--; if (notesLeft > 0) NextNote(); }
                    }
                    mono[i + j] = Dsp.Soft(cabinLp.Process(x));
                }
            }
        }

        void NextNote()
        {
            noteT = 0f; noteLen = 0.05f + 0.04f * rng.Next01(); noteGap = 0.06f + 0.06f * rng.Next01();
            noteF0 = 2800f + 1400f * rng.Next01();
            noteF1 = noteF0 * (rng.Next() > 0f ? 1.4f : 0.75f);
        }
    }
}
