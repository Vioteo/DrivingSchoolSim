using System;
using System.Collections.Generic;
using DrivingSchool.Input;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;

namespace DrivingSchool.Presentation.UI
{
    /// <summary>
    /// Окно поверх экрана настроек (T42): «Калибровка руля и педалей» — пошаговый захват осей и передач шифтера
    /// по движению, без угадывания номеров; «Переназначение кнопок» — таблица действий, клавиша и кнопка руля
    /// назначаются нажатием. Результат — <see cref="WheelProfile"/> (wheel-g29.json) и <see cref="KeyboardProfile"/> (keyboard.json).
    /// Собирается кодом (uGUI), цвета — из текущей темы.
    /// </summary>
    public sealed class ControlsSetupPanel : MonoBehaviour
    {
        enum Mode { None, Calibration, Rebind }
        enum Step { Intro, Steer, SteerBack, Gas, GasBack, Brake, BrakeBack, Clutch, ClutchBack, Shifter, Done }

        const float CardW = 1320f, CardH = 880f, Pad = 40f;
        const float PressThreshold = 0.35f, HoldSeconds = 0.6f, ReleaseThreshold = 0.1f;

        public bool IsOpen => gameObject.activeSelf && mode != Mode.None;
        public event Action<string> Closed;

        Func<UITheme> theme;
        TMP_FontAsset font;
        Mode mode;

        // каркас
        Image dim, card, bodyBg;
        TMP_Text title, subtitle, footerHint, status;
        RectTransform body;
        readonly List<(Image bg, TMP_Text text, Action act)> footer = new List<(Image, TMP_Text, Action)>();
        int footerFocus;
        readonly List<GameObject> bodyObjects = new List<GameObject>();
        readonly DirectionRepeater repeater = new DirectionRepeater();

        // ---------- создание ----------

        public static ControlsSetupPanel Create(RectTransform parent, TMP_FontAsset font, Func<UITheme> theme)
        {
            var go = new GameObject("ControlsSetupPanel", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            var p = go.AddComponent<ControlsSetupPanel>();
            p.font = font; p.theme = theme;
            p.Build();
            go.SetActive(false);
            return p;
        }

        UITheme T => theme?.Invoke();

        void Build()
        {
            dim = Box("Dim", transform, Color.black);
            Stretch(dim.rectTransform);
            card = Box("Card", transform, Color.gray);
            var crt = card.rectTransform;
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f); crt.pivot = new Vector2(0.5f, 0.5f);
            crt.sizeDelta = new Vector2(CardW, CardH); crt.anchoredPosition = Vector2.zero;
            title = Text("Title", card.transform, 36, FontStyles.Bold); Place(title.rectTransform, Pad, 28, CardW - 2 * Pad, 48);
            subtitle = Text("Subtitle", card.transform, 20); Place(subtitle.rectTransform, Pad, 80, CardW - 2 * Pad, 30);
            bodyBg = Box("Body", card.transform, Color.gray); Place(bodyBg.rectTransform, Pad, 124, CardW - 2 * Pad, 600);
            body = bodyBg.rectTransform;
            status = Text("Status", card.transform, 22); Place(status.rectTransform, Pad, 736, CardW - 2 * Pad, 34);
            footerHint = Text("FooterHint", card.transform, 18); Place(footerHint.rectTransform, Pad, 800, 560, 56);
        }

        // ---------- открытие ----------

        public void OpenCalibration()
        {
            Open(Mode.Calibration, "Калибровка руля и педалей");
            BuildCalibration();
        }

        public void OpenRebind()
        {
            Open(Mode.Rebind, "Переназначение кнопок");
            BuildRebind();
        }

        void Open(Mode m, string heading)
        {
            ClearBody();
            mode = m;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            title.text = heading;
            var dev = WheelDevice.Current;
            subtitle.text = dev != null ? "Руль: " + dev.displayName : "Руль не найден — доступна только клавиатура";
            status.text = "";
        }

        public void Close(string message)
        {
            mode = Mode.None;
            capture = Capture.None;
            ClearBody();
            gameObject.SetActive(false);
            Closed?.Invoke(message);
        }

