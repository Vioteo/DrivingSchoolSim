using System;
using System.Collections.Generic;
using System.Linq;
using DrivingSchool.Settings;
using DrivingSchool.Input;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;

namespace DrivingSchool.Presentation.UI
{
    /// <summary>
    /// Экран настроек (T43, docs/ui-settings.md). Один экран для главного меню и паузы. Правки — в черновик
    /// (<see cref="SettingsSession"/>); «Применить» сохраняет в settings.json и применяет; режим экрана — с подтверждением
    /// 15 с; тема видна сразу и без «Применить» откатывается. Клавиатура: ↑↓ пункт, ←→ значение, Q/E вкладка,
    /// R сброс пункта, Enter — переключить/открыть, Esc — назад. Геймпад: крестовина, A, B, Y, бамперы. Мышь — всё.
    /// </summary>
    public sealed class SettingsScreenController : MonoBehaviour
    {
        [Header("Каркас")]
        public GameObject root;
        public TMP_Text crumb, dirtyText;
        public Button[] tabButtons = new Button[0];
        public TMP_Text[] tabLabels = new TMP_Text[0];
        public Image[] tabUnderlines = new Image[0];
        public ScrollRect scroll;
        public RectTransform content;
        public SettingsRowView rowTemplate;
        public TMP_Text groupTemplate;

        [Header("Справка")]
        public GameObject helpStatus; public Image helpStatusDot; public TMP_Text helpStatusText;
        public TMP_Text helpTitle, helpBody, metaKey, metaDefault, metaApply;

        [Header("Нижняя панель: По умолчанию, Отменить, Применить")]
        public Button[] bottomButtons = new Button[3];
        public Image[] bottomBgs = new Image[3];
        public TMP_Text[] bottomLabels = new TMP_Text[3];

        [Header("Диалог")]
        public GameObject dialog;
        public TMP_Text dialogTitle, dialogBody, dialogCountdown;
        public Button[] dialogButtons = new Button[3];
        public Image[] dialogBgs = new Image[3];
        public TMP_Text[] dialogLabels = new TMP_Text[3];

        public GameObject toast; public TMP_Text toastText;
        public UITheme defaultTheme;

        public static bool IsAnyOpen { get; private set; }
        public static int LastClosedFrame { get; private set; } = -1;
        /// <summary>Esc, закрывший настройки, не должен в тот же кадр сработать в меню или паузе.</summary>
        public static bool ClosedThisFrame => LastClosedFrame == Time.frameCount;

        public SettingsSession Session { get; private set; }
        public bool IsOpen => root != null && root.activeSelf;
        public int TabIndex { get; private set; }
        public bool InDrive { get; private set; }
        public bool IsDialogOpen => dialog != null && dialog.activeSelf;
        public SettingItem FocusedItem => zone == Zone.Rows && focus >= 0 && focus < rows.Count ? rows[focus].Item : null;

        enum Zone { Rows, Buttons }
        Zone zone;
        int focus, buttonFocus = 2, dialogFocus, dialogSafe;
        readonly List<SettingsRowView> rows = new List<SettingsRowView>();
        readonly List<GameObject> spawned = new List<GameObject>();
        readonly Action[] dialogActions = new Action[3];
        Action onClosed;
        bool wheel, pendingClose;
        float wheelCheckAt, toastUntil, countdownUntil = -1f;
        (int mode, string res) displayBeforeApply;
        // Повтор при удержании стрелки
        Vector2Int heldDir; float repeatAt;
        bool rebindOpen;
        int rebindWaiting = -1;
        Vector2 rebindScroll;
        bool calibrationOpen;
        WheelProfile calibrationDraft;
        int axisWaiting = -1, gearWaiting = -1;
        readonly Dictionary<string, float> axisBaseline = new Dictionary<string, float>();
        Vector2 calibrationScroll;

