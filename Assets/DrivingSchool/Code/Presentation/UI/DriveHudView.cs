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
    /// приборы снизу (скорость, схема КПП, обороты, контрольные лампы), мини-карта слева внизу.
    /// Навигатора нет: маршрутов пока нет (roadgraph v2), и стрелку без данных не рисуем.
    /// </summary>
    public sealed class DriveHudView : MonoBehaviour
    {
        [Header("Подсказка")] public GameObject hintPanel; public Image hintBar; public TMP_Text hintText, hintWaiting;
        [Header("Замечания")] public GameObject remarksPanel; public TMP_Text remarksCount, remarksNote;
        [Header("Карточки")] public RectTransform cardsRoot; public GameObject cardTemplate;
        [Header("Приборы")] public GameObject instruments, telltalesRow, rpmRow; public TMP_Text speedText, speedUnit;
        public Image rpmFill; public Image indicatorLeft, indicatorRight;
        public Image[] telltaleBgs = new Image[5]; public TMP_Text[] telltaleTexts = new TMP_Text[5];   // ближний, дальний, ручник, ремень, двигатель
        [Header("КПП")] public GameObject manualBox, autoBox; public TMP_Text[] gearLabels = new TMP_Text[8];   // 1..6, R, N-точка
        public Image neutralDot; public TMP_Text[] selectorLabels = new TMP_Text[4];
        [Header("Мини-карта")] public GameObject minimapPanel; public RawImage minimapImage; public RectTransform minimapArrow;
        public UITheme defaultTheme;

        public const float CardSeconds = 7f;
        const int MaxCards = 2;

        sealed class Card { public GameObject go; public float until; public bool severe; public Image bar; public Image bg; }
        readonly List<Card> cards = new List<Card>();

        UITheme Theme => UIThemeState.Current != null ? UIThemeState.Current : defaultTheme;

        void Awake() { if (cardTemplate != null) cardTemplate.SetActive(false); }

        /// <summary>Карточка нарушения: живёт 7 с, на экране не больше двух (старшая уходит).</summary>
        public void ShowCard(string title, string reference, string advice, bool severe)
        {
            if (cardTemplate == null) return;
            while (cards.Count >= MaxCards) { Kill(cards[0].go); cards.RemoveAt(0); }
            var go = Instantiate(cardTemplate, cardsRoot);
            go.SetActive(true);
            var texts = go.GetComponentsInChildren<TMP_Text>(true);
            texts[0].text = title; texts[1].text = reference; texts[2].text = advice;
            var images = go.GetComponentsInChildren<Image>(true);
            cards.Add(new Card { go = go, until = Time.unscaledTime + CardSeconds, severe = severe, bg = images[0], bar = images[1] });
        }

        public int CardCount => cards.Count;

        static void Kill(GameObject go) { if (Application.isPlaying) Destroy(go); else DestroyImmediate(go); }

        public void Render(HudModel m)
        {
            var t = Theme; if (t == null || m == null) return;
            for (int i = cards.Count - 1; i >= 0; i--)
                if (Time.unscaledTime > cards[i].until) { Kill(cards[i].go); cards.RemoveAt(i); }
            foreach (var c in cards) { c.bar.color = c.severe ? t.red : t.accent; c.bg.color = t.bgPanel; }

            // Подсказка: показывается во всех режимах HUD (её отключают в настройках инструктора).
            bool hint = !string.IsNullOrEmpty(m.hintText);
            hintPanel.SetActive(hint);
            if (hint)
            {
                hintText.text = m.hintText;
                hintBar.color = m.hintKind <= 1 ? t.red : m.hintKind == 2 ? t.accent : t.info;
                hintWaiting.text = m.hintWaiting > 0 ? $"ещё {m.hintWaiting} в очереди" : "";
            }

            // Замечания — всегда: это обратная связь обучения, а не украшение (§1).
            remarksCount.text = m.remarks.ToString();
            remarksCount.color = m.severe > 0 ? t.red : m.remarks > 0 ? t.accent : t.text;

            // Приборы: полный — всё; минимальный — скорость и КПП; выкл. — нет; из салона — панель машины.
            bool showInstruments = m.hudMode < 2 && !m.cockpit;
            instruments.SetActive(showInstruments);
            if (showInstruments)
            {
                bool full = m.hudMode == 0;
                telltalesRow.SetActive(full); rpmRow.SetActive(full);
                speedText.text = Mathf.RoundToInt(Mathf.Abs(m.speedKph)).ToString();
                speedText.color = m.overLimit ? t.red : t.text;
                if (full) RenderTelltales(m, t);
                RenderGearbox(m, t);
            }
            bool map = m.hudMode == 0 && m.minimapAvailable && m.minimap != null;
            minimapPanel.SetActive(map);
            if (map)
            {
                minimapImage.texture = m.minimap;
                minimapArrow.localRotation = Quaternion.Euler(0, 0, -m.carYawDeg);   // север вверху, стрелка = курс
            }
        }

        void RenderTelltales(HudModel m, UITheme t)
        {
            float rpm01 = m.redlineRpm > 0 ? Mathf.Clamp01(m.rpm / (m.redlineRpm * 1.15f)) : 0f;
            rpmFill.fillAmount = rpm01;
            rpmFill.color = m.rpm >= m.redlineRpm ? t.red : t.text2;
            indicatorLeft.color = m.leftIndicator && m.indicatorLamp ? t.green : t.bgRowHover;
            indicatorRight.color = m.rightIndicator && m.indicatorLamp ? t.green : t.bgRowHover;
            // Цвет лампы — по смыслу, как на реальной панели: зелёный — включено, синий — дальний, красный — внимание.
            Lamp(0, m.lowBeam, t.green, t);
            Lamp(1, m.highBeam, t.info, t);
            Lamp(2, m.handbrake, t.red, t);
            Lamp(3, !m.seatbelt, t.red, t);
            Lamp(4, !m.engineRunning, m.stalled ? t.red : t.text2, t);
        }

        void Lamp(int i, bool on, Color onColor, UITheme t)
        {
            telltaleBgs[i].color = on ? onColor : t.bgCard;
            telltaleTexts[i].color = on ? new Color(0.06f, 0.06f, 0.07f) : t.muted;   // тёмный текст на горящей лампе
        }

        void RenderGearbox(HudModel m, UITheme t)
        {
            manualBox.SetActive(m.manual); autoBox.SetActive(!m.manual);
            if (m.manual)
            {
                for (int i = 0; i < 6; i++)
                {
                    bool exists = i < m.gearCount;
                    gearLabels[i].gameObject.SetActive(exists);
                    gearLabels[i].color = m.gear == i + 1 ? t.accent : t.muted;
                    gearLabels[i].fontStyle = m.gear == i + 1 ? FontStyles.Bold : FontStyles.Normal;
                }
                gearLabels[6].color = m.gear < 0 ? t.red : t.muted;   // R — красной цифрой
                neutralDot.color = m.gear == 0 ? t.accent : t.bgRowHover;
            }
            else
            {
                for (int i = 0; i < 4; i++)
                    selectorLabels[i].color = m.selector == i ? (i == 1 ? t.red : t.accent) : t.muted;
            }
        }
    }
}
