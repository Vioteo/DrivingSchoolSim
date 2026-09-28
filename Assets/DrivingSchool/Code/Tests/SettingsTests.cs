using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DrivingSchool.Editor;
using DrivingSchool.Presentation;
using DrivingSchool.Presentation.UI;
using DrivingSchool.Settings;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using SettingsService = DrivingSchool.Settings.SettingsService;

namespace DrivingSchool.Tests
{
    /// <summary>T43 / A03: модель настроек (DS.Settings), файл settings.json, экран настроек, применение к камере.</summary>
    public class SettingsTests
    {
        static SettingItem Item(string key) { var i = SettingsSchema.Find(key); Assert.That(i, Is.Not.Null, key); return i; }

        // ---------- модель ----------

        [Test] public void DefaultsFollowSpecAndHighPreset()
        {
            var s = new GameSettings();
            Assert.That(s.graphics.vSync, Is.True);
            Assert.That(s.graphics.fpsLimit, Is.EqualTo(1), "60 FPS");
            Assert.That(s.gameplay.fov, Is.EqualTo(75));
            Assert.That(s.gameplay.uiScale, Is.EqualTo(100));
            Assert.That(s.controls.keyboardSteerSpeed, Is.EqualTo(100));
            Assert.That(s.audio.master, Is.EqualTo(80));
            Assert.That(SettingsSchema.MatchPreset(s), Is.EqualTo(s.graphics.qualityPreset), "Значения по умолчанию = пресет «Высокое»");
            foreach (var item in SettingsSchema.All.Where(i => i.IsValue && i.Key != SettingsSchema.Resolution))
                Assert.That(SettingsSchema.Get(s, item.Key), Is.EqualTo(item.Default), $"{item.Key}: значение по умолчанию в схеме и в GameSettings расходятся");
        }

        [Test] public void EveryKeyMapsToItsOwnField()
        {
            var keys = SettingsSchema.All.Select(i => i.Key).ToList();
            Assert.That(keys, Is.Unique);
            string json = JsonUtility.ToJson(new GameSettings());
            foreach (var item in SettingsSchema.All.Where(i => i.IsValue))
            {
                Assert.That(json, Does.Contain($"\"{item.Key.Split('.')[1]}\""), $"{item.Key}: нет поля в GameSettings");
                // Меняем один ключ — меняется ровно он (ловит перепутанные case в Get/Set).
                var s = new GameSettings();
                int other = item.Next(SettingsSchema.Get(s, item.Key), 1);
                if (other == SettingsSchema.Get(s, item.Key)) other = item.Next(other, -1);
                if (item.Count < 2) continue;
                SettingsSchema.Set(s, item.Key, other);
                Assert.That(SettingsSchema.Get(s, item.Key), Is.EqualTo(other), item.Key);
                foreach (var o in SettingsSchema.All.Where(i => i.IsValue && i.Key != item.Key))
                    Assert.That(SettingsSchema.Get(s, o.Key), Is.EqualTo(SettingsSchema.Get(new GameSettings(), o.Key)), $"{item.Key} задел {o.Key}");
            }
        }

        [Test] public void SanitizeClampsSnapsAndRepairs()
        {
            var s = new GameSettings();
            s.gameplay.fov = 500; s.graphics.shadows = 9; s.graphics.drawDistance = 333; s.audio.master = -20;
            s.graphics.resolution = "garbage"; s.controls = null;
            var fixedKeys = new List<string>();
            SettingsValidator.Sanitize(s, fixedKeys);
            Assert.That(s.gameplay.fov, Is.EqualTo(100));
            Assert.That(s.graphics.shadows, Is.EqualTo(Item("graphics.shadows").Default), "Неизвестный индекс списка → по умолчанию");
            Assert.That(s.graphics.drawDistance, Is.EqualTo(350), "На шаг ползунка 50 м");
            Assert.That(s.audio.master, Is.Zero);
            Assert.That(s.graphics.resolution, Is.EqualTo("1920x1080"));
            Assert.That(s.controls, Is.Not.Null);
            Assert.That(fixedKeys, Does.Contain("gameplay.fov").And.Contain("controls").And.Contain(SettingsSchema.Resolution));

            var ok = new GameSettings(); ok.gameplay.fov = 90;
            var none = new List<string>();
            SettingsValidator.Sanitize(ok, none);
            Assert.That(ok.gameplay.fov, Is.EqualTo(90), "Допустимое значение не трогается");
            Assert.That(none, Is.Empty);
        }

