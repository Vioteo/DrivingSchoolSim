using System;

namespace DrivingSchool.Audio
{
    /// <summary>
    /// A passing train heard from its nearest point: low rumble, rolling roar and the "ta-dam ta-dam" of the wheel
    /// sets crossing a rail joint (two bogies per car, two axles per bogie). Rhythm = speed / car length.
    /// </summary>
    public sealed class TrainSoundSynth : ISampleSource
    {
        public const float CarLengthM = 24.5f;
        /// <summary>Axles along one car, metres from its front.</summary>
        public static readonly float[] AxleOffsetsM = { 2.2f, 4.6f, 19.9f, 22.3f };

        const int Block = 32;
        readonly int sr;
        AudioRng rng;
        float tSpeed, speed, level, tLevel, distance, brown, clackEnv;
        int nextAxle;
        OnePole rumbleLp; Biquad roar, thunk, click;

        public TrainSoundSynth(int sampleRate, uint seed = 21)
        {
            if (sampleRate < 8000) throw new ArgumentOutOfRangeException(nameof(sampleRate));
            sr = sampleRate; rng = new AudioRng(seed);
            rumbleLp.SetLowpass(120f, sr); roar.SetBandpass(600f, 0.7f, sr);
            thunk.SetBandpass(220f, 6f, sr); click.SetBandpass(1800f, 8f, sr);
        }

        /// <summary>Clacks heard so far (tests).</summary>
        public int Clacks { get; private set; }

        public void Set(float speedMps, bool running)
        {
            tSpeed = Dsp.Safe(speedMps, 0f, 70f);
            tLevel = running ? 1f : 0f;
        }

        /// <summary>Joint clacks per second at a speed: four axles per car.</summary>
        public static float ClacksPerSecond(float speedMps) => Math.Max(0f, speedMps) / CarLengthM * AxleOffsetsM.Length;

        public void Render(float[] mono, int count)
        {
            if (mono == null) return;
            count = Math.Min(count, mono.Length);
            float dt = 1f / sr;
            float k = Dsp.Coef(0.3f, sr), clackDecay = MathF.Exp(-1f / (0.006f * sr));
            for (int i = 0; i < count; i++)
            {
                speed += (tSpeed - speed) * k; level += (tLevel - level) * k;
                float n = rng.Next();
                float excite = 0f;
                if (level > 0.01f)
                {
                    distance += speed * dt;
                    if (distance >= CarLengthM) distance -= CarLengthM;
                    float at = AxleOffsetsM[nextAxle];
                    bool wrapped = nextAxle == 0 && distance > AxleOffsetsM[AxleOffsetsM.Length - 1];
                    if (distance >= at && !wrapped)
                    {
                        nextAxle = (nextAxle + 1) % AxleOffsetsM.Length;
                        clackEnv = 0.8f + 0.2f * rng.Next01(); excite = 1f; Clacks++;
                    }
                }
                clackEnv *= clackDecay;
                float sp = Dsp.Clamp01(speed / 25f);
                brown = brown * 0.995f + n * 0.05f;
                float x = rumbleLp.Low(brown) * 1.5f * sp + roar.Process(n) * 0.25f * sp;
                x += thunk.Process(excite * 40f + n * clackEnv) * 0.6f + click.Process(n * clackEnv) * 1.2f;
                mono[i] = Dsp.Soft(x * level);
            }
        }
    }
}
