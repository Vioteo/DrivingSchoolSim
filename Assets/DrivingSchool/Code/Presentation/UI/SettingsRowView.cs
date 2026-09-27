using System;
using DrivingSchool.Settings;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DrivingSchool.Presentation.UI
{
    /// <summary>
    /// Строка экрана настроек: подпись, бейдж («скоро», «нет руля», «после поездки»), точка «изменено» и один из
    /// редакторов значения — список «◀ значение ▶», ползунок, переключатель или кнопка действия. Шаблон собирает UIBuilder.
    /// </summary>
    public sealed class SettingsRowView : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler
    {
        public Image background, focusBar;
        public TMP_Text label;
        public GameObject badge; public Image badgeBg; public TMP_Text badgeText;
        public Image changedDot;

        [Header("Список")] public GameObject cycleRoot; public Button prevButton, nextButton; public Image prevIcon, nextIcon, cycleBg; public TMP_Text cycleText;
        [Header("Ползунок")] public GameObject sliderRoot; public Slider slider; public Image sliderTrack, sliderFill, sliderKnob; public TMP_Text sliderText;
        [Header("Переключатель")] public GameObject switchRoot; public Button switchButton; public Image switchTrack, switchKnob; public TMP_Text switchText;
        [Header("Действие")] public GameObject actionRoot; public Button actionButton; public Image actionBg; public TMP_Text actionText;

        public SettingItem Item { get; private set; }
        public int Index { get; private set; }

        public event Action<SettingsRowView> Hovered, Activated;
        public event Action<SettingsRowView, int> Stepped, SliderSet;

        bool wired, refreshing;

        public void Bind(SettingItem item, int index)
        {
            Item = item; Index = index;
            name = "Row_" + item.Key;
            label.text = item.Label;
            cycleRoot.SetActive(item.Kind == SettingKind.Cycle);
            sliderRoot.SetActive(item.Kind == SettingKind.Slider);
            switchRoot.SetActive(item.Kind == SettingKind.Switch);
            actionRoot.SetActive(item.Kind == SettingKind.Action);
            if (item.Kind == SettingKind.Slider)
            {
                slider.wholeNumbers = true;
                slider.minValue = 0; slider.maxValue = Mathf.Max(1, item.Count - 1);   // индекс шага
            }
            if (item.Kind == SettingKind.Action) actionText.text = item.ActionText;
            if (wired) return;
            wired = true;
            prevButton.onClick.AddListener(() => Stepped?.Invoke(this, -1));
            nextButton.onClick.AddListener(() => Stepped?.Invoke(this, 1));
            switchButton.onClick.AddListener(() => Activated?.Invoke(this));
            actionButton.onClick.AddListener(() => Activated?.Invoke(this));
            slider.onValueChanged.AddListener(v => { if (!refreshing) SliderSet?.Invoke(this, Item.Min + Mathf.RoundToInt(v) * Mathf.Max(1, Item.Step)); });
        }

        public void OnPointerEnter(PointerEventData e) => Hovered?.Invoke(this);

        public void OnPointerClick(PointerEventData e)
        {
            // Клик по строке (не по стрелкам/ползунку): список — следующее значение, переключатель — сменить.
            if (e.button != PointerEventData.InputButton.Left || Item == null) return;
            if (Item.Kind == SettingKind.Cycle) Stepped?.Invoke(this, 1);
            else if (Item.Kind == SettingKind.Switch) Activated?.Invoke(this);
        }

        public void Refresh(int value, bool focused, bool changed, SettingAvailability availability, UITheme t)
        {
            if (t == null || Item == null) return;
            refreshing = true;
            bool editable = SettingsSession.CanChange(availability);
            bool dim = availability != SettingAvailability.Enabled;
            Color text = dim ? t.muted : t.text, valueColor = !editable ? t.muted : focused ? t.accent : t.text;

            background.color = focused ? t.accentSoft : t.bgCard;
            focusBar.enabled = focused; focusBar.color = t.accent;
            label.color = focused && !dim ? t.accent : text;
            changedDot.enabled = changed; changedDot.color = t.accent;

            string badgeLabel = BadgeText(availability);
            badge.SetActive(badgeLabel != null);
            if (badgeLabel != null) { badgeText.text = badgeLabel; badgeText.color = t.muted; badgeBg.color = t.bgRowHover; }

            switch (Item.Kind)
            {
                case SettingKind.Cycle:
                    cycleText.text = Item.Format(value); cycleText.color = valueColor;
                    cycleBg.color = focused ? t.bgPanel : t.bgRowHover;
                    prevIcon.color = nextIcon.color = editable ? (focused ? t.accent : t.text2) : t.muted;
                    prevButton.interactable = nextButton.interactable = editable;
                    break;
                case SettingKind.Slider:
                    slider.SetValueWithoutNotify((value - Item.Min) / (float)Mathf.Max(1, Item.Step));
                    slider.interactable = editable;
                    sliderTrack.color = t.bgRowHover;
                    sliderFill.color = editable ? (focused ? t.accent : t.text2) : t.muted;
                    sliderKnob.color = editable ? (focused ? t.accent : t.text) : t.muted;
                    sliderText.text = Item.Format(value); sliderText.color = valueColor;
                    break;
                case SettingKind.Switch:
                    bool on = value != 0;
                    switchTrack.color = on && editable ? (focused ? t.accent : t.text2) : t.bgRowHover;
                    switchKnob.color = on && editable ? t.onAccent : editable ? t.text : t.muted;
                    var kr = (RectTransform)switchKnob.transform;
                    kr.anchoredPosition = new Vector2(on ? 15f : -15f, 0f);
                    switchText.text = Item.Format(value); switchText.color = valueColor;
                    switchButton.interactable = editable;
                    break;
                case SettingKind.Action:
                    actionBg.color = focused && editable ? t.accent : t.bgRowHover;
                    actionText.color = !editable ? t.muted : focused ? t.onAccent : t.text;
                    actionButton.interactable = editable;
                    break;
            }
            refreshing = false;
        }

        public static string BadgeText(SettingAvailability a)
        {
            switch (a)
            {
                case SettingAvailability.NoWheel: return "нет руля";
                case SettingAvailability.AfterDrive: return "после поездки";
                case SettingAvailability.KeyboardOnly: return "только клавиатура";
                case SettingAvailability.DependsOff: return "при V-Sync не действует";
                case SettingAvailability.Stub: return "скоро";
                default: return null;
            }
        }
    }
}