        [Test] public void MigratorUpgradesUnversionedAndFlagsNewer()
        {
            var old = new GameSettings { version = 0 };
            SettingsMigrator.Migrate(old, out bool newer);
            Assert.That(old.version, Is.EqualTo(GameSettings.CurrentVersion)); Assert.That(newer, Is.False);
            var future = new GameSettings { version = 99 };
            SettingsMigrator.Migrate(future, out newer);
            Assert.That(newer, Is.True); Assert.That(future.version, Is.EqualTo(99));
        }

        [Test] public void PresetDrivesDependentItemsAndManualEditMakesItCustom()
        {
            var session = new SettingsSession(new GameSettings());
            session.Set(SettingsSchema.QualityPreset, 0);
            Assert.That(session.Draft.graphics.shadows, Is.EqualTo(SettingsSchema.Presets[0].shadows));
            Assert.That(session.Draft.graphics.drawDistance, Is.EqualTo(SettingsSchema.Presets[0].draw));
            session.Set("graphics.antiAliasing", 3);
            Assert.That(session.Draft.graphics.qualityPreset, Is.EqualTo(SettingsSchema.CustomPreset), "Ручная правка → «Своё»");
            session.Set("graphics.antiAliasing", SettingsSchema.Presets[0].aa);
            Assert.That(session.Draft.graphics.qualityPreset, Is.EqualTo(0), "Вернули значения пресета → снова пресет");
            session.Set(SettingsSchema.QualityPreset, SettingsSchema.CustomPreset);
            Assert.That(session.Draft.graphics.shadows, Is.EqualTo(SettingsSchema.Presets[0].shadows), "«Своё» ничего не меняет");
        }

        [Test] public void DraftCountsChangesRevertsAndCommits()
        {
            var session = new SettingsSession(new GameSettings());
            Assert.That(session.IsDirty, Is.False);
            session.Set("gameplay.fov", 60);
            session.Set("audio.master", 40);
            Assert.That(session.ChangedCount, Is.EqualTo(2));
            Assert.That(session.IsChanged("gameplay.fov"), Is.True);
            session.Set("audio.master", 80);
            Assert.That(session.ChangedCount, Is.EqualTo(1), "Вернули значение — пункт не изменён");
            session.Revert();
            Assert.That(session.IsDirty, Is.False);
            Assert.That(session.Draft.gameplay.fov, Is.EqualTo(75));
            session.Set("gameplay.fov", 90);
            var changed = session.Commit();
            Assert.That(changed, Is.EquivalentTo(new[] { "gameplay.fov" }));
            Assert.That(session.Saved.gameplay.fov, Is.EqualTo(90));
            Assert.That(session.IsDirty, Is.False);
        }

        [Test] public void ResetTabSkipsLockedItemsAndResetItemRestoresDefault()
        {
            var start = new GameSettings(); start.gameplay.transmission = 1; start.gameplay.fov = 60;
            var session = new SettingsSession(start);
            var tab = SettingsSchema.Tabs.First(t => t.Id == "gameplay");
            session.ResetTab(tab, item => !item.Has(SettingFlags.LockInDrive));
            Assert.That(session.Draft.gameplay.fov, Is.EqualTo(75));
            Assert.That(session.Draft.gameplay.transmission, Is.EqualTo(1), "В паузе КПП не сбрасывается");
            session.Set("audio.master", 10);
            session.ResetItem("audio.master");
            Assert.That(session.Draft.audio.master, Is.EqualTo(80));
        }

        [Test] public void AvailabilityExplainsWhyItemIsLocked()
        {
            var s = new GameSettings();
            Assert.That(SettingsSession.Availability(Item("controls.steeringLock"), s, false, false), Is.EqualTo(SettingAvailability.NoWheel));
            Assert.That(SettingsSession.Availability(Item("gameplay.transmission"), s, true, true), Is.EqualTo(SettingAvailability.AfterDrive));
            Assert.That(SettingsSession.Availability(Item("gameplay.transmission"), s, false, false), Is.EqualTo(SettingAvailability.Enabled));
            Assert.That(SettingsSession.Availability(Item("graphics.fpsLimit"), s, false, false), Is.EqualTo(SettingAvailability.DependsOff), "V-Sync включён");
            s.graphics.vSync = false;
            Assert.That(SettingsSession.Availability(Item("graphics.fpsLimit"), s, false, false), Is.EqualTo(SettingAvailability.Enabled));
            s.controls.device = 1;
            Assert.That(SettingsSession.Availability(Item("controls.keyboardSteerSpeed"), s, false, true), Is.EqualTo(SettingAvailability.KeyboardOnly));
            Assert.That(SettingsSession.Availability(Item("audio.instructor"), s, false, false), Is.EqualTo(SettingAvailability.Stub));   // звук двигателя появился в T67, голоса инструктора ещё нет
            Assert.That(SettingsSession.CanChange(SettingAvailability.Stub), Is.True, "Заглушка сохраняет значение заранее");
            Assert.That(SettingsSession.CanChange(SettingAvailability.NoWheel), Is.False);
        }

