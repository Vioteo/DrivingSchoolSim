using UnityEngine;
using DrivingSchool.Audio;
using DrivingSchool.Contracts;
using DrivingSchool.Presentation.Physics;
using DrivingSchool.Settings;

namespace DrivingSchool.Presentation
{
    /// <summary>
    /// Sound of the player car (T67): the engine and starter from the solver's rpm/torque, tyres and wind from speed and
    /// slip, the controls (key, gear lever, gears grinding without the clutch, handbrake ratchet, seat belt), bumps and
    /// impacts. All procedural (DS.Audio). Heard from the cabin the outside noise is muffled.
    /// The indicator relay and the horn stay in <see cref="DashboardView"/>.
    /// Added by <see cref="VehicleController"/> at start; volumes follow the Sound settings (AudioMix).
    /// </summary>
    [DefaultExecutionOrder(130)]
    public sealed class VehicleAudio : MonoBehaviour
    {
        public VehiclePhysicsAdapter adapter;
        public VehicleController controller;
        [Tooltip("Cabin volume in car space: with the listener inside, outside noise is muffled.")]
        public Vector3 cabinMin = new Vector3(-0.85f, 0.25f, -1.5f), cabinMax = new Vector3(0.85f, 1.6f, 1.0f);
        [Tooltip("Engine voice; exhaust note and seed are adjusted from the car's mass and name at start.")]
        public EngineVoice voice = new EngineVoice();
        [Range(0f, 2f)] public float engineVolume = 1f, roadVolume = 1f, controlsVolume = 1f, impactVolume = 1f;
        [Tooltip("Suspension compression speed, m/s, above which a bump is heard.")]
        public float bumpThresholdMps = 0.6f;

        /// <summary>Listener in the cabin of the player car, 0…1 (smoothed). Ambient sound uses it for rain on the roof.</summary>
        public static float PlayerCabin01 { get; private set; }

        public bool ListenerInCabin { get; private set; }
        public EngineSoundSynth Engine { get; private set; }
        public RoadSoundSynth Road { get; private set; }
        public float Squeal01 { get; private set; }

        ProceduralAudio engineOut, roadOut;
        AudioSource controls;
        AudioClip shift, grind, handbrakeOn, handbrakeOff, beltOn, beltOff, key, impact, bump;
        bool started, lastIgnition, lastHandbrake, lastBelt, lastRefused; int lastGear; AutomaticSelector lastSelector;
        float grindCooldown, lastImpactTime = -10f, cabin01;
        readonly float[] lastCompression = new float[4], bumpCooldown = new float[4];

        void Start()
        {
            if (adapter == null) adapter = GetComponent<VehiclePhysicsAdapter>();
            if (controller == null) controller = GetComponent<VehicleController>();
            if (adapter == null) enabled = false;
        }

        /// <summary>Built on the first frame with a solver: the voice takes the redline and mass of the car.</summary>
        void Build()
        {
            int rate = ProceduralAudio.SampleRate;

            var v = voice != null ? voice.Clone() : new EngineVoice();
            var spec = adapter.Solver.Spec;
            if (spec != null)
            {
                v.redlineRpm = spec.redlineRpm;
                v.exhaustHz *= Mathf.Sqrt(1350f / Mathf.Max(600f, spec.massKg));   // heavier car — deeper note
            }
            v.seed = (uint)Mathf.Abs(gameObject.name.GetHashCode()) | 1u;
            Engine = new EngineSoundSynth(v, rate);
            Road = new RoadSoundSynth(rate, v.seed * 7u + 3u);
            engineOut = ProceduralAudio.Create(transform, "Audio_Engine", new Vector3(0f, 0.6f, 1.5f), Engine, 0.85f, 3f, 90f);
            roadOut = ProceduralAudio.Create(transform, "Audio_Road", new Vector3(0f, 0.3f, 0f), Road, 0.7f, 3f, 60f);

            var go = new GameObject("Audio_Controls");
            go.transform.SetParent(transform, false); go.transform.localPosition = new Vector3(0f, 0.9f, 0.2f);
            controls = go.AddComponent<AudioSource>();
            controls.playOnAwake = false; controls.spatialBlend = 0.6f; controls.minDistance = 2f; controls.maxDistance = 40f; controls.dopplerLevel = 0f;

            shift = Clip("gear_shift", SoundClips.GearShift(rate));
            grind = Clip("gear_grind", SoundClips.GearGrind(rate));
            handbrakeOn = Clip("handbrake_on", SoundClips.Handbrake(rate, true));
            handbrakeOff = Clip("handbrake_off", SoundClips.Handbrake(rate, false));
            beltOn = Clip("belt_on", SoundClips.Seatbelt(rate, true));
            beltOff = Clip("belt_off", SoundClips.Seatbelt(rate, false));
            key = Clip("key", SoundClips.Key(rate));
            impact = Clip("impact", SoundClips.Impact(rate));
            bump = Clip("bump", SoundClips.Bump(rate));
        }