        void ClearBody()
        {
            foreach (var go in bodyObjects) if (go != null) Destroy(go);
            bodyObjects.Clear();
            foreach (var f in footer) if (f.bg != null) Destroy(f.bg.gameObject);
            footer.Clear();
            bars.Clear(); stepLabels.Clear(); rowViews.Clear();
        }

        /// <summary>Вызывает экран настроек каждый кадр, пока окно открыто.</summary>
        public void Tick()
        {
            if (!IsOpen) return;
            if (mode == Mode.Calibration) TickCalibration();
            else if (mode == Mode.Rebind) TickRebind();
            Paint();
        }

        // ==================== калибровка ====================

        Step step;
        WheelProfile work;
        List<AxisControl> axes = new List<AxisControl>();
        float[] rest;
        int steerAxis = -1, gasAxis = -1, brakeAxis = -1, clutchAxis = -1, pressedAxis = -1;
        int peakAxis = -1; float peakDev, holdT;
        int shifterGear;              // 1…6, 7 = R
        readonly List<string> shifterButtons = new List<string>();
        readonly List<Bar> bars = new List<Bar>();
        readonly List<TMP_Text> stepLabels = new List<TMP_Text>();
        TMP_Text instruction, lastButton, gearText;
        Bar holdBar;

        static readonly string[] StepNames = { "Покой: руль прямо, педали отпущены", "Руль — до упора вправо", "Газ — до упора", "Тормоз — до упора", "Сцепление — до упора", "Шифтер: 1–6 и R" };

        void BuildCalibration()
        {
            work = JsonUtility.FromJson<WheelProfile>(JsonUtility.ToJson(WheelProfile.Current)).FillMissing();
            shifterButtons.Clear();
            steerAxis = gasAxis = brakeAxis = clutchAxis = -1;
            for (int i = 0; i < StepNames.Length; i++)
            {
                var t = BodyText("Step" + i, 22); Place(t.rectTransform, 24, 20 + i * 42, 600, 36);
                stepLabels.Add(t);
            }
            instruction = BodyText("Instruction", 28, FontStyles.Bold); Place(instruction.rectTransform, 24, 290, 600, 190);
            instruction.textWrappingMode = TextWrappingModes.Normal;
            holdBar = MakeBar("Hold", 24, 500, 600, false, "");
            float x = 680, w = 540;
            bars.Add(MakeBar("Steer", x, 40, w, true, "Руль"));
            bars.Add(MakeBar("Gas", x, 140, w, false, "Газ"));
            bars.Add(MakeBar("Brake", x, 240, w, false, "Тормоз"));
            bars.Add(MakeBar("Clutch", x, 340, w, false, "Сцепление"));
            gearText = BodyText("Gear", 24); Place(gearText.rectTransform, x, 440, w, 36);
            lastButton = BodyText("LastButton", 20); Place(lastButton.rectTransform, x, 490, w, 60);
            lastButton.textWrappingMode = TextWrappingModes.Normal;
            footerHint.text = "Esc / ○ — выйти без сохранения\n←/→ и Enter — кнопки внизу";
            GoTo(Step.Intro);
        }

        void GoTo(Step s)
        {
            step = s; holdT = 0f; peakAxis = -1; peakDev = 0f;
            switch (s)
            {
                case Step.Intro:
                    SetFooter(("Выйти", () => Close("Калибровка не сохранена")), ("Начать", StartCapture));
                    footerFocus = 1;
                    break;
                case Step.Clutch:
                    SetFooter(("Выйти", () => Close("Калибровка не сохранена")), ("Нет сцепления", () => { clutchAxis = -1; GoTo(Step.Shifter); }), ("Заново", () => GoTo(Step.Intro)));
                    footerFocus = 1;
                    break;
                case Step.Shifter:
                    shifterGear = 1; shifterButtons.Clear();
                    SetFooter(("Выйти", () => Close("Калибровка не сохранена")), ("Нет шифтера", () => GoTo(Step.Done)), ("Заново", () => GoTo(Step.Intro)));
                    footerFocus = 1;
                    break;
                case Step.Done:
                    SetFooter(("Выйти", () => Close("Калибровка не сохранена")), ("Заново", () => GoTo(Step.Intro)), ("Сохранить", SaveCalibration));
                    footerFocus = 2;
                    break;
                default:
                    if (footer.Count == 0 || s == Step.Steer)
                        SetFooter(("Выйти", () => Close("Калибровка не сохранена")), ("Заново", () => GoTo(Step.Intro)));
                    footerFocus = 1;
                    break;
            }
        }

