using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Layouts;

namespace DrivingSchool.Input
{
    /// <summary>
    /// Руль, подключённый к компьютеру (T42): поиск устройства по VID/PID из <see cref="WheelProfile"/> (не по похожему имени),
    /// контролы по путям из раскладки, кнопки действий для остального кода (камера, пауза).
    /// <see cref="Enabled"/> — руль выбран в настройках («Устройство ввода»); без этого кнопки руля ничего не делают.
    /// </summary>
    public static class WheelDevice
    {
        /// <summary>Руль выбран устройством ввода в настройках.</summary>
        public static bool Enabled { get; set; }

        static InputDevice cached;
        static double nextScan;
        static readonly Dictionary<string, InputControl> controls = new Dictionary<string, InputControl>();
        static readonly Dictionary<InputControl, bool> prevPressed = new Dictionary<InputControl, bool>();
        static int loggedDeviceId = -1;

        /// <summary>Найденный руль или null (независимо от <see cref="Enabled"/>).</summary>
        public static InputDevice Current
        {
            get
            {
                if (cached != null && cached.added) return cached;
                if (cached != null) { cached = null; controls.Clear(); prevPressed.Clear(); }
                double now = Time.realtimeSinceStartupAsDouble;   // не сбрасывается между запусками Play Mode
                if (now < nextScan) return null;
                nextScan = now + 1f;
                cached = Find(WheelProfile.Current);
                if (cached != null && cached.deviceId != loggedDeviceId) { loggedDeviceId = cached.deviceId; Debug.Log("[Wheel] найден руль\n" + Describe(cached)); }
                return cached;
            }
        }

        /// <summary>Сброс статики при входе в Play Mode без перезагрузки домена.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            cached = null; nextScan = 0; controls.Clear(); prevPressed.Clear(); loggedDeviceId = -1;
            WheelProfile.Reload();
        }

        /// <summary>Руль выбран в настройках и подключён.</summary>
        public static bool Active => Enabled && Current != null;

        public static InputDevice Find(WheelProfile profile)
        {
            foreach (var d in InputSystem.devices)
                if (!(d is Keyboard) && !(d is Mouse) && Matches(d.description, profile)) return d;
            return null;
        }

        public static bool Matches(InputDeviceDescription d, WheelProfile p) => Matches(d.product, d.capabilities, p);

