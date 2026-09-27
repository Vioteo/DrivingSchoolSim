using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.TextCore.LowLevel;
using DrivingSchool.Presentation.UI;

namespace DrivingSchool.Editor
{
    public static partial class UIBuilder
    {
        private static Color BgColor = new Color(0.08f, 0.10f, 0.12f, 1f);
        private static Color PanelColor = new Color(0.12f, 0.15f, 0.18f, 0.95f);
        private static Color CardBgColor = new Color(0.15f, 0.19f, 0.23f, 1f);
        private static Color AccentGold = new Color(0.98f, 0.69f, 0.23f, 1f);
        private static Color AccentCyan = new Color(0.35f, 0.60f, 0.83f, 1f);
        private static Color TextWhite = new Color(0.95f, 0.95f, 0.95f, 1f);
        private static Color TextDim = new Color(0.60f, 0.65f, 0.70f, 1f);
        private static Color DarkOverlay = new Color(0f, 0f, 0f, 0.85f);
        private static Color RedColor = new Color(0.85f, 0.2f, 0.2f, 1f);
        private static Color GreenColor = new Color(0.2f, 0.75f, 0.3f, 1f);

        [MenuItem("Driving School/Import TMP Essentials")]
        public static void ImportTMP()
        {
            string pkg = "Library/PackageCache/com.unity.ugui@bb329a87fcdc/Package Resources/TMP Essential Resources.unitypackage";
            if (File.Exists(pkg))
            {
                AssetDatabase.ImportPackage(pkg, false);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
            Debug.Log("TMP_ESSENTIALS_IMPORTED");
        }

        [MenuItem("Driving School/Build UI Prefabs")]
        public static void BuildAll()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Directory.CreateDirectory("Assets/DrivingSchool/Prefabs/UI");
            BuildFonts();
            
            BuildHUD();
            BuildMainMenu();
            BuildLessonCatalog();
            BuildConditionsSetup();
            BuildG27Calibration();
            BuildPauseMenu();
            BuildTheoryExam();
            BuildSettings();
            BuildMenuScene();
            
            Debug.Log("UI_PREFABS_BUILT");
        }

        private static void SavePrefab(GameObject go, string name)
        {
            PrefabUtility.SaveAsPrefabAsset(go, $"Assets/DrivingSchool/Prefabs/UI/{name}.prefab");
            Object.DestroyImmediate(go);
        }

        private static GameObject CreateCanvas(string name)
        {
            var go = new GameObject(name);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            go.AddComponent<UIScaleFollower>();   // gameplay.uiScale (T43)
            return go;
        }

        private static RectTransform CreateFill(string name, Transform parent, float left = 0, float right = 0, float top = 0, float bottom = 0)
        {
            var go = new GameObject(name);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 1);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        private static RectTransform CreateFixed(string name, Transform parent, Vector2 anchor, Vector2 size, Vector2 position, Vector2? pivot = null)
        {
            var go = new GameObject(name);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot ?? anchor;
            rt.sizeDelta = size;
            rt.anchoredPosition = position;
            return rt;
        }

        private static Image AddImage(GameObject go, Color color)
        {
            var img = go.AddComponent<Image>();
            img.color = color;
            return img;
        }

        private static TMP_FontAsset s_DefaultFont;
        private static TMP_FontAsset GetFont()
        {
            if (s_DefaultFont != null) return s_DefaultFont;
            s_DefaultFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(UIFontPath); // кириллица; LiberationSans — только запасной вариант
            if (s_DefaultFont == null) s_DefaultFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            if (s_DefaultFont == null)
            {
                var guids = AssetDatabase.FindAssets("t:TMP_FontAsset");
                if (guids.Length > 0)
                    s_DefaultFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }
            return s_DefaultFont;
        }

        private static TextMeshProUGUI AddText(GameObject go, string text, int fontSize, Color color, TextAlignmentOptions align = TextAlignmentOptions.Center, FontStyles style = FontStyles.Normal)
        {
            var tmp = go.AddComponent<TextMeshProUGUI>();
            var font = GetFont();
            if (font != null) tmp.font = font;
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = align;
            tmp.fontStyle = style;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            return tmp;
        }

        // ==========================================
        // 1. HUD
        // ==========================================
        // BuildHUD — в UIBuilder.Hud.cs (T47).

