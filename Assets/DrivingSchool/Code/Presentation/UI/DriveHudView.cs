using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DrivingSchool.Presentation.UI
{
    /// <summary>Снимок для HUD на кадр. Заполняет DriveSession (Presentation) — UI про симуляцию не знает.</summary>
    public sealed class HudModel
    {
        public int hudMode;                 // gameplay.hud: 0 полный, 1 минимальный, 2 выкл.
        public bool cockpit;                // вид из салона: приборы есть на панели машины
        public float speedKph; public bool overLimit;
        public bool manual = true; public int gear; public int gearCount = 6; public int selector;   // selector: 0 P, 1 R, 2 N, 3 D
        public float rpm, redlineRpm = 6500f;
        public bool leftIndicator, rightIndicator, indicatorLamp, lowBeam, highBeam, handbrake, seatbelt, engineRunning, stalled;
        public string hintText; public int hintKind = -1; public int hintWaiting;   // hintKind = DrivingSchool.Learning.HintKind
        public int remarks, severe;
        public Texture minimap; public float carYawDeg; public bool minimapAvailable;
    }

    /// <summary>
    /// HUD поездки (docs/ui-drive.md §1–§3): подсказка инструктора сверху, замечания и карточки нарушений справа,
    /// круглый прибор справа внизу (скорость по дуге, обороты с красной зоной, передача), полоса контрольных ламп
    /// снизу по центру, круглая мини-карта по ходу машины слева внизу. Навигатора нет: маршрутов пока нет.
    /// Появление подсказок и карточек — плавное (в реальном времени, пауза не мешает).
    /// </summary>
    public sealed class DriveHudView : MonoBehaviour
    {
        [Header("Подсказка")] public CanvasGroup hintGroup; public RectTransform hintPanel; public Image hintAccent, hintAvatarRing;
        public TMP_Text hintTitle, hintText, hintWaiting;
        [Header("Замечания")] public GameObject remarksPanel; public Image remarksIcon; public TMP_Text remarksCount, remarksNote, remarksLabel;
        [Header("Карточки")] public RectTransform cardsRoot; public GameObject cardTemplate;
        [Header("Прибор")] public GameObject instruments, rpmGroup; public Image speedArc, rpmArc, rpmRedZone, dialTicks;
        public TMP_Text speedText, speedUnit, gearText, selectorText; public TMP_Text[] tickLabels = new TMP_Text[0];
        [Header("Лампы")] public GameObject telltalesRow;
        public Image lampTurnLeft, lampTurnRight, lampLowBeam, lampHighBeam, lampHandbrake, lampSeatbelt, lampBattery;
        [Header("Мини-карта")] public GameObject minimapPanel; public RawImage minimapImage; public RectTransform minimapNorth;
        public UITheme defaultTheme;

        public const float CardSeconds = 7f;
        public const float SpeedScaleKph = 200f, ArcDegrees = 240f;
        const int MaxCards = 2;
        const float FadeIn = 0.25f, FadeOut = 0.4f;

        sealed class Card { public GameObject go; public CanvasGroup group; public RectTransform body; public float born, until; public bool severe; public Image bar, icon; }
        readonly List<Card> cards = new List<Card>();
        string shownHint; float hintBorn = -10f; float hintBaseY = float.NaN;

        UITheme Theme => UIThemeState.Current != null ? UIThemeState.Current : defaultTheme;

        void Awake() { if (cardTemplate != null) cardTemplate.SetActive(false); }

        /// <summary>Карточка нарушения: выезжает справа, живёт 7 с, на экране не больше двух (старшая уходит).</summary>
        public void ShowCard(string title, string reference, string advice, bool severe)
        {
            if (cardTemplate == null) return;
            while (cards.Count >= MaxCards) { Kill(cards[0].go); cards.RemoveAt(0); }
            var go = Instantiate(cardTemplate, cardsRoot);
            go.SetActive(true);
            var v = go.GetComponent<DriveHudCard>();
            v.title.text = title; v.reference.text = reference; v.advice.text = advice;
            float now = Time.unscaledTime;
            cards.Add(new Card { go = go, group = v.group, body = v.body, born = now, until = now + CardSeconds, severe = severe, bar = v.bar, icon = v.icon });
        }

        public int CardCount => cards.Count;
        static void Kill(GameObject go) { if (Application.isPlaying) Destroy(go); else DestroyImmediate(go); }

        public void Render(HudModel m)
        {
            var t = Theme; if (t == null || m == null) return;
            float now = Time.unscaledTime;
            RenderCards(t, now);
            RenderHint(m, t, now);

            remarksCount.text = m.remarks.ToString();
            if (remarksLabel != null) remarksLabel.text = RemarksWord(m.remarks);
            Color remarkColor = m.severe > 0 ? t.red : m.remarks > 0 ? t.accent : t.text2;
            remarksCount.color = remarkColor; remarksIcon.color = remarkColor;

            // Прибор: полный — всё; минимальный — скорость и передача; выкл. — нет; из салона — панель машины.
            bool showInstruments = m.hudMode < 2 && !m.cockpit;
            instruments.SetActive(showInstruments);
            telltalesRow.SetActive(showInstruments && m.hudMode == 0);
            if (showInstruments)
            {
                bool full = m.hudMode == 0;
                rpmGroup.SetActive(full);
                float kph = Mathf.Abs(m.speedKph);
                speedText.text = Mathf.RoundToInt(kph).ToString();
                speedText.color = m.overLimit ? t.red : t.text;
                speedArc.fillAmount = Mathf.Clamp01(kph / SpeedScaleKph) * ArcDegrees / 360f;
                speedArc.color = m.overLimit ? t.red : t.accent;
                dialTicks.color = t.text2;
                foreach (var l in tickLabels) l.color = t.muted;
                if (full) RenderRpm(m, t);
                RenderGear(m, t);
                if (full) RenderLamps(m, t);
            }
            bool map = m.hudMode == 0 && m.minimapAvailable && m.minimap != null;
            minimapPanel.SetActive(map);
            if (map)
            {
                // Карта по ходу машины: картинку «север вверху» поворачиваем на курс, стрелка машины всегда вверх.
                minimapImage.texture = m.minimap;
                minimapImage.rectTransform.localRotation = Quaternion.Euler(0, 0, m.carYawDeg);
                minimapNorth.localRotation = Quaternion.Euler(0, 0, m.carYawDeg);
            }
        }

        /// <summary>1 замечание, 2–4 замечания, 5–20 замечаний, 21 замечание…</summary>
        public static string RemarksWord(int n)
        {
            int a = n % 100, b = n % 10;
            if (a >= 11 && a <= 14) return "замечаний";
            return b == 1 ? "замечание" : b >= 2 && b <= 4 ? "замечания" : "замечаний";
        }

        public static float RpmScale(float redline) => Mathf.Max(7000f, Mathf.Ceil(redline * 1.2f / 1000f) * 1000f);

        void RenderRpm(HudModel m, UITheme t)
        {
            float scale = RpmScale(m.redlineRpm);
            rpmArc.fillAmount = Mathf.Clamp01(m.rpm / scale) * ArcDegrees / 360f;
            rpmArc.color = m.rpm >= m.redlineRpm ? t.red : t.text;
            float red = Mathf.Clamp01(m.redlineRpm / scale);
            rpmRedZone.rectTransform.localRotation = Quaternion.Euler(0, 0, ArcDegrees / 2f - ArcDegrees * red);
            rpmRedZone.fillAmount = (1f - red) * ArcDegrees / 360f;
            rpmRedZone.color = new Color(t.red.r, t.red.g, t.red.b, 0.55f);
        }

        void RenderGear(HudModel m, UITheme t)
        {
            if (m.manual)
            {
                gearText.text = m.gear > 0 ? m.gear.ToString() : m.gear < 0 ? "R" : "N";
                gearText.color = m.gear < 0 ? t.red : m.gear == 0 ? t.text2 : t.accent;
                selectorText.text = "";
            }
            else
            {
                string[] sel = { "P", "R", "N", "D" };
                int s = Mathf.Clamp(m.selector, 0, 3);
                gearText.text = sel[s];
                gearText.color = s == 1 ? t.red : s == 3 ? t.accent : t.text2;
                string muted = ColorUtility.ToHtmlStringRGB(t.muted), on = ColorUtility.ToHtmlStringRGB(s == 1 ? t.red : t.accent);
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < 4; i++) sb.Append($"<color=#{(i == s ? on : muted)}>{sel[i]}</color>").Append(i < 3 ? " " : "");
                selectorText.text = sb.ToString();
            }
        }

        void RenderLamps(HudModel m, UITheme t)
        {
            // Цвета — как на реальной панели: зелёный — включено, синий — дальний, красный — внимание.
            Color off = new Color(t.muted.r, t.muted.g, t.muted.b, 0.35f);
            lampTurnLeft.color = m.leftIndicator && m.indicatorLamp ? t.green : off;
            lampTurnRight.color = m.rightIndicator && m.indicatorLamp ? t.green : off;
            lampLowBeam.color = m.lowBeam ? t.green : off;
            lampHighBeam.color = m.highBeam ? t.info : off;
            lampHandbrake.color = m.handbrake ? t.red : off;
            lampSeatbelt.color = !m.seatbelt ? t.red : off;
            lampBattery.color = !m.engineRunning ? t.red : off;
        }

        void RenderHint(HudModel m, UITheme t, float now)
        {
            bool hint = !string.IsNullOrEmpty(m.hintText);
            hintPanel.gameObject.SetActive(hint);
            if (!hint) { shownHint = null; return; }
            if (float.IsNaN(hintBaseY)) hintBaseY = hintPanel.anchoredPosition.y;
            if (m.hintText != shownHint) { shownHint = m.hintText; hintBorn = now; }
            float p = Mathf.Clamp01((now - hintBorn) / FadeIn);
            float e = 1f - (1f - p) * (1f - p);
            hintGroup.alpha = e;
            hintPanel.anchoredPosition = new Vector2(hintPanel.anchoredPosition.x, hintBaseY + (1f - e) * 16f);

            Color kind = m.hintKind <= 1 ? t.red : m.hintKind == 2 ? t.accent : t.info;
            string[] titles = { "ОПАСНОСТЬ", "ЗАМЕЧАНИЕ", "ИНСТРУКТОР", "НАВИГАЦИЯ", "УПРАЖНЕНИЕ" };
            hintTitle.text = titles[Mathf.Clamp(m.hintKind, 0, titles.Length - 1)];
            hintTitle.color = kind; hintAccent.color = kind; hintAvatarRing.color = kind;
            hintText.text = m.hintText;
            hintWaiting.text = m.hintWaiting > 0 ? $"+{m.hintWaiting}" : "";
        }

        void RenderCards(UITheme t, float now)
        {
            for (int i = cards.Count - 1; i >= 0; i--)
                if (now > cards[i].until) { Kill(cards[i].go); cards.RemoveAt(i); }
            foreach (var c in cards)
            {
                float pin = Mathf.Clamp01((now - c.born) / FadeIn), pout = Mathf.Clamp01((c.until - now) / FadeOut);
                float e = 1f - (1f - pin) * (1f - pin);
                c.group.alpha = Mathf.Min(e, pout);
                c.body.anchoredPosition = new Vector2((1f - e) * 60f, 0f);
                c.bar.color = c.severe ? t.red : t.accent;
                c.icon.color = c.severe ? t.red : t.accent;
            }
        }
    }
}
