using UnityEngine;
using DrivingSchool.Audio;
using DrivingSchool.Settings;

namespace DrivingSchool.Presentation
{
    /// <summary>
    /// Sound of the train at a railway crossing (T67): rumble and wheel clacks from the point of the train nearest to
    /// the listener, and the locomotive horn twice on the approach. Created by <see cref="RailwayCrossingView"/>.
    /// </summary>
    public sealed class TrainAudio : MonoBehaviour
    {
        /// <summary>Horn when the train front is this far before the crossing, metres.</summary>
        public static readonly float[] HornAtM = { 300f, 120f };

        public RailwayCrossingView crossing;
        public TrainSoundSynth Synth { get; private set; }
        public int HornsBlown { get; private set; }
        ProceduralAudio output;
        AudioSource horn;
        AudioClip hornClip;
        float lastDistance = float.PositiveInfinity;

        void Start()
        {
            int rate = ProceduralAudio.SampleRate;
            Synth = new TrainSoundSynth(rate);
            output = ProceduralAudio.Create(transform, "Audio_Train", Vector3.zero, Synth, 1f, 12f, 400f);
            var go = new GameObject("Audio_TrainHorn");
            go.transform.SetParent(transform, false);
            horn = go.AddComponent<AudioSource>();
            horn.playOnAwake = false; horn.spatialBlend = 1f; horn.minDistance = 25f; horn.maxDistance = 900f; horn.dopplerLevel = 0.5f;
            var d = SoundClips.TrainHorn(rate);
            hornClip = AudioClip.Create("TrainHorn", d.Length, 1, rate, false); hornClip.SetData(d, 0);
        }

        void Update()
        {
            if (crossing == null || Synth == null) return;
            bool running = crossing.TrainRunning;
            float env = AudioMix.Current(AudioChannel.Environment);
            output.Gain = env;
            Synth.Set(crossing.trainSpeedKmh / 3.6f, running);
            if (!running) { lastDistance = float.PositiveInfinity; return; }

            // The whole train sounds; the loudest part is the one nearest to the listener.
            Vector3 dir = crossing.trainDirection, front = crossing.TrainFront, rear = front - dir * crossing.trainLength;
            var listener = AudioListenerLocator.Active;
            Vector3 at = front;
            if (listener != null)
            {
                float s = Mathf.Clamp(Vector3.Dot(listener.transform.position - rear, dir), 0f, crossing.trainLength);
                at = rear + dir * s;
            }
            output.transform.position = at + Vector3.up * 1.2f;
            horn.transform.position = front + Vector3.up * 3.5f;

            float distance = crossing.TrainDistanceToCrossing;   // front of the train before the crossing, metres
            foreach (var h in HornAtM)
                if (lastDistance > h && distance <= h) { horn.PlayOneShot(hornClip, 0.9f * env); HornsBlown++; }
            lastDistance = distance;
        }
    }
}