        // ==========================================
        // 2. MAIN MENU
        // ==========================================
        private static void BuildMainMenu()
        {
            var theme = BuildThemes();
            var canvas = CreateCanvas("MainMenu");
            var menu = canvas.AddComponent<MainMenuController>();
            menu.defaultTheme = theme;

            // Фон — не сплошная заливка, а затемнение слева (T46): справа видна живая 3D-сцена меню.
            var bg = CreateFill("Background", canvas.transform);
            var scrim = AddThemedImage(bg.gameObject, ThemeRole.BgDark, theme);
            scrim.sprite = BuildMenuScrim();
            scrim.raycastTarget = false;

            var title = CreateFixed("Title", bg, new Vector2(0, 1), new Vector2(1000, 90), new Vector2(96, -110), new Vector2(0, 1));
            AddThemedText(title.gameObject, "DrivingSchoolSim", 72, ThemeRole.Text, theme, TextAlignmentOptions.Left, FontWeight.Bold);
            var subtitle = CreateFixed("Subtitle", bg, new Vector2(0, 1), new Vector2(1000, 40), new Vector2(96, -204), new Vector2(0, 1));
            AddThemedText(subtitle.gameObject, "Подготовка к практическому экзамену · категория B", 26, ThemeRole.Muted, theme, TextAlignmentOptions.Left);

            // Пункты сверху вниз; порядок = порядок навигации. Фон справа свободен под 3D-сцену меню.
            var list = CreateFixed("Items", bg, new Vector2(0, 1), new Vector2(560, 5 * 80), new Vector2(96, -290), new Vector2(0, 1));
            string[] labels = { "Задания", "Теория ПДД", "Экзаменационный маршрут", "Настройки", "Выход" };
            var buttons = new Button[labels.Length];
            for (int i = 0; i < labels.Length; i++)
                buttons[i] = CreateMenuItem($"Item{i}_{labels[i]}", list, new Vector2(0, -i * 80), new Vector2(560, 72), labels[i], 30, theme, i == 2 ? "скоро" : null);
            menu.lessonsButton = buttons[0];
            menu.theoryButton = buttons[1];
            menu.examButton = buttons[2];
            menu.settingsButton = buttons[3];
            menu.exitButton = buttons[4];
            buttons[2].interactable = false;

            var hint = CreateFixed("Hint", bg, new Vector2(0, 0), new Vector2(1200, 40), new Vector2(96, 56), new Vector2(0, 0));
            AddThemedText(hint.gameObject, "↑↓ Tab — выбор     Enter — открыть     Esc — выход", 20, ThemeRole.Muted, theme, TextAlignmentOptions.Left);

            // Диалог выхода: Escape на главном экране не закрывает программу без подтверждения (T03 §5).
            var dialog = CreateFill("ExitDialog", canvas.transform);
            var shade = AddImage(dialog.gameObject, new Color(0, 0, 0, 0.72f));
            shade.raycastTarget = true; // клики мимо диалога не доходят до меню
            var panel = CreateFixed("Panel", dialog, new Vector2(0.5f, 0.5f), new Vector2(680, 280), Vector2.zero);
            AddThemedImage(panel.gameObject, ThemeRole.BgPanel, theme);
            var top = CreateFixed("AccentLine", panel, new Vector2(0.5f, 1), new Vector2(680, 6), Vector2.zero, new Vector2(0.5f, 1));
            AddThemedImage(top.gameObject, ThemeRole.Accent, theme);
            var dTitle = CreateFixed("Title", panel, new Vector2(0, 1), new Vector2(620, 60), new Vector2(32, -36), new Vector2(0, 1));
            AddThemedText(dTitle.gameObject, "Выйти из программы?", 40, ThemeRole.Text, theme, TextAlignmentOptions.Left, FontWeight.Bold);
            var dBody = CreateFixed("Body", panel, new Vector2(0, 1), new Vector2(620, 40), new Vector2(32, -100), new Vector2(0, 1));
            AddThemedText(dBody.gameObject, "Вы вернётесь на рабочий стол Windows.", 24, ThemeRole.Text2, theme, TextAlignmentOptions.Left);
            menu.exitCancelButton = CreateMenuItem("Cancel", panel, new Vector2(-252, 32), new Vector2(200, 64), "Отмена", 26, theme, null, new Vector2(1, 0));
            menu.exitConfirmButton = CreateMenuItem("Confirm", panel, new Vector2(-32, 32), new Vector2(200, 64), "Выйти", 26, theme, null, new Vector2(1, 0));
            menu.exitDialog = dialog.gameObject;
            dialog.gameObject.SetActive(false);

            SavePrefab(canvas, "MainMenu");
        }

        private static Button CreateMenuItem(string name, Transform parent, Vector2 position, Vector2 size, string text, int fontSize, UITheme theme, string badge, Vector2? anchor = null)
        {
            var a = anchor ?? new Vector2(0, 1);
            var rt = CreateFixed(name, parent, a, size, position, a);
            var bgImage = AddImage(rt.gameObject, theme.bgCard);
            var button = rt.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None; // вид целиком ведёт MenuItemView по теме
            button.targetGraphic = bgImage;

            var bar = CreateFixed("FocusBar", rt, new Vector2(0, 0.5f), new Vector2(6, size.y), Vector2.zero, new Vector2(0, 0.5f));
            var barImage = AddImage(bar.gameObject, theme.accent);
            barImage.raycastTarget = false;
            barImage.enabled = false;

            var labelRt = CreateFill("Label", rt, 28, badge != null ? 150 : 16, 0, 0);
            var label = AddThemedText(labelRt.gameObject, text, fontSize, ThemeRole.Text, theme, TextAlignmentOptions.Left, FontWeight.Medium, themed: false);
            label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;

            if (badge != null)
            {
                var badgeRt = CreateFixed("Badge", rt, new Vector2(1, 0.5f), new Vector2(130, 40), new Vector2(-20, 0), new Vector2(1, 0.5f));
                var b = AddThemedText(badgeRt.gameObject, badge, 20, ThemeRole.Muted, theme, TextAlignmentOptions.Right);
                b.raycastTarget = false;
            }

            var view = rt.gameObject.AddComponent<MenuItemView>();
            view.background = bgImage;
            view.focusBar = barImage;
            view.label = label;
            view.fallbackTheme = theme;
            return button;
        }

        private static Image AddThemedImage(GameObject go, ThemeRole role, UITheme theme)
        {
            var img = AddImage(go, theme.Get(role));
            go.AddComponent<ThemedGraphic>().role = role;
            return img;
        }

        private static TextMeshProUGUI AddThemedText(GameObject go, string text, int fontSize, ThemeRole role, UITheme theme,
            TextAlignmentOptions align = TextAlignmentOptions.Left, FontWeight weight = FontWeight.Regular, bool themed = true)
        {
            var tmp = AddText(go, text, fontSize, theme.Get(role), align);
            tmp.fontWeight = weight;
            if (themed) go.AddComponent<ThemedGraphic>().role = role;
            return tmp;
        }

        // ==========================================
        // Шрифты и темы (docs/ui-settings.md §6, §8)
        // ==========================================
        private const string FontRoot = "Assets/DrivingSchool/Art/Fonts";
        private const string ThemeRoot = "Assets/DrivingSchool/Data/UI/Themes";
        public const string UIFontPath = FontRoot + "/GolosText/GolosText-Regular SDF.asset";