        [Test] public void SliderAndCycleStepping()
        {
            var fov = Item("gameplay.fov");
            Assert.That(fov.Next(100, 1), Is.EqualTo(100), "Ползунок упирается");
            Assert.That(fov.Next(75, -1), Is.EqualTo(74));
            var theme = Item("gameplay.uiTheme");
            Assert.That(theme.Next(3, 1), Is.EqualTo(0), "Список циклический");
            Assert.That(theme.Next(0, -1), Is.EqualTo(3));
            Assert.That(Item("graphics.drawDistance").Format(800), Is.EqualTo("800 м"));
        }

        // ---------- файл ----------

        string tempPath;
        [SetUp] public void UseTempFile() { tempPath = Path.Combine(Path.GetTempPath(), $"ds-settings-{System.Guid.NewGuid():N}.json"); SettingsStore.FilePath = tempPath; }
        [TearDown] public void RestoreFile() { SettingsStore.FilePath = null; if (File.Exists(tempPath)) File.Delete(tempPath); }

        [Test] public void StoreRoundTripsAndMissingFileGivesDefaults()
        {
            Assert.That(SettingsStore.Load().gameplay.fov, Is.EqualTo(75), "Нет файла → по умолчанию");
            var s = new GameSettings(); s.gameplay.fov = 88; s.graphics.resolution = "2560x1440"; s.audio.muteInBackground = false;
            SettingsStore.Save(s);
            Assert.That(File.Exists(tempPath + ".tmp"), Is.False, "Временный файл заменён");
            var back = SettingsStore.Load();
            Assert.That(back.gameplay.fov, Is.EqualTo(88));
            Assert.That(back.graphics.resolution, Is.EqualTo("2560x1440"));
            Assert.That(back.audio.muteInBackground, Is.False);
            SettingsStore.Save(back);   // перезапись существующего файла
            Assert.That(SettingsStore.Load().gameplay.fov, Is.EqualTo(88));
        }

        [Test] public void BrokenFileFallsBackToDefaultsWithWarning()
        {
            File.WriteAllText(tempPath, "{ \"gameplay\": { \"fov\": 9");
            LogAssert.Expect(LogType.Warning, new Regex("повреждён|Исправлены"));
            var s = SettingsStore.Load(out string warning);
            Assert.That(warning, Is.Not.Null);
            Assert.That(s.gameplay.fov, Is.InRange(50, 100));
        }

        [Test] public void PartialFileKeepsDefaultsIgnoresUnknownAndClamps()
        {
            File.WriteAllText(tempPath, "{\"version\":1,\"gameplay\":{\"fov\":300,\"surprise\":1},\"unknownSection\":{\"x\":1}}");
            LogAssert.Expect(LogType.Warning, new Regex("Исправлены"));
            var s = SettingsStore.Load(out string warning);
            Assert.That(s.gameplay.fov, Is.EqualTo(100));
            Assert.That(s.gameplay.uiScale, Is.EqualTo(100), "Отсутствующее поле → по умолчанию");
            Assert.That(s.graphics.vSync, Is.True, "Отсутствующий раздел → по умолчанию");
        }

        // ---------- экран ----------

        sealed class OpenedScreen : System.IDisposable
        {
            public readonly GameObject Root;
            public readonly SettingsScreenController C;
            readonly GameSettings before = SettingsService.Current.Clone();
            public OpenedScreen(bool inDrive = false)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(UIBuilder.SettingsPrefabPath);
                Assert.That(prefab, Is.Not.Null, "Нет Settings.prefab — Driving School/Build Settings screen");
                Root = Object.Instantiate(prefab);
                C = Root.GetComponent<SettingsScreenController>();
                SettingsService.Publish(new GameSettings());
                C.Open(inDrive);
            }
            public void Dispose() { Object.DestroyImmediate(Root); SettingsService.Publish(before); }
        }

        [Test] public void ScreenBuildsRowsForTabAndUsesUIFonts()
        {
            using (var s = new OpenedScreen())
            {
                Assert.That(s.C.IsOpen, Is.True);
                var rows = s.Root.GetComponentsInChildren<SettingsRowView>(false);
                Assert.That(rows.Length, Is.EqualTo(SettingsSchema.Tabs[0].Items.Count()), "Строки вкладки «Графика»");
                s.C.SwitchTab(3);
                rows = s.Root.GetComponentsInChildren<SettingsRowView>(false);
                Assert.That(rows.Select(r => r.Item.Key), Is.EquivalentTo(SettingsSchema.Tabs[3].Items.Select(i => i.Key)));
                foreach (var t in s.Root.GetComponentsInChildren<TMP_Text>(true))
                {
                    Assert.That(t.font.name, Does.StartWith("GolosText").Or.StartWith("RobotoMono"), t.name);
                    string plain = Regex.Replace(t.text, "<[^>]+>", "");
                    Assert.That(t.font.HasCharacters(plain, out List<char> missing), Is.True, $"{t.name}: нет глифов {string.Join(" ", missing ?? new List<char>())}");
                }
                foreach (var item in SettingsSchema.All)
                    Assert.That(item.Label + item.Help + string.Concat(item.Options), Has.None.EqualTo('◀'), "Стрелки — спрайты, в шрифте их нет");
            }
        }