        static AudioClip Clip(string name, float[] data)
        {
            var c = AudioClip.Create(name, data.Length, 1, ProceduralAudio.SampleRate, false);
            c.SetData(data, 0);
            return c;
        }

        void OnDisable() { ListenerInCabin = false; PlayerCabin01 = 0f; }

        void Update()
        {
            if (adapter == null || adapter.Solver == null) return;
            if (Engine == null) Build();
            var st = adapter.CurrentState; var cmd = adapter.LastCommand; var solver = adapter.Solver;
            float dt = Time.deltaTime;

            // Where is the listener?
            var listener = AudioListenerLocator.Active;
            bool inside = false;
            if (listener != null)
            {
                Vector3 p = transform.InverseTransformPoint(listener.transform.position);
                inside = p.x > cabinMin.x && p.x < cabinMax.x && p.y > cabinMin.y && p.y < cabinMax.y && p.z > cabinMin.z && p.z < cabinMax.z;
            }
            ListenerInCabin = inside;
            cabin01 = Mathf.MoveTowards(cabin01, inside ? 1f : 0f, dt * 6f);
            PlayerCabin01 = cabin01;

            // Engine bay: between the front wheels, a little up and forward.
            Vector3 fl = adapter.WheelCentreLocal[0], fr = adapter.WheelCentreLocal[1];
            if (fl != fr) engineOut.transform.localPosition = 0.5f * (fl + fr) + new Vector3(0f, 0.35f, 0.3f);

            float fullLoad = Mathf.Max(1f, solver.Engine.FullLoadTorqueNm);
            Engine.Set(new EngineSoundInput
            {
                rpm = st.engineRpm,
                load = st.engine == EnginePhase.Running ? Mathf.Clamp01(st.engineTorqueNm / fullLoad) : 0f,
                throttle = cmd.throttle,
                combustion = st.engine == EnginePhase.Running,
                starter = cmd.ignition && cmd.starter && !solver.StarterInhibited,
                cabin01 = cabin01,
            });

            float squeal = 0f, factor = SquealFactor(adapter.surface);
            float r = adapter.WheelRadius;
            for (int k = 0; k < 4; k++)
            {
                var w = solver.Wheels[k];
                if (!w.grounded) continue;
                float sliding = Mathf.Max(Mathf.Abs(st.signedSpeedMps), Mathf.Abs(w.angularVelocityRadS * r));
                squeal = Mathf.Max(squeal, TyreSound.SquealIntensity(w.slipRatio, w.slipAngleRad, sliding, factor));
            }
            Squeal01 = squeal;
            bool anyGrounded = adapter.Grounded[0] || adapter.Grounded[1] || adapter.Grounded[2] || adapter.Grounded[3];
            Road.Set(new RoadSoundInput
            {
                speedMps = anyGrounded ? st.signedSpeedMps : 0f,
                squeal01 = squeal,
                wet01 = adapter.surface == SurfaceType.WetAsphalt ? 1f : 0f,
                snow01 = adapter.surface == SurfaceType.PackedSnow ? 1f : 0f,
                cabin01 = cabin01,
            });

            float env = AudioMix.Current(AudioChannel.Environment);
            engineOut.Gain = AudioMix.Current(AudioChannel.Engine) * engineVolume;
            roadOut.Gain = env * roadVolume * (adapter.surface == SurfaceType.BlackIce ? 0.6f : 1f);

            if (!started)
            {
                started = true; lastIgnition = cmd.ignition; lastHandbrake = cmd.handbrake; lastBelt = cmd.seatbelt;
                lastGear = st.gear; lastSelector = st.selector;
                for (int k = 0; k < 4; k++) lastCompression[k] = adapter.Compression[k];
                if (controller != null) lastImpactTime = controller.LastImpactTime;
                return;
            }
            ControlSounds(cmd, st, solver, dt);
            ImpactSounds(env, dt);
        }