        /// <summary>ASCII, кириллица 0x0400–0x045F, типографика и стрелки. ◀ ▶ ✓ в шрифтах нет — такие значки рисуются спрайтами.
        /// Исключены Ѐ Ѝ ѐ ѝ (U+0400, U+040D, U+0450, U+045D): в Golos Text их нет, в русском они не используются.</summary>
        public static string UICharset()
        {
            var sb = new System.Text.StringBuilder();
            for (int c = 0x20; c < 0x7F; c++) sb.Append((char)c);
            for (int c = 0x400; c < 0x460; c++)
                if (c != 0x400 && c != 0x40D && c != 0x450 && c != 0x45D) sb.Append((char)c);
            sb.Append("\u00A0«»—–…°×№·₽↑↓←→");
            return sb.ToString();
        }

        [MenuItem("Driving School/Build UI Fonts")]
        public static void BuildFonts()
        {
            var regular = BuildFontAsset("GolosText", "GolosText-Regular");
            var medium = BuildFontAsset("GolosText", "GolosText-Medium");
            var bold = BuildFontAsset("GolosText", "GolosText-Bold");
            var monoMedium = BuildFontAsset("RobotoMono", "RobotoMono-Medium");
            var monoBold = BuildFontAsset("RobotoMono", "RobotoMono-Bold");
            // fontWeight = Medium/Bold у текста берёт настоящие начертания, а не синтетическое утолщение.
            if (regular != null)
            {
                if (medium != null) regular.fontWeightTable[5].regularTypeface = medium;
                if (bold != null) regular.fontWeightTable[7].regularTypeface = bold;
                EditorUtility.SetDirty(regular);
            }
            if (monoMedium != null && monoBold != null) { monoMedium.fontWeightTable[7].regularTypeface = monoBold; EditorUtility.SetDirty(monoMedium); }
            AssetDatabase.SaveAssets();
            s_DefaultFont = null;
            Debug.Log("UI_FONTS_BUILT");
        }

        private static TMP_FontAsset BuildFontAsset(string family, string file)
        {
            string ttf = $"{FontRoot}/{family}/{file}.ttf", path = $"{FontRoot}/{family}/{file} SDF.asset";
            var font = AssetDatabase.LoadAssetAtPath<Font>(ttf);
            if (font == null) { Debug.LogError($"UI_FONT_MISSING {ttf}"); return null; }

            var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (fa == null)
            {
                fa = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
                fa.name = file + " SDF";
                AssetDatabase.CreateAsset(fa, path);
                fa.atlasTextures[0].name = file + " Atlas";
                AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
                fa.material.name = file + " Material";
                AssetDatabase.AddObjectToAsset(fa.material, fa);
            }
            else
            {
                // Пересборка без смены GUID: ссылки из префабов сохраняются.
                fa.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                fa.ClearFontAssetData();
            }
            if (!fa.TryAddCharacters(UICharset(), out string missing))
                Debug.LogError($"UI_FONT_GLYPHS_MISSING {file}: {missing}");
            // Статический атлас: набор символов фиксирован и проверяется тестом, в сборку TTF не нужен.
            fa.atlasPopulationMode = AtlasPopulationMode.Static;
            EditorUtility.SetDirty(fa);
            return fa;
        }

        // Порядок: bgDark, bgPanel, bgCard, bgRowHover, accent, accentSoft, onAccent, text, text2, muted, line, red, green, info.
        private static readonly (string id, string name, string[] c)[] Themes =
        {
            ("Asphalt", "Асфальт", new[] { "#161718", "#1E2021", "#26292B", "#2D3033", "#FFD84D", "#FFD84D1A", "#1A1A1A", "#F4F4F2", "#B3B5B6", "#6E7173", "#FFFFFF14", "#E5484D", "#3DBE6B", "#4DA3FF" }),
            ("Graphite", "Графит", new[] { "#0F1216", "#161A20", "#1C2129", "#222833", "#4DA3FF", "#4DA3FF1F", "#08121F", "#F2F5F8", "#A9B3C1", "#626C7A", "#FFFFFF14", "#E5484D", "#3DBE6B", "#7CC4FF" }),
            ("Sign", "Знак", new[] { "#0E1320", "#141B2B", "#1A2335", "#202B41", "#FFFFFF", "#FFFFFF14", "#1348A8", "#FFFFFF", "#B4C0D6", "#66738C", "#FFFFFF14", "#E5484D", "#3DBE6B", "#8FB8FF" }),
            ("Teal", "Бирюза", new[] { "#0D1414", "#131C1D", "#192425", "#1F2D2E", "#2EC4A6", "#2EC4A61F", "#04201A", "#EEF6F4", "#A3B8B4", "#5E7470", "#FFFFFF14", "#E5484D", "#3DBE6B", "#8EE3D0" }),
        };

        [MenuItem("Driving School/Build UI Themes")]
        public static void BuildThemesMenu() { BuildThemes(); Debug.Log("UI_THEMES_BUILT"); }

        /// <summary>Создаёт/обновляет ассеты тем. Возвращает тему по умолчанию («Асфальт»).</summary>
        public static UITheme BuildThemes()
        {
            Directory.CreateDirectory(ThemeRoot);
            UITheme first = null;
            foreach (var (id, name, c) in Themes)
            {
                string path = $"{ThemeRoot}/UITheme_{id}.asset";
                var t = AssetDatabase.LoadAssetAtPath<UITheme>(path);
                if (t == null) { t = ScriptableObject.CreateInstance<UITheme>(); AssetDatabase.CreateAsset(t, path); }
                t.displayName = name;
                t.bgDark = Hex(c[0]); t.bgPanel = Hex(c[1]); t.bgCard = Hex(c[2]); t.bgRowHover = Hex(c[3]);
                t.accent = Hex(c[4]); t.accentSoft = Hex(c[5]); t.onAccent = Hex(c[6]);
                t.text = Hex(c[7]); t.text2 = Hex(c[8]); t.muted = Hex(c[9]); t.line = Hex(c[10]);
                t.red = Hex(c[11]); t.green = Hex(c[12]); t.info = Hex(c[13]);
                EditorUtility.SetDirty(t);
                if (first == null) first = t;
            }
            AssetDatabase.SaveAssets();
            return first;
        }

