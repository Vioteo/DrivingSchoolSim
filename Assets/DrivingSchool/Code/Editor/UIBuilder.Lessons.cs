using DrivingSchool.Presentation.UI;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace DrivingSchool.Editor
{
    public static partial class UIBuilder
    {
        [MenuItem("Driving School/Lessons/Build assignments UI")]
        public static void BuildAssignmentsUI()
        {
            BuildMainMenu();
            BuildLessonCatalog();
            BuildMenuScene();
            Debug.Log("ASSIGNMENTS_UI_BUILT");
        }

        static void BuildLessonCatalog()
        {
            var theme = BuildThemes();
            var canvas = CreateCanvas("LessonCatalog");
            canvas.GetComponent<Canvas>().sortingOrder = 10;
            var catalog = canvas.AddComponent<LessonCatalogController>();
            var bg = CreateFill("Background", canvas.transform);
            AddThemedImage(bg.gameObject, ThemeRole.BgDark, theme);
            var content = CreateFixed("Content", bg, new Vector2(.5f, .5f), new Vector2(1600, 860), Vector2.zero);
            var heading = CreateFixed("Heading", content, new Vector2(0, 1), new Vector2(1500, 80), Vector2.zero);
            AddThemedText(heading.gameObject, "Задания", 64, ThemeRole.Text, theme, TextAlignmentOptions.Left, FontWeight.Bold);
            var subtitle = CreateFixed("Subtitle", content, new Vector2(0, 1), new Vector2(1500, 50), new Vector2(0, -90));
            AddThemedText(subtitle.gameObject, "Выберите урок или свободную практику", 28, ThemeRole.Muted, theme, TextAlignmentOptions.Left);
            catalog.lessonButton = CreateMenuItem("FirstLesson", content, new Vector2(0, -200), new Vector2(590, 100), "Начало движения", 32, theme, null);
            catalog.rangeButton = CreateMenuItem("TestRange", content, new Vector2(0, -316), new Vector2(590, 100), LessonCatalogController.FreeDriveTitle, 32, theme, null);
            var panel = CreateFixed("Details", content, new Vector2(1, 1), new Vector2(950, 580), new Vector2(0, -200));
            AddThemedImage(panel.gameObject, ThemeRole.BgPanel, theme);
            var accent = CreateFixed("Accent", panel, new Vector2(0, 1), new Vector2(950, 5), Vector2.zero);
            AddThemedImage(accent.gameObject, ThemeRole.Accent, theme);
            var mode = CreateFixed("Mode", panel, new Vector2(0, 1), new Vector2(854, 42), new Vector2(48, -40));
            catalog.mode = AddThemedText(mode.gameObject, "", 22, ThemeRole.Accent, theme, TextAlignmentOptions.Left);
            var title = CreateFixed("Title", panel, new Vector2(0, 1), new Vector2(854, 70), new Vector2(48, -100));
            catalog.title = AddThemedText(title.gameObject, "", 44, ThemeRole.Text, theme, TextAlignmentOptions.Left, FontWeight.Bold);
            var body = CreateFixed("Description", panel, new Vector2(0, 1), new Vector2(854, 260), new Vector2(48, -190));
            catalog.description = AddThemedText(body.gameObject, "", 28, ThemeRole.Text2, theme, TextAlignmentOptions.TopLeft);
            catalog.startButton = CreateMenuItem("Start", panel, new Vector2(48, 36), new Vector2(400, 76), "Начать задание", 30, theme, null, new Vector2(0, 0));
            catalog.backButton = CreateMenuItem("Back", content, Vector2.zero, new Vector2(340, 72), "Назад", 28, theme, null, new Vector2(0, 0));
            var hint = CreateFixed("Hint", content, new Vector2(1, 0), new Vector2(1140, 40), Vector2.zero);
            AddThemedText(hint.gameObject, "Стрелки / Tab — выбор     Enter — подтвердить     Esc — назад", 22, ThemeRole.Muted, theme, TextAlignmentOptions.Right);
            catalog.SelectAssignment(LessonLaunch.FirstLesson);
            SavePrefab(canvas, "LessonCatalog");
        }
    }
}
