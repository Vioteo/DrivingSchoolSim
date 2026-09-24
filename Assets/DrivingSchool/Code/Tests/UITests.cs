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
    }
}