        private static Color Hex(string html) { return ColorUtility.TryParseHtmlString(html, out var c) ? c : Color.magenta; }

        // ==========================================
        // 3. LESSON CATALOG
        // ==========================================
        // BuildLessonCatalog — UIBuilder.Lessons.cs (T53).

        // ==========================================
        // 4. CONDITIONS SETUP
        // ==========================================
        private static void BuildConditionsSetup()
        {
            var canvas = CreateCanvas("ConditionsSetup");
            var bg = CreateFill("Background", canvas.transform);
            AddImage(bg.gameObject, BgColor);

            var title = CreateFixed("Title", bg, new Vector2(0, 1), new Vector2(1000, 60), new Vector2(80, -60), new Vector2(0, 1));
            AddText(title.gameObject, "SIMULATION & SESSION SETUP", 48, TextWhite, TextAlignmentOptions.Left, FontStyles.Bold);

            // Left Panel: Configuration Rows (width 1050)
            var panel = CreateFixed("SettingsPanel", bg, new Vector2(0, 1), new Vector2(1050, 720), new Vector2(80, -160), new Vector2(0, 1));
            AddImage(panel.gameObject, PanelColor);

            var vl = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.padding = new RectOffset(35, 35, 35, 35);
            vl.spacing = 25;
            vl.childControlWidth = true;
            vl.childControlHeight = false;
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;

            string[,] options = {
                { "TRANSMISSION TYPE", "<  MANUAL 5-SPEED + CLUTCH (G27 H-SHIFTER)  >" },
                { "WEATHER CONDITIONS", "<  CLEAR SKY (DRY ASPHALT)  >" },
                { "TIME OF DAY", "<  DAY (14:00)  >" },
                { "AI TRAFFIC DENSITY", "<  MODERATE (30%)  >" },
                { "PEDESTRIAN BEHAVIOR", "<  ACTIVE (CROSSINGS & JAYWALKING)  >" },
                { "INSTRUCTOR AUDITING", "<  STRICT EXAM RULES (5 PENALTY POINTS LIMIT)  >" }
            };

            for (int i = 0; i < options.GetLength(0); i++)
            {
                var row = new GameObject($"Row_{i}").AddComponent<RectTransform>();
                row.SetParent(panel, false);
                row.sizeDelta = new Vector2(980, 80);
                AddImage(row.gameObject, CardBgColor);
                
                var label = CreateFixed("Label", row, new Vector2(0, 0.5f), new Vector2(380, 60), new Vector2(25, 0), new Vector2(0, 0.5f));
                AddText(label.gameObject, options[i, 0], 22, TextDim, TextAlignmentOptions.Left, FontStyles.Bold);

                var value = CreateFixed("Value", row, new Vector2(1, 0.5f), new Vector2(560, 60), new Vector2(-25, 0), new Vector2(1, 0.5f));
                AddText(value.gameObject, options[i, 1], 22, AccentGold, TextAlignmentOptions.Right, FontStyles.Bold);
            }

            // Right Panel: Summary & Launch (width 650)
            var rightPanel = CreateFixed("RightPanel", bg, new Vector2(1, 1), new Vector2(650, 720), new Vector2(-80, -160), new Vector2(1, 1));
            AddImage(rightPanel.gameObject, PanelColor);

            var rTitle = CreateFixed("Title", rightPanel, new Vector2(0.5f, 1), new Vector2(570, 50), new Vector2(0, -30), new Vector2(0.5f, 1));
            AddText(rTitle.gameObject, "SESSION SUMMARY", 28, AccentCyan, TextAlignmentOptions.Left, FontStyles.Bold);

            var rSummary = CreateFixed("Summary", rightPanel, new Vector2(0.5f, 1), new Vector2(570, 420), new Vector2(0, -90), new Vector2(0.5f, 1));
            AddImage(rSummary.gameObject, CardBgColor);
            var rTxt = CreateFill("Txt", rSummary, 25, 25, 25, 25);
            AddText(rTxt.gameObject, 
                "<b>Location:</b> Official Autodrome Ground\n" +
                "<b>Dimensions:</b> 120 x 120 meters\n" +
                "<b>Vehicle:</b> Training Sedan Category B\n" +
                "<b>Controls:</b> Logitech G27 (900° Steering)\n" +
                "<b>Clutch Stall:</b> Enabled (RPM < 600)\n" +
                "<b>Handbrake Hill Start:</b> Required on 10% ramp\n\n" +
                "<color=#80A0C0>Make sure pedals and steering are calibrated before launching the simulation.</color>", 
                22, TextWhite, TextAlignmentOptions.Left);

            var startBtn = CreateFixed("StartBtn", rightPanel, new Vector2(0.5f, 0), new Vector2(570, 70), new Vector2(0, 40), new Vector2(0.5f, 0));
            AddImage(startBtn.gameObject, AccentGold);
            var startTxt = CreateFill("Txt", startBtn);
            AddText(startTxt.gameObject, "START SIMULATION", 28, Color.black, TextAlignmentOptions.Center, FontStyles.Bold);

            SavePrefab(canvas, "ConditionsSetup");
        }

