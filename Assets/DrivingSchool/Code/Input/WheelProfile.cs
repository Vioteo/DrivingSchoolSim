using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DrivingSchool.Input
{
    /// <summary>Действия, которые можно повесить на кнопку руля (T42).</summary>
    public enum WheelAction
    {
        Ignition, Starter, Handbrake, Belt, LeftSignal, RightSignal, Hazard, Lights, HighBeam, Flash, Horn, Wipers, Washer,
        Park, Gear1, Gear2, Gear3, Gear4, Gear5, Gear6, Reverse, Camera, LookLeft, LookRight, Pause,
    }

    /// <summary>Кнопка руля: путь контрола Input System (варианты через «|») и подпись для подсказок урока.</summary>
    [Serializable]
    public sealed class WheelButtonBinding
    {
        public string action;
        public string control;
        public string label;

        public WheelButtonBinding() { }
        public WheelButtonBinding(WheelAction a, string control, string label) { action = a.ToString(); this.control = control; this.label = label; }
    }

    /// <summary>
    /// Ось руля или педали. <see cref="releasedEnd"/>: 0 — определить по параметрам контрола, 1 — отпущенная педаль
    /// у максимума, −1 — у минимума. Для руля 1 = «вправо — к максимуму», −1 — наоборот.
    /// </summary>
    [Serializable]
    public sealed class WheelAxisBinding
    {
        public string control;
        public int releasedEnd;
        public string label;

        public WheelAxisBinding() { }
        public WheelAxisBinding(string control, string label, int releasedEnd = 0) { this.control = control; this.label = label; this.releasedEnd = releasedEnd; }
    }

    /// <summary>
    /// Раскладка руля как данные (T42, architecture.md §«Ввод»): оси, кнопки и подписи для уроков.
    /// Лежит в Application.persistentDataPath/wheel-g29.json; если файла нет — пишется раскладка по умолчанию,
    /// её можно править руками (перезапуск поездки подхватит). Раскладка по умолчанию — в духе City Car Driving:
    /// поворотники на лепестках, передачи на H-шифтере, педали — газ/тормоз/сцепление.
    /// Номера кнопок G29 (HID, с единицы): 1 ✕, 2 □, 3 ○, 4 △, 5 правый лепесток, 6 левый лепесток, 7 R2, 8 L2,
    /// 9 SHARE, 10 OPTIONS, 11 R3, 12 L3, 13–18 шифтер 1–6, 19 шифтер R, 20 «+», 21 «−», 22/23 колесо-селектор,
    /// 24 красная кнопка, 25 PS. Сверить на устройстве — оверлей F11 (WheelDiagnosticsOverlay).
    /// </summary>
    [Serializable]
    public sealed class WheelProfile
    {
        public const int CurrentVersion = 1;
        public const string FileName = "wheel-g29.json";

        public int version = CurrentVersion;
        public string name = "Logitech G29 + Driving Force Shifter (как в City Car Driving)";
        public int vendorId = 0x046D;
        /// <summary>G29 (PS3/PS4-режим) и G923 для PlayStation — у них одинаковые кнопки. G920/G923 Xbox — другая раскладка.</summary>
        public int[] productIds = { 0xC24F, 0xC260, 0xC266, 0xC267 };
        /// <summary>Если у устройства нет VID/PID в описании — искать по имени.</summary>
        public string[] productNames = { "G29 Driving Force", "G923 Racing Wheel for PlayStation" };

        public WheelAxisBinding steering = new WheelAxisBinding("stick/x|x", "руль");
        public WheelAxisBinding throttle = new WheelAxisBinding("z", "педаль газа");
        public WheelAxisBinding brake = new WheelAxisBinding("rz", "педаль тормоза");
        public WheelAxisBinding clutch = new WheelAxisBinding("stick/y|y", "педаль сцепления");

        public List<WheelButtonBinding> buttons = DefaultButtons();

        public static List<WheelButtonBinding> DefaultButtons() => new List<WheelButtonBinding>
        {
            new WheelButtonBinding(WheelAction.LeftSignal, "button6", "левый лепесток"),
            new WheelButtonBinding(WheelAction.RightSignal, "button5", "правый лепесток"),
            new WheelButtonBinding(WheelAction.Ignition, "button4", "△"),
            new WheelButtonBinding(WheelAction.Starter, "button3", "○"),
            new WheelButtonBinding(WheelAction.Belt, "button2", "□"),
            new WheelButtonBinding(WheelAction.Handbrake, "button1|trigger", "✕"),
            new WheelButtonBinding(WheelAction.Lights, "button7", "R2"),
            new WheelButtonBinding(WheelAction.HighBeam, "button12", "L3"),
            new WheelButtonBinding(WheelAction.Flash, "hat/up", "крестовина ↑"),
            new WheelButtonBinding(WheelAction.Hazard, "button8", "L2"),
            new WheelButtonBinding(WheelAction.Horn, "button11", "R3"),
            new WheelButtonBinding(WheelAction.Wipers, "button21", "«−»"),
            new WheelButtonBinding(WheelAction.Washer, "button20", "«+»"),
            new WheelButtonBinding(WheelAction.Park, "button24", "красная кнопка"),
            new WheelButtonBinding(WheelAction.Gear1, "button13", "рычаг КПП в 1"),
            new WheelButtonBinding(WheelAction.Gear2, "button14", "рычаг КПП во 2"),
            new WheelButtonBinding(WheelAction.Gear3, "button15", "рычаг КПП в 3"),
            new WheelButtonBinding(WheelAction.Gear4, "button16", "рычаг КПП в 4"),
            new WheelButtonBinding(WheelAction.Gear5, "button17", "рычаг КПП в 5"),
            new WheelButtonBinding(WheelAction.Gear6, "button18", "рычаг КПП в 6"),
            new WheelButtonBinding(WheelAction.Reverse, "button19", "рычаг КПП в R"),
            new WheelButtonBinding(WheelAction.Camera, "button9", "SHARE"),
            new WheelButtonBinding(WheelAction.LookLeft, "hat/left", "крестовина ←"),
            new WheelButtonBinding(WheelAction.LookRight, "hat/right", "крестовина →"),
            new WheelButtonBinding(WheelAction.Pause, "button10", "OPTIONS"),
        };

        public WheelButtonBinding Find(WheelAction action)
        {
            if (buttons == null) return null;
            string key = action.ToString();
            foreach (var b in buttons)
                if (b != null && string.Equals(b.action, key, StringComparison.OrdinalIgnoreCase)) return b;
            return null;
        }

        /// <summary>Подпись кнопки для текста урока; null — действие не назначено.</summary>
        public string Label(WheelAction action)
        {
            var b = Find(action);
            return b == null || string.IsNullOrEmpty(b.control) ? null : string.IsNullOrEmpty(b.label) ? b.control : b.label;
        }

        /// <summary>Недостающие поля и действия берутся из раскладки по умолчанию.</summary>
        public WheelProfile FillMissing()
        {
            var d = new WheelProfile { buttons = DefaultButtons() };
            if (steering == null || string.IsNullOrEmpty(steering.control)) steering = d.steering;
            if (throttle == null || string.IsNullOrEmpty(throttle.control)) throttle = d.throttle;
            if (brake == null || string.IsNullOrEmpty(brake.control)) brake = d.brake;
            if (clutch == null || string.IsNullOrEmpty(clutch.control)) clutch = d.clutch;
            if (productIds == null) productIds = d.productIds;
            if (productNames == null) productNames = d.productNames;
            if (buttons == null) buttons = new List<WheelButtonBinding>();
            foreach (WheelAction a in Enum.GetValues(typeof(WheelAction)))
                if (Find(a) == null) buttons.Add(d.Find(a));
            return this;
        }

        // ---------- файл ----------

        static WheelProfile loaded;
        static string overridePath;

        public static string FilePath
        {
            get => overridePath ?? Path.Combine(Application.persistentDataPath, FileName);
            set { overridePath = value; loaded = null; }
        }

        /// <summary>Раскладка процесса: читается один раз (Reload — перечитать).</summary>
        public static WheelProfile Current => loaded ?? (loaded = Load());

        public static void Reload() => loaded = null;

        public static WheelProfile Load()
        {
            string path = FilePath;
            try
            {
                if (File.Exists(path))
                {
                    var p = JsonUtility.FromJson<WheelProfile>(File.ReadAllText(path));
                    if (p != null && p.version >= CurrentVersion) return p.FillMissing();
                    Debug.LogWarning($"[Wheel] {path}: старая версия раскладки — записана раскладка по умолчанию");
                }
            }
            catch (Exception e) { Debug.LogWarning($"[Wheel] {path} не читается ({e.Message}) — раскладка по умолчанию"); return new WheelProfile(); }
            var def = new WheelProfile();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonUtility.ToJson(def, true));
            }
            catch (Exception e) { Debug.LogWarning($"[Wheel] не удалось записать {path}: {e.Message}"); }
            return def;
        }
    }
}
