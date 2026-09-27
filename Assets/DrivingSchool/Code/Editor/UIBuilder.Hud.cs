using System.IO;
using DrivingSchool.Presentation.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace DrivingSchool.Editor
{
    // T47: HUD поездки и разбор поездки по docs/ui-drive.md. Значки и шкалы — tools/build_ui_icons.py → Art/UI/Hud.
    public static partial class UIBuilder
    {
        public const string WhiteSpritePath = "Assets/DrivingSchool/Art/UI/White.png";
        public const string HudArt = "Assets/DrivingSchool/Art/UI/Hud/";
        static Sprite s_White;
        static Sprite White() => s_White != null ? s_White : s_White = ShapeSprite(WhiteSpritePath, 8, (u, v) => 1f, 0);

        [MenuItem("Driving School/Build Drive HUD")]
        public static void BuildHudMenu() { BuildHUD(); Debug.Log("HUD_PREFAB_BUILT"); }

        /// <summary>Спрайт из Art/UI/Hud с настройками импорта из кода (правило проекта: без ручного Inspector).</summary>
        static Sprite HudSprite(string name, int border = 0)
        {
            string path = HudArt + name + ".png";
            if (!File.Exists(path)) throw new FileNotFoundException("Нет " + path + " — запустите python tools/build_ui_icons.py");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            bool dirty = imp.textureType != TextureImporterType.Sprite || imp.spriteBorder != new Vector4(border, border, border, border) || imp.mipmapEnabled;
            if (dirty)
            {
                imp.textureType = TextureImporterType.Sprite; imp.spriteImportMode = SpriteImportMode.Single;
                imp.spriteBorder = new Vector4(border, border, border, border);
                imp.wrapMode = TextureWrapMode.Clamp; imp.mipmapEnabled = false; imp.alphaIsTransparency = true;
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        static Image Panel(RectTransform rt, UITheme theme, float alpha = 0.92f, ThemeRole role = ThemeRole.BgPanel)
        {
            var img = AddThemedImage(rt.gameObject, role, theme);
            img.GetComponent<ThemedGraphic>().alpha = alpha;
            img.color = new Color(img.color.r, img.color.g, img.color.b, alpha);
            img.raycastTarget = false;
            return img;
        }

        /// <summary>Скруглённая полупрозрачная панель с мягкой тенью.</summary>
        static Image RoundPanel(RectTransform rt, UITheme theme, float alpha = 0.88f, ThemeRole role = ThemeRole.BgPanel)
        {
            var sh = Node("Shadow", rt); Stretch(sh, -16, -16, -12, -20);
            sh.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var shi = AddImage(sh.gameObject, new Color(0, 0, 0, 0.45f)); shi.sprite = HudSprite("shadow", 40); shi.type = Image.Type.Sliced; shi.raycastTarget = false;
            sh.SetAsFirstSibling();
            var img = Panel(rt, theme, alpha, role);
            img.sprite = HudSprite("rounded", 24); img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = 1.6f;
            return img;
        }

        static Image Icon(Transform parent, string name, string sprite, float size, Color c)
        {
            var rt = Node(name, parent);
            rt.sizeDelta = new Vector2(size, size);
            var img = AddImage(rt.gameObject, c); img.sprite = HudSprite(sprite); img.preserveAspect = true; img.raycastTarget = false;
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

        static Image Arc(Transform parent, string name, string sprite, float size, Color c, float fill, float rotationZ)
        {
            var rt = Node(name, parent);
            Place(rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(size, size), Vector2.zero);
            rt.localRotation = Quaternion.Euler(0, 0, rotationZ);
            var img = AddImage(rt.gameObject, c); img.sprite = HudSprite(sprite); img.raycastTarget = false;
            img.type = Image.Type.Filled; img.fillMethod = Image.FillMethod.Radial360; img.fillOrigin = (int)Image.Origin360.Top; img.fillClockwise = true;
            img.fillAmount = fill;
            return img;
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
            BuildHint(v, root, theme);
            BuildRemarks(v, root, theme);
            BuildCards(v, root, theme);
            BuildCluster(v, root, theme);
            BuildLamps(v, root, theme);
            BuildMinimap(v, root, theme);
            BuildDebrief(canvas.transform, theme);
            SavePrefab(canvas, "HUD");
        }

        static void BuildHint(DriveHudView v, Transform root, UITheme theme)
        {
            var hint = Node("Hint", root);
            Place(hint, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(980, 0), new Vector2(0, -32));
            v.hintGroup = hint.gameObject.AddComponent<CanvasGroup>(); v.hintGroup.blocksRaycasts = false;
            RoundPanel(hint, theme);
            var h = Layout<HorizontalLayoutGroup>(hint, 18, 30, 14, 14, 18); h.childAlignment = TextAnchor.MiddleLeft;
            hint.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            // Аватар инструктора: цветное кольцо по типу подсказки.
            var av = Node("Avatar", hint); LE(av, 64, 64);
            v.hintAvatarRing = AddImage(av.gameObject, theme.accent); v.hintAvatarRing.sprite = Circle(); v.hintAvatarRing.raycastTarget = false;
            var inner = Node("Inner", av); Stretch(inner, 4, 4, 4, 4);
            AddThemedImage(inner.gameObject, ThemeRole.BgCard, theme).sprite = Circle();
            var ic = Icon(av, "Icon", "icon_instructor", 40, theme.text); Place((RectTransform)ic.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(40, 40), new Vector2(0, -2));
            var col = Node("Text", hint); Layout<VerticalLayoutGroup>(col, 0, 0, 0, 0, 2); LE(col, -1, -1, 1);
            v.hintTitle = Label(col, "Title", "ИНСТРУКТОР", 15, ThemeRole.Accent, theme, TextAlignmentOptions.Left, FontWeight.Bold, themed: false);
            v.hintTitle.characterSpacing = 8;
            v.hintText = Label(col, "Hint", "Подсказка инструктора", 25, ThemeRole.Text, theme, TextAlignmentOptions.Left, FontWeight.Medium);
            v.hintText.textWrappingMode = TextWrappingModes.Normal;
            v.hintWaiting = Label(hint, "Waiting", "", 16, ThemeRole.Muted, theme, TextAlignmentOptions.Right, FontWeight.Bold, true, mono: true);
            v.hintWaiting.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Place(v.hintWaiting.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(60, 24), new Vector2(-16, -10));
            var line = Node("Accent", hint); line.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            line.anchorMin = new Vector2(0, 0); line.anchorMax = new Vector2(1, 0); line.pivot = new Vector2(0.5f, 0);
            line.offsetMin = new Vector2(100, 0); line.offsetMax = new Vector2(-30, 3);
            v.hintAccent = AddImage(line.gameObject, theme.accent); v.hintAccent.raycastTarget = false;
            v.hintPanel = hint;
        }

        static void BuildRemarks(DriveHudView v, Transform root, UITheme theme)
        {
            var rem = Node("Remarks", root);
            Place(rem, new Vector2(1, 1), new Vector2(1, 1), new Vector2(236, 76), new Vector2(-36, -32));
            RoundPanel(rem, theme);
            v.remarksIcon = Icon(rem, "Icon", "icon_warning", 34, theme.text2);
            Place((RectTransform)v.remarksIcon.transform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(34, 34), new Vector2(20, 0));
            v.remarksCount = Label(rem, "Count", "0", 34, ThemeRole.Text, theme, TextAlignmentOptions.Left, FontWeight.Bold, themed: false, mono: true);
            Place(v.remarksCount.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(60, 40), new Vector2(66, 9));
            var lbl = Label(rem, "Label", "замечаний", 16, ThemeRole.Text2, theme, TextAlignmentOptions.Left, FontWeight.Medium);
            v.remarksLabel = lbl;
            Place(lbl.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(110, 22), new Vector2(112, 11));
            v.remarksNote = Label(rem, "Note", "баллы — после T37", 13, ThemeRole.Muted, theme);
            Place(v.remarksNote.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(160, 18), new Vector2(66, -18));
            v.remarksPanel = rem.gameObject;
        }

        static void BuildCards(DriveHudView v, Transform root, UITheme theme)
        {
            var cards = Node("Cards", root);
            Place(cards, new Vector2(1, 1), new Vector2(1, 1), new Vector2(440, 260), new Vector2(-36, -124));
            var cv = Layout<VerticalLayoutGroup>(cards, 0, 0, 0, 0, 12); cv.childForceExpandWidth = true;
            v.cardsRoot = cards;
            // Внешний слой держит место в столбце (layout), тело — выезжает справа (не под layout).
            var card = Node("CardTemplate", cards); LE(card, 440, 118, -1, 0);
            var dc = card.gameObject.AddComponent<DriveHudCard>();
            dc.group = card.gameObject.AddComponent<CanvasGroup>(); dc.group.blocksRaycasts = false;
            var body = Node("Body", card); Stretch(body); dc.body = body;
            RoundPanel(body, theme, 0.92f);
            var bar = Node("Bar", body); Place(bar, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(4, 70), new Vector2(8, 0));
            dc.bar = AddImage(bar.gameObject, theme.red); dc.bar.raycastTarget = false;
            dc.icon = Icon(body, "Icon", "icon_warning", 30, theme.red);
            Place((RectTransform)dc.icon.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, 30), new Vector2(22, -16));
            dc.title = Label(body, "Title", "Нарушение", 20, ThemeRole.Text, theme, TextAlignmentOptions.Left, FontWeight.Bold);
            Place(dc.title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(362, 28), new Vector2(64, -14));
            dc.reference = Label(body, "Reference", "PDD", 13, ThemeRole.Muted, theme, TextAlignmentOptions.Left, FontWeight.Regular, true, mono: true);
            Place(dc.reference.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(362, 18), new Vector2(64, -42));
            dc.reference.overflowMode = TextOverflowModes.Ellipsis; dc.title.overflowMode = TextOverflowModes.Ellipsis;
            dc.advice = Label(body, "Advice", "Как правильно", 16, ThemeRole.Text2, theme);
            dc.advice.textWrappingMode = TextWrappingModes.Normal;
            Place(dc.advice.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(362, 50), new Vector2(64, -62));
            dc.advice.overflowMode = TextOverflowModes.Ellipsis;
            v.cardTemplate = card.gameObject;
            card.gameObject.SetActive(false);
        }

        // Круглый прибор: дуга скорости 0–200 км/ч, внешняя тонкая дуга оборотов с красной зоной, передача в нижнем зазоре.
        static void BuildCluster(DriveHudView v, Transform root, UITheme theme)
        {
            const float D = 330;
            var dial = Node("Instruments", root);
            Place(dial, new Vector2(1, 0), new Vector2(1, 0), new Vector2(D, D), new Vector2(-40, 28));
            v.instruments = dial.gameObject;
            var sh = Node("Shadow", dial); Place(sh, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(D + 14, D + 14), new Vector2(0, -4));
            var shi = AddImage(sh.gameObject, new Color(0, 0, 0, 0.35f)); shi.sprite = HudSprite("vignette"); shi.raycastTarget = false;
            var bg = Node("Face", dial); Stretch(bg);
            var face = AddThemedImage(bg.gameObject, ThemeRole.BgDark, theme); face.sprite = HudSprite("dial_bg"); face.raycastTarget = false;
            face.GetComponent<ThemedGraphic>().alpha = 0.9f;
            float half = FillOf(1f);
            // Обороты — внешнее тонкое кольцо.
            var rpm = Node("Rpm", dial); Stretch(rpm); v.rpmGroup = rpm.gameObject;
            Arc(rpm, "Track", "ring_thin", D - 6, new Color(1, 1, 1, 0.08f), half, 120);
            v.rpmRedZone = Arc(rpm, "RedZone", "ring_thin", D - 6, theme.red, 0.1f, -80);
            v.rpmArc = Arc(rpm, "Arc", "ring_thin", D - 6, theme.text, 0.2f, 120);
            // Скорость — толстое кольцо внутри.
            Arc(dial, "SpeedTrack", "ring_thick", D * 0.86f, new Color(1, 1, 1, 0.07f), half, 120);
            v.speedArc = Arc(dial, "SpeedArc", "ring_thick", D * 0.86f, theme.accent, 0.2f, 120);
            var ticks = Node("Ticks", dial); Stretch(ticks);
            v.dialTicks = AddImage(ticks.gameObject, theme.text2); v.dialTicks.sprite = HudSprite("dial_ticks"); v.dialTicks.raycastTarget = false;
            int[] marks = { 0, 40, 80, 120, 160, 200 };
            v.tickLabels = new TMP_Text[marks.Length];
            for (int i = 0; i < marks.Length; i++)
            {
                float a = Mathf.Deg2Rad * (-DriveHudView.ArcDegrees / 2f + DriveHudView.ArcDegrees * marks[i] / DriveHudView.SpeedScaleKph);
                float r = D * 0.25f;
                var l = Label(dial, "Tick" + marks[i], marks[i].ToString(), 15, ThemeRole.Muted, theme, TextAlignmentOptions.Center, FontWeight.Medium, themed: false, mono: true);
                Place(l.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(44, 20), new Vector2(r * Mathf.Sin(a), r * Mathf.Cos(a)));
                v.tickLabels[i] = l;
            }
            v.speedText = Label(dial, "Speed", "0", 70, ThemeRole.Text, theme, TextAlignmentOptions.Center, FontWeight.Bold, themed: false, mono: true);
            Place(v.speedText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(200, 80), new Vector2(0, 14));
            v.speedUnit = Label(dial, "Unit", "км/ч", 16, ThemeRole.Muted, theme, TextAlignmentOptions.Center, FontWeight.Medium);
            Place(v.speedUnit.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(100, 22), new Vector2(0, -30));
            v.selectorText = Label(dial, "Selector", "", 15, ThemeRole.Muted, theme, TextAlignmentOptions.Center, FontWeight.Bold, themed: false, mono: true);
            Place(v.selectorText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(140, 22), new Vector2(0, -58));
            v.selectorText.richText = true;
            var gb = Node("Gear", dial); Place(gb, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(62, 62), new Vector2(0, -D * 0.38f));
            var gbi = AddThemedImage(gb.gameObject, ThemeRole.BgCard, theme); gbi.sprite = Circle(); gbi.raycastTarget = false;
            v.gearText = Label(gb, "Text", "N", 34, ThemeRole.Text, theme, TextAlignmentOptions.Center, FontWeight.Bold, themed: false, mono: true);
            Stretch(v.gearText.rectTransform);
        }

        static float FillOf(float frac) => frac * DriveHudView.ArcDegrees / 360f;

        static void BuildLamps(DriveHudView v, Transform root, UITheme theme)
        {
            var row = Node("Telltales", root);
            Place(row, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 58), new Vector2(0, 30));
            RoundPanel(row, theme, 0.82f);
            var h = Layout<HorizontalLayoutGroup>(row, 24, 24, 12, 12, 22); h.childAlignment = TextAnchor.MiddleCenter; h.childControlWidth = false; h.childControlHeight = false;
            row.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            Image L(string name, string sprite, bool flip = false)
            {
                var img = Icon(row, name, sprite, 34, theme.muted);
                if (flip) img.transform.localRotation = Quaternion.Euler(0, 0, 180);
                LE(img, 34, 34);
                return img;
            }
            v.lampTurnLeft = L("TurnLeft", "icon_turn", true);
            v.lampLowBeam = L("LowBeam", "icon_lowbeam");
            v.lampHighBeam = L("HighBeam", "icon_highbeam");
            v.lampHandbrake = L("Handbrake", "icon_handbrake");
            v.lampSeatbelt = L("Seatbelt", "icon_seatbelt");
            v.lampBattery = L("Battery", "icon_battery");
            v.lampTurnRight = L("TurnRight", "icon_turn");
            v.telltalesRow = row.gameObject;
        }

        static void BuildMinimap(DriveHudView v, Transform root, UITheme theme)
        {
            const float D = 250, inset = 8;
            var map = Node("Minimap", root);
            Place(map, new Vector2(0, 0), new Vector2(0, 0), new Vector2(D, D), new Vector2(40, 28));
            var sh = Node("Shadow", map); Place(sh, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(D + 12, D + 12), new Vector2(0, -4));
            var shi = AddImage(sh.gameObject, new Color(0, 0, 0, 0.35f)); shi.sprite = HudSprite("vignette"); shi.raycastTarget = false;
            var bezel = Node("Bezel", map); Stretch(bezel);
            var bz = AddThemedImage(bezel.gameObject, ThemeRole.BgPanel, theme); bz.sprite = Circle(); bz.raycastTarget = false;
            var mask = Node("Mask", map); Stretch(mask, inset, inset, inset, inset);
            var mi = AddImage(mask.gameObject, Color.white); mi.sprite = Circle(); mi.raycastTarget = false;
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var img = Node("Map", mask);
            float big = (D - 2 * inset) * 1.42f;   // запас на поворот: углы квадрата не видны в круге
            Place(img, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(big, big), Vector2.zero);
            v.minimapImage = img.gameObject.AddComponent<RawImage>(); v.minimapImage.raycastTarget = false;
            v.minimapImage.color = new Color(0.78f, 0.82f, 0.82f);   // приглушаем яркую траву
            var vig = Node("Vignette", mask); Stretch(vig);
            var vi = AddImage(vig.gameObject, new Color(0, 0, 0, 0.7f)); vi.sprite = HudSprite("vignette"); vi.raycastTarget = false;
            var car = Icon(map, "Car", "icon_turn", 26, theme.accent);
            Place((RectTransform)car.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(26, 26), Vector2.zero);
            car.transform.localRotation = Quaternion.Euler(0, 0, 90);   // стрелка «вперёд» = вверх
            var north = Node("North", map); Stretch(north);
            var nb = Node("Badge", north); Place(nb, new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(26, 26), new Vector2(0, -inset - 2));
            var nbi = AddThemedImage(nb.gameObject, ThemeRole.BgDark, theme); nbi.sprite = Circle(); nbi.raycastTarget = false;
            var n = Label(nb, "Text", "С", 15, ThemeRole.Text, theme, TextAlignmentOptions.Center, FontWeight.Bold);
            Stretch(n.rectTransform);
            v.minimapNorth = north;
            var scp = Node("ScaleBadge", map); Place(scp, new Vector2(0.5f, 0), new Vector2(0.5f, 0.5f), new Vector2(64, 22), new Vector2(0, inset + 14));
            var scpi = Panel(scp, theme, 0.9f, ThemeRole.BgDark); scpi.sprite = HudSprite("rounded", 24); scpi.type = Image.Type.Sliced; scpi.pixelsPerUnitMultiplier = 3f;
            var sc = Label(scp, "Scale", "100 м", 13, ThemeRole.Text2, theme, TextAlignmentOptions.Center, FontWeight.Medium, true, mono: true);
            Stretch(sc.rectTransform);
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
