using DrivingSchool.Presentation.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace DrivingSchool.Editor
{
    // T47: HUD поездки и разбор поездки по docs/ui-drive.md (макет artifacts/visual-review/drive/index.html).
    public static partial class UIBuilder
    {
        public const string WhiteSpritePath = "Assets/DrivingSchool/Art/UI/White.png";
        static Sprite s_White;
        static Sprite White() => s_White != null ? s_White : s_White = ShapeSprite(WhiteSpritePath, 8, (u, v) => 1f, 0);

        [MenuItem("Driving School/Build Drive HUD")]
        public static void BuildHudMenu() { BuildHUD(); Debug.Log("HUD_PREFAB_BUILT"); }

        static Image Panel(RectTransform rt, UITheme theme, float alpha = 0.92f, ThemeRole role = ThemeRole.BgPanel)
        {
            var img = AddThemedImage(rt.gameObject, role, theme);
            img.GetComponent<ThemedGraphic>().alpha = alpha;
            img.color = new Color(img.color.r, img.color.g, img.color.b, alpha);
            img.raycastTarget = false;
            return img;
        }

        static LayoutElement LE(Component c, float w = -1, float h = -1, float flexW = -1, float flexH = -1)
        {
            var le = c.gameObject.GetComponent<LayoutElement>();
            if (le == null) le = c.gameObject.AddComponent<LayoutElement>();   // не «??»: в редакторе у Unity «фальшивый null»
            le.preferredWidth = w; le.preferredHeight = h; le.flexibleWidth = flexW; le.flexibleHeight = flexH;
            return le;
        }

        static T Layout<T>(RectTransform rt, int l, int r, int t, int b, float spacing, bool forceH = false) where T : HorizontalOrVerticalLayoutGroup
        {
            var g = rt.gameObject.AddComponent<T>();
            g.padding = new RectOffset(l, r, t, b); g.spacing = spacing;
            g.childControlWidth = true; g.childControlHeight = true; g.childForceExpandWidth = false; g.childForceExpandHeight = forceH;
            return g;
        }

        public static void BuildHUD()
        {
            var theme = BuildThemes();
            var canvas = CreateCanvas("HUD");
            canvas.GetComponent<Canvas>().sortingOrder = 10;
            Object.DestroyImmediate(canvas.GetComponent<GraphicRaycaster>());   // HUD не ловит мышь
            var v = canvas.AddComponent<DriveHudView>();
            v.defaultTheme = theme;
            var root = canvas.transform;

            // --- Подсказка инструктора: сверху по центру ---
            var hint = Node("Hint", root);
            Place(hint, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(1000, 0), new Vector2(0, -36));
            Panel(hint, theme);
            Layout<HorizontalLayoutGroup>(hint, 0, 28, 0, 0, 22, forceH: true);
            hint.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var bar = Node("Bar", hint); v.hintBar = AddImage(bar.gameObject, theme.accent); v.hintBar.raycastTarget = false; LE(bar, 8);
            var col = Node("Text", hint); Layout<VerticalLayoutGroup>(col, 0, 0, 16, 16, 4); LE(col, -1, -1, 1);
            v.hintText = Label(col, "Hint", "Подсказка инструктора", 26, ThemeRole.Text, theme, TextAlignmentOptions.Left, FontWeight.Medium);
            v.hintText.textWrappingMode = TextWrappingModes.Normal;
            v.hintWaiting = Label(col, "Waiting", "", 16, ThemeRole.Muted, theme);
            v.hintPanel = hint.gameObject;

            // --- Замечания: справа вверху ---
            var rem = Node("Remarks", root);
            Place(rem, new Vector2(1, 1), new Vector2(1, 1), new Vector2(300, 124), new Vector2(-40, -36));
            Panel(rem, theme);
            var rl = Label(rem, "Label", "ЗАМЕЧАНИЯ", 16, ThemeRole.Muted, theme, TextAlignmentOptions.Left, FontWeight.Bold);
            rl.characterSpacing = 6; Place(rl.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(260, 24), new Vector2(24, -14));
            v.remarksCount = Label(rem, "Count", "0", 52, ThemeRole.Text, theme, TextAlignmentOptions.Left, FontWeight.Bold, themed: false, mono: true);
            Place(v.remarksCount.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(200, 60), new Vector2(22, -36));
            v.remarksNote = Label(rem, "Note", "тренировка · баллы — после T37", 15, ThemeRole.Muted, theme);
            Place(v.remarksNote.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(270, 22), new Vector2(24, 12));
            v.remarksPanel = rem.gameObject;

            // --- Карточки нарушений: под замечаниями ---
            var cards = Node("Cards", root);
            Place(cards, new Vector2(1, 1), new Vector2(1, 1), new Vector2(440, 420), new Vector2(-40, -174));
            var cv = Layout<VerticalLayoutGroup>(cards, 0, 0, 0, 0, 10); cv.childForceExpandWidth = true; cv.childAlignment = TextAnchor.UpperRight;
            v.cardsRoot = cards;
            var card = Node("CardTemplate", cards);
            var cardBg = Panel(card, theme, 0.95f);
            LE(card, -1, -1, -1, 0);   // высота по содержимому: карточка не растягивается на весь столбец
            Layout<HorizontalLayoutGroup>(card, 0, 18, 0, 0, 16, forceH: true);
            var cbar = Node("Bar", card); AddImage(cbar.gameObject, theme.red).raycastTarget = false; LE(cbar, 6);
            var cc = Node("Content", card); Layout<VerticalLayoutGroup>(cc, 0, 0, 14, 14, 4); LE(cc, -1, -1, 1);
            var ct = Label(cc, "Title", "Нарушение", 22, ThemeRole.Text, theme, TextAlignmentOptions.Left, FontWeight.Bold); ct.textWrappingMode = TextWrappingModes.Normal;
            Label(cc, "Reference", "PDD", 15, ThemeRole.Muted, theme, TextAlignmentOptions.Left, FontWeight.Regular, true, mono: true).textWrappingMode = TextWrappingModes.Normal;
            var ca = Label(cc, "Advice", "Как правильно", 18, ThemeRole.Text2, theme); ca.textWrappingMode = TextWrappingModes.Normal;
            v.cardTemplate = card.gameObject;
            card.gameObject.SetActive(false);
            _ = cardBg;

            BuildInstruments(v, root, theme);
            BuildMinimap(v, root, theme);
            BuildDebrief(canvas.transform, theme);
            SavePrefab(canvas, "HUD");
        }

        static void BuildInstruments(DriveHudView v, Transform root, UITheme theme)
        {
            var ins = Node("Instruments", root);
            Place(ins, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(820, 190), new Vector2(0, 36));
            Panel(ins, theme, 0.85f);
            v.instruments = ins.gameObject;

            // Контрольные лампы
            var row = Node("Telltales", ins);
            Place(row, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(780, 34), new Vector2(0, -14));
            var h = Layout<HorizontalLayoutGroup>(row, 0, 0, 0, 0, 10); h.childAlignment = TextAnchor.MiddleCenter;
            v.indicatorLeft = Arrow(row, "IndicatorLeft", false, theme);
            string[] lamps = { "Ближний", "Дальний", "Ручник", "Ремень", "Двигатель" };
            for (int i = 0; i < lamps.Length; i++)
            {
                var chip = Node("Lamp_" + lamps[i], row); LE(chip, 116, 30);
                v.telltaleBgs[i] = Pill(chip.gameObject, theme.bgCard); v.telltaleBgs[i].raycastTarget = false;
                v.telltaleTexts[i] = Label(chip, "Text", lamps[i], 16, ThemeRole.Muted, theme, TextAlignmentOptions.Center, FontWeight.Bold, themed: false);
                Stretch(v.telltaleTexts[i].rectTransform);
            }
            v.indicatorRight = Arrow(row, "IndicatorRight", true, theme);
            v.telltalesRow = row.gameObject;

            // Скорость
            v.speedText = Label(ins, "Speed", "0", 76, ThemeRole.Text, theme, TextAlignmentOptions.Right, FontWeight.Bold, themed: false, mono: true);
            Place(v.speedText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(1, 0.5f), new Vector2(240, 90), new Vector2(-60, -8));
            v.speedUnit = Label(ins, "Unit", "км/ч", 20, ThemeRole.Muted, theme);
            Place(v.speedUnit.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 0.5f), new Vector2(80, 30), new Vector2(-50, -30));

            // Схема КПП: H-паттерн 6+R (R справа внизу, как у Driving Force Shifter) или строка P R N D.
            var gb = Node("Gearbox", ins);
            Place(gb, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(220, 110), new Vector2(210, -8));
            var man = Node("Manual", gb); Stretch(man); v.manualBox = man.gameObject;
            float[] xs = { -72, -24, 24, 72 };
            Line(man, new Vector2(0, 0), new Vector2(146, 3), theme);
            for (int c = 0; c < 3; c++) Line(man, new Vector2(xs[c], 0), new Vector2(3, 60), theme);
            Line(man, new Vector2(xs[3], -15), new Vector2(3, 30), theme);
            string[] names = { "1", "2", "3", "4", "5", "6", "R" };
            for (int i = 0; i < 7; i++)
            {
                int c = i == 6 ? 3 : i / 2; bool top = i != 6 && i % 2 == 0;
                v.gearLabels[i] = Label(man, "Gear_" + names[i], names[i], 22, ThemeRole.Muted, theme, TextAlignmentOptions.Center, FontWeight.Bold, themed: false, mono: true);
                Place(v.gearLabels[i].rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(30, 26), new Vector2(xs[c], top ? 44 : -44));
            }
            var dot = Node("Neutral", man); Place(dot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(14, 14), Vector2.zero);
            v.neutralDot = AddImage(dot.gameObject, theme.accent); v.neutralDot.sprite = Circle(); v.neutralDot.raycastTarget = false;
            v.gearLabels[7] = null;
            var au = Node("Automatic", gb); Stretch(au); v.autoBox = au.gameObject;
            string[] sel = { "P", "R", "N", "D" };
            for (int i = 0; i < 4; i++)
            {
                v.selectorLabels[i] = Label(au, "Sel_" + sel[i], sel[i], 36, ThemeRole.Muted, theme, TextAlignmentOptions.Center, FontWeight.Bold, themed: false, mono: true);
                Place(v.selectorLabels[i].rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(44, 50), new Vector2(-72 + i * 48, 0));
            }
            au.gameObject.SetActive(false);

            // Обороты
            var rpm = Node("Rpm", ins);
            Place(rpm, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(740, 26), new Vector2(0, 14));
            var rl = Label(rpm, "Label", "об/мин", 14, ThemeRole.Muted, theme);
            Place(rl.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(70, 20), Vector2.zero);
            var track = Node("Track", rpm); Place(track, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(660, 8), new Vector2(78, 0));
            AddThemedImage(track.gameObject, ThemeRole.BgCard, theme).raycastTarget = false;
            var fill = Node("Fill", track); Stretch(fill);
            v.rpmFill = AddImage(fill.gameObject, theme.text2); v.rpmFill.sprite = White();
            v.rpmFill.type = Image.Type.Filled; v.rpmFill.fillMethod = Image.FillMethod.Horizontal; v.rpmFill.fillAmount = 0.15f; v.rpmFill.raycastTarget = false;
            v.rpmRow = rpm.gameObject;
        }

        static Image Arrow(Transform parent, string name, bool right, UITheme theme)
        {
            var a = Node(name, parent); LE(a, 30, 30);
            var ic = Node("Icon", a); Place(ic, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(26, 26), Vector2.zero);
            if (!right) ic.localRotation = Quaternion.Euler(0, 0, 180);
            var img = AddImage(ic.gameObject, theme.bgRowHover); img.sprite = Triangle(); img.raycastTarget = false;
            return img;
        }

        static void Line(Transform parent, Vector2 pos, Vector2 size, UITheme theme)
        {
            var l = Node("Line", parent); Place(l, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), size, pos);
            AddThemedImage(l.gameObject, ThemeRole.Muted, theme).raycastTarget = false;
        }

        static void BuildMinimap(DriveHudView v, Transform root, UITheme theme)
        {
            var map = Node("Minimap", root);
            Place(map, new Vector2(0, 0), new Vector2(0, 0), new Vector2(270, 270), new Vector2(40, 36));
            Panel(map, theme, 0.9f);
            var img = Node("Image", map); Stretch(img, 6, 6, 6, 6);
            v.minimapImage = img.gameObject.AddComponent<RawImage>(); v.minimapImage.raycastTarget = false;
            var arrow = Node("Arrow", map); Place(arrow, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(26, 26), Vector2.zero);
            var ic = Node("Icon", arrow); Stretch(ic); ic.localRotation = Quaternion.Euler(0, 0, 90);   // треугольник смотрит вправо → вверх
            var ai = AddImage(ic.gameObject, Color.white); ai.sprite = Triangle(); ai.raycastTarget = false;
            v.minimapArrow = arrow;
            var n = Label(map, "North", "С", 18, ThemeRole.Text, theme, TextAlignmentOptions.Center, FontWeight.Bold);
            Place(n.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(30, 24), new Vector2(0, -8));
            var sc = Label(map, "Scale", "100 м", 14, ThemeRole.Text2, theme, TextAlignmentOptions.Right, FontWeight.Medium, true, mono: true);
            Place(sc.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(90, 20), new Vector2(-12, 10));
            v.minimapPanel = map.gameObject;
        }

        static void BuildDebrief(Transform hudRoot, UITheme theme)
        {
            var go = Node("Debrief", hudRoot); Stretch(go);
            var cv = go.gameObject.AddComponent<Canvas>(); cv.overrideSorting = true; cv.sortingOrder = 120;
            go.gameObject.AddComponent<GraphicRaycaster>();
            var d = go.gameObject.AddComponent<DebriefView>();
            d.defaultTheme = theme;
            var root = Node("Root", go); Stretch(root);
            var bg = AddThemedImage(root.gameObject, ThemeRole.BgDark, theme); bg.GetComponent<ThemedGraphic>().alpha = 0.97f;
            d.root = root.gameObject;

            d.title = Label(root, "Title", "Разбор поездки", 56, ThemeRole.Text, theme, TextAlignmentOptions.Left, FontWeight.Bold);
            Place(d.title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(1200, 70), new Vector2(96, -48));
            d.subtitle = Label(root, "Subtitle", "Тренировка · тестовый полигон", 24, ThemeRole.Text2, theme);
            Place(d.subtitle.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(1400, 34), new Vector2(96, -122));

            // Сводка
            var sum = Node("Summary", root); Place(sum, new Vector2(0, 1), new Vector2(0, 1), new Vector2(380, 640), new Vector2(96, -196));
            Panel(sum, theme, 1f);
            var sh = Label(sum, "Header", "ИТОГИ", 16, ThemeRole.Muted, theme, TextAlignmentOptions.Left, FontWeight.Bold); sh.characterSpacing = 6;
            Place(sh.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(330, 24), new Vector2(28, -24));
            for (int i = 0; i < 6; i++)
            {
                d.summaryLabels[i] = Label(sum, "L" + i, "Параметр", 18, ThemeRole.Muted, theme);
                Place(d.summaryLabels[i].rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(330, 24), new Vector2(28, -70 - i * 92));
                d.summaryValues[i] = Label(sum, "V" + i, "—", 30, ThemeRole.Text, theme, TextAlignmentOptions.Left, FontWeight.Bold);
                Place(d.summaryValues[i].rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(330, 40), new Vector2(28, -96 - i * 92));
            }

            // Хронология
            var lh = Label(root, "EventsHeader", "ХРОНОЛОГИЯ ЗАМЕЧАНИЙ", 16, ThemeRole.Muted, theme, TextAlignmentOptions.Left, FontWeight.Bold); lh.characterSpacing = 6;
            Place(lh.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(600, 24), new Vector2(508, -196));
            var list = Node("List", root);
            list.anchorMin = new Vector2(0, 0); list.anchorMax = new Vector2(0, 1); list.pivot = new Vector2(0, 1);
            list.offsetMin = new Vector2(508, 130); list.offsetMax = new Vector2(508 + 760, -232);
            var vp = Node("Viewport", list); Stretch(vp); AddImage(vp.gameObject, new Color(0, 0, 0, 0)); vp.gameObject.AddComponent<RectMask2D>();
            var content = Node("Content", vp);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1); content.sizeDelta = Vector2.zero;
            var cvl = Layout<VerticalLayoutGroup>(content, 0, 0, 0, 0, 4); cvl.childForceExpandWidth = true;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var sr = list.gameObject.AddComponent<ScrollRect>(); sr.viewport = vp; sr.content = content; sr.horizontal = false; sr.movementType = ScrollRect.MovementType.Clamped; sr.scrollSensitivity = 40;
            d.list = content;
            var row = Node("RowTemplate", content); LE(row, -1, 56);
            var rbg = AddImage(row.gameObject, theme.bgCard);
            var rbtn = row.gameObject.AddComponent<Button>(); rbtn.transition = Selectable.Transition.None; rbtn.targetGraphic = rbg; rbtn.navigation = new Navigation { mode = Navigation.Mode.None };
            var rbar = Node("Bar", row); Place(rbar, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(6, 56), Vector2.zero);
            AddImage(rbar.gameObject, theme.red).raycastTarget = false;
            var rt = Label(row, "Time", "00:00", 18, ThemeRole.Muted, theme, TextAlignmentOptions.Left, FontWeight.Medium, themed: false, mono: true);
            Place(rt.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(90, 30), new Vector2(24, 0));
            var rti = Label(row, "Title", "Событие", 22, ThemeRole.Text, theme, TextAlignmentOptions.Left, FontWeight.Regular, themed: false);
            Place(rti.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(620, 30), new Vector2(120, 0));
            d.rowTemplate = row.gameObject; row.gameObject.SetActive(false);
            d.emptyText = Label(root, "Empty", "Замечаний нет — чистая поездка.", 26, ThemeRole.Text2, theme, TextAlignmentOptions.Center, FontWeight.Medium);
            Place(d.emptyText.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(760, 60), new Vector2(508, -300));

            // Подробности
            var det = Node("Detail", root); Place(det, new Vector2(1, 1), new Vector2(1, 1), new Vector2(460, 640), new Vector2(-96, -196));
            Panel(det, theme, 1f);
            Layout<VerticalLayoutGroup>(det, 28, 28, 28, 28, 16).childForceExpandWidth = true;
            d.detailTitle = Label(det, "Title", "Событие", 30, ThemeRole.Text, theme, TextAlignmentOptions.Left, FontWeight.Bold, themed: false); d.detailTitle.textWrappingMode = TextWrappingModes.Normal;
            d.detailAdvice = Label(det, "Advice", "Как правильно", 21, ThemeRole.Text2, theme); d.detailAdvice.textWrappingMode = TextWrappingModes.Normal; d.detailAdvice.lineSpacing = 6;
            d.detailReference = Label(det, "Reference", "", 16, ThemeRole.Muted, theme, TextAlignmentOptions.Left, FontWeight.Regular, true, mono: true); d.detailReference.textWrappingMode = TextWrappingModes.Normal;
            d.detailPlace = Label(det, "Place", "", 16, ThemeRole.Muted, theme, TextAlignmentOptions.Left, FontWeight.Regular, true, mono: true);
            d.detailPanel = det.gameObject;

            // Нижняя панель
            var barGo = Node("BottomBar", root);
            barGo.anchorMin = new Vector2(0, 0); barGo.anchorMax = new Vector2(1, 0); barGo.pivot = new Vector2(0.5f, 0); barGo.sizeDelta = new Vector2(0, 96);
            AddThemedImage(barGo.gameObject, ThemeRole.BgPanel, theme);
            var hints = Label(barGo, "Hints", "↑↓ событие     ←→ кнопка     Enter выбрать     Esc в главное меню", 20, ThemeRole.Muted, theme);
            Place(hints.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(1000, 40), new Vector2(96, 0));
            var menu = Node("Btn_Menu", barGo); Place(menu, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(300, 60), new Vector2(-96, 0));
            d.menuBg = AddImage(menu.gameObject, theme.accent); d.menuButton = PlainButton(menu, d.menuBg);
            d.menuLabel = Label(menu, "Label", "В главное меню", 24, ThemeRole.Text, theme, TextAlignmentOptions.Center, FontWeight.Bold, themed: false); Stretch(d.menuLabel.rectTransform);
            var again = Node("Btn_Restart", barGo); Place(again, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(260, 60), new Vector2(-412, 0));
            d.restartBg = AddImage(again.gameObject, theme.bgRowHover); d.restartButton = PlainButton(again, d.restartBg);
            d.restartLabel = Label(again, "Label", "Пройти снова", 24, ThemeRole.Text, theme, TextAlignmentOptions.Center, FontWeight.Bold, themed: false); Stretch(d.restartLabel.rectTransform);

            root.gameObject.SetActive(false);
        }
    }
}
