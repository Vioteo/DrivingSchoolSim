using UnityEngine;
using DrivingSchool.Audio;
using DrivingSchool.Settings;

namespace DrivingSchool.Presentation
{
    /// <summary>
    /// Engine note of an AI car (T67): one shared procedural loop, pitched by a virtual gearbox from the car's speed and
    /// acceleration (<see cref="TrafficEngineSound"/>), louder under load. Only cars within <see cref="AudibleM"/> of
    /// the listener play; farther ones are stopped, so a full district costs a handful of voices.
    /// Added by <see cref="TrafficVehicleView"/> when the car is bound to an agent.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TrafficVehicleAudio : MonoBehaviour
    {
        public const float AudibleM = 45f, BaseRpm = 1600f;
        static AudioClip loop;

        AudioSource src;
        Vector3 lastPos; bool hasPos;
        float speed, lastSpeed, accel;

        public float SpeedMps => speed;
        public bool Audible => src != null && src.isPlaying;

        void Awake()
        {
            src = gameObject.AddComponent<AudioSource>();
            src.clip = Loop(); src.loop = true; src.playOnAwake = false;
            src.spatialBlend = 1f; src.minDistance = 4f; src.maxDistance = AudibleM; src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.dopplerLevel = 0.6f; src.priority = 160; src.volume = 0f;
        }

        static AudioClip Loop()
        {
            if (loop != null) return loop;
            int rate = ProceduralAudio.SampleRate;
            var voice = new EngineVoice { exhaustHz = 95f, seed = 17, roughness = 0.04f };
            var d = SoundClips.EngineLoop(rate, voice, BaseRpm, 0.45f);
            loop = AudioClip.Create("TrafficEngineLoop", d.Length, 1, rate, false);
            loop.SetData(d, 0);
            return loop;
        }

        /// <summary>The car was re-used for another agent (teleported): forget its motion.</summary>
        public void ResetMotion() { hasPos = false; speed = lastSpeed = accel = 0f; }

        void OnDisable() { if (src != null) src.Stop(); hasPos = false; }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || src == null) return;
            Vector3 pos = transform.position;
            if (!hasPos || (pos - lastPos).sqrMagnitude > 100f) { lastPos = pos; hasPos = true; return; }
            float v = Mathf.Min((pos - lastPos).magnitude / dt, 40f);
            lastPos = pos;
            speed = Mathf.Lerp(speed, v, 1f - Mathf.Exp(-dt / 0.3f));
            accel = Mathf.Lerp(accel, (speed - lastSpeed) / dt, 1f - Mathf.Exp(-dt / 0.5f));
            lastSpeed = speed;

            var listener = AudioListenerLocator.Active;
            float d = listener != null ? Vector3.Distance(listener.transform.position, pos) : float.PositiveInfinity;
            if (d > AudibleM + 5f) { if (src.isPlaying) src.Stop(); return; }
            if (!src.isPlaying) { src.Play(); src.time = Random.value * src.clip.length * 0.9f; }

            float rpm = TrafficEngineSound.Rpm(speed, accel);
            float load = TrafficEngineSound.Load(accel);
            float edge = Mathf.Clamp01((AudibleM + 5f - d) / 12f);          // fade out towards the edge instead of a cut
            src.pitch = Mathf.Clamp(rpm / BaseRpm, 0.45f, 2.6f);
            src.volume = 0.7f * (0.45f + 0.55f * load) * (0.6f + 0.4f * Mathf.Clamp01(speed / 15f)) * edge * AudioMix.Current(AudioChannel.Environment);
        }
    }
}