        void Awake()
        {
            for (int i = 0; i < tabButtons.Length; i++) { int k = i; tabButtons[i].onClick.AddListener(() => SwitchTab(k)); }
            bottomButtons[0].onClick.AddListener(AskDefaults);
            bottomButtons[1].onClick.AddListener(RevertDraft);
            bottomButtons[2].onClick.AddListener(() => Apply(false));
            for (int i = 0; i < dialogButtons.Length; i++) { int k = i; dialogButtons[i].onClick.AddListener(() => DialogChoose(k)); }
            foreach (var b in tabButtons.Concat(bottomButtons).Concat(dialogButtons)) b.navigation = new Navigation { mode = Navigation.Mode.None };
            if (rowTemplate != null) rowTemplate.gameObject.SetActive(false);
            if (groupTemplate != null) groupTemplate.gameObject.SetActive(false);
            if (root != null) root.SetActive(false);
            if (dialog != null) dialog.SetActive(false);
            if (toast != null) toast.SetActive(false);
        }

        void OnEnable() { UIThemeState.Changed += OnThemeChanged; }
        void OnDisable() { UIThemeState.Changed -= OnThemeChanged; }
        void OnDestroy() { if (IsOpen) IsAnyOpen = false; }
        void OnThemeChanged(UITheme _) { if (IsOpen) RefreshAll(); }

        UITheme Theme => UIThemeState.Current != null ? UIThemeState.Current : defaultTheme;

        // ---------- открытие и закрытие ----------

        /// <summary>Открыть экран. <paramref name="inDrive"/> — из паузы: пункты «только до поездки» заблокированы.</summary>
        public void Open(bool inDrive, Action closed = null)
        {
            if (IsOpen) return;
            if (Application.isPlaying) EnsureEventSystem();
            InDrive = inDrive;
            onClosed = closed;
            Session = new SettingsSession(SettingsService.Current);
            wheel = WheelDetector.IsConnected; wheelCheckAt = Time.unscaledTime + 1f;
            if (crumb != null) crumb.text = inDrive ? "Пауза / Настройки" : "Главное меню / Настройки";
            root.SetActive(true);
            IsAnyOpen = true;
            zone = Zone.Rows; focus = 0; buttonFocus = 2;
            BuildTab(0);
            Debug.Log($"[Settings] открыты из {(inDrive ? "паузы" : "главного меню")}");
        }

        void Close()
        {
            rebindOpen = calibrationOpen = false;
            rebindWaiting = axisWaiting = gearWaiting = -1;
            if (Session != null) SettingsApplier.ApplyTheme(Session.Saved.gameplay.uiTheme);   // предпросмотр без «Применить» откатывается
            CloseDialog();
            root.SetActive(false);
            if (toast != null) toast.SetActive(false);
            IsAnyOpen = false;
            LastClosedFrame = Time.frameCount;
            var cb = onClosed; onClosed = null;
            cb?.Invoke();
        }

        /// <summary>Esc / «Назад»: без изменений — выход, с изменениями — «Сохранить изменения?».</summary>
        public void Back()
        {
            if (IsDialogOpen) { DialogChoose(dialogSafe); return; }
            if (!Session.IsDirty) { Close(); return; }
            int n = Session.ChangedCount;
            ShowDialog("Сохранить изменения?", $"Изменено параметров: {n}.", null,
                new[] { "Остаться", "Не сохранять", "Сохранить" }, safe: 0, focusIndex: 2,
                () => { }, () => { Session.Revert(); Close(); }, () => { pendingClose = true; Apply(true); });
        }

        // ---------- вкладки и строки ----------

        public void SwitchTab(int index)
        {
            if (IsDialogOpen || index == TabIndex && rows.Count > 0) return;
            zone = Zone.Rows; focus = 0;
            BuildTab(((index % SettingsSchema.Tabs.Length) + SettingsSchema.Tabs.Length) % SettingsSchema.Tabs.Length);
        }

