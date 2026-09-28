using UnityEngine;
using DrivingSchool.Audio;
using DrivingSchool.Settings;

namespace DrivingSchool.Presentation.UI
{
    /// <summary>
    /// Interface sounds (T67): focus moves, confirm, back, value steps, tab switch, a violation alert and the
    /// instructor-hint chime. Procedural clips (DS.Audio), a 2D source that keeps playing on the pause menu
    /// (ignoreListenerPause). Volume — «Интерфейс» in the Sound settings.
    /// </summary>
    public static class UISound
    {
        static AudioSource src;
        static AudioClip[] clips;
        static float lastAt = -1f, movesMutedUntil = -1f;
        static SoundClips.Ui lastKind;

        /// <summary>Sounds actually started (tests).</summary>
        public static int Played { get; private set; }

        /// <summary>
        /// A screen just opened: its first focus is placed by code, not by the player — no "move" click for it.
        /// </summary>
        public static void MuteMoves(float seconds) { if (Application.isPlaying) movesMutedUntil = Time.unscaledTime + seconds; }

        public static void Play(SoundClips.Ui kind, float volume = 1f)
        {
            if (!Application.isPlaying) return;
            float now = Time.unscaledTime;
            if (kind == SoundClips.Ui.Move && now < movesMutedUntil) return;
            if (kind == lastKind && now - lastAt < 0.05f) return;     // the same event reported twice in one frame
            lastKind = kind; lastAt = now;
            float g = AudioMix.Current(AudioChannel.Interface) * volume;
            if (g <= 0f) return;
            Ensure();
            src.PlayOneShot(clips[(int)kind], g);
            Played++;
        }

        static void Ensure()
        {
            if (src != null) return;
            var go = new GameObject("UISound") { hideFlags = HideFlags.HideInHierarchy };
            Object.DontDestroyOnLoad(go);
            src = go.AddComponent<AudioSource>();
            src.playOnAwake = false; src.spatialBlend = 0f; src.ignoreListenerPause = true; src.bypassReverbZones = true; src.priority = 32;
            int rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 44100;
            var kinds = (SoundClips.Ui[])System.Enum.GetValues(typeof(SoundClips.Ui));
            clips = new AudioClip[kinds.Length];
            foreach (var k in kinds)
            {
                var d = SoundClips.Interface(rate, k);
                var c = AudioClip.Create("ui_" + k, d.Length, 1, rate, false);
                c.SetData(d, 0);
                clips[(int)k] = c;
            }
        }
    }
}
