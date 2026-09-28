using UnityEngine;
using DrivingSchool.Audio;

namespace DrivingSchool.Presentation
{
    /// <summary>
    /// Plays a procedural <see cref="ISampleSource"/> through an AudioSource. The source loops a constant 1.0 clip and
    /// the filter multiplies it by the synthesised signal, so Unity's 3D panning and distance roll-off still apply
    /// (a clip-less source would be heard flat in 2D). The source is stopped whenever this component is off —
    /// otherwise the bare constant would reach the speakers.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class ProceduralAudio : MonoBehaviour
    {
        static AudioClip unityClip;

        ISampleSource sampleSource;
        float[] mono = new float[1024];
        volatile float gain = 1f;
        float appliedGain;

        public AudioSource Source { get; private set; }
        /// <summary>Output gain (channel volume × level), smoothed on the audio thread.</summary>
        public float Gain { get => gain; set => gain = Mathf.Clamp(value, 0f, 4f); }

        /// <summary>Output sample rate for synthesisers (44100 when audio is off, e.g. batch mode).</summary>
        public static int SampleRate => AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 44100;

        public static ProceduralAudio Create(Transform parent, string name, Vector3 localPosition, ISampleSource source,
                                             float spatialBlend, float minDistance, float maxDistance)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            var a = go.AddComponent<AudioSource>();
            a.clip = UnityClip(); a.loop = true; a.playOnAwake = false; a.volume = 1f;
            a.spatialBlend = spatialBlend; a.minDistance = minDistance; a.maxDistance = maxDistance;
            a.rolloffMode = AudioRolloffMode.Logarithmic; a.dopplerLevel = 0f;
            var p = go.AddComponent<ProceduralAudio>();
            p.Source = a; p.sampleSource = source;
            if (Application.isPlaying) a.Play();
            return p;
        }

        static AudioClip UnityClip()
        {
            if (unityClip != null) return unityClip;
            const int n = 4096;
            var d = new float[n];
            for (int i = 0; i < n; i++) d[i] = 1f;
            unityClip = AudioClip.Create("ProceduralCarrier", n, 1, SampleRate, false);
            unityClip.SetData(d, 0);
            return unityClip;
        }

        void Awake() { if (Source == null) Source = GetComponent<AudioSource>(); }
        void OnEnable() { if (Source != null && Source.clip != null && Application.isPlaying && !Source.isPlaying) Source.Play(); }
        void OnDisable() { if (Source != null) Source.Stop(); }

        void OnAudioFilterRead(float[] data, int channels)
        {
            var src = sampleSource;
            int frames = channels > 0 ? data.Length / channels : 0;
            if (src == null || frames == 0) { System.Array.Clear(data, 0, data.Length); return; }
            if (mono.Length < frames) mono = new float[frames];
            src.Render(mono, frames);
            float g0 = appliedGain, g1 = gain, step = (g1 - g0) / frames;
            for (int i = 0, k = 0; i < frames; i++)
            {
                float s = mono[i] * (g0 + step * i);
                for (int c = 0; c < channels; c++, k++) data[k] *= s;
            }
            appliedGain = g1;
        }
    }

    /// <summary>The enabled AudioListener of the loaded scenes (the menu backdrop disables the scene's own).</summary>
    public static class AudioListenerLocator
    {
        static AudioListener cached;
        static float nextSearch;

        public static AudioListener Active
        {
            get
            {
                if (cached == null || !cached.isActiveAndEnabled || Time.unscaledTime >= nextSearch)
                {
                    nextSearch = Time.unscaledTime + 1f;
                    cached = null;
                    foreach (var l in Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
                        if (l.isActiveAndEnabled) { cached = l; break; }
                }
                return cached;
            }
        }
    }
}