        [Test] public void ScreenEditsDraftAndPreviewThemeRevertsOnClose()
        {
            using (var s = new OpenedScreen())
            {
                s.C.SwitchTab(3);
                var theme = Item("gameplay.uiTheme");
                s.C.SetValue(theme, 2);
                Assert.That(s.C.Session.IsChanged("gameplay.uiTheme"), Is.True);
                Assert.That(UIThemeState.Current.name, Does.Contain("Sign"), "Тема видна сразу");
                s.C.RevertDraft();
                Assert.That(UIThemeState.Current.name, Does.Contain("Asphalt"), "«Отменить» возвращает тему");
                s.C.Step(Item("gameplay.fov"), 1);
                Assert.That(s.C.Session.Draft.gameplay.fov, Is.EqualTo(76));
                Assert.That(s.C.dirtyText.text, Does.Contain("1"));
            }
        }

        [Test] public void LockedItemsIgnoreEditsInDrive()
        {
            using (var s = new OpenedScreen(inDrive: true))
            {
                s.C.SwitchTab(3);
                s.C.Step(Item("gameplay.transmission"), 1);
                Assert.That(s.C.Session.Draft.gameplay.transmission, Is.Zero, "КПП в паузе не меняется");
                s.C.SwitchTab(1);
                s.C.Step(Item("controls.steeringLock"), -1);
                Assert.That(s.C.Session.Draft.controls.steeringLock, Is.EqualTo(900), "Без руля угол не меняется");
            }
        }

        [Test] public void ApplySavesFileAndPublishes()
        {
            using (var s = new OpenedScreen())
            {
                s.C.SwitchTab(3);
                s.C.SetValue(Item("gameplay.fov"), 64);
                s.C.Apply(false);
                Assert.That(SettingsService.Current.gameplay.fov, Is.EqualTo(64));
                Assert.That(SettingsStore.Load().gameplay.fov, Is.EqualTo(64), "Сохранено в settings.json");
                Assert.That(s.C.Session.IsDirty, Is.False);
            }
        }

        [Test] public void ResetTabGoesToDraftNotFile()
        {
            using (var s = new OpenedScreen())
            {
                s.C.SwitchTab(2);
                s.C.SetValue(Item("audio.master"), 30);
                s.C.Apply(false);
                s.C.ResetTabToDefaults();
                Assert.That(s.C.Session.Draft.audio.master, Is.EqualTo(80));
                Assert.That(SettingsStore.Load().audio.master, Is.EqualTo(30), "До «Применить» файл не меняется");
            }
        }

        // ---------- применение к поездке ----------

        [Test] public void DriveApplierSetsCameraFovDistanceAndStartView()
        {
            var go = new GameObject("TestCam", typeof(Camera));
            var rig = go.AddComponent<DriverCameraRig>();
            try
            {
                var st = new GameSettings(); st.gameplay.fov = 62; st.graphics.drawDistance = 450; st.gameplay.defaultCamera = 1;
                DriveSettingsApplier.Apply(st, atDriveStart: false);
                Assert.That(rig.cockpitFov, Is.EqualTo(62));
                Assert.That(go.GetComponent<Camera>().farClipPlane, Is.EqualTo(450));
                Assert.That(rig.mode, Is.EqualTo(DriverCameraRig.Mode.Cockpit), "Вид меняется только при старте поездки");
                DriveSettingsApplier.Apply(st, atDriveStart: true);
                Assert.That(rig.mode, Is.EqualTo(DriverCameraRig.Mode.Chase));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test] public void MirrorQualityLevelsGrow()
        {
            var lo = DriveSettingsApplier.MirrorQuality(0); var mid = DriveSettingsApplier.MirrorQuality(1); var hi = DriveSettingsApplier.MirrorQuality(2);
            Assert.That(lo.resolution.x, Is.LessThan(mid.resolution.x)); Assert.That(mid.resolution.x, Is.LessThan(hi.resolution.x));
            Assert.That(mid.resolution, Is.EqualTo(new Vector2Int(512, 256)), "Среднее = прежнее качество зеркал");
            Assert.That(hi.timeSlicing, Is.False);
        }
    }
}