        // ==========================================
        // 5. G27 CALIBRATION
        // ==========================================
        private static void BuildG27Calibration()
        {
            var canvas = CreateCanvas("G27Calibration");
            var bg = CreateFill("Background", canvas.transform);
            AddImage(bg.gameObject, BgColor);

            var title = CreateFixed("Title", bg, new Vector2(0, 1), new Vector2(1200, 60), new Vector2(80, -60), new Vector2(0, 1));
            AddText(title.gameObject, "LOGITECH G27 HARDWARE CALIBRATION", 48, TextWhite, TextAlignmentOptions.Left, FontStyles.Bold);

            // Left Side: Wheel Calibration (850 x 700)
            var wheel = CreateFixed("WheelPanel", bg, new Vector2(0, 1), new Vector2(850, 700), new Vector2(80, -160), new Vector2(0, 1));
            AddImage(wheel.gameObject, PanelColor);
            
            var wTitle = CreateFixed("Title", wheel, new Vector2(0.5f, 1), new Vector2(750, 50), new Vector2(0, -30), new Vector2(0.5f, 1));
            AddText(wTitle.gameObject, "STEERING WHEEL (900° LOCK-TO-LOCK)", 28, AccentGold, TextAlignmentOptions.Center, FontStyles.Bold);

            var wGaugeBg = CreateFixed("GaugeBg", wheel, new Vector2(0.5f, 1), new Vector2(750, 80), new Vector2(0, -130), new Vector2(0.5f, 1));
            AddImage(wGaugeBg.gameObject, new Color(0.08f, 0.10f, 0.13f));
            
            // Center mark line
            var wCenter = CreateFixed("CenterLine", wGaugeBg, new Vector2(0.5f, 0.5f), new Vector2(4, 80), new Vector2(0, 0), new Vector2(0.5f, 0.5f));
            AddImage(wCenter.gameObject, Color.white);

            // Wheel indicator bar
            var wIndicator = CreateFixed("Indicator", wGaugeBg, new Vector2(0.5f, 0.5f), new Vector2(180, 60), new Vector2(60, 0), new Vector2(0.5f, 0.5f));
            AddImage(wIndicator.gameObject, AccentCyan);

            var wAngle = CreateFixed("Angle", wheel, new Vector2(0.5f, 1), new Vector2(750, 40), new Vector2(0, -230), new Vector2(0.5f, 1));
            AddText(wAngle.gameObject, "Current Angle: <color=#5999D3>+14.5°</color> (Centered: OK)", 24, TextWhite, TextAlignmentOptions.Center);

            // FFB Settings Box
            var ffbBox = CreateFixed("FFBBox", wheel, new Vector2(0.5f, 1), new Vector2(750, 240), new Vector2(0, -300), new Vector2(0.5f, 1));
            AddImage(ffbBox.gameObject, CardBgColor);
            var ffbTxt = CreateFill("Txt", ffbBox, 30, 30, 25, 25);
            AddText(ffbTxt.gameObject, 
                "<b>Force Feedback Profile:</b> Road Surface + Curb Rumble\n" +
                "<b>Overall FFB Strength:</b> 100%\n" +
                "<b>Damper / Friction:</b> 20% (Realistic hydraulic steering feel)\n" +
                "<b>Centering Spring:</b> Off (Hardware controlled by simulator physics)", 
                22, TextWhite, TextAlignmentOptions.Left);

            var ffbBtn = CreateFixed("TestFFBBtn", wheel, new Vector2(0.5f, 0), new Vector2(320, 55), new Vector2(0, 35), new Vector2(0.5f, 0));
            AddImage(ffbBtn.gameObject, AccentCyan);
            var ffbBtnTxt = CreateFill("Txt", ffbBtn);
            AddText(ffbBtnTxt.gameObject, "TEST FORCE FEEDBACK", 22, BgColor, TextAlignmentOptions.Center, FontStyles.Bold);

            // Right Side: 3 Pedals (850 x 700)
            var pedals = CreateFixed("PedalsPanel", bg, new Vector2(1, 1), new Vector2(850, 700), new Vector2(-80, -160), new Vector2(1, 1));
            AddImage(pedals.gameObject, PanelColor);

            var pTitle = CreateFixed("Title", pedals, new Vector2(0.5f, 1), new Vector2(750, 50), new Vector2(0, -30), new Vector2(0.5f, 1));
            AddText(pTitle.gameObject, "PEDALS (CLUTCH, BRAKE, THROTTLE)", 28, AccentGold, TextAlignmentOptions.Center, FontStyles.Bold);

            string[] pNames = { "CLUTCH", "BRAKE", "THROTTLE" };
            Color[] pColors = { AccentCyan, RedColor, AccentGold };
            float[] pFills = { 0.45f, 0.15f, 0.0f };
            string[] pVals = { "45% (Bite)", "15%", "0%" };

            for (int i = 0; i < 3; i++)
            {
                var pedalCol = CreateFixed($"Pedal_{i}", pedals, new Vector2(0, 1), new Vector2(210, 480), new Vector2(50 + i * 260, -110), new Vector2(0, 1));
                AddImage(pedalCol.gameObject, CardBgColor);

                // Vertical gauge track (140 x 320)
                var track = CreateFixed("Track", pedalCol, new Vector2(0.5f, 1), new Vector2(130, 320), new Vector2(0, -25), new Vector2(0.5f, 1));
                AddImage(track.gameObject, new Color(0.08f, 0.10f, 0.13f));

                // Vertical fill from bottom
                float fillHeight = 320f * pFills[i];
                if (fillHeight > 5f)
                {
                    var fill = CreateFixed("Fill", track, new Vector2(0.5f, 0), new Vector2(130, fillHeight), new Vector2(0, 0), new Vector2(0.5f, 0));
                    AddImage(fill.gameObject, pColors[i]);
                }

                // Clutch Bite Point Marker
                if (i == 0)
                {
                    var biteLine = CreateFixed("BiteLine", track, new Vector2(0.5f, 0.45f), new Vector2(130, 4), new Vector2(0, 0), new Vector2(0.5f, 0.5f));
                    AddImage(biteLine.gameObject, Color.white);
                }

                var pLabel = CreateFixed("Label", pedalCol, new Vector2(0.5f, 0), new Vector2(190, 35), new Vector2(0, 70), new Vector2(0.5f, 0));
                AddText(pLabel.gameObject, pNames[i], 24, TextWhite, TextAlignmentOptions.Center, FontStyles.Bold);

                var pVal = CreateFixed("Val", pedalCol, new Vector2(0.5f, 0), new Vector2(190, 30), new Vector2(0, 30), new Vector2(0.5f, 0));
                AddText(pVal.gameObject, pVals[i], 20, pColors[i], TextAlignmentOptions.Center, FontStyles.Bold);
            }

            var calBtn = CreateFixed("CalibrateBtn", pedals, new Vector2(0.5f, 0), new Vector2(400, 55), new Vector2(0, 35), new Vector2(0.5f, 0));
            AddImage(calBtn.gameObject, AccentGold);
            var calBtnTxt = CreateFill("Txt", calBtn);
            AddText(calBtnTxt.gameObject, "CALIBRATE DEADZONES", 22, Color.black, TextAlignmentOptions.Center, FontStyles.Bold);

            SavePrefab(canvas, "G27Calibration");
        }

