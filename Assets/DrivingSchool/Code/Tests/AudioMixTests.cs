using NUnit.Framework;
using DrivingSchool.Settings;

namespace DrivingSchool.Tests
{
    /// <summary>T67: volume groups of the Sound tab.</summary>
    public sealed class AudioMixTests
    {
        [Test] public void GroupGainFollowsSlider()
        {
            var a = new AudioSection { engine = 40, environment = 0, instructor = 100, ui = 55 };
            Assert.That(AudioMix.Gain(a, AudioChannel.Engine), Is.EqualTo(0.4f).Within(1e-6f));
            Assert.That(AudioMix.Gain(a, AudioChannel.Environment), Is.Zero);
            Assert.That(AudioMix.Gain(a, AudioChannel.Instructor), Is.EqualTo(1f));
            Assert.That(AudioMix.Gain(a, AudioChannel.Interface), Is.EqualTo(0.55f).Within(1e-6f));
            Assert.That(AudioMix.Gain(a, AudioChannel.Controls), Is.EqualTo(1f), "own car controls: master volume only");
            Assert.That(AudioMix.Gain(null, AudioChannel.Engine), Is.EqualTo(0.8f).Within(1e-6f), "defaults");
        }

        [Test] public void SoundGroupsAreRealSettings()
        {
            // Engine, environment and interface have sound now; the instructor voice does not yet.
            var s = new GameSettings();
            foreach (var key in new[] { "audio.engine", "audio.environment", "audio.ui" })
                Assert.That(SettingsSession.Availability(SettingsSchema.Find(key), s, false, false), Is.EqualTo(SettingAvailability.Enabled), key);
            Assert.That(SettingsSession.Availability(SettingsSchema.Find("audio.instructor"), s, false, false), Is.EqualTo(SettingAvailability.Stub));
        }
    }
}
