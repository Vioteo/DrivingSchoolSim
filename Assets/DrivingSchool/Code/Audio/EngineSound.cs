using System;

namespace DrivingSchool.Audio
{
    /// <summary>Character of an engine voice. Defaults: inline-4 petrol 1.6 l (the sedan).</summary>
    [Serializable]
    public sealed class EngineVoice
    {
        public int cylinders = 4;
        public float redlineRpm = 6500f;
        /// <summary>Noise seed: two cars with different seeds do not sound identical.</summary>
        public uint seed = 7;
        /// <summary>Main exhaust resonance, Hz (lower = deeper, bigger car).</summary>
        public float exhaustHz = 90f;
        /// <summary>Intake roar band under throttle, Hz.</summary>
        public float intakeHz = 1300f;
        /// <summary>Cycle-to-cycle combustion jitter at idle (0…0.3).</summary>
        public float roughness = 0.06f;
        /// <summary>Fixed cylinder-to-cylinder imbalance (0…0.2): gives the lumpy idle.</summary>
        public float imbalance = 0.07f;
        /// <summary>Starter motor whine, Hz.</summary>
        public float starterHz = 190f;
        public float gain = 1f;

        public EngineVoice Clone() => (EngineVoice)MemberwiseClone();
    }

    /// <summary>What the engine is doing right now (main thread → audio thread).</summary>
    public struct EngineSoundInput
    {
        public float rpm;
        /// <summary>Engine torque / full-load torque at this rpm, 0…1 (0 on overrun).</summary>
        public float load;
        /// <summary>Throttle plate, 0…1.</summary>
        public float throttle;
        /// <summary>Fuel is burning (engine running).</summary>
        public bool combustion;
        /// <summary>Starter motor engaged.</summary>
        public bool starter;
        /// <summary>0 outside, 1 listener in the cabin (muffled, lower).</summary>
        public float cabin01;
    }

    /// <summary>
    /// Procedural engine: each firing is a short exhaust pulse (a fixed few milliseconds wide, so low revs sound as
    /// separate beats and high revs merge into a tone) through an exhaust resonance, plus intake roar under throttle,
    /// valve-train noise and the starter motor. The pitch follows rpm exactly — the sound is a cue for starting off
    /// without the tachometer. Stopping the fuel lets the crank run down on compression beats (stall, key off).
    /// </summary>
    public sealed class EngineSoundSynth : ISampleSource
    {
        const int Block = 32;
        readonly EngineVoice v;
        readonly int sr;
        readonly float[] cylAmp;
        AudioRng rng;

        // Targets (written by Set on the main thread).
        float tRpm, tLoad, tThrottle, tCabin; bool tComb, tStarter;
        // Smoothed state (audio thread).
        float rpm, load, throttle, comb, starter, cabin;
        float phase, pulseT = 1f, pulseAmp, pulseWidth = 0.005f, starterPhase;
        int lastCyl = -1;
        OnePole body, dcBlock; Biquad exhaust, intake, mech, starterBand, cabinLp;

        public EngineSoundSynth(EngineVoice voice, int sampleRate)
        {
            if (sampleRate < 8000) throw new ArgumentOutOfRangeException(nameof(sampleRate));
            v = (voice ?? new EngineVoice()).Clone();
            v.cylinders = Math.Max(1, Math.Min(12, v.cylinders));
            v.redlineRpm = Math.Max(2000f, v.redlineRpm);
            sr = sampleRate;
            rng = new AudioRng(v.seed * 2654435761u + 1u);
            var r = new AudioRng(v.seed * 31u + 5u);
            cylAmp = new float[v.cylinders];
            for (int k = 0; k < cylAmp.Length; k++) cylAmp[k] = 1f + v.imbalance * r.Next();
            exhaust.SetBandpass(v.exhaustHz, 1.6f, sr);
            intake.SetBandpass(v.intakeHz, 1.1f, sr);
            mech.SetBandpass(3200f, 2f, sr);
            starterBand.SetBandpass(v.starterHz * 2f, 1.2f, sr);
            dcBlock.SetLowpass(25f, sr);
            cabinLp.SetLowpass(14000f, 0.7f, sr);
        }

        public EngineVoice Voice => v;
        public float SmoothedRpm => rpm;

        /// <summary>Firing frequency of a four-stroke engine, Hz: rpm/60 × cylinders/2.</summary>
        public static float FiringHz(float rpm, int cylinders) => Math.Max(0f, rpm) / 120f * Math.Max(1, cylinders);