        void StartCapture()
        {
            var dev = WheelDevice.Current;
            if (dev == null) { status.text = "Руль не найден. Подключите G29 и откройте калибровку снова."; return; }
            axes = WheelDevice.Axes(dev);
            rest = new float[axes.Count];
            for (int i = 0; i < axes.Count; i++) rest[i] = axes[i].ReadValue();
            WheelDevice.Trace("калибровка: покой " + string.Join(" ", axes.ConvertAll(a => WheelDevice.RelativePath(a) + "=" + a.ReadValue().ToString("0.00"))));
            GoTo(Step.Steer);
        }

        void TickCalibration()
        {
            var dev = WheelDevice.Current;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            bool keyboardEnter = KeyboardSubmit();
            var pressed = dev != null ? WheelDevice.FirstPressedButton(dev) : null;
            if (pressed != null) lastButton.text = "Нажата кнопка: " + WheelDevice.RelativePath(pressed) + " (" + WheelProfile.FriendlyName(WheelDevice.RelativePath(pressed)) + ")";

            bool wheelCancel = pressed != null && WheelDevice.RelativePath(pressed) == MenuInput.WheelCancel && step != Step.Shifter;
            if (KeyboardCancel() || wheelCancel) { Close("Калибровка не сохранена"); return; }
            var d = repeater.Next(MenuInput.HeldDirection);
            if (d.x != 0 && footer.Count > 0) footerFocus = (footerFocus + d.x + footer.Count) % footer.Count;

            switch (step)
            {
                case Step.Intro:
                    instruction.text = "Поставьте руль прямо и отпустите все педали.\nЗатем нажмите любую кнопку на руле или Enter.";
                    if (keyboardEnter) { ActivateFooter(); return; }
                    if (pressed != null) { StartCapture(); return; }
                    break;
                case Step.Steer:
                    instruction.text = "Поверните руль до упора ВПРАВО и держите.";
                    TrackPress(dt, new int[0], i => { steerAxis = i; SetAxis(work.steering, i, "руль"); GoTo(Step.SteerBack); });
                    break;
                case Step.SteerBack:
                    instruction.text = "Отпустите руль — верните его в центр.";
                    if (Released(steerAxis)) GoTo(Step.Gas);
                    break;
                case Step.Gas:
                    instruction.text = "Выжмите педаль ГАЗА до упора и держите.";
                    TrackPress(dt, new[] { steerAxis }, i => { gasAxis = i; SetAxis(work.throttle, i, "педаль газа"); GoTo(Step.GasBack); });
                    break;
                case Step.GasBack:
                    instruction.text = "Отпустите газ.";
                    if (Released(gasAxis)) GoTo(Step.Brake);
                    break;
                case Step.Brake:
                    instruction.text = "Выжмите педаль ТОРМОЗА до упора и держите.";
                    TrackPress(dt, new[] { steerAxis, gasAxis }, i => { brakeAxis = i; SetAxis(work.brake, i, "педаль тормоза"); GoTo(Step.BrakeBack); });
                    break;
                case Step.BrakeBack:
                    instruction.text = "Отпустите тормоз.";
                    if (Released(brakeAxis)) GoTo(Step.Clutch);
                    break;
                case Step.Clutch:
                    instruction.text = "Выжмите педаль СЦЕПЛЕНИЯ до упора и держите.\nНет сцепления — «Нет сцепления».";
                    TrackPress(dt, new[] { steerAxis, gasAxis, brakeAxis }, i => { clutchAxis = i; SetAxis(work.clutch, i, "педаль сцепления"); GoTo(Step.ClutchBack); });
                    break;
                case Step.ClutchBack:
                    instruction.text = "Отпустите сцепление.";
                    if (Released(clutchAxis)) GoTo(Step.Shifter);
                    break;
                case Step.Shifter:
                    instruction.text = shifterGear <= 6 ? $"Включите рычагом {shifterGear}-ю передачу." : "Включите рычагом задний ход (R).";
                    if (pressed != null)
                    {
                        string path = WheelDevice.RelativePath(pressed);
                        if (!shifterButtons.Contains(path))
                        {
                            shifterButtons.Add(path);
                            var gear = shifterGear <= 6 ? DriveAction.Gear1 + (shifterGear - 1) : DriveAction.Reverse;
                            work.Assign(gear, path);
                            WheelDevice.Trace($"калибровка: {gear} = {path}");
                            if (++shifterGear > 7) GoTo(Step.Done);
                        }
                    }
                    break;
                case Step.Done:
                    instruction.text = "Готово. Покрутите руль и понажимайте педали — полоски справа должны двигаться правильно.\nЕсли всё верно — «Сохранить».";
                    if (keyboardEnter || (pressed != null && (WheelDevice.RelativePath(pressed) == "trigger" || WheelDevice.RelativePath(pressed) == "button24"))) { ActivateFooter(); return; }
                    break;
            }
            if (step != Step.Intro && step != Step.Done && keyboardEnter) ActivateFooter();
            holdBar.Set(step == Step.Steer || step == Step.Gas || step == Step.Brake || step == Step.Clutch ? holdT / HoldSeconds : 0f);

            // Живые значения — по рабочей (ещё не сохранённой) раскладке.
            bars[0].Set(WheelDevice.Steering(work.steering)); bars[0].value.text = Mathf.RoundToInt(WheelDevice.Steering(work.steering) * 450f) + "°";
            SetPedal(bars[1], work.throttle); SetPedal(bars[2], work.brake); SetPedal(bars[3], work.clutch);
            gearText.text = "Рычаг КПП: " + ShifterText();
            for (int i = 0; i < stepLabels.Count; i++)
            {
                int s = StepIndex(step);
                stepLabels[i].text = (i < s ? "✓  " : i == s ? "▶  " : "·  ") + StepNames[i];
            }
        }

