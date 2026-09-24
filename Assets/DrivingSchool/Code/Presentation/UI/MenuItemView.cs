using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DrivingSchool.Presentation.UI
{
    /// <summary>
    /// Вид пункта меню: фокус с клавиатуры/руля и наведение мышью выглядят одинаково —
    /// полоса акцента слева, мягкий фон, текст цвета акцента. Transition у Selectable = None, цвета берутся из темы.
    /// </summary>
    [RequireComponent(typeof(Selectable))]
    public sealed class MenuItemView : MonoBehaviour, ISelectHandler, IDeselectHandler, IPointerEnterHandler
    {
        public Image background;
        public Image focusBar;
        public TMP_Text label;
        public UITheme fallbackTheme;

        Selectable selectable;
        bool focused;

        public event Action<MenuItemView> Focused;
        public Selectable Selectable => selectable != null ? selectable : selectable = GetComponent<Selectable>();
        public bool IsFocused => focused;

        void OnEnable() { UIThemeState.Changed += OnThemeChanged; Refresh(); }
        void OnDisable() { UIThemeState.Changed -= OnThemeChanged; }
        void OnThemeChanged(UITheme _) { Refresh(); }

        public void OnSelect(BaseEventData eventData) { SetFocused(true); }
        public void OnDeselect(BaseEventData eventData) { SetFocused(false); }

        public void OnPointerEnter(PointerEventData eventData)
        {
            // Наведение мышью переносит фокус: иначе клавиатурный и мышиный фокус расходятся.
            if (Selectable.IsInteractable() && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(gameObject);
        }

        public void SetFocused(bool value)
        {
            focused = value;
            Refresh();
            if (value) Focused?.Invoke(this);
        }

        public void Refresh()
        {
            var theme = UIThemeState.Current != null ? UIThemeState.Current : fallbackTheme;
            if (theme == null) return;
            bool interactable = Selectable.IsInteractable();
            bool on = focused && interactable;
            if (background != null) background.color = on ? theme.accentSoft : theme.bgCard;
            if (focusBar != null) { focusBar.enabled = on; focusBar.color = theme.accent; }
            if (label != null) label.color = !interactable ? theme.muted : on ? theme.accent : theme.text;
        }
    }
}
