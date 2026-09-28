using System;

namespace DrivingSchool.Audio
{
    /// <summary>
    /// Short procedural sounds as mono sample arrays (−1…1). No recorded assets: every clip is made from sines,
    /// filtered noise and envelopes, the same on every machine. The Unity side wraps them into AudioClips.
    /// </summary>
    public static class SoundClips
    {
        static float[] Buffer(int rate, float seconds)
        {
            if (rate < 8000) throw new ArgumentOutOfRangeException(nameof(rate));
            return new float[Math.Max(1, (int)(rate * seconds))];
        }

        static float Sin(float hz, float t) => MathF.Sin(2f * MathF.PI * hz * t);
        static float Env(float t, float attack, float decay) => (attack > 0f ? Dsp.Clamp01(t / attack) : 1f) * MathF.Exp(-t / decay);

        static void Normalize(float[] d, float peak)
        {
            float m = 0f; foreach (var x in d) m = Math.Max(m, Math.Abs(x));
            if (m < 1e-6f) return;
            float g = peak / m; for (int i = 0; i < d.Length; i++) d[i] *= g;
        }

        /// <summary>Short sine click (indicator relay: 2400 Hz tick, 1700 Hz tock).</summary>
        public static float[] Click(int rate, float hz, float seconds = 0.025f, float peak = 0.8f)
        {
            var d = Buffer(rate, seconds);
            for (int i = 0; i < d.Length; i++) { float t = i / (float)rate; d[i] = Sin(hz, t) * MathF.Exp(-t * 350f) * peak; }
            return d;
        }

        /// <summary>
        /// Two-tone car horn (420 + 500 Hz) as a seamless 0.5 s loop: whole numbers of cycles of both tones.
        /// Square-ish tone through a 2.5 kHz low-pass — a horn, not a buzzer.
        /// </summary>
        public static float[] Horn(int rate)
        {
            var d = Buffer(rate, 0.5f);
            // Warm the filter up on one loop so the loop point has no click.
            var lp = new Biquad(); lp.SetLowpass(2500f, 0.9f, rate);
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < d.Length; i++)
                {
                    float t = i / (float)rate;
                    float s = MathF.Tanh(3f * Sin(420f, t)) + MathF.Tanh(3f * Sin(500f, t));
                    d[i] = lp.Process(s);
                }
            Normalize(d, 0.6f);
            return d;
        }

