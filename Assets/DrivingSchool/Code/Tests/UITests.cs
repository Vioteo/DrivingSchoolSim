using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using DrivingSchool.Editor;
using DrivingSchool.Presentation.UI;

namespace DrivingSchool.Tests
{
    /// <summary>T03 / A03. Ассеты создаёт генератор: Driving School/Build UI Prefabs (он же собирает шрифты и темы).</summary>
    public class UITests
    {
        const string MenuPrefab = "Assets/DrivingSchool/Prefabs/UI/MainMenu.prefab";
        const string Rebuild = "Запустите Driving School/Build UI Prefabs";

        static TMP_FontAsset UIFont()
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(UIBuilder.UIFontPath);
            Assert.That(font, Is.Not.Null, $"Нет {UIBuilder.UIFontPath}. {Rebuild}");
            return font;
        }

        [Test] public void UIFontContainsCyrillicAndTypography()
        {
            var font = UIFont();
            Assert.That(font.HasCharacters(UIBuilder.UICharset(), out List<char> missing), Is.True,
                "Нет глифов: " + string.Join(" ", (missing ?? new List<char>()).Select(c => $"U+{(int)c:X4}")));
            Assert.That(font.HasCharacters("Съешь ещё этих мягких французских булок, да выпей же чаю. Ёё №«»—", out List<char> _), Is.True);
        }

        [Test] public void GlyphCheckRejectsSymbolAbsentFromFont()
        {
            // ◀ отсутствует в Golos Text (docs/ui-settings.md §6): проверка обязана это заметить, иначе она ничего не доказывает.
            Assert.That(UIFont().HasCharacters("◀", out List<char> missing), Is.False);
            Assert.That(missing, Does.Contain('◀'));
        }

        [Test] public void UIFontHasRealMediumAndBoldFaces()
        {
            var table = UIFont().fontWeightTable;
            Assert.That(table[5].regularTypeface, Is.Not.Null, "Medium (500)");
            Assert.That(table[7].regularTypeface, Is.Not.Null, "Bold (700)");
        }