        void BuildTab(int index)
        {
            TabIndex = index;
            foreach (var go in spawned) { if (Application.isPlaying) Destroy(go); else DestroyImmediate(go); }
            spawned.Clear(); rows.Clear();
            var tab = SettingsSchema.Tabs[index];
            foreach (var group in tab.Groups)
            {
                var g = Instantiate(groupTemplate, content);
                g.gameObject.SetActive(true); g.text = group.Title.ToUpperInvariant(); g.name = "Group_" + group.Title;
                spawned.Add(g.gameObject);
                foreach (var item in group.Items)
                {
                    var r = Instantiate(rowTemplate, content);
                    r.gameObject.SetActive(true);
                    r.Bind(item, rows.Count);
                    r.Hovered += v => { if (!IsDialogOpen) { zone = Zone.Rows; focus = v.Index; RefreshAll(); } };
                    r.Stepped += (v, d) => { zone = Zone.Rows; focus = v.Index; Step(v.Item, d); };
                    r.Activated += v => { zone = Zone.Rows; focus = v.Index; Activate(v.Item); };
                    r.SliderSet += (v, value) => { zone = Zone.Rows; focus = v.Index; SetValue(v.Item, value); };
                    rows.Add(r); spawned.Add(r.gameObject);
                }
            }
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, 0f);
            RefreshAll();
        }

        SettingAvailability Availability(SettingItem item) => SettingsSession.Availability(item, Session.Draft, InDrive, wheel);

        public void Step(SettingItem item, int dir)
        {
            if (item == null || !item.IsValue || !SettingsSession.CanChange(Availability(item))) return;
            SetValue(item, item.Next(Session.Get(item.Key), dir));
        }

        public void SetValue(SettingItem item, int value)
        {
            if (!SettingsSession.CanChange(Availability(item))) return;
            Session.Set(item.Key, value);
            if (item.Has(SettingFlags.Preview)) SettingsApplier.ApplyTheme(Session.Draft.gameplay.uiTheme);
            RefreshAll();
        }

        void Activate(SettingItem item)
        {
            if (item == null) return;
            var av = Availability(item);
            if (item.Kind == SettingKind.Action)
            {
                if (item.Key == "controls.rebind") { rebindOpen = true; rebindWaiting = -1; return; }
                if (item.Key == "controls.calibrate")
                {
                    calibrationDraft = Session.Saved.controls.wheel?.Clone() ?? new WheelProfile();
                    calibrationOpen = true; axisWaiting = gearWaiting = -1; return;
                }
                ShowToast(av == SettingAvailability.NoWheel ? "Руль G29 не подключён" : "Пока не реализовано");
                return;
            }
            if (item.Kind == SettingKind.Switch || item.Kind == SettingKind.Cycle) Step(item, 1);
        }

        public void ResetFocused()
        {
            var item = FocusedItem;
            if (item == null || !item.IsValue || !SettingsSession.CanChange(Availability(item))) return;
            Session.ResetItem(item.Key);
            if (item.Has(SettingFlags.Preview)) SettingsApplier.ApplyTheme(Session.Draft.gameplay.uiTheme);
            RefreshAll();
        }

        // ---------- применить / отменить / по умолчанию ----------

        public void Apply(bool thenClose)
        {
            if (!Session.IsDirty) { if (thenClose) Close(); return; }
            displayBeforeApply = (Session.Saved.graphics.displayMode, Session.Saved.graphics.resolution);
            var changed = Session.Commit();
            SettingsStore.Save(Session.Saved);
            SettingsService.Publish(Session.Saved);
            Debug.Log("[Settings] применено: " + string.Join(", ", changed));
            RefreshAll();
            if (changed.Contains("graphics.displayMode") || changed.Contains(SettingsSchema.Resolution))
            {
                SettingsApplier.ApplyDisplay(Session.Saved);
                countdownUntil = Time.unscaledTime + 15f;
                ShowDialog("Оставить этот режим экрана?",
                    "Если изображение пропало или нечитаемо, ничего не нажимайте — прежний режим вернётся автоматически.", "15",
                    new[] { null, "Вернуть прежний", "Оставить" }, safe: 1, focusIndex: 2,
                    null, RevertDisplay, KeepDisplay);
                return;
            }
            ShowToast("Настройки применены");
            if (thenClose || pendingClose) { pendingClose = false; Close(); }
        }