        /// <summary>Body hitting something: low thump, crunch and a short metallic ring. Scale by impact speed.</summary>
        public static float[] Impact(int rate, uint seed = 1)
        {
            var d = Buffer(rate, 0.6f); var r = new AudioRng(seed);
            var crunch = new Biquad(); crunch.SetBandpass(1500f, 0.9f, rate);
            float[] ring = { 430f, 1170f, 2130f, 3390f };
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)rate;
                float x = (Sin(58f, t) + 0.6f * Sin(110f, t)) * Env(t, 0.002f, 0.09f);
                x += crunch.Process(r.Next()) * Env(t, 0.001f, 0.07f) * 3f;
                for (int k = 0; k < ring.Length; k++) x += Sin(ring[k], t) * Env(t, 0.001f, 0.18f / (k + 1)) * 0.12f;
                d[i] = x;
            }
            Normalize(d, 0.9f);
            return d;
        }

        /// <summary>Suspension hitting a bump (speed bump, pothole): a dull thud.</summary>
        public static float[] Bump(int rate, uint seed = 2)
        {
            var d = Buffer(rate, 0.3f); var r = new AudioRng(seed);
            var lp = new OnePole(); lp.SetLowpass(300f, rate);
            float phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)rate;
                phase += (45f - 15f * Dsp.Clamp01(t / 0.15f)) / rate;
                d[i] = MathF.Sin(2f * MathF.PI * phase) * Env(t, 0.003f, 0.07f) + lp.Low(r.Next()) * Env(t, 0.001f, 0.03f) * 2f;
            }
            Normalize(d, 0.8f);
            return d;
        }

        /// <summary>Manual gear lever going into a gate: a click and a low clunk.</summary>
        public static float[] GearShift(int rate, uint seed = 3)
        {
            var d = Buffer(rate, 0.12f); var r = new AudioRng(seed);
            var bp = new Biquad(); bp.SetBandpass(900f, 2f, rate);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)rate;
                d[i] = bp.Process(r.Next()) * Env(t, 0.0005f, 0.012f) * 2.5f + Sin(3000f, t) * Env(t, 0f, 0.003f) * 0.4f
                     + Sin(120f, t) * Env(t, 0.002f, 0.035f) * 0.6f;
            }
            Normalize(d, 0.6f);
            return d;
        }

        /// <summary>Gears grinding — a gear forced in without the clutch (the gearbox refuses it).</summary>
        public static float[] GearGrind(int rate, uint seed = 4)
        {
            var d = Buffer(rate, 0.4f); var r = new AudioRng(seed);
            var bp = new Biquad(); bp.SetBandpass(2400f, 1.5f, rate);
            var lo = new Biquad(); lo.SetBandpass(700f, 2f, rate);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)rate;
                float am = 0.5f + 0.5f * MathF.Abs(Sin(80f, t));      // teeth skipping over each other
                float n = r.Next();
                d[i] = (bp.Process(n) * 2f + lo.Process(n)) * am * Dsp.Clamp01(t / 0.01f) * Dsp.Clamp01((0.4f - t) / 0.08f);
            }
            Normalize(d, 0.7f);
            return d;
        }

        /// <summary>Handbrake pulled: ratchet clicks. Released: button click and the lever dropping.</summary>
        public static float[] Handbrake(int rate, bool pulled, uint seed = 5)
        {
            var d = Buffer(rate, pulled ? 0.42f : 0.18f); var r = new AudioRng(seed);
            var bp = new Biquad(); bp.SetBandpass(3200f, 3f, rate);
            float[] clicks = pulled ? new[] { 0.02f, 0.09f, 0.16f, 0.24f, 0.33f } : new[] { 0.01f };
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)rate, env = 0f;
                foreach (var c in clicks) if (t >= c) env += Env(t - c, 0f, 0.004f);
                float x = bp.Process(r.Next()) * env * 3f;
                if (!pulled) x += Sin(160f, t) * Env(Math.Max(0f, t - 0.05f), 0.004f, 0.04f) * (t >= 0.05f ? 0.7f : 0f);
                d[i] = x;
            }
            Normalize(d, 0.5f);
            return d;
        }

        /// <summary>Seat-belt tongue latching: two quick metallic clicks.</summary>
        public static float[] Seatbelt(int rate, bool fasten)
        {
            var d = Buffer(rate, 0.08f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)rate;
                float x = (Sin(2800f, t) + 0.5f * Sin(4100f, t)) * Env(t, 0f, 0.004f);
                if (fasten && t >= 0.03f) x += (Sin(3300f, t) + 0.4f * Sin(5200f, t)) * Env(t - 0.03f, 0f, 0.005f);
                d[i] = x;
            }
            Normalize(d, 0.45f);
            return d;
        }

        /// <summary>Ignition key turned one position.</summary>
        public static float[] Key(int rate, uint seed = 6)
        {
            var d = Buffer(rate, 0.05f); var r = new AudioRng(seed);
            var bp = new Biquad(); bp.SetBandpass(4500f, 2f, rate);
            for (int i = 0; i < d.Length; i++) { float t = i / (float)rate; d[i] = bp.Process(r.Next()) * Env(t, 0f, 0.006f) * 3f + Sin(1900f, t) * Env(t, 0f, 0.008f) * 0.3f; }
            Normalize(d, 0.35f);
            return d;
        }

        /// <summary>Locomotive horn (тифон): two low reedy tones, 1.6 s.</summary>
        public static float[] TrainHorn(int rate)
        {
            var d = Buffer(rate, 1.6f);
            var lp = new Biquad(); lp.SetLowpass(1800f, 0.8f, rate);
            float p1 = 0f, p2 = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)rate;
                float vib = 1f + 0.003f * Sin(5f, t);
                p1 += 311f * vib / rate; p2 += 370f * vib / rate; p1 -= (float)Math.Floor(p1); p2 -= (float)Math.Floor(p2);
                float s = (2f * p1 - 1f) + (2f * p2 - 1f);                   // sawtooth pair, filtered
                float env = Dsp.Clamp01(t / 0.06f) * Dsp.Clamp01((1.6f - t) / 0.15f);
                d[i] = lp.Process(s) * env;
            }
            Normalize(d, 0.8f);
            return d;
        }

        // ---------------------------------------------------------------- interface

        public enum Ui { Move, Confirm, Back, Tick, Tab, Alert, Hint }

        /// <summary>Interface sounds: soft, short, never louder than 0.5.</summary>
        public static float[] Interface(int rate, Ui kind)
        {
            switch (kind)
            {
                case Ui.Move: return Tone(rate, 0.035f, 0.18f, (1760f, 0f));
                case Ui.Tick: return Tone(rate, 0.02f, 0.15f, (1320f, 0f));
                case Ui.Confirm: return Tone(rate, 0.14f, 0.3f, (660f, 0f), (990f, 0.05f));
                case Ui.Back: return Tone(rate, 0.14f, 0.26f, (990f, 0f), (660f, 0.05f));
                case Ui.Tab: return Tone(rate, 0.06f, 0.2f, (1175f, 0f), (1480f, 0.025f));
                case Ui.Alert: return Tone(rate, 0.42f, 0.45f, (880f, 0f), (659f, 0.16f));
                default: return Tone(rate, 0.5f, 0.3f, (1319f, 0f), (1760f, 0.09f));
            }
        }

        static float[] Tone(int rate, float seconds, float peak, params (float hz, float at)[] notes)
        {
            var d = Buffer(rate, seconds);
            float decay = Math.Max(0.012f, seconds * 0.35f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)rate, x = 0f;
                foreach (var n in notes)
                {
                    if (t < n.at) continue;
                    float u = t - n.at;
                    // Soft bell: fundamental plus a quiet octave, 3 ms attack.
                    x += (Sin(n.hz, u) + 0.25f * Sin(n.hz * 2f, u)) * Env(u, 0.003f, decay);
                }
                d[i] = x * Dsp.Clamp01((seconds - t) / 0.01f);
            }
            Normalize(d, peak);
            return d;
        }

        // ---------------------------------------------------------------- loops

        /// <summary>
        /// Makes a seamless loop of length <c>data.Length − fade</c>: the tail is cross-faded into the head,
        /// so sample L−1 runs on into sample 0 without a click.
        /// </summary>
        public static float[] MakeLoop(float[] data, int fade)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            fade = Math.Max(1, Math.Min(fade, data.Length / 2));
            int len = data.Length - fade;
            var o = new float[len];
            for (int i = 0; i < len; i++) o[i] = data[i];
            for (int j = 0; j < fade; j++) { float a = j / (float)fade; o[j] = data[j] * a + data[len + j] * (1f - a); }
            return o;
        }

        /// <summary>Steady engine note for AI cars (played with pitch = rpm / baseRpm): a seamless loop.</summary>
        public static float[] EngineLoop(int rate, EngineVoice voice, float baseRpm, float load, float seconds = 1.5f)
        {
            var synth = new EngineSoundSynth(voice, rate);
            synth.Set(new EngineSoundInput { rpm = baseRpm, load = load, throttle = load * 0.6f, combustion = true });
            var warm = new float[rate / 2]; synth.Render(warm, warm.Length);   // settle the smoothers and filters
            int fade = rate / 10;
            var d = new float[(int)(rate * seconds) + fade];
            synth.Render(d, d.Length);
            return MakeLoop(d, fade);
        }
    }
}