        /// <summary>Tyre squeal by surface: dry asphalt squeals, wet much less, snow and ice not at all.</summary>
        public static float SquealFactor(SurfaceType s) => s == SurfaceType.DryAsphalt ? 1f : s == SurfaceType.WetAsphalt ? 0.3f : 0f;

        void ControlSounds(DriverCommand cmd, VehicleState st, DrivingSchool.Simulation.VehicleSolver solver, float dt)
        {
            float vol = controlsVolume;
            if (cmd.ignition != lastIgnition) controls.PlayOneShot(key, 0.8f * vol);
            if (cmd.handbrake != lastHandbrake) controls.PlayOneShot(cmd.handbrake ? handbrakeOn : handbrakeOff, 0.7f * vol);
            if (cmd.seatbelt != lastBelt) controls.PlayOneShot(cmd.seatbelt ? beltOn : beltOff, 0.8f * vol);
            if (st.transmission == TransmissionType.Manual)
            {
                if (st.gear != lastGear) controls.PlayOneShot(shift, 0.6f * vol);
            }
            else if (st.selector != lastSelector) controls.PlayOneShot(shift, 0.35f * vol);

            // A gear forced without the clutch: the gearbox refuses it and grinds while the driver insists.
            bool refused = st.transmission == TransmissionType.Manual && solver.Gearbox.LastRequestRefused;
            grindCooldown -= dt;
            if (refused && (!lastRefused || grindCooldown <= 0f) && st.engine == EnginePhase.Running)
            {
                controls.PlayOneShot(grind, 0.7f * vol);
                grindCooldown = 0.9f;
            }
            lastRefused = refused;
            lastIgnition = cmd.ignition; lastHandbrake = cmd.handbrake; lastBelt = cmd.seatbelt;
            lastGear = st.gear; lastSelector = st.selector;
        }

        void ImpactSounds(float env, float dt)
        {
            if (controller != null && controller.LastImpactTime > lastImpactTime + 0.05f)
            {
                lastImpactTime = controller.LastImpactTime;
                float v = controller.LastImpactSpeedMps;
                controls.PlayOneShot(impact, Mathf.Clamp(v / 10f, 0.15f, 1f) * env * impactVolume);
            }
            if (dt <= 0f) return;
            for (int k = 0; k < 4; k++)
            {
                float c = adapter.Compression[k];
                float rate = (c - lastCompression[k]) / dt;
                lastCompression[k] = c;
                bumpCooldown[k] -= dt;
                if (adapter.Grounded[k] && rate > bumpThresholdMps && bumpCooldown[k] <= 0f)
                {
                    bumpCooldown[k] = 0.2f;
                    controls.PlayOneShot(bump, Mathf.Clamp01((rate - bumpThresholdMps) / 2.5f + 0.2f) * 0.8f * env * impactVolume);
                }
            }
        }
    }
}