        void KeepDisplay()
        {
            countdownUntil = -1f;
            ShowToast("Настройки применены");
            if (pendingClose) { pendingClose = false; Close(); }
        }

        void RevertDisplay()
        {
            countdownUntil = -1f;
            Session.RestoreDisplay(displayBeforeApply.mode, displayBeforeApply.res);
            SettingsStore.Save(Session.Saved);
            SettingsService.Publish(Session.Saved);
            SettingsApplier.ApplyDisplay(Session.Saved);
            RefreshAll();
            ShowToast("Прежний режим экрана восстановлен");
            if (pendingClose) { pendingClose = false; Close(); }
        }

        public void RevertDraft()
        {
            if (!Session.IsDirty) return;
            Session.Revert();
            SettingsApplier.ApplyTheme(Session.Draft.gameplay.uiTheme);
            RefreshAll();
            ShowToast("Изменения отменены");
        }

        public void AskDefaults()
        {
            var tab = SettingsSchema.Tabs[TabIndex];
            ShowDialog($"Сбросить вкладку «{tab.Title}»?",
                "Значения вкладки вернутся к стандартным. Сохранить их можно кнопкой «Применить».", null,
                new[] { null, "Отмена", "Сбросить" }, safe: 1, focusIndex: 1,
                null, () => { }, () => ResetTabToDefaults());
        }

        public void ResetTabToDefaults()
        {
            Session.ResetTab(SettingsSchema.Tabs[TabIndex], item => SettingsSession.CanChange(Availability(item)));
            SettingsApplier.ApplyTheme(Session.Draft.gameplay.uiTheme);
            RefreshAll();
        }

        // ---------- диалог ----------

        void ShowDialog(string title, string body, string countdown, string[] labels, int safe, int focusIndex, Action a0, Action a1, Action a2)
        {
            dialogTitle.text = title; dialogBody.text = body;
            dialogCountdown.gameObject.SetActive(countdown != null);
            if (countdown != null) dialogCountdown.text = countdown;
            dialogActions[0] = a0; dialogActions[1] = a1; dialogActions[2] = a2;
            for (int i = 0; i < 3; i++)
            {
                bool on = labels[i] != null;
                dialogButtons[i].gameObject.SetActive(on);
                if (on) dialogLabels[i].text = labels[i];
            }
            dialogSafe = safe; dialogFocus = focusIndex;
            dialog.SetActive(true);
            RefreshDialog();
        }

        void CloseDialog() { if (dialog != null) dialog.SetActive(false); countdownUntil = -1f; }

        void DialogChoose(int i)
        {
            if (!IsDialogOpen || !dialogButtons[i].gameObject.activeSelf) return;
            var a = dialogActions[i];
            CloseDialog();
            a?.Invoke();
        }

        void MoveDialogFocus(int dir)
        {
            for (int n = 0; n < 3; n++)
            {
                dialogFocus = (dialogFocus + dir + 3) % 3;
                if (dialogButtons[dialogFocus].gameObject.activeSelf) break;
            }
            RefreshDialog();
        }

        void RefreshDialog()
        {
            var t = Theme; if (t == null) return;
            for (int i = 0; i < 3; i++)
            {
                bool primary = i == 2, focused = i == dialogFocus;
                dialogBgs[i].color = focused ? t.accent : primary ? t.bgRowHover : t.bgCard;
                dialogLabels[i].color = focused ? t.onAccent : t.text;
            }
        }

        // ---------- обновление вида ----------