        // ==========================================
        // 6. PAUSE MENU
        // ==========================================
        private static void BuildPauseMenu()
        {
            // T46: рабочая пауза в теме меню — настоящие Button, навигация с клавиатуры, намерения для AppNavigator.
            var theme = BuildThemes();
            var canvas = CreateCanvas("PauseMenu");
            canvas.GetComponent<Canvas>().sortingOrder = 100; // поверх HUD и OnGUI-подсказок сцены
            var pause = canvas.AddComponent<PauseMenuController>();
            pause.defaultTheme = theme;

            var overlay = CreateFill("Overlay", canvas.transform);
            var shade = AddImage(overlay.gameObject, new Color(0, 0, 0, 0.72f));
            shade.raycastTarget = true;
            var panel = CreateFixed("Panel", overlay, new Vector2(0.5f, 0.5f), new Vector2(640, 580), Vector2.zero);
            AddThemedImage(panel.gameObject, ThemeRole.BgPanel, theme);
            var top = CreateFixed("AccentLine", panel, new Vector2(0.5f, 1), new Vector2(640, 6), Vector2.zero, new Vector2(0.5f, 1));
            AddThemedImage(top.gameObject, ThemeRole.Accent, theme);
            var title = CreateFixed("Title", panel, new Vector2(0, 1), new Vector2(580, 60), new Vector2(40, -36), new Vector2(0, 1));
            AddThemedText(title.gameObject, "Пауза", 44, ThemeRole.Text, theme, TextAlignmentOptions.Left, FontWeight.Bold);

            var items = CreateFixed("Items", panel, new Vector2(0, 1), new Vector2(560, 5 * 80), new Vector2(40, -120), new Vector2(0, 1));
            string[] labels = { "Продолжить", "Завершить поездку", "Начать заново", "Настройки", "Выйти в главное меню" };
            var buttons = new Button[labels.Length];
            for (int i = 0; i < labels.Length; i++)
                buttons[i] = CreateMenuItem($"Item{i}_{labels[i]}", items, new Vector2(0, -i * 80), new Vector2(560, 72), labels[i], 30, theme, null);
            pause.resumeButton = buttons[0];
            pause.finishButton = buttons[1];
            pause.restartButton = buttons[2];
            pause.settingsButton = buttons[3];
            pause.exitToMenuButton = buttons[4];

            var hint = CreateFixed("Hint", panel, new Vector2(0, 0), new Vector2(580, 32), new Vector2(40, 20), new Vector2(0, 0));
            AddThemedText(hint.gameObject, "↑↓ выбор   Enter выбрать   Esc продолжить   F4 клавиши", 20, ThemeRole.Muted, theme, TextAlignmentOptions.Left);

            pause.panel = overlay.gameObject;
            overlay.gameObject.SetActive(false);
            SavePrefab(canvas, "PauseMenu");
        }

        public const string MenuScrimPath = "Assets/DrivingSchool/Art/UI/MenuScrim.png";

        /// <summary>Горизонтальный градиент затемнения под пунктами меню: плотный слева, прозрачный к середине экрана.</summary>
        private static Sprite BuildMenuScrim()
        {
            const int w = 512, h = 4;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (int x = 0; x < w; x++)
            {
                float u = x / (w - 1f);
                float a = 0.94f * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.30f, 0.66f, u)));
                for (int y = 0; y < h; y++) tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            Directory.CreateDirectory(Path.GetDirectoryName(MenuScrimPath));
            File.WriteAllBytes(MenuScrimPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(MenuScrimPath, ImportAssetOptions.ForceSynchronousImport);
            var imp = (TextureImporter)AssetImporter.GetAtPath(MenuScrimPath);
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.mipmapEnabled = false;
            imp.alphaIsTransparency = true;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(MenuScrimPath);
        }