        static int StepIndex(Step s)
        {
            switch (s)
            {
                case Step.Intro: return 0;
                case Step.Steer: case Step.SteerBack: return 1;
                case Step.Gas: case Step.GasBack: return 2;
                case Step.Brake: case Step.BrakeBack: return 3;
                case Step.Clutch: case Step.ClutchBack: return 4;
                case Step.Shifter: return 5;
                default: return 6;
            }
        }

        string ShifterText()
        {
            for (int g = 1; g <= 6; g++) if (WheelDevice.IsPressed(WheelDevice.Control(work.Find(DriveAction.Gear1 + (g - 1))?.control))) return g.ToString();
            if (WheelDevice.IsPressed(WheelDevice.Control(work.Find(DriveAction.Reverse)?.control))) return "R";
            return "нейтраль";
        }

        void SetPedal(Bar bar, WheelAxisBinding b)
        {
            float v = WheelDevice.Pedal(b);
            bar.Set(v); bar.value.text = Mathf.RoundToInt(v * 100f) + " %";
        }

        /// <summary>Ось, сильнее всех ушедшая от покоя (кроме занятых), удержана у упора HoldSeconds — захват.</summary>
        void TrackPress(float dt, int[] exclude, Action<int> captured)
        {
            if (axes == null || rest == null) return;
            int best = -1; float bestDev = 0f;
            for (int i = 0; i < axes.Count; i++)
            {
                if (Array.IndexOf(exclude, i) >= 0) continue;
                float dev = Mathf.Abs(axes[i].ReadValue() - rest[i]);
                if (dev > bestDev) { bestDev = dev; best = i; }
            }
            if (best < 0 || bestDev < PressThreshold) { holdT = 0f; peakAxis = -1; peakDev = 0f; return; }
            if (best != peakAxis || bestDev > peakDev + 0.02f) { peakAxis = best; peakDev = bestDev; holdT = 0f; return; }
            if (bestDev < peakDev - 0.05f) { holdT = 0f; peakDev = bestDev; return; }
            holdT += dt;
            if (holdT >= HoldSeconds) { pressedAxis = best; captured(best); }
        }

        bool Released(int axis) => axis < 0 || axis >= axes.Count || Mathf.Abs(axes[axis].ReadValue() - rest[axis]) < ReleaseThreshold;

        void SetAxis(WheelAxisBinding b, int i, string label)
        {
            b.control = WheelDevice.RelativePath(axes[i]);
            b.calibrated = true;
            b.rawReleased = rest[i];
            b.rawPressed = axes[i].ReadValue();
            b.label = label;
            WheelDevice.Trace($"калибровка: {label} = {b.control} покой {b.rawReleased:0.000} упор {b.rawPressed:0.000}");
        }

