using System.IO;
using DrivingSchool.Presentation.UI;
using DrivingSchool.Settings;
using NUnit.Framework;
using UnityEngine;

namespace DrivingSchool.Tests
{
    /// <summary>T65: профиль ученика — выбранная машина и её настройки сохраняются, коробка совпадает с настройками игры.</summary>
    public sealed class ProfileTests
    {
        string dir;
        GameSettings settingsBefore;

        [SetUp] public void SetUp()
        {
            dir = Path.Combine(Path.GetTempPath(), "ds-profile-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            ProfileStore.FilePath = Path.Combine(dir, "profile.json");
            SettingsStore.FilePath = Path.Combine(dir, "settings.json");
            settingsBefore = SettingsService.Current.Clone();
            SettingsService.Publish(new GameSettings());
            ProfileService.Reset();
        }

        [TearDown] public void TearDown()
        {
            // The test profile goes first: restoring the settings would otherwise save it into the player's real profile.
            ProfileService.Reset(new PlayerProfile());
            SettingsService.Publish(settingsBefore);
            ProfileStore.FilePath = null;
            SettingsStore.FilePath = null;
            ProfileService.Reset();
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }

        [Test] public void CarChoiceAndSetupSurviveARestart()
        {
            ProfileService.SelectCar("DS_Crossover_A");
            ProfileService.UpdateSetup("DS_Crossover_A", s => { s.transmission = 1; s.abs = false; s.paint = 3; });
            Assert.That(SettingsService.Current.gameplay.transmission, Is.EqualTo(1), "коробка выбранной машины — в настройках игры");

            ProfileService.Reset();   // «перезапуск»: профиль читается из файла
            var p = ProfileService.Current;
            Assert.That(p.carId, Is.EqualTo("DS_Crossover_A"));
            var s2 = p.For("DS_Crossover_A");
            Assert.That((s2.transmission, s2.abs, s2.paint), Is.EqualTo((1, false, 3)));
            Assert.That(JsonUtility.FromJson<GameSettings>(File.ReadAllText(SettingsStore.FilePath)).gameplay.transmission, Is.EqualTo(1));
        }

        [Test] public void EachCarRemembersItsGearbox()
        {
            ProfileService.SelectCar("DS_Sedan_A");
            ProfileService.UpdateSetup("DS_Sedan_A", s => s.transmission = 0);
            ProfileService.SelectCar("DS_Crossover_A");
            ProfileService.UpdateSetup("DS_Crossover_A", s => s.transmission = 1);
            ProfileService.SelectCar("DS_Sedan_A");
            Assert.That(SettingsService.Current.gameplay.transmission, Is.EqualTo(0));
            ProfileService.SelectCar("DS_Crossover_A");
            Assert.That(SettingsService.Current.gameplay.transmission, Is.EqualTo(1));
            // Changed in the settings screen: that is the gearbox of the chosen car.
            var s = SettingsService.Current.Clone(); s.gameplay.transmission = 0; SettingsService.Publish(s);
            Assert.That(ProfileService.Current.For("DS_Crossover_A").transmission, Is.EqualTo(0));
            Assert.That(ProfileService.Current.For("DS_Sedan_A").transmission, Is.EqualTo(0));
        }

        [Test] public void ABrokenOrOddFileGivesASaneProfile()
        {
            File.WriteAllText(ProfileStore.FilePath, "{ nonsense");
            Assert.That(ProfileStore.Load().name, Is.EqualTo(PlayerProfile.DefaultName));
            File.WriteAllText(ProfileStore.FilePath, "{\"name\":\"\",\"carId\":\"DS_Sedan_A\",\"cars\":[{\"carId\":\"DS_Sedan_A\",\"transmission\":7,\"paint\":99},{\"carId\":\"DS_Sedan_A\"}]}");
            var p = ProfileStore.Load();
            Assert.That(p.name, Is.EqualTo(PlayerProfile.DefaultName));
            Assert.That(p.cars.Length, Is.EqualTo(1), "повтор машины отброшен");
            Assert.That((p.cars[0].transmission, p.cars[0].paint), Is.EqualTo((0, 0)));
            Assert.That(CarPaints.TryGet(0, out _, out _, out _), Is.False, "0 — заводской цвет");
            Assert.That(CarPaints.TryGet(4, out float r, out _, out _) && r > 0.3f, Is.True);
        }
    }
}
