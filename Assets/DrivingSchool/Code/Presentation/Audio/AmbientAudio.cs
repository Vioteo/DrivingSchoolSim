using UnityEngine;
using DrivingSchool.Audio;
using DrivingSchool.Settings;

namespace DrivingSchool.Presentation
{
    /// <summary>
    /// Scene background (T67): distant city, wind, rain (on the roof when the listener sits in the player car),
    /// birds on a dry day. Follows <see cref="WeatherController"/>; added to it at start.
    /// </summary>
    public sealed class AmbientAudio : MonoBehaviour
    {
        [Range(0f, 1f)] public float city01 = 0.7f;
        public AmbientSoundSynth Synth { get; private set; }
        ProceduralAudio output;

        void Start()
        {
            Synth = new AmbientSoundSynth(ProceduralAudio.SampleRate);
            output = ProceduralAudio.Create(transform, "Audio_Ambient", Vector3.zero, Synth, 0f, 1f, 500f);
            Update();
        }

        void Update()
        {
            if (Synth == null) return;
            var w = WeatherController.Current;
            Synth.Set(new AmbientSoundInput
            {
                rain01 = w.rainIntensity01, snow01 = w.snowIntensity01,
                daylight01 = WeatherController.Daylight01, city01 = city01, cabin01 = VehicleAudio.PlayerCabin01,
            });
            output.Gain = AudioMix.Current(AudioChannel.Environment);
        }
    }
}