        void SaveCalibration()
        {
            if (steerAxis < 0 || gasAxis < 0 || brakeAxis < 0) { status.text = "Руль, газ и тормоз должны быть откалиброваны."; return; }
            if (clutchAxis < 0) { work.clutch.control = ""; work.clutch.calibrated = false; }
            WheelProfile.Save(work);
            Close("Калибровка руля сохранена");
        }

        // ==================== переназначение ====================

        enum Capture { None, Key, Wheel }
        Capture capture;
        int captureFrame;
        int row, col, top;
        const int VisibleRows = 12;
        static readonly DriveAction[] Actions = (DriveAction[])Enum.GetValues(typeof(DriveAction));
        readonly List<RowView> rowViews = new List<RowView>();
        bool footerZone;

        sealed class RowView { public Image bg; public TMP_Text name; public Image[] cellBg = new Image[2]; public TMP_Text[] cell = new TMP_Text[2]; }

        void BuildRebind()
        {
            row = 0; col = WheelDevice.Current != null ? 1 : 0; top = 0; capture = Capture.None; footerZone = false;
            var h1 = BodyText("HeadAction", 18); Place(h1.rectTransform, 24, 12, 460, 28); h1.text = "ДЕЙСТВИЕ";
            var h2 = BodyText("HeadKey", 18); Place(h2.rectTransform, 500, 12, 330, 28); h2.text = "КЛАВИАТУРА";
            var h3 = BodyText("HeadWheel", 18); Place(h3.rectTransform, 850, 12, 360, 28); h3.text = "РУЛЬ";
            for (int i = 0; i < VisibleRows; i++)
            {
                var rv = new RowView();
                float y = 46 + i * 46;
                rv.bg = BodyBox("Row" + i, 12, y, CardW - 2 * Pad - 24, 42);
                rv.name = BodyText("Name" + i, 22); Place(rv.name.rectTransform, 24, y + 6, 460, 32);
                for (int c = 0; c < 2; c++)
                {
                    float cx = c == 0 ? 500 : 850, cw = c == 0 ? 330 : 360;
                    rv.cellBg[c] = BodyBox("Cell" + i + "_" + c, cx, y + 3, cw, 36);
                    rv.cell[c] = Text("CellText", rv.cellBg[c].transform, 20);
                    Stretch(rv.cell[c].rectTransform, 12, 0);
                    int ri = i, ci = c;
                    var btn = rv.cellBg[c].gameObject.AddComponent<Button>();
                    btn.transition = Selectable.Transition.None;
                    btn.navigation = new Navigation { mode = Navigation.Mode.None };
                    btn.onClick.AddListener(() => { if (capture != Capture.None) return; footerZone = false; row = top + ri; col = ci; BeginCapture(); });
                }
                rowViews.Add(rv);
            }
            SetFooter(("Клавиатура по умолчанию", () => { KeyboardProfile.Save(new KeyboardProfile()); status.text = "Клавиши — по умолчанию"; }),
                      ("Руль по умолчанию", ResetWheelButtons),
                      ("Готово", () => Close("Назначения сохранены")));
            footerFocus = 2;
            footerHint.text = "↑↓ действие, ←→ клавиатура/руль, Enter / ✕ — назначить,\nDel — очистить, Esc / ○ — готово";
        }

        void ResetWheelButtons()
        {
            var p = JsonUtility.FromJson<WheelProfile>(JsonUtility.ToJson(WheelProfile.Current));
            var def = new WheelProfile();
            // Кнопки — по умолчанию, но передачи шифтера из калибровки сохраняем.
            foreach (var b in def.buttons)
            {
                if (!Enum.TryParse(b.action, out DriveAction a)) continue;
                bool gear = a >= DriveAction.Gear1 && a <= DriveAction.Reverse;
                if (gear && p.Find(a) != null && !string.IsNullOrEmpty(p.Find(a).control)) continue;
                var mine = p.Find(a);
                if (mine == null) p.buttons.Add(new WheelButtonBinding(a, b.control, b.label));
                else { mine.control = b.control; mine.label = b.label; }
            }
            WheelProfile.Save(p.FillMissing());
            status.text = "Кнопки руля — по умолчанию (как в City Car Driving)";
        }

