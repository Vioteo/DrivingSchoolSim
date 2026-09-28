using System;
using NUnit.Framework;
using DrivingSchool.Audio;

namespace DrivingSchool.Tests.Audio
{
    /// <summary>T67: procedural sound (DS.Audio) — pitch, level, silence and limits, measured on rendered samples.</summary>
    public sealed class AudioSynthTests
    {
        const int Rate = 44100;

        static float[] Render(ISampleSource s, float seconds)
        {
            var d = new float[(int)(Rate * seconds)];
            // Render in audio-thread sized blocks, like Unity does.
            var block = new float[1024];
            for (int i = 0; i < d.Length; i += block.Length)
            {
                int n = Math.Min(block.Length, d.Length - i);
                s.Render(block, n);
                Array.Copy(block, 0, d, i, n);
            }
            return d;
        }

        static float Rms(float[] d) { double s = 0; foreach (var x in d) s += x * x; return (float)Math.Sqrt(s / d.Length); }
        static float DiffRms(float[] d) { double s = 0; for (int i = 1; i < d.Length; i++) { double v = d[i] - d[i - 1]; s += v * v; } return (float)Math.Sqrt(s / (d.Length - 1)); }
        /// <summary>RMS of the second difference: weights the spectrum by f², i.e. measures the treble.</summary>
        static float TrebleRms(float[] d) { double s = 0; for (int i = 1; i + 1 < d.Length; i++) { double v = d[i + 1] - 2.0 * d[i] + d[i - 1]; s += v * v; } return (float)Math.Sqrt(s / (d.Length - 2)); }

        static EngineSoundSynth Engine(float rpm, float load, float throttle, bool combustion = true, float cabin = 0f, bool starter = false)
        {
            var e = new EngineSoundSynth(new EngineVoice(), Rate);
            e.Set(new EngineSoundInput { rpm = rpm, load = load, throttle = throttle, combustion = combustion, starter = starter, cabin01 = cabin });
            Render(e, 0.6f);   // let the smoothers settle
            return e;
        }

        /// <summary>Lag (samples) of the autocorrelation maximum in [lo, hi].</summary>
        static int PeriodLag(float[] d, int lo, int hi)
        {
            int best = lo; double bestV = double.NegativeInfinity;
            for (int lag = lo; lag <= hi; lag++)
            {
                double s = 0; for (int i = 0; i + lag < d.Length; i++) s += d[i] * d[i + lag];
                if (s > bestV) { bestV = s; best = lag; }
            }
            return best;
        }

        [Test] public void FiringFrequencyOfFourStroke()
        {
            Assert.That(EngineSoundSynth.FiringHz(850f, 4), Is.EqualTo(28.333f).Within(0.01f));
            Assert.That(EngineSoundSynth.FiringHz(6000f, 4), Is.EqualTo(200f).Within(0.01f));
            Assert.That(EngineSoundSynth.FiringHz(3000f, 6), Is.EqualTo(150f).Within(0.01f));
            Assert.That(EngineSoundSynth.FiringHz(-5f, 4), Is.Zero);
        }

        [TestCase(1500f)]
        [TestCase(3000f)]
        [TestCase(5000f)]
        public void EnginePitchFollowsRpm(float rpm)
        {
            var d = Render(Engine(rpm, 0.3f, 0f), 0.5f);
            float period = Rate / EngineSoundSynth.FiringHz(rpm, 4);
            int lag = PeriodLag(d, (int)(period * 0.6f), (int)(period * 1.4f));
            Assert.That(lag, Is.EqualTo(period).Within(period * 0.05f), $"firing period at {rpm} rpm");
        }

        [Test] public void EngineIsLouderUnderLoad()
        {
            float light = Rms(Render(Engine(3000f, 0.05f, 0f), 1f));
            float full = Rms(Render(Engine(3000f, 1f, 1f), 1f));
            Assert.That(full, Is.GreaterThan(light * 1.3f));
        }

        [Test] public void EngineOffIsSilent_StarterIsHeard()
        {
            var off = Render(Engine(0f, 0f, 0f, combustion: false), 0.5f);
            Assert.That(Rms(off), Is.LessThan(1e-4f));
            var cranking = Render(Engine(300f, 0f, 0f, combustion: false, starter: true), 0.5f);
            Assert.That(Rms(cranking), Is.GreaterThan(0.01f));
        }