        public void RefreshAll()
        {
            var t = Theme;
            if (t == null || Session == null) return;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                r.Refresh(Session.Get(r.Item.Key), zone == Zone.Rows && i == focus, Session.IsChanged(r.Item.Key), Availability(r.Item), t);
            }
            for (int i = 0; i < tabLabels.Length; i++)
            {
                bool on = i == TabIndex;
                tabLabels[i].color = on ? t.accent : t.text2;
                tabUnderlines[i].enabled = on; tabUnderlines[i].color = t.accent;
            }
            int n = Session.ChangedCount;
            dirtyText.text = n > 0 ? $"Изменено параметров: {n}" : "";
            dirtyText.color = t.accent;
            bool[] enabled = { true, n > 0, n > 0 };
            for (int i = 0; i < 3; i++)
            {
                bool focused = zone == Zone.Buttons && i == buttonFocus;
                bottomButtons[i].interactable = enabled[i];
                bottomBgs[i].color = !enabled[i] ? t.bgCard : focused ? t.accent : i == 2 ? t.accentSoft : t.bgRowHover;
                bottomLabels[i].color = !enabled[i] ? t.muted : focused ? t.onAccent : i == 2 ? t.accent : t.text;
            }
            RefreshHelp(t);
            if (IsDialogOpen) RefreshDialog();
        }

        void RefreshHelp(UITheme t)
        {
            var item = FocusedItem;
            bool controlsTab = SettingsSchema.Tabs[TabIndex].Id == "controls";
            helpStatus.SetActive(controlsTab);
            if (controlsTab)
            {
                helpStatusDot.color = wheel ? t.green : t.red;
                helpStatusText.text = wheel ? "Руль подключён" : "Руль не найден — доступна клавиатура";
            }
            if (item == null)
            {
                var labels = new[] { "По умолчанию", "Отменить", "Применить" };
                var help = new[] { "Вернуть значения текущей вкладки к стандартным. Изменения попадут в черновик.",
                                   "Вернуть сохранённые значения всех вкладок.", "Сохранить и применить изменения." };
                helpTitle.text = labels[buttonFocus]; helpBody.text = help[buttonFocus];
                metaKey.text = metaDefault.text = metaApply.text = "—";
                return;
            }
            helpTitle.text = item.Label;
            var av = Availability(item);
            string reason = av == SettingAvailability.NoWheel ? "\n\nНужен подключённый руль."
                          : av == SettingAvailability.AfterDrive ? "\n\nМеняется только до начала поездки — в главном меню."
                          : av == SettingAvailability.KeyboardOnly ? "\n\nДействует только при управлении с клавиатуры."
                          : av == SettingAvailability.DependsOff ? "\n\nПри включённой вертикальной синхронизации не действует."
                          : av == SettingAvailability.Stub ? "\n\nПодсистемы пока нет: значение сохранится и заработает, когда она появится." : "";
            helpBody.text = item.Help + reason;
            metaKey.text = item.Key;
            metaDefault.text = item.IsValue ? item.Format(item.Default) : "—";
            metaApply.text = ApplyNote(item);
        }

        static string ApplyNote(SettingItem item)
        {
            if (!string.IsNullOrEmpty(item.ApplyNote)) return item.ApplyNote;
            if (item.Kind == SettingKind.Action) return "отдельный экран";
            if (item.Has(SettingFlags.Stub)) return "когда появится подсистема";
            if (item.Has(SettingFlags.ConfirmDisplay)) return "с подтверждением 15 с";
            if (item.Has(SettingFlags.LockInDrive)) return "до начала поездки";
            if (item.Has(SettingFlags.Preview)) return "видно сразу";
            if (item.Key == "gameplay.defaultCamera") return "при старте поездки";
            return "по «Применить»";
        }

        void ShowToast(string text)
        {
            if (toast == null) return;
            toastText.text = text; toast.SetActive(true);
            toastUntil = Time.unscaledTime + 2.5f;
        }

        void EnsureFocusVisible()
        {
            if (zone != Zone.Rows || focus < 0 || focus >= rows.Count || scroll == null) return;
            var rt = (RectTransform)rows[focus].transform;
            float viewH = scroll.viewport.rect.height;
            float top = -rt.anchoredPosition.y - rt.rect.height * rt.pivot.y;
            float bottom = top + rt.rect.height;
            float y = content.anchoredPosition.y;
            const float margin = 56f;   // заголовок группы над первой строкой
            if (top - margin < y) y = Mathf.Max(0f, top - margin);
            else if (bottom + 8f > y + viewH) y = bottom + 8f - viewH;
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, y);
        }

        // ---------- ввод ----------

        void Update()
        {
            if (!IsOpen || Session == null) return;
            if (calibrationOpen)
            {
                PollCalibration();
                if (Keyboard.current?.escapeKey.wasPressedThisFrame == true)
                { calibrationOpen = false; axisWaiting = gearWaiting = -1; }
                return;
            }
            if (rebindOpen)
            {
                var keys = Keyboard.current;
                if (keys != null && rebindWaiting >= 0 && keys.anyKey.wasPressedThisFrame)
                {
                    foreach (var control in keys.allKeys)
                    {
                        if (!control.wasPressedThisFrame) continue;
                        if (control.keyCode == Key.Escape) { rebindWaiting = -1; break; }
                        KeyboardBindings.Set(rebindWaiting, control.keyCode, Session.Saved.controls);
                        Session.Draft.controls.keyBindings = (string[])Session.Saved.controls.keyBindings.Clone();
                        SettingsStore.Save(Session.Saved);
                        SettingsService.Publish(Session.Saved);
                        rebindWaiting = -1;
                        break;
                    }
                }
                else if (keys?.escapeKey.wasPressedThisFrame == true) rebindOpen = false;
                return;
            }
            if (toast != null && toast.activeSelf && Time.unscaledTime > toastUntil) toast.SetActive(false);
            if (Time.unscaledTime > wheelCheckAt)
            {
                wheelCheckAt = Time.unscaledTime + 1f;
                bool w = WheelDetector.IsConnected;
                if (w != wheel) { wheel = w; RefreshAll(); }
            }
            // Кнопки uGUI не держат выделение: иначе Enter сработает дважды (Submit модуля ввода и наш).
            var es = EventSystem.current;
            if (es != null && es.currentSelectedGameObject != null && !(es.currentSelectedGameObject.GetComponent<Slider>() != null && Mouse.current != null && Mouse.current.leftButton.isPressed))
                es.SetSelectedGameObject(null);

            if (IsDialogOpen && countdownUntil > 0f)
            {
                float left = countdownUntil - Time.unscaledTime;
                dialogCountdown.text = Mathf.CeilToInt(Mathf.Max(0f, left)).ToString();
                if (left <= 0f) { CloseDialog(); RevertDisplay(); return; }
            }

            var kb = Keyboard.current; var pad = Gamepad.current;
            bool back = Pressed(kb?.escapeKey) || Pressed(pad?.buttonEast);
            bool confirm = Pressed(kb?.enterKey) || Pressed(kb?.numpadEnterKey) || Pressed(kb?.spaceKey) || Pressed(pad?.buttonSouth);
            Vector2Int dir = RepeatedDirection(kb, pad);

            if (IsDialogOpen)
            {
                if (back) DialogChoose(dialogSafe);
                else if (confirm) DialogChoose(dialogFocus);
                else if (dir.x != 0) MoveDialogFocus(dir.x);
                return;
            }
            if (back) { Back(); return; }
            if (Pressed(kb?.qKey) || Pressed(pad?.leftShoulder)) { SwitchTab(TabIndex - 1); return; }
            if (Pressed(kb?.eKey) || Pressed(pad?.rightShoulder)) { SwitchTab(TabIndex + 1); return; }
            if (Pressed(kb?.rKey) || Pressed(pad?.buttonNorth)) { ResetFocused(); return; }

            if (zone == Zone.Rows)
            {
                if (dir.y != 0)
                {
                    int next = focus - dir.y;   // вверх = +1 по оси, но −1 по списку
                    if (next >= rows.Count) { zone = Zone.Buttons; buttonFocus = Session.IsDirty ? 2 : 0; }
                    else focus = Mathf.Clamp(next, 0, rows.Count - 1);
                    RefreshAll(); EnsureFocusVisible();
                }
                else if (dir.x != 0) Step(FocusedItem, dir.x);
                else if (confirm) Activate(FocusedItem);
            }
            else
            {
                if (dir.y > 0) { zone = Zone.Rows; focus = rows.Count - 1; RefreshAll(); EnsureFocusVisible(); }
                else if (dir.x != 0)
                {
                    for (int n = 0; n < 3; n++)
                    {
                        buttonFocus = (buttonFocus + dir.x + 3) % 3;
                        if (bottomButtons[buttonFocus].interactable) break;
                    }
                    RefreshAll();
                }
                else if (confirm && bottomButtons[buttonFocus].interactable) bottomButtons[buttonFocus].onClick.Invoke();
            }
        }

        void OnGUI()
        {
            if (!IsOpen || Session == null) return;
            if (calibrationOpen) { DrawCalibration(); return; }
            if (!rebindOpen) return;
            var area = new Rect(Screen.width * 0.2f, Screen.height * 0.08f, Screen.width * 0.6f, Screen.height * 0.84f);
            GUI.Box(area, "Клавиши управления — изменения сохраняются сразу");
            GUILayout.BeginArea(new Rect(area.x + 16f, area.y + 28f, area.width - 32f, area.height - 40f));
            GUILayout.Label(rebindWaiting >= 0 ? "Нажмите новую клавишу (Esc — отмена)" : "Выберите действие для переназначения");
            rebindScroll = GUILayout.BeginScrollView(rebindScroll);
            for (int i = 0; i < KeyboardBindings.Labels.Length; i++)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(KeyboardBindings.Labels[i], GUILayout.Width(area.width * 0.55f));
                if (GUILayout.Button(KeyboardBindings.Get(i, Session.Saved.controls).ToString())) rebindWaiting = i;
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            if (GUILayout.Button("Закрыть")) { rebindOpen = false; rebindWaiting = -1; }
            GUILayout.EndArea();
        }

        WheelAxisBinding Axis(int index) => index == 0 ? calibrationDraft.steering
            : index == 1 ? calibrationDraft.throttle : index == 2 ? calibrationDraft.brake : calibrationDraft.clutch;

        void BeginAxisCapture(int index, InputDevice device)
        {
            axisWaiting = index; gearWaiting = -1; axisBaseline.Clear();
            foreach (var control in device.allControls)
                if (control is AxisControl axis && !(control is ButtonControl))
                    axisBaseline[G29InputSource.RelativePath(device, control)] = axis.ReadValue();
        }

        void PollCalibration()
        {
            var device = G29InputSource.FindDevice();
            if (device == null || calibrationDraft == null) return;
            if (axisWaiting >= 0)
            {
                foreach (var control in device.allControls)
                {
                    if (!(control is AxisControl axis) || control is ButtonControl) continue;
                    string path = G29InputSource.RelativePath(device, control);
                    if (!axisBaseline.TryGetValue(path, out float baseline) || Mathf.Abs(axis.ReadValue() - baseline) < 0.25f) continue;
                    var binding = Axis(axisWaiting);
                    binding.control = path; binding.min = binding.max = binding.center = baseline;
                    axisWaiting = -1;
                    break;
                }
            }
            if (gearWaiting >= 0)
                foreach (var control in device.allControls)
                    if (control is ButtonControl button && button.wasPressedThisFrame)
                    { calibrationDraft.gears[gearWaiting] = G29InputSource.RelativePath(device, control); gearWaiting = -1; break; }
            for (int i = 0; i < 4; i++)
            {
                var b = Axis(i);
                if (!(G29InputSource.FindControl(device, b.control) is AxisControl axis)) continue;
                float raw = axis.ReadValue();
                b.min = Mathf.Min(b.min, raw); b.max = Mathf.Max(b.max, raw);
            }
        }

        void DrawCalibration()
        {
            var area = new Rect(Screen.width * 0.14f, Screen.height * 0.07f, Screen.width * 0.72f, Screen.height * 0.86f);
            GUI.Box(area, "G29 — назначение осей и H-шифтера");
            GUILayout.BeginArea(new Rect(area.x + 16f, area.y + 28f, area.width - 32f, area.height - 40f));
            var device = G29InputSource.FindDevice();
            if (device == null) { GUILayout.Label("G29 (USB 046D:C24F) не найден в Unity Input System."); if (GUILayout.Button("Закрыть")) calibrationOpen = false; GUILayout.EndArea(); return; }
            GUILayout.Label(device.displayName + " — переместите каждую ось в оба упора; для передачи нажмите рычаг после выбора строки.");
            GUILayout.Label(axisWaiting >= 0 ? "Поверните руль или нажмите выбранную педаль" : gearWaiting >= 0 ? "Включите выбранную передачу" : "Сохранение доступно после полного хода четырёх осей.");
            calibrationScroll = GUILayout.BeginScrollView(calibrationScroll);
            string[] names = { "Руль", "Газ", "Тормоз", "Сцепление" };
            for (int i = 0; i < 4; i++)
            {
                var b = Axis(i);
                GUILayout.BeginHorizontal();
                GUILayout.Label(names[i], GUILayout.Width(90));
                if (GUILayout.Button(string.IsNullOrEmpty(b.control) ? "Назначить" : b.control, GUILayout.Width(180))) BeginAxisCapture(i, device);
                float raw = G29InputSource.FindControl(device, b.control) is AxisControl axis ? axis.ReadValue() : 0f;
                GUILayout.Label($"{raw:F2}  [{b.min:F2} .. {b.max:F2}]", GUILayout.Width(160));
                if (i == 0 && GUILayout.Button("Центр", GUILayout.Width(65))) b.center = raw;
                if (GUILayout.Button(b.inverted ? "Инв. ✓" : "Инв.", GUILayout.Width(65))) b.inverted = !b.inverted;
                GUILayout.EndHorizontal();
            }
            string[] gears = { "R", "1", "2", "3", "4", "5", "6" };
            for (int i = 0; i < 7; i++)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Передача " + gears[i], GUILayout.Width(110));
                if (GUILayout.Button(string.IsNullOrEmpty(calibrationDraft.gears[i]) ? "Назначить" : calibrationDraft.gears[i]))
                { gearWaiting = i; axisWaiting = -1; }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            bool valid = true;
            for (int i = 0; i < 4; i++) valid &= !string.IsNullOrEmpty(Axis(i).control) && Axis(i).max - Axis(i).min >= 0.1f;
            GUI.enabled = valid;
            if (GUILayout.Button("Сохранить калибровку"))
            {
                Session.Saved.controls.wheel = calibrationDraft.Clone();
                Session.Draft.controls.wheel = calibrationDraft.Clone();
                SettingsStore.Save(Session.Saved); SettingsService.Publish(Session.Saved);
                calibrationOpen = false;
            }
            GUI.enabled = true;
            if (GUILayout.Button("Закрыть без сохранения")) calibrationOpen = false;
            GUILayout.EndArea();
        }

        static bool Pressed(UnityEngine.InputSystem.Controls.ButtonControl b) => b != null && b.wasPressedThisFrame;
        static bool Held(UnityEngine.InputSystem.Controls.ButtonControl b) => b != null && b.isPressed;

        Vector2Int RepeatedDirection(Keyboard kb, Gamepad pad)
        {
            var d = Vector2Int.zero;
            if (Held(kb?.upArrowKey) || Held(pad?.dpad.up)) d.y = 1;
            else if (Held(kb?.downArrowKey) || Held(pad?.dpad.down)) d.y = -1;
            else if (Held(kb?.leftArrowKey) || Held(pad?.dpad.left)) d.x = -1;
            else if (Held(kb?.rightArrowKey) || Held(pad?.dpad.right)) d.x = 1;
            if (d == Vector2Int.zero) { heldDir = d; return d; }
            float now = Time.unscaledTime;
            if (d != heldDir) { heldDir = d; repeatAt = now + 0.35f; return d; }
            if (now >= repeatAt) { repeatAt = now + 0.07f; return d; }
            return Vector2Int.zero;
        }

        static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }
    }
}