        static GameObject LoadMenu()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MenuPrefab);
            Assert.That(prefab, Is.Not.Null, $"Нет {MenuPrefab}. {Rebuild}");
            return prefab;
        }

        [Test] public void MainMenuTextUsesCyrillicFontWithoutMissingGlyphs()
        {
            var texts = LoadMenu().GetComponentsInChildren<TMP_Text>(true);
            Assert.That(texts, Is.Not.Empty);
            foreach (var t in texts)
            {
                Assert.That(t.font, Is.Not.Null, t.name);
                Assert.That(t.font.name, Does.StartWith("GolosText").Or.StartWith("RobotoMono"), $"{t.name}: {t.font.name}");
                string plain = Regex.Replace(t.text, "<[^>]+>", "").Replace("\n", "");
                Assert.That(t.font.HasCharacters(plain, out List<char> missing), Is.True,
                    $"{t.name}: нет глифов {string.Join(" ", missing ?? new List<char>())}");
            }
        }

        [Test] public void MainMenuScalesFrom1080pReference()
        {
            var scaler = LoadMenu().GetComponent<CanvasScaler>();
            Assert.That(scaler.uiScaleMode, Is.EqualTo(CanvasScaler.ScaleMode.ScaleWithScreenSize));
            Assert.That(scaler.referenceResolution, Is.EqualTo(new Vector2(1920, 1080)));
            Assert.That(scaler.matchWidthOrHeight, Is.EqualTo(0.5f));
        }

        sealed class Menu : System.IDisposable
        {
            public readonly GameObject Root;
            public readonly MainMenuController C;
            public Menu() { Root = Object.Instantiate(LoadMenu()); C = Root.GetComponent<MainMenuController>(); Assert.That(C, Is.Not.Null); C.Initialize(); }
            public void Dispose() { Object.DestroyImmediate(Root); }
        }

        [Test] public void NavigationWrapsAndSkipsUnavailableExamRoute()
        {
            using (var m = new Menu())
            {
                var c = m.C;
                Assert.That(c.examButton.interactable, Is.False, "Экзаменационный маршрут ещё не реализован");
                Assert.That(c.continueButton.navigation.selectOnUp, Is.EqualTo(c.exitButton));
                Assert.That(c.exitButton.navigation.selectOnDown, Is.EqualTo(c.continueButton));
                Assert.That(c.theoryButton.navigation.selectOnDown, Is.EqualTo(c.settingsButton));
                Assert.That(c.settingsButton.navigation.selectOnUp, Is.EqualTo(c.theoryButton));
            }
        }

        [Test] public void EscapeOpensConfirmationAndNeverExitsDirectly()
        {
            using (var m = new Menu())
            {
                var c = m.C;
                int exits = 0;
                c.OnExitConfirmed.AddListener(() => exits++);
                Assert.That(c.IsExitDialogOpen, Is.False);

                c.HandleCancel();
                Assert.That(c.IsExitDialogOpen, Is.True);
                Assert.That(c.Focused, Is.EqualTo(c.exitCancelButton), "По умолчанию фокус на безопасной «Отмене»");

                c.HandleCancel();
                Assert.That(c.IsExitDialogOpen, Is.False);
                Assert.That(c.Focused, Is.EqualTo(c.exitButton));
                Assert.That(exits, Is.Zero);

                c.RequestExit();
                c.exitConfirmButton.onClick.Invoke();
                Assert.That(exits, Is.EqualTo(1));
            }
        }

        [Test] public void TabCyclesAndRecoversWhenNothingIsFocused()
        {
            using (var m = new Menu())
            {
                var c = m.C;
                Assert.DoesNotThrow(() => c.MoveFocus(-1));
                Assert.That(c.Focused, Is.EqualTo(c.exitButton), "Без фокуса Shift+Tab встаёт на последний пункт");
                c.MoveFocus(1);
                Assert.That(c.Focused, Is.EqualTo(c.continueButton), "Переход по кругу");
                for (int i = 0; i < 3; i++) c.MoveFocus(1);
                Assert.That(c.Focused, Is.EqualTo(c.settingsButton), "Недоступный пункт пропускается");
            }
        }

        // ===== T46: пауза и запуск через главное меню =====
        const string PausePrefab = "Assets/DrivingSchool/Prefabs/UI/PauseMenu.prefab";

        /// <summary>Заглушка «скрипта поездки»: пространство имён DrivingSchool.*, не UI — пауза обязана её выключить.</summary>
        sealed class FakeDriveScript : MonoBehaviour { }

        sealed class Pause : System.IDisposable
        {
            public readonly GameObject Root;
            public readonly PauseMenuController C;
            readonly float timeScale;
            public Pause()
            {
                timeScale = Time.timeScale;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PausePrefab);
                Assert.That(prefab, Is.Not.Null, $"Нет {PausePrefab}. {Rebuild}");
                Root = Object.Instantiate(prefab);
                C = Root.GetComponent<PauseMenuController>();
                Assert.That(C, Is.Not.Null);
                C.Initialize();
            }
            public void Dispose() { Object.DestroyImmediate(Root); Time.timeScale = timeScale; AudioListener.pause = false; }
        }

        [Test] public void PauseMenuHasWorkingButtonsAndStartsHidden()
        {
            using (var p = new Pause())
            {
                var c = p.C;
                Assert.That(new[] { c.resumeButton, c.restartButton, c.settingsButton, c.exitToMenuButton }, Has.None.Null);
                Assert.That(c.panel.activeSelf, Is.False);
                Assert.That(c.resumeButton.navigation.selectOnUp, Is.EqualTo(c.exitToMenuButton), "Навигация по кругу");
                foreach (var t in p.Root.GetComponentsInChildren<TMP_Text>(true))
                    Assert.That(t.font.name, Does.StartWith("GolosText").Or.StartWith("RobotoMono"), t.name);
            }
        }

        [Test] public void PauseFreezesTimeAndDriveScriptsAndResumeRestoresThem()
        {
            var drive = new GameObject("FakeDrive").AddComponent<FakeDriveScript>();
            try
            {
                using (var p = new Pause())
                {
                    Time.timeScale = 0.5f;
                    p.C.Pause();
                    Assert.That(p.C.IsPaused, Is.True);
                    Assert.That(Time.timeScale, Is.Zero);
                    Assert.That(drive.enabled, Is.False, "Скрипт поездки должен быть выключен на паузе");
                    Assert.That(p.C.enabled, Is.True, "Сама пауза не замораживается");
                    Assert.That(p.C.panel.activeSelf, Is.True);

                    p.C.Resume();
                    Assert.That(Time.timeScale, Is.EqualTo(0.5f), "Возвращается прежний масштаб времени, а не 1");
                    Assert.That(drive.enabled, Is.True);
                    Assert.That(p.C.panel.activeSelf, Is.False);
                }
            }
            finally { Object.DestroyImmediate(drive.gameObject); }
        }

        [Test] public void ResumeWithoutPauseChangesNothing()
        {
            using (var p = new Pause())
            {
                Time.timeScale = 0.25f;
                p.C.Resume();
                Assert.That(Time.timeScale, Is.EqualTo(0.25f));
                Assert.That(p.C.IsPaused, Is.False);
            }
        }

        [Test] public void ExitToMenuRaisesIntentAndUnfreezesTime()
        {
            using (var p = new Pause())
            {
                int exits = 0, restarts = 0;
                p.C.OnExitToMenuRequested.AddListener(() => exits++);
                p.C.OnRestartRequested.AddListener(() => restarts++);
                Time.timeScale = 1f;
                p.C.Pause();
                p.C.exitToMenuButton.onClick.Invoke();
                Assert.That(exits, Is.EqualTo(1));
                Assert.That(restarts, Is.Zero);
                Assert.That(Time.timeScale, Is.EqualTo(1f), "Следующая сцена не должна стартовать замороженной");
                Assert.That(AudioListener.pause, Is.False);
            }
        }

        [Test] public void BuildStartsFromMainMenuAndContainsTestRange()
        {
            var scenes = EditorBuildSettings.scenes;
            Assert.That(scenes, Is.Not.Empty);
            Assert.That(scenes[0].path, Is.EqualTo(UIBuilder.MenuScenePath), "Первая сцена сборки — главное меню. Driving School/Build Main Menu scene");
            Assert.That(scenes[0].enabled, Is.True);
            Assert.That(scenes.Any(s => s.enabled && s.path == UIBuilder.DriveScenePath), Is.True, "Тестовый полигон должен быть в сборке, иначе меню его не загрузит");
            Assert.That(System.IO.Path.GetFileNameWithoutExtension(UIBuilder.MenuScenePath), Is.EqualTo(AppNavigator.MainMenuScene));
        }

        [Test] public void BackdropPickNeverRepeatsPreviousAndCoversTheRest()
        {
            var rng = new System.Random(7);
            var seen = new HashSet<int>();
            for (int i = 0; i < 300; i++)
            {
                int k = DrivingSchool.Presentation.MenuBackdropDirector.Pick(3, 1, rng);
                Assert.That(k, Is.Not.EqualTo(1), "Тот же фон второй раз подряд");
                Assert.That(k, Is.InRange(0, 2));
                seen.Add(k);
            }
            Assert.That(seen, Is.EquivalentTo(new[] { 0, 2 }));
            Assert.That(DrivingSchool.Presentation.MenuBackdropDirector.Pick(1, 0, rng), Is.EqualTo(0), "Единственный фон повторять можно");
            Assert.That(DrivingSchool.Presentation.MenuBackdropDirector.Pick(0, -1, rng), Is.EqualTo(-1));
        }

        [Test] public void EveryMenuBackdropSceneIsInBuild()
        {
            var backdrops = UIBuilder.MenuBackdrops();
            Assert.That(backdrops.Length, Is.GreaterThanOrEqualTo(3));
            foreach (var b in backdrops)
                Assert.That(EditorBuildSettings.scenes.Any(s => s.enabled && s.path == $"Assets/DrivingSchool/Scenes/{b.sceneName}.unity"), Is.True,
                    $"Фон «{b.title}»: сцены {b.sceneName} нет в сборке — меню её не загрузит");
        }
    }
}