        public void Set(EngineSoundInput i)
        {
            tRpm = Dsp.Safe(i.rpm, 0f, 12000f);
            tLoad = Dsp.Safe(i.load, 0f, 1f);
            tThrottle = Dsp.Safe(i.throttle, 0f, 1f);
            tCabin = Dsp.Safe(i.cabin01, 0f, 1f);
            tComb = i.combustion; tStarter = i.starter;
        }

        public void Render(float[] mono, int count)
        {
            if (mono == null) return;
            count = Math.Min(count, mono.Length);
            float dt = 1f / sr;
            for (int i = 0; i < count; i += Block)
            {
                int m = Math.Min(Block, count - i);
                // Per-block coefficients.
                float rpmN = Math.Min(1.2f, rpm / v.redlineRpm);
                float bright = Dsp.Clamp01(0.15f + 0.6f * load + 0.35f * rpmN);
                body.SetLowpass(600f + 3000f * bright, sr);
                cabinLp.SetLowpass(Dsp.Lerp(14000f, 900f, cabin), 0.7f, sr);
                bool driven = tComb || tStarter;
                // Running or cranking: rpm follows the model closely. Fuel cut: the crank runs down on its inertia.
                float kRpm = Dsp.Coef(driven ? 0.012f : 0.2f, sr);
                float kLoad = Dsp.Coef(0.04f, sr), kComb = Dsp.Coef(0.05f, sr), kCabin = Dsp.Coef(0.15f, sr);
                float combT = tComb ? 1f : 0f, starterT = tStarter ? 1f : 0f;
                float gain = (0.55f + 0.45f * rpmN) * (0.45f + 0.35f * load);   // louder with revs and under load

                for (int j = 0; j < m; j++)
                {
                    rpm += (tRpm - rpm) * kRpm;
                    load += (tLoad - load) * kLoad;
                    throttle += (tThrottle - throttle) * kLoad;
                    comb += (combT - comb) * kComb;
                    starter += (starterT - starter) * kComb;
                    cabin += (tCabin - cabin) * kCabin;

                    float cycleHz = rpm / 120f;                 // one four-stroke cycle = two crank turns
                    phase += cycleHz * dt;
                    if (phase >= 1f) phase -= (float)Math.Floor(phase);
                    int k = Math.Min(v.cylinders - 1, (int)(phase * v.cylinders));
                    if (k != lastCyl)
                    {
                        // A cylinder fires (or, without fuel, just compresses).
                        lastCyl = k; pulseT = 0f;
                        float burn = comb * (0.25f + 0.75f * load);
                        pulseAmp = rpm < 15f ? 0f : cylAmp[k] * (1f + v.roughness * (1.2f - load) * rng.Next()) * (0.3f + 0.7f * burn);
                        float fire = FiringHz(rpm, v.cylinders);
                        pulseWidth = Math.Min(0.0065f - 0.0025f * load, 0.8f / Math.Max(fire, 1f));
                    }
                    float e = 0f;
                    if (pulseT < pulseWidth) { float q = MathF.Sin(MathF.PI * pulseT / pulseWidth); e = q * q * pulseAmp; }
                    pulseT += dt;
                    float env = pulseAmp > 1e-5f ? e / pulseAmp : 0f;
                    float n = rng.Next();

                    float x = body.Low(e) * 0.7f + exhaust.Process(e) * 2.2f;
                    x += intake.Process(n) * MathF.Pow(throttle, 1.3f) * (0.3f + 0.7f * rpmN) * 0.35f * (0.35f + 0.65f * env) * comb;
                    x += mech.Process(n) * 0.05f * rpmN * (0.5f + 0.5f * env) * Math.Min(1f, rpm / 300f);
                    if (starter > 0.002f)
                    {
                        // Starter motor: a whine that sags on every compression stroke.
                        starterPhase += v.starterHz * (1f - 0.3f * env) * dt;
                        starterPhase -= (float)Math.Floor(starterPhase);
                        x += starterBand.Process(2f * starterPhase - 1f + 0.4f * n) * 0.45f * starter;
                    }
                    x = dcBlock.High(x) * gain;
                    x = cabinLp.Process(x) * (1f - 0.25f * cabin) * v.gain;
                    mono[i + j] = Dsp.Soft(x);
                }
            }
        }
    }
}
