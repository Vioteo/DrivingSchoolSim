namespace DrivingSchool.Settings
{
    /// <summary>Volume groups of the Sound tab (docs/ui-settings.md). Master volume is applied once, on the listener.</summary>
    public enum AudioChannel
    {
        /// <summary>Own engine and starter — «Двигатель».</summary>
        Engine,
        /// <summary>Tyres, wind, rain, city, traffic, trains, crossing bell, impacts — «Окружение и трафик».</summary>
        Environment,
        /// <summary>Instructor voice — «Голос инструктора» (no voice yet).</summary>
        Instructor,
        /// <summary>Menu clicks and notifications — «Интерфейс».</summary>
        Interface,
        /// <summary>Controls of the own car (relay, key, belt, gear lever, handbrake, horn): master volume only.</summary>
        Controls,
    }

    public static class AudioMix
    {
        /// <summary>Linear gain 0…1 of a group (without master).</summary>
        public static float Gain(AudioSection audio, AudioChannel channel)
        {
            if (audio == null) audio = new AudioSection();
            int percent;
            switch (channel)
            {
                case AudioChannel.Engine: percent = audio.engine; break;
                case AudioChannel.Environment: percent = audio.environment; break;
                case AudioChannel.Instructor: percent = audio.instructor; break;
                case AudioChannel.Interface: percent = audio.ui; break;
                default: return 1f;
            }
            return percent <= 0 ? 0f : percent >= 100 ? 1f : percent / 100f;
        }

        /// <summary>Gain of a group with the settings currently applied.</summary>
        public static float Current(AudioChannel channel) => Gain(SettingsService.Current.audio, channel);
    }
}