        void BeginCapture()
        {
            var a = Actions[row];
            if (col == 1)
            {
                if (DriveActions.IsAxis(a)) { status.text = "Руль и педали назначаются в «Калибровке руля и педалей»."; return; }
                if (WheelDevice.Current == null) { status.text = "Руль не найден."; return; }
                capture = Capture.Wheel;
                status.text = $"Нажмите кнопку на руле для «{DriveActions.Title(a)}» (Esc — отмена)";
            }
            else
            {
                capture = Capture.Key;
                status.text = $"Нажмите клавишу для «{DriveActions.Title(a)}» (Esc — отмена)";
            }
            captureFrame = Time.frameCount;
        }

        void TickRebind()
        {
            var kb = Keyboard.current;
            if (capture != Capture.None)
            {
                if (Time.frameCount == captureFrame) return;
                if (kb != null && kb.escapeKey.wasPressedThisFrame) { capture = Capture.None; status.text = "Отменено"; return; }
                var a = Actions[row];
                if (capture == Capture.Key && kb != null)
                {
                    foreach (var k in kb.allKeys)
                    {
                        if (k == null || !k.wasPressedThisFrame || k.keyCode == Key.Escape) continue;
                        var p = KeyboardProfile.Current;
                        var taken = p.Assign(a, k.keyCode);
                        KeyboardProfile.Save(p);
                        capture = Capture.None;
                        status.text = $"«{DriveActions.Title(a)}» — {KeyboardProfile.KeyLabel(k.keyCode)}" + (taken.HasValue ? $" (снята с «{DriveActions.Title(taken.Value)}»)" : "");
                        return;
                    }
                }
                else if (capture == Capture.Wheel)
                {
                    var b = WheelDevice.FirstPressedButton(WheelDevice.Current);
                    if (b != null)
                    {
                        string path = WheelDevice.RelativePath(b);
                        var p = WheelProfile.Current;
                        var taken = p.Assign(a, path);
                        WheelProfile.Save(p);
                        WheelDevice.Trace($"назначение: {a} = {path}");
                        capture = Capture.None;
                        status.text = $"«{DriveActions.Title(a)}» — {WheelProfile.FriendlyName(path)}" + (taken.HasValue ? $" (снята с «{DriveActions.Title(taken.Value)}»)" : "");
                    }
                }
                return;
            }

            if (MenuInput.Cancel) { Close("Назначения сохранены"); return; }
            var d = repeater.Next(MenuInput.HeldDirection);
            if (footerZone)
            {
                if (d.y > 0) footerZone = false;
                else if (d.x != 0) footerFocus = (footerFocus + d.x + footer.Count) % footer.Count;
                else if (MenuInput.Submit) ActivateFooter();
                return;
            }
            if (d.y != 0)
            {
                int next = row - d.y;
                if (next >= Actions.Length) { footerZone = true; return; }
                row = Mathf.Clamp(next, 0, Actions.Length - 1);
            }
            else if (d.x != 0) col = Mathf.Clamp(col + d.x, 0, 1);
            else if (MenuInput.Submit) BeginCapture();
            else if (kb != null && (kb.deleteKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame)) ClearCell();
            if (row < top) top = row;
            if (row >= top + VisibleRows) top = row - VisibleRows + 1;
        }

        void ClearCell()
        {
            var a = Actions[row];
            if (col == 0)
            {
                var p = KeyboardProfile.Current;
                var b = p.Find(a);
                if (b != null) { b.key = Key.None.ToString(); b.alt = Key.None.ToString(); KeyboardProfile.Save(p); }
            }
            else if (!DriveActions.IsAxis(a))
            {
                var p = WheelProfile.Current; p.Assign(a, ""); WheelProfile.Save(p);
            }
            status.text = $"«{DriveActions.Title(a)}» — не назначено";
        }

        string KeyCell(DriveAction a)
        {
            var p = KeyboardProfile.Current;
            var k1 = p.Primary(a); var k2 = p.Alternate(a);
            if (k1 == Key.None && k2 == Key.None) return "—";
            if (k2 == Key.None) return KeyboardProfile.KeyLabel(k1);
            if (k1 == Key.None) return KeyboardProfile.KeyLabel(k2);
            return KeyboardProfile.KeyLabel(k1) + "  /  " + KeyboardProfile.KeyLabel(k2);
        }