        /// <summary>Устройство с раскладкой профиля: по VID/PID, а без них в описании — по названию продукта.</summary>
        public static bool Matches(string product, string capabilities, WheelProfile p)
        {
            if (p == null) return false;
            if (TryIds(capabilities, out int vid, out int pid))
            {
                if (vid != p.vendorId || p.productIds == null) return false;
                return Array.IndexOf(p.productIds, pid) >= 0;
            }
            product = product ?? "";
            if (p.productNames != null)
                foreach (var n in p.productNames)
                    if (!string.IsNullOrEmpty(n) && product.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        /// <summary>VID/PID из JSON-описания HID-устройства ("vendorId":1133,"productId":49743).</summary>
        public static bool TryIds(string capabilities, out int vendorId, out int productId)
        {
            vendorId = productId = 0;
            return !string.IsNullOrEmpty(capabilities)
                && TryInt(capabilities, "\"vendorId\"", out vendorId)
                && TryInt(capabilities, "\"productId\"", out productId);
        }

        static bool TryInt(string json, string key, out int value)
        {
            value = 0;
            int i = json.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) return false;
            i = json.IndexOf(':', i + key.Length);
            if (i < 0) return false;
            i++;
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
            int start = i;
            while (i < json.Length && char.IsDigit(json[i])) i++;
            return i > start && int.TryParse(json.Substring(start, i - start), out value);
        }

        /// <summary>Контрол руля по пути из раскладки («button5», «stick/x|x» — первый найденный вариант).</summary>
        public static InputControl Control(string paths)
        {
            var dev = Current;
            if (dev == null || string.IsNullOrEmpty(paths)) return null;
            if (controls.TryGetValue(paths, out var c)) return c;
            foreach (var p in paths.Split('|'))
            {
                var path = p.Trim();
                if (path.Length == 0) continue;
                c = dev.TryGetChildControl(path);
                if (c != null) break;
            }
            if (c == null) Debug.LogWarning($"[Wheel] у «{dev.displayName}» нет контрола «{paths}» — проверьте {WheelProfile.FileName} (F11 — список контролов)");
            controls[paths] = c;
            return c;
        }

        public static InputControl Control(WheelAction action)
        {
            var b = WheelProfile.Current.Find(action);
            return b == null ? null : Control(b.control);
        }

        public static bool IsPressed(InputControl c) => c != null && c.IsPressed();

        /// <summary>Нажат в этом кадре. Для не-кнопок — по прошлому опросу (вызывать раз в кадр).</summary>
        public static bool WasPressed(InputControl c)
        {
            if (c == null) return false;
            if (c is ButtonControl b) return b.wasPressedThisFrame;
            bool now = c.IsPressed();
            prevPressed.TryGetValue(c, out bool before);
            prevPressed[c] = now;
            return now && !before;
        }

        /// <summary>Кнопка действия нажата (только если руль выбран в настройках).</summary>
        public static bool IsPressed(WheelAction a) => Active && IsPressed(Control(a));

        /// <summary>Кнопка действия нажата в этом кадре (только если руль выбран в настройках).</summary>
        public static bool WasPressed(WheelAction a) => Active && WasPressed(Control(a));

        // ---------- оси ----------

        /// <summary>
        /// Доля нажатия педали 0…1. Диапазон контрола берётся из его нормализации (Input System даёт −1…1 или 0…1);
        /// отпущенный конец — из раскладки или, при 0, из флага invert контрола (без инверсии отпущенная педаль у максимума).
        /// </summary>
        public static float PedalFraction(InputControl c, int releasedEnd)
        {
            if (!(c is AxisControl a)) return c is ButtonControl bc && bc.isPressed ? 1f : 0f;
            Range(a, out float lo, out float hi);
            float v = Mathf.Clamp(a.ReadValue(), lo, hi);
            int end = releasedEnd != 0 ? releasedEnd : (a.invert ? -1 : 1);
            float t = end > 0 ? (hi - v) / (hi - lo) : (v - lo) / (hi - lo);
            return Mathf.Clamp01(t);
        }

        /// <summary>Положение руля −1…1 (плюс — вправо).</summary>
        public static float Bipolar(InputControl c, int rightEnd)
        {
            if (!(c is AxisControl a)) return 0f;
            Range(a, out float lo, out float hi);
            float v = Mathf.Clamp(a.ReadValue(), lo, hi);
            float s = (v - (lo + hi) * 0.5f) / ((hi - lo) * 0.5f);
            if (rightEnd < 0) s = -s;
            return Mathf.Clamp(s, -1f, 1f);
        }

        static void Range(AxisControl a, out float lo, out float hi)
        {
            if (a.normalize) { lo = a.normalizeZero > a.normalizeMin + 1e-4f ? -1f : 0f; hi = 1f; }
            else if (a.clamp != AxisControl.Clamp.None && a.clampMax > a.clampMin) { lo = a.clampMin; hi = a.clampMax; }
            else { lo = -1f; hi = 1f; }
        }

        // ---------- диагностика ----------

        public static string Describe(InputDevice d)
        {
            if (d == null) return "руль не найден";
            var sb = new StringBuilder();
            TryIds(d.description.capabilities, out int vid, out int pid);
            sb.Append(d.displayName).Append(" | product=").Append(d.description.product)
              .Append(" | VID=").Append(vid.ToString("X4")).Append(" PID=").Append(pid.ToString("X4"))
              .Append(" | layout=").Append(d.layout).Append('\n');
            foreach (var c in d.allControls)
            {
                if (c.children.Count > 0) continue;   // только листья: stick/x, hat/up, button5…
                sb.Append(c.path.Substring(d.path.Length + 1)).Append(c is AxisControl ax && !(c is ButtonControl)
                    ? $"(axis norm={ax.normalize} min={ax.normalizeMin} zero={ax.normalizeZero} inv={ax.invert})" : "").Append("  ");
            }
            return sb.ToString();
        }
    }
}
