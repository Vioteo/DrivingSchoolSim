using DrivingSchool.Presentation.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

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

        /// <summary>
        /// Экран «Задания» (T70): вкладки разделов, список заданий с отметкой прогресса (строки создаёт
        /// <see cref="LessonCatalogController"/> по образцу RowTemplate), описание и прогресс выбранного задания.
        /// </summary>
        static void BuildLessonCatalog()
        {
            var theme = BuildThemes();
            var canvas = CreateCanvas("LessonCatalog");
            canvas.GetComponent<Canvas>().sortingOrder = 10;
            var catalog = canvas.AddComponent<LessonCatalogController>();
            var bg = CreateFill("Background", canvas.transform);
            AddThemedImage(bg.gameObject, ThemeRole.BgDark, theme);
            var content = CreateFixed("Content", bg, new Vector2(.5f, .5f), new Vector2(1600, 900), Vector2.zero);

            var heading = CreateFixed("Heading", content, new Vector2(0, 1), new Vector2(800, 80), Vector2.zero);
            AddThemedText(heading.gameObject, "Задания", 64, ThemeRole.Text, theme, TextAlignmentOptions.Left, FontWeight.Bold);
            var overall = CreateFixed("Overall", content, new Vector2(1, 1), new Vector2(700, 60), new Vector2(0, -10));
            catalog.overall = AddThemedText(overall.gameObject, "", 28, ThemeRole.Muted, theme, TextAlignmentOptions.Right);

            // Разделы: лепестки руля / Q E / ←→; мышью — щелчок.
            int n = LessonCatalogController.SectionTitles.Length;
            catalog.sectionButtons = new Button[n];
            catalog.sectionLabels = new TMP_Text[n];
            for (int i = 0; i < n; i++)
            {
                var tab = CreateMenuItem("Section_" + i, content, new Vector2(i * 306, -104), new Vector2(294, 64), LessonCatalogController.SectionTitles[i], 28, theme, null);
                catalog.sectionButtons[i] = tab;
                catalog.sectionLabels[i] = tab.GetComponent<MenuItemView>().label;
            }

            // Список заданий раздела: окно с маской, строки листаются под выбранное задание.
            var viewport = CreateFixed("List", content, new Vector2(0, 1), new Vector2(720, 600), new Vector2(0, -196));
            viewport.gameObject.AddComponent<RectMask2D>();
            var rows = CreateFixed("Rows", viewport, new Vector2(0, 1), new Vector2(720, 0), Vector2.zero, new Vector2(0, 1));
            catalog.listViewport = viewport;
            catalog.listContent = rows;
            var row = CreateMenuItem("RowTemplate", rows, Vector2.zero, new Vector2(720, 64), "Задание", 28, theme, null);
            var rowLabel = row.GetComponent<MenuItemView>().label.rectTransform;
            rowLabel.offsetMax = new Vector2(-250, rowLabel.offsetMax.y);
            var status = CreateFixed("Status", row.transform, new Vector2(1, .5f), new Vector2(200, 40), new Vector2(-44, 0), new Vector2(1, .5f));
            var statusText = AddThemedText(status.gameObject, "", 22, ThemeRole.Muted, theme, TextAlignmentOptions.Right);
            statusText.raycastTarget = false;
            statusText.textWrappingMode = TextWrappingModes.NoWrap;
            var dot = CreateFixed("Dot", row.transform, new Vector2(1, .5f), new Vector2(14, 14), new Vector2(-18, 0), new Vector2(1, .5f));
            AddImage(dot.gameObject, theme.line).raycastTarget = false;
            catalog.rowTemplate = row;
            catalog.rowHeight = 64; catalog.rowGap = 8;
            row.gameObject.SetActive(false);

            // Описание и прогресс выбранного задания.
            var panel = CreateFixed("Details", content, new Vector2(1, 1), new Vector2(850, 600), new Vector2(0, -196));
            AddThemedImage(panel.gameObject, ThemeRole.BgPanel, theme);
            var accent = CreateFixed("Accent", panel, new Vector2(0, 1), new Vector2(850, 5), Vector2.zero);
            AddThemedImage(accent.gameObject, ThemeRole.Accent, theme);
            var mode = CreateFixed("Mode", panel, new Vector2(0, 1), new Vector2(760, 40), new Vector2(44, -32));
            catalog.mode = AddThemedText(mode.gameObject, "", 22, ThemeRole.Accent, theme, TextAlignmentOptions.Left);
            var title = CreateFixed("Title", panel, new Vector2(0, 1), new Vector2(760, 64), new Vector2(44, -80));
            catalog.title = AddThemedText(title.gameObject, "", 42, ThemeRole.Text, theme, TextAlignmentOptions.Left, FontWeight.Bold);
            var body = CreateFixed("Description", panel, new Vector2(0, 1), new Vector2(760, 250), new Vector2(44, -160));
            catalog.description = AddThemedText(body.gameObject, "", 25, ThemeRole.Text2, theme, TextAlignmentOptions.TopLeft);
            var line = CreateFixed("Line", panel, new Vector2(0, 1), new Vector2(760, 2), new Vector2(44, -420));
            AddThemedImage(line.gameObject, ThemeRole.Line, theme);
            var progress = CreateFixed("Progress", panel, new Vector2(0, 1), new Vector2(760, 90), new Vector2(44, -432));
            catalog.progress = AddThemedText(progress.gameObject, "", 24, ThemeRole.Text, theme, TextAlignmentOptions.TopLeft);
            catalog.startButton = CreateMenuItem("Start", panel, new Vector2(44, 28), new Vector2(380, 64), "Начать задание", 28, theme, null, new Vector2(0, 0));

            catalog.backButton = CreateMenuItem("Back", content, Vector2.zero, new Vector2(280, 60), "Назад", 26, theme, null, new Vector2(0, 0));
            var hint = CreateFixed("Hint", content, new Vector2(1, 0), new Vector2(1280, 40), new Vector2(0, 10));
            catalog.hint = AddThemedText(hint.gameObject, "↑↓ — задание    ←→ / Q E — раздел    Enter — начать    Esc — назад", 22, ThemeRole.Muted, theme, TextAlignmentOptions.Right);
            SavePrefab(canvas, "LessonCatalog");
        }
    }
}
