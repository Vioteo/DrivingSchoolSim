using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DrivingSchool.Presentation.UI
{
    /// <summary>Данные разбора поездки (docs/ui-drive.md §5). Заполняет DriveSession.</summary>
    public sealed class DebriefModel
    {
        public string title = "Разбор поездки", subtitle = "";
        public List<(string label, string value)> summary = new List<(string, string)>();
        public List<DebriefEvent> events = new List<DebriefEvent>();
    }

    public sealed class DebriefEvent
    {
        public string time, title, advice, reference, place;
        public bool severe;
    }

    /// <summary>
    /// Разбор поездки: слева сводка, в центре хронология замечаний (↑↓), справа выбранное событие —
    /// что произошло, как правильно, пункт ПДД, место. Кнопки «Пройти снова» и «В главное меню» (←→, Enter; Esc — в меню).
    /// </summary>
    public sealed class DebriefView : MonoBehaviour
    {
        public GameObject root;
        public TMP_Text title, subtitle;
        public TMP_Text[] summaryLabels = new TMP_Text[6], summaryValues = new TMP_Text[6];
        public RectTransform list; public GameObject rowTemplate; public TMP_Text emptyText;
        public TMP_Text detailTitle, detailAdvice, detailReference, detailPlace;
        public GameObject detailPanel;
        public Button restartButton, menuButton; public Image restartBg, menuBg; public TMP_Text restartLabel, menuLabel;
        public UITheme defaultTheme;

        public static bool IsOpen { get; private set; }
        public event Action RestartRequested, MenuRequested;

        readonly List<GameObject> rows = new List<GameObject>();
        DebriefModel model;
        int selected, button = 1;

        UITheme Theme => UIThemeState.Current != null ? UIThemeState.Current : defaultTheme;

        void Awake()
        {
            if (rowTemplate != null) rowTemplate.SetActive(false);
            if (root != null) root.SetActive(false);
            if (restartButton != null) restartButton.onClick.AddListener(() => Choose(0));
            if (menuButton != null) menuButton.onClick.AddListener(() => Choose(1));
        }

        void OnDestroy() { if (root != null && root.activeSelf) IsOpen = false; }

        public void Show(DebriefModel m)
        {
            model = m;
            title.text = m.title; subtitle.text = m.subtitle;
            for (int i = 0; i < summaryLabels.Length; i++)
            {
                bool on = i < m.summary.Count;
                summaryLabels[i].gameObject.SetActive(on); summaryValues[i].gameObject.SetActive(on);
                if (on) { summaryLabels[i].text = m.summary[i].label; summaryValues[i].text = m.summary[i].value; }
            }
            foreach (var r in rows) { if (Application.isPlaying) Destroy(r); else DestroyImmediate(r); }
            rows.Clear();
            foreach (var e in m.events)
            {
                var r = Instantiate(rowTemplate, list);
                r.SetActive(true);
                var texts = r.GetComponentsInChildren<TMP_Text>(true);
                texts[0].text = e.time; texts[1].text = e.title;
                int index = rows.Count;
                var btn = r.GetComponent<Button>();
                if (btn != null) btn.onClick.AddListener(() => { selected = index; Refresh(); });
                rows.Add(r);
            }
            emptyText.gameObject.SetActive(m.events.Count == 0);
            detailPanel.SetActive(m.events.Count > 0);
            selected = 0; button = 1;
            root.SetActive(true);
            IsOpen = true;
            Refresh();
        }

        public void Hide() { root.SetActive(false); IsOpen = false; }

        public int Selected => selected;

        void Choose(int b)
        {
            Hide();
            if (b == 0) RestartRequested?.Invoke(); else MenuRequested?.Invoke();
        }

        readonly DirectionRepeater wheelRepeat = new DirectionRepeater();

        void Update()
        {
            if (!IsOpen || model == null) return;
            var kb = Keyboard.current; var pad = Gamepad.current;
            bool up = (kb != null && kb.upArrowKey.wasPressedThisFrame) || (pad != null && pad.dpad.up.wasPressedThisFrame);
            bool down = (kb != null && kb.downArrowKey.wasPressedThisFrame) || (pad != null && pad.dpad.down.wasPressedThisFrame);
            bool left = (kb != null && kb.leftArrowKey.wasPressedThisFrame) || (pad != null && pad.dpad.left.wasPressedThisFrame);
            bool right = (kb != null && kb.rightArrowKey.wasPressedThisFrame) || (pad != null && pad.dpad.right.wasPressedThisFrame);
            bool enter = (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) || (pad != null && pad.buttonSouth.wasPressedThisFrame) || MenuInput.WheelSubmitPressed;
            bool esc = (kb != null && kb.escapeKey.wasPressedThisFrame) || (pad != null && pad.buttonEast.wasPressedThisFrame) || MenuInput.WheelCancelPressed;
            var wheelDir = wheelRepeat.Next(MenuInput.HeldDirection);   // крестовина руля (стрелки уже учтены выше)
            up |= wheelDir.y > 0 && !(kb != null && kb.upArrowKey.isPressed); down |= wheelDir.y < 0 && !(kb != null && kb.downArrowKey.isPressed);
            left |= wheelDir.x < 0 && !(kb != null && kb.leftArrowKey.isPressed); right |= wheelDir.x > 0 && !(kb != null && kb.rightArrowKey.isPressed);
            if (esc) { Choose(1); return; }
            if (enter) { Choose(button); return; }
            if (up && model.events.Count > 0) { selected = (selected - 1 + model.events.Count) % model.events.Count; Refresh(); }
            if (down && model.events.Count > 0) { selected = (selected + 1) % model.events.Count; Refresh(); }
            if (left || right) { button = 1 - button; Refresh(); }
        }

        void Refresh()
        {
            var t = Theme; if (t == null || model == null) return;
            for (int i = 0; i < rows.Count; i++)
            {
                bool on = i == selected;
                var e = model.events[i];
                var imgs = rows[i].GetComponentsInChildren<Image>(true);
                imgs[0].color = on ? t.accentSoft : t.bgCard;
                imgs[1].color = e.severe ? t.red : t.accent;
                var texts = rows[i].GetComponentsInChildren<TMP_Text>(true);
                texts[0].color = t.muted; texts[1].color = on ? t.accent : t.text;
            }
            if (model.events.Count > 0)
            {
                var e = model.events[Mathf.Clamp(selected, 0, model.events.Count - 1)];
                detailTitle.text = e.title; detailTitle.color = e.severe ? t.red : t.text;
                detailAdvice.text = e.advice; detailReference.text = e.reference; detailPlace.text = e.place;
            }
            restartBg.color = button == 0 ? t.accent : t.bgRowHover; restartLabel.color = button == 0 ? t.onAccent : t.text;
            menuBg.color = button == 1 ? t.accent : t.bgRowHover; menuLabel.color = button == 1 ? t.onAccent : t.text;
        }
    }
}
