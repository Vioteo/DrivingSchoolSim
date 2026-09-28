using System.Collections.Generic;
using System.Text;
using DrivingSchool.Input;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace DrivingSchool.Presentation
{
    /// <summary>
    /// F11 — проверка руля (T42): найден ли руль и выбран ли он в настройках, живые значения осей, какие кнопки нажаты
    /// и какое действие на них висит. По нему сверяют раскладку wheel-g29.json с реальным G29 и шифтером.
    /// </summary>
    public sealed class WheelDiagnosticsOverlay : MonoBehaviour
    {
        bool visible;
        GUIStyle style;
        readonly StringBuilder sb = new StringBuilder();
        readonly Dictionary<string, string> actionByControl = new Dictionary<string, string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (FindAnyObjectByType<WheelDiagnosticsOverlay>() != null) return;
            var go = new GameObject("WheelDiagnosticsOverlay");
            DontDestroyOnLoad(go);
            go.AddComponent<WheelDiagnosticsOverlay>();
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f11Key.wasPressedThisFrame)
            {
                visible = !visible;
                if (visible) { WheelProfile.Reload(); Index(); Debug.Log("[Wheel] " + WheelDevice.Describe(WheelDevice.Current)); }
            }
        }

        void Index()
        {
            actionByControl.Clear();
            foreach (var b in WheelProfile.Current.buttons)
                if (b != null && !string.IsNullOrEmpty(b.control))
                    foreach (var path in b.control.Split('|'))
                        actionByControl[path.Trim()] = b.action;
        }

        void OnGUI()
        {
            if (!visible) return;
            if (style == null) style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 15, richText = true, wordWrap = true };
            var p = WheelProfile.Current;
            var dev = WheelDevice.Current;
            sb.Clear();
            sb.AppendLine("<b>Руль — проверка (F11)</b>");
            sb.AppendLine("Раскладка: " + WheelProfile.FilePath);
            sb.AppendLine("Выбран в настройках: " + (WheelDevice.Enabled ? "да" : "нет (Настройки → Управление → Устройство ввода)")
                          + "   Подсказки уроков: " + (LessonControls.UsingWheel ? "руль" : "клавиатура"));
            if (dev == null)
            {
                sb.AppendLine("<color=#ff8080>Руль G29 не найден.</color> Устройства:");
                foreach (var d in InputSystem.devices)
                {
                    WheelDevice.TryIds(d.description.capabilities, out int vid, out int pid);
                    sb.AppendLine($"  {d.displayName} [{d.layout}] VID={vid:X4} PID={pid:X4}");
                }
            }
            else
            {
                string first = WheelDevice.Describe(dev); int nl = first.IndexOf('\n');
                sb.AppendLine(nl > 0 ? first.Substring(0, nl) : first);
                Axis("Руль", p.steering, true);
                Axis("Газ", p.throttle, false);
                Axis("Тормоз", p.brake, false);
                Axis("Сцепление", p.clutch, false);
                sb.Append("Нажато: ");
                bool any = false;
                foreach (var c in dev.allControls)
                {
                    if (!(c is ButtonControl b) || c.children.Count > 0 || !b.isPressed) continue;
                    string rel = c.path.Substring(dev.path.Length + 1);
                    if (rel.StartsWith("stick/")) continue;
                    actionByControl.TryGetValue(rel, out string act);
                    sb.Append(rel).Append(act != null ? " → " + act : "").Append("   ");
                    any = true;
                }
                sb.AppendLine(any ? "" : "ничего");
            }
            GUI.Box(new Rect(10, 10, Mathf.Min(760, Screen.width - 20), 330), sb.ToString(), style);
        }

        void Axis(string title, WheelAxisBinding bind, bool bipolar)
        {
            var c = WheelDevice.Control(bind.control);
            if (c == null) { sb.AppendLine($"{title}: <color=#ff8080>нет контрола «{bind.control}»</color>"); return; }
            float raw = c is AxisControl a ? a.ReadValue() : 0f;
            float v = bipolar ? WheelDevice.Bipolar(c, bind.releasedEnd) : WheelDevice.PedalFraction(c, bind.releasedEnd);
            sb.AppendLine($"{title}: {c.name}  сырое {raw:+0.000;-0.000}  →  {(bipolar ? v.ToString("+0.00;-0.00") : (v * 100f).ToString("0") + " %")}");
        }
    }
}
