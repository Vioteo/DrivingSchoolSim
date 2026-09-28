using DrivingSchool.Contracts;
using DrivingSchool.Presentation.Physics;
using DrivingSchool.Settings;
using UnityEngine;

namespace DrivingSchool.Presentation
{
    /// <summary>Procedural engine, tyre and collision sounds for the player car.</summary>
    public sealed class VehicleAudio : MonoBehaviour
    {
        VehiclePhysicsAdapter car;
        AudioSource motor, road, impact;
        AudioClip motorClip, roadClip, impactClip;
        float engineLevel = 0.8f, environmentLevel = 0.7f;

        void Awake()
        {
            car = GetComponent<VehiclePhysicsAdapter>();
            motorClip = MakeClip("EngineLoop", 22050, i =>
            {
                float t = i / 22050f;
                return 0.55f * Mathf.Sin(2f * Mathf.PI * 30f * t)
                     + 0.25f * Mathf.Sin(2f * Mathf.PI * 60f * t)
                     + 0.12f * Mathf.Sin(2f * Mathf.PI * 90f * t);
            });
            roadClip = MakeClip("RoadLoop", 22050, i =>
            {
                // Deterministic filtered noise. A whole-second loop keeps the seam quiet.
                float t = i / 22050f;
                return 0.12f * (Mathf.Sin(2f * Mathf.PI * 83f * t) + Mathf.Sin(2f * Mathf.PI * 157f * t));
            });
            impactClip = MakeClip("BodyImpact", 5512, i =>
            {
                float t = i / 22050f;
                return Mathf.Exp(-22f * t) * (0.6f * Mathf.Sin(2f * Mathf.PI * 93f * t) + 0.25f * Mathf.Sin(2f * Mathf.PI * 137f * t));
            });
            motor = Source(motorClip, true, 0.65f);
            road = Source(roadClip, true, 0.8f);
            impact = Source(impactClip, false, 0.85f);
            motor.Play(); road.Play();
        }

        static AudioClip MakeClip(string name, int samples, System.Func<int, float> sample)
        {
            var data = new float[samples];
            for (int i = 0; i < samples; i++) data[i] = sample(i);
            var clip = AudioClip.Create(name, samples, 1, 22050, false);
            clip.SetData(data, 0);
            return clip;
        }

        AudioSource Source(AudioClip clip, bool loop, float spatial)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.clip = clip; source.loop = loop; source.playOnAwake = false;
            source.spatialBlend = spatial; source.minDistance = 2f; source.maxDistance = 50f;
            return source;
        }

        void OnEnable() { SettingsService.Applied += Apply; Apply(SettingsService.Current); }
        void OnDisable() { SettingsService.Applied -= Apply; }

        void Apply(GameSettings settings)
        {
            if (settings == null) return;
            engineLevel = Mathf.Clamp01(settings.audio.engine / 100f);
            environmentLevel = Mathf.Clamp01(settings.audio.environment / 100f);
        }

        void Update()
        {
            if (car == null || motor == null) return;
            var state = car.CurrentState;
            bool running = state.engine == EnginePhase.Running;
            float rpm = Mathf.Max(0f, state.engineRpm);
            motor.pitch = Mathf.Clamp(rpm / 900f, 0.6f, 4f);
            motor.volume = running ? engineLevel * (0.12f + 0.09f * car.LastCommand.throttle) : 0f;
            float speed = Mathf.Abs(state.signedSpeedMps);
            road.pitch = Mathf.Lerp(0.7f, 1.6f, Mathf.Clamp01(speed / 30f));
            road.volume = environmentLevel * Mathf.Clamp01((speed - 1f) / 25f) * 0.12f;
        }

        public void PlayImpact(float speedMps)
        {
            if (impact == null || speedMps < 0.3f) return;
            impact.volume = environmentLevel * Mathf.Clamp(speedMps / 12f, 0.08f, 0.55f);
            impact.Stop(); impact.Play();
        }

        void OnDestroy()
        {
            if (motorClip != null) Destroy(motorClip);
            if (roadClip != null) Destroy(roadClip);
            if (impactClip != null) Destroy(impactClip);
        }
    }
}