        [Test] public void StallRunsDownToSilence()
        {
            var e = Engine(900f, 0.2f, 0f);
            e.Set(new EngineSoundInput { rpm = 0f, combustion = false });
            var first = Render(e, 0.1f);
            Assert.That(Rms(first), Is.GreaterThan(0.005f), "compression beats while the crank runs down");
            Render(e, 1.5f);
            Assert.That(Rms(Render(e, 0.3f)), Is.LessThan(1e-3f));
        }

        [Test] public void CabinMufflesHighFrequencies()
        {
            var outside = Render(Engine(4000f, 0.8f, 0.8f), 1f);
            var inside = Render(Engine(4000f, 0.8f, 0.8f, cabin: 1f), 1f);
            Assert.That(TrebleRms(inside), Is.LessThan(TrebleRms(outside) * 0.3f));
        }

        [Test] public void OutputStaysBoundedAndFinite()
        {
            var e = new EngineSoundSynth(new EngineVoice { gain = 4f }, Rate);
            e.Set(new EngineSoundInput { rpm = float.NaN, load = float.PositiveInfinity, throttle = -3f, combustion = true, starter = true, cabin01 = float.NaN });
            foreach (var x in Render(e, 0.3f)) Assert.That(Math.Abs(x), Is.LessThanOrEqualTo(1f));
            e.Set(new EngineSoundInput { rpm = 12000f, load = 1f, throttle = 1f, combustion = true, starter = true });
            foreach (var x in Render(e, 0.5f)) { Assert.That(float.IsNaN(x), Is.False); Assert.That(Math.Abs(x), Is.LessThanOrEqualTo(1f)); }
        }

        [Test] public void RoadNoiseGrowsWithSpeed_SilentAtRest()
        {
            float Level(float v) { var r = new RoadSoundSynth(Rate); r.Set(new RoadSoundInput { speedMps = v }); Render(r, 0.5f); return Rms(Render(r, 1f)); }
            Assert.That(Level(0f), Is.LessThan(1e-3f));
            float slow = Level(8f), fast = Level(30f);
            Assert.That(slow, Is.GreaterThan(0.002f));
            Assert.That(fast, Is.GreaterThan(slow * 2f));
        }

        [Test] public void SquealAddsSound()
        {
            var a = new RoadSoundSynth(Rate); a.Set(new RoadSoundInput { speedMps = 10f }); Render(a, 0.5f);
            var b = new RoadSoundSynth(Rate); b.Set(new RoadSoundInput { speedMps = 10f, squeal01 = 1f }); Render(b, 0.5f);
            Assert.That(Rms(Render(b, 1f)), Is.GreaterThan(Rms(Render(a, 1f)) * 1.5f));
        }

        [Test] public void SquealIntensityThresholds()
        {
            Assert.That(TyreSound.SquealIntensity(0.05f, 0.03f, 10f, 1f), Is.Zero, "grip: below the saturation of the tyre");
            Assert.That(TyreSound.SquealIntensity(0.5f, 0f, 10f, 1f), Is.EqualTo(1f), "wheelspin");
            Assert.That(TyreSound.SquealIntensity(-0.6f, 0f, 10f, 1f), Is.EqualTo(1f), "locked wheel");
            Assert.That(TyreSound.SquealIntensity(0f, 0.3f, 10f, 1f), Is.EqualTo(1f), "cornering slide");
            Assert.That(TyreSound.SquealIntensity(0.5f, 0f, 10f, 0f), Is.Zero, "snow does not squeal");
            Assert.That(TyreSound.SquealIntensity(0.5f, 0f, 0f, 1f), Is.Zero, "nothing slides");
            Assert.That(TyreSound.SquealIntensity(float.NaN, 0f, 10f, 1f), Is.Zero);
            float half = TyreSound.SquealIntensity(0.18f, 0f, 10f, 1f);
            Assert.That(half, Is.GreaterThan(0.2f).And.LessThan(0.8f), "grows between onset and full slide");
        }