        /// <summary>Фоны меню (T46): сцены-источники и облёт камеры. Координаты — из генераторов этих сцен.</summary>
        public static DrivingSchool.Presentation.MenuBackdropDirector.Backdrop[] MenuBackdrops()
        {
            float rx = VehicleTestRangeBuilder.RoadCX, rz = VehicleTestRangeBuilder.RailZ;
            return new[]
            {
                new DrivingSchool.Presentation.MenuBackdropDirector.Backdrop
                {
                    title = "Шоурум: седан", sceneName = "Showroom", pivot = new Vector3(0f, 0.65f, 0f),
                    radius = 6.5f, height = 1.0f, fieldOfView = 40f, startYaw = 140f, orbitDegPerSec = 5f, subjectRightDeg = 12f,
                },
                new DrivingSchool.Presentation.MenuBackdropDirector.Backdrop
                {
                    title = "Ж/д переезд", sceneName = Path.GetFileNameWithoutExtension(DriveScenePath), pivot = new Vector3(rx, 2f, rz),
                    radius = 24f, height = 3f, fieldOfView = 50f, startYaw = -40f, swayDeg = 10f, swayPeriod = 60f, subjectRightDeg = 16f,
                    trainIntervalSeconds = 40f,
                },
                new DrivingSchool.Presentation.MenuBackdropDirector.Backdrop
                {
                    title = "Автодром", sceneName = "Autodrome_Training", pivot = new Vector3(-10f, 0f, -15f),
                    radius = 70f, height = 55f, fieldOfView = 50f, startYaw = 30f, orbitDegPerSec = 1.5f, subjectRightDeg = 8f,
                },
            };
        }

        // ==========================================
        // 6b. MAIN MENU SCENE (T46): стартовая сцена сборки и Play в редакторе
        // ==========================================
        public const string MenuScenePath = "Assets/DrivingSchool/Scenes/MainMenu.unity";
        public const string DriveScenePath = "Assets/DrivingSchool/Scenes/VehicleTestRange.unity";

        [MenuItem("Driving School/Build Main Menu scene")]
        public static void BuildMenuSceneMenu() { BuildMenuScene(); }