        string WheelCell(DriveAction a)
        {
            var p = WheelProfile.Current;
            if (DriveActions.IsAxis(a))
            {
                var b = a == DriveAction.Gas ? p.throttle : a == DriveAction.Brake ? p.brake : a == DriveAction.Clutch ? p.clutch : p.steering;
                if (b == null || string.IsNullOrEmpty(b.control)) return "нет (калибровка)";
                return "ось " + b.control + (b.calibrated ? "" : " — не откалибр.");
            }
            var l = p.Label(a);
            return string.IsNullOrEmpty(l) ? "—" : l;
        }

        // ==================== отрисовка ====================

        void Paint()
        {
            var t = T; if (t == null) return;
            dim.color = new Color(t.bgDark.r, t.bgDark.g, t.bgDark.b, 0.92f);
            card.color = t.bgPanel; bodyBg.color = t.bgDark;
            title.color = t.text; subtitle.color = t.text2; footerHint.color = t.muted; status.color = t.accent;
            for (int i = 0; i < footer.Count; i++)
            {
                bool focused = i == footerFocus && (mode == Mode.Calibration || footerZone);
                footer[i].bg.color = focused ? t.accent : t.bgCard;
                footer[i].text.color = focused ? t.onAccent : t.text;
            }
            if (mode == Mode.Calibration)
            {
                foreach (var b in bars) b.Paint(t);
                holdBar.Paint(t);
                int s = StepIndex(step);
                for (int i = 0; i < stepLabels.Count; i++) stepLabels[i].color = i < s ? t.green : i == s ? t.accent : t.muted;
                if (instruction != null) instruction.color = t.text;
                if (gearText != null) gearText.color = t.text;
                if (lastButton != null) lastButton.color = t.text2;
            }
            else if (mode == Mode.Rebind)
            {
                foreach (var go in bodyObjects) { var tx = go.GetComponent<TMP_Text>(); if (tx != null && tx.name.StartsWith("Head")) tx.color = t.muted; }
                for (int i = 0; i < rowViews.Count; i++)
                {
                    var rv = rowViews[i]; int r = top + i;
                    bool on = r < Actions.Length;
                    rv.bg.gameObject.SetActive(on);
                    rv.name.gameObject.SetActive(on);
                    for (int c = 0; c < 2; c++) rv.cellBg[c].gameObject.SetActive(on);
                    if (!on) continue;
                    var a = Actions[r];
                    bool focusedRow = r == row && !footerZone;
                    rv.bg.color = focusedRow ? t.bgRowHover : t.bgCard;
                    rv.name.text = DriveActions.Title(a); rv.name.color = t.text;
                    rv.cell[0].text = capture == Capture.Key && focusedRow && col == 0 ? "нажмите клавишу…" : KeyCell(a);
                    rv.cell[1].text = capture == Capture.Wheel && focusedRow && col == 1 ? "нажмите кнопку…" : WheelCell(a);
                    for (int c = 0; c < 2; c++)
                    {
                        bool cellFocus = focusedRow && c == col;
                        rv.cellBg[c].color = cellFocus ? (capture != Capture.None ? t.accentSoft : t.accent) : t.bgPanel;
                        rv.cell[c].color = cellFocus && capture == Capture.None ? t.onAccent : cellFocus ? t.accent : t.text;
                    }
                }
            }
        }

        // ==================== кнопки внизу ====================

        void SetFooter(params (string label, Action act)[] items)
        {
            foreach (var f in footer) if (f.bg != null) Destroy(f.bg.gameObject);
            footer.Clear();
            float w = 250f, gap = 16f, x = CardW - Pad - items.Length * w - (items.Length - 1) * gap;
            for (int i = 0; i < items.Length; i++)
            {
                var bg = Box("Button_" + items[i].label, card.transform, Color.gray);
                Place(bg.rectTransform, x + i * (w + gap), 800, w, 56);
                var tx = Text("Label", bg.transform, 20, FontStyles.Bold, TextAlignmentOptions.Center);
                Stretch(tx.rectTransform);
                tx.text = items[i].label;
                var btn = bg.gameObject.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                btn.navigation = new Navigation { mode = Navigation.Mode.None };
                var act = items[i].act; int k = i;
                btn.onClick.AddListener(() => { footerFocus = k; act?.Invoke(); });
                footer.Add((bg, tx, act));
            }
            footerFocus = Mathf.Clamp(footerFocus, 0, Mathf.Max(0, footer.Count - 1));
        }