        [Test] public void TrafficEngineStaysInRangeAndShiftsUp()
        {
            for (float v = 0f; v <= 40f; v += 0.5f)
                for (float a = -4f; a <= 3f; a += 0.5f)
                {
                    float rpm = TrafficEngineSound.Rpm(v, a);
                    Assert.That(rpm, Is.InRange(TrafficEngineSound.IdleRpm, 4200f), $"v={v} a={a}");
                }
            Assert.That(TrafficEngineSound.Gear(3f, 0f), Is.EqualTo(1));
            Assert.That(TrafficEngineSound.Gear(30f, 0f), Is.GreaterThanOrEqualTo(5));
            Assert.That(TrafficEngineSound.Gear(10f, 2.5f), Is.LessThan(TrafficEngineSound.Gear(10f, 0f)), "hard acceleration holds a lower gear");
            Assert.That(TrafficEngineSound.Rpm(0f, 0f), Is.EqualTo(TrafficEngineSound.IdleRpm));
            Assert.That(TrafficEngineSound.Load(2.5f), Is.EqualTo(1f));
            Assert.That(TrafficEngineSound.Load(-2f), Is.Zero);
        }

        [Test] public void TrainClacksAtWheelsetRate()
        {
            var t = new TrainSoundSynth(Rate);
            float v = 60f / 3.6f;
            t.Set(v, true);
            Render(t, 2f);                                   // speed settles (0.3 s smoothing)
            int before = t.Clacks;
            Render(t, 10f);
            float expected = TrainSoundSynth.ClacksPerSecond(v) * 10f;
            Assert.That(t.Clacks - before, Is.EqualTo(expected).Within(3f));
            t.Set(v, false);
            Render(t, 3f);
            Assert.That(Rms(Render(t, 0.5f)), Is.LessThan(2e-3f), "no train — no sound");
        }

        [Test] public void AmbientRainAndBirds()
        {
            float Level(float rain) { var a = new AmbientSoundSynth(Rate); a.Set(new AmbientSoundInput { rain01 = rain, city01 = 0.5f, daylight01 = 1f }); Render(a, 1f); return Rms(Render(a, 2f)); }
            Assert.That(Level(1f), Is.GreaterThan(Level(0f) * 1.5f));

            bool Sings(float rain)
            {
                var a = new AmbientSoundSynth(Rate); a.Set(new AmbientSoundInput { rain01 = rain, city01 = 1f, daylight01 = 1f });
                var block = new float[1024];
                for (int i = 0; i < Rate * 20 / 1024; i++) { a.Render(block, block.Length); if (a.BirdSinging) return true; }
                return false;
            }
            Assert.That(Sings(0f), Is.True, "birds on a dry day");
            Assert.That(Sings(1f), Is.False, "no birds in the rain");
        }

        [Test] public void ClipsAreAudibleAndBounded()
        {
            foreach (SoundClips.Ui k in Enum.GetValues(typeof(SoundClips.Ui)))
            {
                var d = SoundClips.Interface(Rate, k);
                float peak = 0f; foreach (var x in d) peak = Math.Max(peak, Math.Abs(x));
                Assert.That(peak, Is.InRange(0.1f, 0.5f), k.ToString());
            }
            foreach (var d in new[] { SoundClips.Impact(Rate), SoundClips.Bump(Rate), SoundClips.GearShift(Rate), SoundClips.GearGrind(Rate),
                                      SoundClips.Handbrake(Rate, true), SoundClips.Handbrake(Rate, false), SoundClips.Seatbelt(Rate, true),
                                      SoundClips.Key(Rate), SoundClips.TrainHorn(Rate), SoundClips.Horn(Rate) })
            {
                float peak = 0f; foreach (var x in d) { Assert.That(float.IsNaN(x), Is.False); peak = Math.Max(peak, Math.Abs(x)); }
                Assert.That(peak, Is.InRange(0.2f, 1f));
            }
        }

        [Test] public void LoopsJoinWithoutClick()
        {
            var horn = SoundClips.Horn(Rate);
            Assert.That(Math.Abs(horn[horn.Length - 1] - horn[0]), Is.LessThan(DiffRms(horn) * 4f), "horn loop point");

            var ramp = new float[1000]; for (int i = 0; i < ramp.Length; i++) ramp[i] = i;
            var loop = SoundClips.MakeLoop(ramp, 100);
            Assert.That(loop.Length, Is.EqualTo(900));
            Assert.That(loop[0], Is.EqualTo(900f), "head starts where the tail ends");
            Assert.That(loop[899], Is.EqualTo(899f));

            var engine = SoundClips.EngineLoop(Rate, new EngineVoice(), 1600f, 0.4f, 1f);
            Assert.That(Math.Abs(engine[engine.Length - 1] - engine[0]), Is.LessThan(DiffRms(engine) * 6f), "engine loop point");
        }
    }
}
