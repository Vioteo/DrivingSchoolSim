using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DrivingSchool.Input
{
    /// <summary>Кнопка руля: путь контрола Input System (варианты через «|») и подпись для подсказок урока.</summary>
    [Serializable]
    public sealed class WheelButtonBinding
    {
        public string action;
        public string control;
        public string label;

        public WheelButtonBinding() { }
        public WheelButtonBinding(DriveAction a, string control, string label) { action = a.ToString(); this.control = control; this.label = label; }
    }

    /// <summary>
    /// Ось руля или педали. Если <see cref="calibrated"/> — значения сняты калибровкой: у педали rawReleased — отпущена,
    /// rawPressed — выжата до упора; у руля rawReleased — центр, rawPressed — упор вправо.
    /// Без калибровки <see cref="releasedEnd"/>: 0 — по параметрам контрола, 1 — отпущенная педаль у максимума, −1 — у минимума.
    /// </summary>
    [Serializable]
    public sealed class WheelAxisBinding
    {
        public string control;
        public int releasedEnd;
        public string label;
        public bool calibrated;
        public float rawReleased, rawPressed;

        public WheelAxisBinding() { }
        public WheelAxisBinding(string control, string label, int releasedEnd = 0) { this.control = control; this.label = label; this.releasedEnd = releasedEnd; }
    }

    /// <summary>
    /// Раскладка руля как данные (T42, architecture.md §«Ввод»): оси, кнопки и подписи для уроков.
    /// Лежит в Application.persistentDataPath/wheel-g29.json; если файла нет — пишется раскладка по умолчанию,
    /// её можно править руками (перезапуск поездки подхватит). Раскладка по умолчанию — в духе City Car Driving:
    /// поворотники на лепестках, передачи на H-шифтере, педали — газ/тормоз/сцепление. Оси педалей и шифтер уточняются
    /// калибровкой (Настройки → Управление → Калибровка), кнопки — экраном «Переназначение кнопок».
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
            new WheelButtonBinding(DriveAction.LeftSignal, "button6", "левый лепесток"),
            new WheelButtonBinding(DriveAction.RightSignal, "button5", "правый лепесток"),
            new WheelButtonBinding(DriveAction.Ignition, "button4", "треугольник"),
            new WheelButtonBinding(DriveAction.Starter, "button3", "кружок"),
            new WheelButtonBinding(DriveAction.Belt, "button2", "квадрат"),
            new WheelButtonBinding(DriveAction.Handbrake, "button1|trigger", "крестик"),
            new WheelButtonBinding(DriveAction.Lights, "button7", "R2"),
            new WheelButtonBinding(DriveAction.HighBeam, "button12", "L3"),
            new WheelButtonBinding(DriveAction.Flash, "hat/up", "крестовина ↑"),
            new WheelButtonBinding(DriveAction.Hazard, "button8", "L2"),
            new WheelButtonBinding(DriveAction.Horn, "button11", "R3"),
            new WheelButtonBinding(DriveAction.Wipers, "button21", "кнопка «-»"),
            new WheelButtonBinding(DriveAction.Washer, "button20", "кнопка «+»"),
            new WheelButtonBinding(DriveAction.Park, "button24", "красная кнопка"),
            new WheelButtonBinding(DriveAction.Neutral, "", ""),
            new WheelButtonBinding(DriveAction.Gear1, "button13", "рычаг КПП в 1"),
            new WheelButtonBinding(DriveAction.Gear2, "button14", "рычаг КПП во 2"),
            new WheelButtonBinding(DriveAction.Gear3, "button15", "рычаг КПП в 3"),
            new WheelButtonBinding(DriveAction.Gear4, "button16", "рычаг КПП в 4"),
            new WheelButtonBinding(DriveAction.Gear5, "button17", "рычаг КПП в 5"),
            new WheelButtonBinding(DriveAction.Gear6, "button18", "рычаг КПП в 6"),
            new WheelButtonBinding(DriveAction.Reverse, "button19", "рычаг КПП в R"),
            new WheelButtonBinding(DriveAction.Camera, "button9", "SHARE"),
            new WheelButtonBinding(DriveAction.LookLeft, "hat/left", "крестовина ←"),
            new WheelButtonBinding(DriveAction.LookRight, "hat/right", "крестовина →"),
            new WheelButtonBinding(DriveAction.Pause, "button10", "OPTIONS"),
        };

        public WheelButtonBinding Find(DriveAction action)
        {
            if (buttons == null) return null;
            string key = action.ToString();
            foreach (var b in buttons)
                if (b != null && string.Equals(b.action, key, StringComparison.OrdinalIgnoreCase)) return b;
            return null;
        }

        /// <summary>Назначить кнопку действию; если она уже занята другим — у того снимается (возвращается его имя).</summary>
        public DriveAction? Assign(DriveAction action, string control)
        {
            DriveAction? taken = null;
            if (!string.IsNullOrEmpty(control))
                foreach (var b in buttons)
                    if (b != null && b.action != action.ToString() && b.control == control)
                    {
                        b.control = ""; b.label = "";
                        if (Enum.TryParse(b.action, out DriveAction other)) taken = other;
                    }
            var mine = Find(action);
            if (mine == null) buttons.Add(mine = new WheelButtonBinding(action, "", ""));
            mine.control = control ?? "";
            mine.label = string.IsNullOrEmpty(control) ? "" : FriendlyName(control);
            return taken;
        }

        /// <summary>Имя кнопки G29 по пути Input System (HID: кнопка 1 — «trigger», дальше buttonN; крестовина — hat).</summary>
        public static string FriendlyName(string control)
        {
            if (string.IsNullOrEmpty(control)) return "";
            string c = control.Split('|')[0].Trim();
            switch (c)
            {
                case "trigger": case "button1": return "крестик";
                case "button2": return "квадрат";
                case "button3": return "кружок";
                case "button4": return "треугольник";
                case "button5": return "правый лепесток";
                case "button6": return "левый лепесток";
                case "button7": return "R2";
                case "button8": return "L2";
                case "button9": return "SHARE";
                case "button10": return "OPTIONS";
                case "button11": return "R3";
                case "button12": return "L3";
                case "button13": return "рычаг КПП в 1";
                case "button14": return "рычаг КПП во 2";
                case "button15": return "рычаг КПП в 3";
                case "button16": return "рычаг КПП в 4";
                case "button17": return "рычаг КПП в 5";
                case "button18": return "рычаг КПП в 6";
                case "button19": return "рычаг КПП в R";
                case "button20": return "кнопка «+»";
                case "button21": return "кнопка «-»";
                case "button22": return "колесо-селектор вправо";
                case "button23": return "колесо-селектор влево";
                case "button24": return "красная кнопка";
                case "button25": return "PS";
                case "hat/up": return "крестовина ↑";
                case "hat/down": return "крестовина ↓";
                case "hat/left": return "крестовина ←";
                case "hat/right": return "крестовина →";
            }
            if (c.StartsWith("button")) return "кнопка " + c.Substring(6);
            return c;
        }

        /// <summary>
        /// Подпись кнопки для текста урока; null — действие не назначено. Всегда по имени кнопки (<see cref="FriendlyName"/>),
        /// а не по полю label из файла: в старых файлах там значки ✕ □ ○ △, которых нет в шрифте интерфейса.
        /// </summary>
        public string Label(DriveAction action)
        {
            var b = Find(action);
            return b == null || string.IsNullOrEmpty(b.control) ? null : FriendlyName(b.control);
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
            foreach (DriveAction a in Enum.GetValues(typeof(DriveAction)))
                if (!DriveActions.IsAxis(a) && Find(a) == null && d.Find(a) != null) buttons.Add(d.Find(a));
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

        public static void Save(WheelProfile p)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonUtility.ToJson(p, true));
                loaded = p;
            }
            catch (Exception e) { Debug.LogError($"[Wheel] не удалось сохранить {FilePath}: {e.Message}"); }
        }

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