        void ActivateFooter()
        {
            if (footerFocus >= 0 && footerFocus < footer.Count) footer[footerFocus].act?.Invoke();
        }

        static bool KeyboardSubmit()
        {
            var kb = Keyboard.current; var pad = Gamepad.current;
            return (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame))
                   || (pad != null && pad.buttonSouth.wasPressedThisFrame);
        }

        static bool KeyboardCancel()
        {
            var kb = Keyboard.current; var pad = Gamepad.current;
            return (kb != null && kb.escapeKey.wasPressedThisFrame) || (pad != null && pad.buttonEast.wasPressedThisFrame);
        }

        // ==================== строительные блоки ====================

        sealed class Bar
        {
            public Image bg, fill, centre; public TMP_Text label, value; public bool bipolar;

            public void Set(float v)
            {
                var rt = fill.rectTransform;
                if (bipolar)
                {
                    v = Mathf.Clamp(v, -1f, 1f);
                    rt.anchorMin = new Vector2(0.5f + Mathf.Min(0f, v) * 0.5f, 0f);
                    rt.anchorMax = new Vector2(0.5f + Mathf.Max(0f, v) * 0.5f, 1f);
                }
                else { rt.anchorMin = Vector2.zero; rt.anchorMax = new Vector2(Mathf.Clamp01(v), 1f); }
                rt.offsetMin = rt.offsetMax = Vector2.zero;
            }

            public void Paint(UITheme t)
            {
                bg.color = t.bgCard; fill.color = t.accent;
                if (centre != null) centre.color = t.text2;
                if (label != null) label.color = t.text2;
                if (value != null) value.color = t.text;
            }
        }

        Bar MakeBar(string name, float x, float y, float w, bool bipolar, string caption)
        {
            var bar = new Bar { bipolar = bipolar };
            if (!string.IsNullOrEmpty(caption))
            {
                bar.label = BodyText(name + "Label", 20); Place(bar.label.rectTransform, x, y, w * 0.6f, 30); bar.label.text = caption;
                bar.value = BodyText(name + "Value", 20); Place(bar.value.rectTransform, x + w * 0.6f, y, w * 0.4f, 30);
                bar.value.alignment = TextAlignmentOptions.Right;
                y += 36;
            }
            bar.bg = BodyBox(name + "Bg", x, y, w, 26);
            bar.fill = Box("Fill", bar.bg.transform, Color.white);
            if (bipolar)
            {
                bar.centre = Box("Centre", bar.bg.transform, Color.white);
                var c = bar.centre.rectTransform;
                c.anchorMin = new Vector2(0.5f, 0f); c.anchorMax = new Vector2(0.5f, 1f); c.sizeDelta = new Vector2(2f, 0f); c.anchoredPosition = Vector2.zero;
            }
            bar.Set(0f);
            return bar;
        }

        TMP_Text BodyText(string name, float size, FontStyles style = FontStyles.Normal)
        {
            var t = Text(name, body, size, style);
            bodyObjects.Add(t.gameObject);
            return t;
        }

        Image BodyBox(string name, float x, float y, float w, float h)
        {
            var b = Box(name, body, Color.gray);
            Place(b.rectTransform, x, y, w, h);
            bodyObjects.Add(b.gameObject);
            return b;
        }

        static Image Box(string name, Transform parent, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = c;
            return img;
        }

        TMP_Text Text(string name, Transform parent, float size, FontStyles style = FontStyles.Normal, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.fontSize = size; t.fontStyle = style; t.alignment = align;
            t.textWrappingMode = TextWrappingModes.NoWrap; t.raycastTarget = false;
            t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        /// <summary>Прямоугольник от левого верхнего угла родителя.</summary>
        static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(w, h);
        }

        static void Stretch(RectTransform rt, float padX = 0f, float padY = 0f)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(padX, padY); rt.offsetMax = new Vector2(-padX, -padY);
        }
    }
}