        /// <summary>Перезаписывает Scenes/MainMenu.unity и ставит её первой в Build Settings. Префабы MainMenu/PauseMenu должны быть собраны.</summary>
        public static void BuildMenuScene()
        {
            var menuPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/DrivingSchool/Prefabs/UI/MainMenu.prefab");
            var pausePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/DrivingSchool/Prefabs/UI/PauseMenu.prefab");
            if (menuPrefab == null || pausePrefab == null) { Debug.LogError("MAIN_MENU_SCENE_FAIL: нет префабов MainMenu/PauseMenu — Driving School/Build UI Prefabs"); return; }
            if (!Application.isBatchMode && !UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var theme = BuildThemes();

            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = theme.bgDark;
            camGo.AddComponent<AudioListener>();

            var es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>().AssignDefaultActions();

            var menuGo = (GameObject)PrefabUtility.InstantiatePrefab(menuPrefab, scene);

            // Живой фон: занавес под меню (гаснет, когда 3D-сцена загрузилась) и директор фона.
            var curtainCanvas = CreateCanvas("BackdropCurtain");
            curtainCanvas.GetComponent<Canvas>().sortingOrder = -10;
            Object.DestroyImmediate(curtainCanvas.GetComponent<GraphicRaycaster>());
            var curtainFill = CreateFill("Fill", curtainCanvas.transform);
            AddThemedImage(curtainFill.gameObject, ThemeRole.BgDark, theme).raycastTarget = false;
            var curtain = curtainCanvas.AddComponent<CanvasGroup>();
            curtain.interactable = false; curtain.blocksRaycasts = false;
            var backdrop = new GameObject("MenuBackdrop").AddComponent<DrivingSchool.Presentation.MenuBackdropDirector>();
            backdrop.view = cam;
            backdrop.curtain = curtain;
            backdrop.backdrops = MenuBackdrops();

            // Поверх меню: уведомление «в разработке» и затемнение «Загрузка…».
            var overlayCanvas = CreateCanvas("MenuOverlay");
            overlayCanvas.GetComponent<Canvas>().sortingOrder = 50;
            var notice = CreateFixed("Notice", overlayCanvas.transform, new Vector2(0, 0), new Vector2(1100, 64), new Vector2(96, 120), new Vector2(0, 0));
            AddThemedImage(notice.gameObject, ThemeRole.BgCard, theme);
            var noticeText = CreateFill("Text", notice, 24, 24, 0, 0);
            var noticeTmp = AddThemedText(noticeText.gameObject, "Раздел в разработке", 24, ThemeRole.Text, theme, TextAlignmentOptions.Left);
            noticeTmp.raycastTarget = false;
            notice.GetComponent<Image>().raycastTarget = false;

            var loading = CreateFill("Loading", overlayCanvas.transform);
            AddThemedImage(loading.gameObject, ThemeRole.BgDark, theme);
            var loadingText = CreateFixed("Text", loading, new Vector2(0.5f, 0.5f), new Vector2(900, 80), Vector2.zero);
            AddThemedText(loadingText.gameObject, "Загрузка задания…", 40, ThemeRole.Text, theme, TextAlignmentOptions.Center, FontWeight.Medium);

            var settingsPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SettingsPrefabPath);
            var settingsGo = settingsPrefab != null ? (GameObject)PrefabUtility.InstantiatePrefab(settingsPrefab, scene) : null;

            var flow = new GameObject("AppFlow").AddComponent<MainMenuFlow>();
            flow.settingsPrefab = settingsPrefab;
            flow.hudPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/DrivingSchool/Prefabs/UI/HUD.prefab");
            flow.settings = settingsGo != null ? settingsGo.GetComponent<SettingsScreenController>() : null;
            flow.menu = menuGo.GetComponent<MainMenuController>();
            var catalogPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/DrivingSchool/Prefabs/UI/LessonCatalog.prefab");
            var catalogGo = (GameObject)PrefabUtility.InstantiatePrefab(catalogPrefab, scene);
            flow.catalog = catalogGo.GetComponent<LessonCatalogController>();
            catalogGo.SetActive(false);
            flow.pauseMenuPrefab = pausePrefab;
            flow.driveScene = Path.GetFileNameWithoutExtension(DriveScenePath);
            flow.loadingOverlay = loading.gameObject;
            flow.notice = noticeTmp;
            flow.noticePanel = notice.gameObject;
            loading.gameObject.SetActive(false);
            notice.gameObject.SetActive(false);

            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, MenuScenePath);

            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>();
            list.Add(new EditorBuildSettingsScene(MenuScenePath, true));
            foreach (var s in EditorBuildSettings.scenes) if (s.path != MenuScenePath) list.Add(s);
            if (!list.Exists(s => s.path == DriveScenePath)) list.Add(new EditorBuildSettingsScene(DriveScenePath, true));
            foreach (var b in backdrop.backdrops)
            {
                string path = $"Assets/DrivingSchool/Scenes/{b.sceneName}.unity";
                int i = list.FindIndex(s => s.path == path);
                if (i < 0) list.Add(new EditorBuildSettingsScene(path, true));
                else if (!list[i].enabled) list[i] = new EditorBuildSettingsScene(path, true);
            }
            EditorBuildSettings.scenes = list.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("MAIN_MENU_SCENE_BUILT");
        }

        // ==========================================
        // 7. THEORY EXAM
        // ==========================================
        private static void BuildTheoryExam()
        {
            var canvas = CreateCanvas("TheoryExam");
            var bg = CreateFill("Background", canvas.transform);
            AddImage(bg.gameObject, BgColor);

            // Header
            var header = CreateFixed("Header", bg, new Vector2(0.5f, 1), new Vector2(1920, 90), new Vector2(0, 0), new Vector2(0.5f, 1));
            AddImage(header.gameObject, PanelColor);
            
            var hTxt = CreateFixed("Txt", header, new Vector2(0, 0.5f), new Vector2(700, 60), new Vector2(80, 0), new Vector2(0, 0.5f));
            AddText(hTxt.gameObject, "TICKET 12 - QUESTION 14 OF 20", 32, AccentCyan, TextAlignmentOptions.Left, FontStyles.Bold);
            
            // Question indicator strip (20 boxes of fixed size 24x24)
            var strip = CreateFixed("Strip", header, new Vector2(1, 0.5f), new Vector2(650, 30), new Vector2(-80, 0), new Vector2(1, 0.5f));
            for(int i = 0; i < 20; i++) 
            {
                var qb = CreateFixed($"QBox_{i}", strip, new Vector2(0, 0.5f), new Vector2(24, 24), new Vector2(i * 32, 0), new Vector2(0, 0.5f));
                Color boxColor = (i < 13) ? GreenColor : (i == 13 ? AccentGold : new Color(0.25f, 0.30f, 0.35f));
                AddImage(qb.gameObject, boxColor);
                var qTxt = CreateFill("Txt", qb);
                AddText(qTxt.gameObject, (i+1).ToString(), 12, Color.black, TextAlignmentOptions.Center, FontStyles.Bold);
            }

            // 3D Illustration Viewport
            var ill = CreateFixed("Illustration", bg, new Vector2(0.5f, 1), new Vector2(1400, 420), new Vector2(0, -120), new Vector2(0.5f, 1));
            AddImage(ill.gameObject, CardBgColor);
            var illTxt = CreateFill("Txt", ill);
            AddText(illTxt.gameObject, "[ 3D TRAFFIC SITUATION VIEWPORT ]\n<size=70%>Unregulated 4-way intersection. Blue sedan (Your vehicle) going straight.\nWhite delivery van approaching from your right turning left.</size>", 28, TextDim, TextAlignmentOptions.Center);

            // Question Box
            var qBox = CreateFixed("Question", bg, new Vector2(0.5f, 1), new Vector2(1400, 70), new Vector2(0, -560), new Vector2(0.5f, 1));
            AddImage(qBox.gameObject, PanelColor);
            var qTxt2 = CreateFill("Txt", qBox, 20, 20, 0, 0);
            AddText(qTxt2.gameObject, "WHO HAS THE RIGHT OF WAY AT THIS EQUAL UNREGULATED INTERSECTION?", 28, TextWhite, TextAlignmentOptions.Center, FontStyles.Bold);

            // 4 Answer Options
            string[] answers = {
                "1. You have right of way because you are moving straight without turning.",
                "2. The white delivery van has right of way because it approaches from your right (Rule 13.11).",
                "3. The heavier vehicle with greater tonnage passes first.",
                "4. Right of way is determined by mutual gesture agreement between drivers."
            };

            for (int i = 0; i < answers.Length; i++)
            {
                var aBox = CreateFixed($"Answer_{i}", bg, new Vector2(0.5f, 1), new Vector2(1400, 65), new Vector2(0, -650 - i * 78), new Vector2(0.5f, 1));
                AddImage(aBox.gameObject, i == 1 ? new Color(0.18f, 0.28f, 0.38f) : PanelColor);
                
                var border = CreateFill("Border", aBox, 2, 2, 2, 2);
                AddImage(border.gameObject, i == 1 ? AccentCyan : CardBgColor);
                
                var aTxt = CreateFill("Txt", border, 25, 25, 0, 0);
                AddText(aTxt.gameObject, answers[i], 22, i == 1 ? AccentGold : TextWhite, TextAlignmentOptions.Left);
            }

            // Footer
            var footer = CreateFixed("Footer", bg, new Vector2(0.5f, 0), new Vector2(1400, 50), new Vector2(0, 30), new Vector2(0.5f, 0));
            AddImage(footer.gameObject, PanelColor);
            var fTxt = CreateFill("Txt", footer, 20, 20, 0, 0);
            AddText(fTxt.gameObject, "Exam Timer: <color=#5999D3>16:42 remaining</color>   |   Errors Allowed: 2   |   Current Mistakes: <color=#4CAF50>0</color>", 20, TextDim, TextAlignmentOptions.Center);

            SavePrefab(canvas, "TheoryExam");
        }
    }
}
