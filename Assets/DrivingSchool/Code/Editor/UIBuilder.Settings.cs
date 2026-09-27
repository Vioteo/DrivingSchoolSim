using System.IO;
using DrivingSchool.Presentation.UI;
using DrivingSchool.Settings;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace DrivingSchool.Editor
{
    // T43: экран настроек по docs/ui-settings.md и макету artifacts/visual-review/settings/index.html.
    // Каркас и шаблоны строк; строки создаёт SettingsScreenController по схеме DS.Settings.
    public static partial class UIBuilder
    {
        public const string SettingsPrefabPath = "Assets/DrivingSchool/Prefabs/UI/Settings.prefab";
        public const string CircleSpritePath = "Assets/DrivingSchool/Art/UI/Circle.png";
        public const string TriangleSpritePath = "Assets/DrivingSchool/Art/UI/Triangle.png";
        public const string ThemeCatalogPath = "Assets/DrivingSchool/Data/UI/Resources/UIThemeCatalog.asset";
        const string MonoFontPath = FontRoot + "/RobotoMono/RobotoMono-Medium SDF.asset";

        [MenuItem("Driving School/Build Settings screen")]
        public static void BuildSettingsMenu() { BuildSettings(); Debug.Log("SETTINGS_PREFAB_BUILT"); }

        /// <summary>Каталог тем в Resources: SettingsApplier находит темы по индексу gameplay.uiTheme без ссылок из сцены.</summary>
        public static UIThemeCatalog BuildThemeCatalog()
        {
            BuildThemes();
            Directory.CreateDirectory(Path.GetDirectoryName(ThemeCatalogPath));
            var cat = AssetDatabase.LoadAssetAtPath<UIThemeCatalog>(ThemeCatalogPath);
            if (cat == null) { cat = ScriptableObject.CreateInstance<UIThemeCatalog>(); AssetDatabase.CreateAsset(cat, ThemeCatalogPath); }
            cat.themes = new UITheme[Themes.Length];
            for (int i = 0; i < Themes.Length; i++) cat.themes[i] = AssetDatabase.LoadAssetAtPath<UITheme>($"{ThemeRoot}/UITheme_{Themes[i].id}.asset");
            EditorUtility.SetDirty(cat);
            AssetDatabase.SaveAssets();
            return cat;
        }

        /// <summary>Спрайты-фигуры для UI: круг (9-slice → «таблетка») и треугольник-стрелка. Глифов ◀ ▶ в шрифте нет.</summary>
        static Sprite ShapeSprite(string path, int size, System.Func<float, float, float> alpha, int border)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(alpha((x + 0.5f) / size, (y + 0.5f) / size))));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.spriteBorder = new Vector4(border, border, border, border);
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.mipmapEnabled = false;
            imp.alphaIsTransparency = true;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        static Sprite s_Circle, s_Triangle;
        static Sprite Circle() => s_Circle != null ? s_Circle : s_Circle = ShapeSprite(CircleSpritePath, 64,
            (u, v) => { float d = Mathf.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f)) * 64f; return 32f - d; }, 31);
        static Sprite Triangle() => s_Triangle != null ? s_Triangle : s_Triangle = ShapeSprite(TriangleSpritePath, 32,
            (u, v) => { float half = 0.5f * (1f - u) * 0.9f; float e = half - Mathf.Abs(v - 0.5f); return Mathf.Min(e * 32f + 0.5f, (u - 0.1f) * 32f + 0.5f, (0.95f - u) * 32f + 0.5f); }, 0);

        static RectTransform Node(string name, Transform parent)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            return rt;
        }

        static TextMeshProUGUI Label(Transform parent, string name, string text, int size, ThemeRole role, UITheme theme,
            TextAlignmentOptions align = TextAlignmentOptions.Left, FontWeight weight = FontWeight.Regular, bool themed = true, bool mono = false)
        {
            var rt = Node(name, parent);
            var tmp = AddThemedText(rt.gameObject, text, size, role, theme, align, weight, themed);
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.raycastTarget = false;
            if (mono)
            {
                var f = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(MonoFontPath);
                if (f != null) tmp.font = f;
            }
            return tmp;
        }

        static void Stretch(RectTransform rt, float l = 0, float r = 0, float t = 0, float b = 0)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(-r, -t);
        }

        static void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 size, Vector2 pos)
        {
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = pivot; rt.sizeDelta = size; rt.anchoredPosition = pos;
        }

        static Image Pill(GameObject go, Color c)
        {
            var img = AddImage(go, c);
            img.sprite = Circle(); img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = 2f;
            return img;
        }

        static Button PlainButton(RectTransform rt, Image target)
        {
            var b = rt.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.targetGraphic = target;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            return b;
        }

        static void KeyChip(Transform parent, string key, Vector2 pos, UITheme theme)
        {
            var chip = Node("Key_" + key, parent);
            Place(chip, new Vector2(0, 1), new Vector2(0, 1), new Vector2(32, 30), pos);
            var img = AddImage(chip.gameObject, theme.bgRowHover); img.raycastTarget = false;
            var t = Label(chip, "Text", key, 16, ThemeRole.Muted, theme, TextAlignmentOptions.Center, FontWeight.Bold, true, true);
            Stretch(t.rectTransform);
        }

        public static void BuildSettings()
        {
            var theme = BuildThemes();
            BuildThemeCatalog();
            var canvasGo = CreateCanvas("Settings");
            canvasGo.GetComponent<Canvas>().sortingOrder = 110;   // поверх паузы (100) и меню
            var c = canvasGo.AddComponent<SettingsScreenController>();
            c.defaultTheme = theme;

            var root = Node("Root", canvasGo.transform); Stretch(root);
            var rootBg = AddThemedImage(root.gameObject, ThemeRole.BgDark, theme);
            rootBg.GetComponent<ThemedGraphic>().alpha = 0.97f;
            rootBg.color = new Color(rootBg.color.r, rootBg.color.g, rootBg.color.b, 0.97f);
            c.root = root.gameObject;

            // Шапка
            var title = Label(root, "Title", "Настройки", 56, ThemeRole.Text, theme, TextAlignmentOptions.Left, FontWeight.Bold);
            Place(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(900, 70), new Vector2(96, -48));
            c.crumb = Label(root, "Crumb", "Главное меню / Настройки", 22, ThemeRole.Muted, theme);
            Place(c.crumb.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(900, 32), new Vector2(96, -122));
            c.dirtyText = Label(root, "Dirty", "", 22, ThemeRole.Accent, theme, TextAlignmentOptions.Right, FontWeight.Medium, themed: false);
            Place(c.dirtyText.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(700, 36), new Vector2(-96, -122));

            // Вкладки: Q [Графика] [Управление] [Звук] [Обучение] E
            var tabs = SettingsSchema.Tabs;
            c.tabButtons = new Button[tabs.Length]; c.tabLabels = new TMP_Text[tabs.Length]; c.tabUnderlines = new Image[tabs.Length];
            KeyChip(root, "Q", new Vector2(96, -181), theme);
            float x = 144;
            for (int i = 0; i < tabs.Length; i++)
            {
                float w = 70 + tabs[i].Title.Length * 17;
                var tab = Node("Tab_" + tabs[i].Id, root);
                Place(tab, new Vector2(0, 1), new Vector2(0, 1), new Vector2(w, 60), new Vector2(x, -166));
                var hit = AddImage(tab.gameObject, new Color(0, 0, 0, 0));
                c.tabButtons[i] = PlainButton(tab, hit);
                c.tabLabels[i] = Label(tab, "Label", tabs[i].Title, 28, ThemeRole.Text2, theme, TextAlignmentOptions.Center, FontWeight.Bold, themed: false);
                Stretch(c.tabLabels[i].rectTransform);
                var line = Node("Underline", tab);
                Place(line, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(w - 24, 4), Vector2.zero);
                c.tabUnderlines[i] = AddImage(line.gameObject, theme.accent); c.tabUnderlines[i].raycastTarget = false;
                x += w + 8;
            }
            KeyChip(root, "E", new Vector2(x + 8, -181), theme);
            var divider = Node("Divider", root);
            divider.anchorMin = new Vector2(0, 1); divider.anchorMax = new Vector2(1, 1); divider.pivot = new Vector2(0.5f, 1);
            divider.sizeDelta = new Vector2(0, 2); divider.anchoredPosition = new Vector2(0, -234);
            AddThemedImage(divider.gameObject, ThemeRole.Line, theme).raycastTarget = false;

            // Список пунктов с прокруткой
            var list = Node("List", root);
            list.anchorMin = new Vector2(0, 0); list.anchorMax = new Vector2(0, 1); list.pivot = new Vector2(0, 1);
            list.offsetMin = new Vector2(96, 120); list.offsetMax = new Vector2(96 + 1240, -252);
            var viewport = Node("Viewport", list); Stretch(viewport);
            AddImage(viewport.gameObject, new Color(0, 0, 0, 0));   // ловит колесо мыши
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = Node("Content", viewport);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1);
            content.sizeDelta = Vector2.zero; content.anchoredPosition = Vector2.zero;
            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 4; vlg.padding = new RectOffset(0, 0, 0, 16);
            vlg.childControlWidth = true; vlg.childForceExpandWidth = true; vlg.childControlHeight = true; vlg.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var sr = list.gameObject.AddComponent<ScrollRect>();
            sr.viewport = viewport; sr.content = content; sr.horizontal = false; sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped; sr.scrollSensitivity = 40f; sr.inertia = false;
            c.scroll = sr; c.content = content;

            // Шаблон заголовка группы
            var group = Label(content, "GroupTemplate", "ГРУППА", 18, ThemeRole.Muted, theme, TextAlignmentOptions.BottomLeft, FontWeight.Bold);
            group.characterSpacing = 8f;
            group.margin = new Vector4(14, 0, 0, 8);
            group.gameObject.AddComponent<LayoutElement>().preferredHeight = 52;
            c.groupTemplate = group;

            c.rowTemplate = BuildSettingsRow(content, theme);

            // Справка справа
            var help = Node("Help", root);
            Place(help, new Vector2(1, 1), new Vector2(1, 1), new Vector2(460, 600), new Vector2(-96, -256));
            AddThemedImage(help.gameObject, ThemeRole.BgPanel, theme).raycastTarget = false;
            var hv = help.gameObject.AddComponent<VerticalLayoutGroup>();
            hv.padding = new RectOffset(28, 28, 28, 28); hv.spacing = 14;
            hv.childControlWidth = true; hv.childForceExpandWidth = true; hv.childControlHeight = true; hv.childForceExpandHeight = false;
            var status = Node("Status", help);
            AddThemedImage(status.gameObject, ThemeRole.BgCard, theme).raycastTarget = false;
            var sh = status.gameObject.AddComponent<HorizontalLayoutGroup>();
            sh.padding = new RectOffset(16, 16, 12, 12); sh.spacing = 10; sh.childAlignment = TextAnchor.MiddleLeft;
            sh.childControlWidth = true; sh.childForceExpandWidth = false; sh.childControlHeight = true; sh.childForceExpandHeight = false;
            var dot = Node("Dot", status);
            c.helpStatusDot = AddImage(dot.gameObject, theme.red); c.helpStatusDot.sprite = Circle(); c.helpStatusDot.raycastTarget = false;
            var dle = dot.gameObject.AddComponent<LayoutElement>(); dle.preferredWidth = dle.preferredHeight = 12;
            c.helpStatusText = Label(status, "Text", "Руль не найден — доступна клавиатура", 18, ThemeRole.Text2, theme);
            c.helpStatus = status.gameObject;
            c.helpTitle = Label(help, "Title", "Пункт", 30, ThemeRole.Text, theme, TextAlignmentOptions.Left, FontWeight.Bold);
            c.helpTitle.textWrappingMode = TextWrappingModes.Normal;
            c.helpBody = Label(help, "Body", "Описание", 21, ThemeRole.Text2, theme);
            c.helpBody.textWrappingMode = TextWrappingModes.Normal; c.helpBody.lineSpacing = 6;
            var hline = Node("Line", help); AddThemedImage(hline.gameObject, ThemeRole.Line, theme).raycastTarget = false;
            hline.gameObject.AddComponent<LayoutElement>().preferredHeight = 2;
            c.metaKey = MetaRow(help, "Ключ", theme);
            c.metaDefault = MetaRow(help, "По умолчанию", theme);
            c.metaApply = MetaRow(help, "Применение", theme);

            // Нижняя панель
            var bar = Node("BottomBar", root);
            bar.anchorMin = new Vector2(0, 0); bar.anchorMax = new Vector2(1, 0); bar.pivot = new Vector2(0.5f, 0);
            bar.sizeDelta = new Vector2(0, 96); bar.anchoredPosition = Vector2.zero;
            var barBg = AddThemedImage(bar.gameObject, ThemeRole.BgPanel, theme); barBg.raycastTarget = true;
            var topLine = Node("TopLine", bar);
            topLine.anchorMin = new Vector2(0, 1); topLine.anchorMax = new Vector2(1, 1); topLine.pivot = new Vector2(0.5f, 1); topLine.sizeDelta = new Vector2(0, 2);
            AddThemedImage(topLine.gameObject, ThemeRole.Line, theme).raycastTarget = false;
            var hints = Label(bar, "Hints", "↑↓ пункт     ←→ значение     Q E вкладка     R сброс пункта     Enter выбрать     Esc назад", 20, ThemeRole.Muted, theme);
            Place(hints.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(1100, 40), new Vector2(96, 0));
            string[] labels = { "По умолчанию", "Отменить", "Применить" };
            float[] widths = { 260, 220, 240 };
            float bx = -96;
            for (int i = 2; i >= 0; i--)
            {
                var b = Node("Btn_" + labels[i], bar);
                Place(b, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(widths[i], 60), new Vector2(bx, 0));
                c.bottomBgs[i] = AddImage(b.gameObject, theme.bgRowHover);
                c.bottomButtons[i] = PlainButton(b, c.bottomBgs[i]);
                c.bottomLabels[i] = Label(b, "Label", labels[i], 24, ThemeRole.Text, theme, TextAlignmentOptions.Center, FontWeight.Bold, themed: false);
                Stretch(c.bottomLabels[i].rectTransform);
                bx -= widths[i] + 16;
            }

            BuildSettingsDialog(root, c, theme);

            // Уведомление
            var toast = Node("Toast", root);
            Place(toast, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(520, 60), new Vector2(0, 124));
            AddThemedImage(toast.gameObject, ThemeRole.BgCard, theme).raycastTarget = false;
            var tbar = Node("Bar", toast); Place(tbar, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(5, 60), Vector2.zero);
            AddThemedImage(tbar.gameObject, ThemeRole.Green, theme).raycastTarget = false;
            c.toastText = Label(toast, "Text", "Настройки применены", 22, ThemeRole.Text, theme, TextAlignmentOptions.Center);
            Stretch(c.toastText.rectTransform, 16, 16, 0, 0);
            c.toast = toast.gameObject;

            root.gameObject.SetActive(false);
            SavePrefab(canvasGo, "Settings");
        }

        static TMP_Text MetaRow(Transform parent, string title, UITheme theme)
        {
            var row = Node("Meta_" + title, parent);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 12; h.childControlWidth = true; h.childForceExpandWidth = false; h.childControlHeight = true; h.childForceExpandHeight = false;
            var k = Label(row, "Title", title, 18, ThemeRole.Muted, theme);
            k.gameObject.AddComponent<LayoutElement>().preferredWidth = 140;
            var v = Label(row, "Value", "—", 16, ThemeRole.Text2, theme, TextAlignmentOptions.Left, FontWeight.Medium, true, mono: true);
            v.textWrappingMode = TextWrappingModes.Normal;
            var vle = v.gameObject.AddComponent<LayoutElement>(); vle.preferredWidth = 0; vle.flexibleWidth = 1;
            return v;
        }

        static SettingsRowView BuildSettingsRow(Transform content, UITheme theme)
        {
            var row = Node("RowTemplate", content);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 60;
            var v = row.gameObject.AddComponent<SettingsRowView>();
            v.background = AddImage(row.gameObject, theme.bgCard);   // ловит наведение и клики по строке
            var bar = Node("FocusBar", row); Place(bar, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(6, 60), Vector2.zero);
            v.focusBar = AddImage(bar.gameObject, theme.accent); v.focusBar.raycastTarget = false; v.focusBar.enabled = false;

            var left = Node("LabelGroup", row);
            left.anchorMin = new Vector2(0, 0); left.anchorMax = new Vector2(0, 1); left.pivot = new Vector2(0, 0.5f);
            left.sizeDelta = new Vector2(820, 0); left.anchoredPosition = new Vector2(30, 0);
            var h = left.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 12; h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = true; h.childForceExpandWidth = false; h.childControlHeight = true; h.childForceExpandHeight = false;
            v.label = Label(left, "Label", "Пункт", 24, ThemeRole.Text, theme, TextAlignmentOptions.Left, FontWeight.Regular, themed: false);
            var badge = Node("Badge", left);
            v.badgeBg = AddImage(badge.gameObject, theme.bgRowHover); v.badgeBg.raycastTarget = false;
            var bh = badge.gameObject.AddComponent<HorizontalLayoutGroup>();
            bh.padding = new RectOffset(10, 10, 3, 3); bh.childControlWidth = true; bh.childControlHeight = true; bh.childForceExpandWidth = false; bh.childForceExpandHeight = false;
            v.badgeText = Label(badge, "Text", "скоро", 16, ThemeRole.Muted, theme, TextAlignmentOptions.Center, FontWeight.Medium, themed: false);
            v.badge = badge.gameObject;
            var dot = Node("ChangedDot", left);
            v.changedDot = AddImage(dot.gameObject, theme.accent); v.changedDot.sprite = Circle(); v.changedDot.raycastTarget = false;
            var dle = dot.gameObject.AddComponent<LayoutElement>(); dle.preferredWidth = dle.preferredHeight = 10;

            var value = Node("Value", row);
            Place(value, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(440, 44), new Vector2(-24, 0));

            // Список: ◀ значение ▶
            var cyc = Node("Cycle", value); Stretch(cyc);
            v.cycleRoot = cyc.gameObject;
            v.cycleBg = AddImage(cyc.gameObject, theme.bgRowHover); v.cycleBg.raycastTarget = false;
            v.prevButton = ArrowButton(cyc, "Prev", false, out v.prevIcon, theme);
            v.nextButton = ArrowButton(cyc, "Next", true, out v.nextIcon, theme);
            v.cycleText = Label(cyc, "Text", "Значение", 22, ThemeRole.Text, theme, TextAlignmentOptions.Center, FontWeight.Medium, themed: false);
            Stretch(v.cycleText.rectTransform, 48, 48, 0, 0);

            // Ползунок
            var sl = Node("Slider", value); Stretch(sl);
            v.sliderRoot = sl.gameObject;
            var sliderRt = Node("Track", sl);
            Place(sliderRt, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(300, 28), new Vector2(8, 0));
            var hitArea = AddImage(sliderRt.gameObject, new Color(0, 0, 0, 0));   // клик по всей высоте дорожки
            var track = Node("Line", sliderRt);
            track.anchorMin = new Vector2(0, 0.5f); track.anchorMax = new Vector2(1, 0.5f); track.sizeDelta = new Vector2(0, 6);
            v.sliderTrack = Pill(track.gameObject, theme.bgRowHover); v.sliderTrack.raycastTarget = false;
            var fillArea = Node("FillArea", sliderRt); Stretch(fillArea, 0, 0, 11, 11);
            var fill = Node("Fill", fillArea); fill.sizeDelta = Vector2.zero;
            v.sliderFill = Pill(fill.gameObject, theme.accent); v.sliderFill.raycastTarget = false;
            var handleArea = Node("HandleArea", sliderRt); Stretch(handleArea, 11, 11, 0, 0);
            var knob = Node("Knob", handleArea); knob.sizeDelta = new Vector2(22, -6);   // Slider растягивает ручку по высоте области (28): 28 − 6 = 22
            v.sliderKnob = AddImage(knob.gameObject, theme.text); v.sliderKnob.sprite = Circle();
            v.slider = sliderRt.gameObject.AddComponent<Slider>();
            v.slider.fillRect = fill; v.slider.handleRect = knob; v.slider.targetGraphic = v.sliderKnob;
            v.slider.direction = Slider.Direction.LeftToRight; v.slider.transition = Selectable.Transition.None;
            v.slider.navigation = new Navigation { mode = Navigation.Mode.None };
            hitArea.raycastTarget = true;
            v.sliderText = Label(sl, "Text", "100 %", 22, ThemeRole.Text, theme, TextAlignmentOptions.Right, FontWeight.Medium, themed: false, mono: true);
            Place(v.sliderText.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(120, 40), new Vector2(-8, 0));

            // Переключатель
            var sw = Node("Switch", value); Stretch(sw);
            v.switchRoot = sw.gameObject;
            var swTrack = Node("Track", sw);
            Place(swTrack, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(58, 30), new Vector2(8, 0));
            v.switchTrack = Pill(swTrack.gameObject, theme.bgRowHover);
            v.switchButton = PlainButton(swTrack, v.switchTrack);
            var swKnob = Node("Knob", swTrack); Place(swKnob, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(22, 22), new Vector2(-15, 0));
            v.switchKnob = AddImage(swKnob.gameObject, theme.text); v.switchKnob.sprite = Circle(); v.switchKnob.raycastTarget = false;
            v.switchText = Label(sw, "Text", "Выкл.", 22, ThemeRole.Text, theme, TextAlignmentOptions.Left, FontWeight.Medium, themed: false);
            Place(v.switchText.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(200, 40), new Vector2(84, 0));

            // Действие
            var act = Node("Action", value); Stretch(act);
            v.actionRoot = act.gameObject;
            var actBtn = Node("Button", act);
            Place(actBtn, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(220, 44), new Vector2(0, 0));
            v.actionBg = AddImage(actBtn.gameObject, theme.bgRowHover);
            v.actionButton = PlainButton(actBtn, v.actionBg);
            v.actionText = Label(actBtn, "Text", "Открыть →", 22, ThemeRole.Text, theme, TextAlignmentOptions.Center, FontWeight.Medium, themed: false);
            Stretch(v.actionText.rectTransform);

            row.gameObject.SetActive(false);
            return v;
        }

        static Button ArrowButton(Transform parent, string name, bool right, out Image icon, UITheme theme)
        {
            var b = Node(name, parent);
            var a = new Vector2(right ? 1 : 0, 0.5f);
            Place(b, a, a, new Vector2(44, 44), Vector2.zero);
            var hit = AddImage(b.gameObject, new Color(0, 0, 0, 0));
            var btn = PlainButton(b, hit);
            var ic = Node("Icon", b); Place(ic, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(16, 16), Vector2.zero);
            if (!right) ic.localRotation = Quaternion.Euler(0, 0, 180);
            icon = AddImage(ic.gameObject, theme.text2); icon.sprite = Triangle(); icon.raycastTarget = false;
            return btn;
        }

        static void BuildSettingsDialog(Transform root, SettingsScreenController c, UITheme theme)
        {
            var dlg = Node("Dialog", root); Stretch(dlg);
            var shade = AddImage(dlg.gameObject, new Color(0, 0, 0, 0.72f)); shade.raycastTarget = true;
            var panel = Node("Panel", dlg);
            Place(panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(760, 0), Vector2.zero);
            AddThemedImage(panel.gameObject, ThemeRole.BgPanel, theme);
            var v = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(40, 40, 44, 36); v.spacing = 18;
            v.childControlWidth = true; v.childForceExpandWidth = true; v.childControlHeight = true; v.childForceExpandHeight = false;
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var accent = Node("AccentLine", panel);
            accent.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            accent.anchorMin = new Vector2(0, 1); accent.anchorMax = new Vector2(1, 1); accent.pivot = new Vector2(0.5f, 1); accent.sizeDelta = new Vector2(0, 6);
            AddThemedImage(accent.gameObject, ThemeRole.Accent, theme).raycastTarget = false;
            c.dialogTitle = Label(panel, "Title", "Заголовок", 36, ThemeRole.Text, theme, TextAlignmentOptions.Left, FontWeight.Bold);
            c.dialogTitle.textWrappingMode = TextWrappingModes.Normal;
            c.dialogCountdown = Label(panel, "Countdown", "15", 72, ThemeRole.Accent, theme, TextAlignmentOptions.Left, FontWeight.Bold, true, mono: true);
            c.dialogBody = Label(panel, "Body", "Текст", 22, ThemeRole.Text2, theme);
            c.dialogBody.textWrappingMode = TextWrappingModes.Normal; c.dialogBody.lineSpacing = 6;
            var row = Node("Buttons", panel);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 16; h.childAlignment = TextAnchor.MiddleRight; h.padding = new RectOffset(0, 0, 14, 0);
            h.childControlWidth = true; h.childForceExpandWidth = false; h.childControlHeight = true; h.childForceExpandHeight = false;
            for (int i = 0; i < 3; i++)
            {
                var b = Node("Btn" + i, row);
                var le = b.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = 210; le.preferredHeight = 60;
                c.dialogBgs[i] = AddImage(b.gameObject, theme.bgRowHover);
                c.dialogButtons[i] = PlainButton(b, c.dialogBgs[i]);
                c.dialogLabels[i] = Label(b, "Label", "Кнопка", 22, ThemeRole.Text, theme, TextAlignmentOptions.Center, FontWeight.Bold, themed: false);
                Stretch(c.dialogLabels[i].rectTransform);
            }
            c.dialog = dlg.gameObject;
            dlg.gameObject.SetActive(false);
        }
    }
}
