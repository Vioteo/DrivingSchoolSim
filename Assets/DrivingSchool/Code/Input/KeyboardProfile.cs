using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DrivingSchool.Input
{
    /// <summary>Клавиша действия: основная и запасная (имена <see cref="Key"/>, «None» — нет).</summary>
    [Serializable]
    public sealed class KeyBinding
    {
        public string action;
        public string key;
        public string alt;

        public KeyBinding() { }
        public KeyBinding(DriveAction a, Key key, Key alt = Key.None) { action = a.ToString(); this.key = key.ToString(); this.alt = alt.ToString(); }
    }

    /// <summary>
    /// Клавиши поездки как данные (T42): Application.persistentDataPath/keyboard.json, меняются на экране
    /// «Переназначение кнопок». По умолчанию — прежняя раскладка (docs/vehicle-test-range.md).
    /// </summary>
    [Serializable]
    public sealed class KeyboardProfile
    {
        public const int CurrentVersion = 1;
        public const string FileName = "keyboard.json";

        public int version = CurrentVersion;
        public List<KeyBinding> keys = Defaults();

        public static List<KeyBinding> Defaults() => new List<KeyBinding>
        {
            new KeyBinding(DriveAction.Gas, Key.W, Key.UpArrow),
            new KeyBinding(DriveAction.Brake, Key.S, Key.DownArrow),
            new KeyBinding(DriveAction.SteerLeft, Key.A, Key.LeftArrow),
            new KeyBinding(DriveAction.SteerRight, Key.D, Key.RightArrow),
            new KeyBinding(DriveAction.Clutch, Key.LeftShift, Key.LeftCtrl),
            new KeyBinding(DriveAction.Ignition, Key.I),
            new KeyBinding(DriveAction.Starter, Key.Enter, Key.NumpadEnter),
            new KeyBinding(DriveAction.Handbrake, Key.Space),
            new KeyBinding(DriveAction.Belt, Key.T),
            new KeyBinding(DriveAction.LeftSignal, Key.Q),
            new KeyBinding(DriveAction.RightSignal, Key.E),
            new KeyBinding(DriveAction.Hazard, Key.X),
            new KeyBinding(DriveAction.Lights, Key.L),
            new KeyBinding(DriveAction.HighBeam, Key.K),
            new KeyBinding(DriveAction.Flash, Key.J),
            new KeyBinding(DriveAction.Horn, Key.H),
            new KeyBinding(DriveAction.Wipers, Key.V),
            new KeyBinding(DriveAction.Washer, Key.B),
            new KeyBinding(DriveAction.Park, Key.P),
            new KeyBinding(DriveAction.Neutral, Key.N, Key.Digit0),
            new KeyBinding(DriveAction.Gear1, Key.Digit1),
            new KeyBinding(DriveAction.Gear2, Key.Digit2),
            new KeyBinding(DriveAction.Gear3, Key.Digit3),
            new KeyBinding(DriveAction.Gear4, Key.Digit4),
            new KeyBinding(DriveAction.Gear5, Key.Digit5),
            new KeyBinding(DriveAction.Gear6, Key.Digit6),
            new KeyBinding(DriveAction.Reverse, Key.R),
            new KeyBinding(DriveAction.Camera, Key.C),
            new KeyBinding(DriveAction.LookLeft, Key.Z, Key.Comma),
            new KeyBinding(DriveAction.LookRight, Key.Period),
            new KeyBinding(DriveAction.Pause, Key.Escape),
        };

        public KeyBinding Find(DriveAction a)
        {
            string name = a.ToString();
            if (keys != null) foreach (var k in keys) if (k != null && k.action == name) return k;
            return null;
        }

        public Key Primary(DriveAction a) => Parse(Find(a)?.key);
        public Key Alternate(DriveAction a) => Parse(Find(a)?.alt);

        public static Key Parse(string s) => !string.IsNullOrEmpty(s) && Enum.TryParse(s, out Key k) ? k : Key.None;

        /// <summary>Назначить основную клавишу. Если она занята другим действием — у того она снимается (возвращается его имя).</summary>
        public DriveAction? Assign(DriveAction a, Key key)
        {
            DriveAction? taken = null;
            if (key != Key.None)
                foreach (var b in keys)
                {
                    if (b == null || b.action == a.ToString()) continue;
                    if (Parse(b.key) == key) { b.key = Key.None.ToString(); taken = (DriveAction)Enum.Parse(typeof(DriveAction), b.action); }
                    if (Parse(b.alt) == key) { b.alt = Key.None.ToString(); taken = (DriveAction)Enum.Parse(typeof(DriveAction), b.action); }
                }
            var mine = Find(a);
            if (mine == null) keys.Add(mine = new KeyBinding(a, Key.None));
            if (Parse(mine.alt) == key) mine.alt = Key.None.ToString();
            mine.key = key.ToString();
            return taken;
        }

        public bool Held(Keyboard kb, DriveAction a) => kb != null && (IsHeld(kb, Primary(a)) || IsHeld(kb, Alternate(a)));
        public bool Down(Keyboard kb, DriveAction a) => kb != null && (IsDown(kb, Primary(a)) || IsDown(kb, Alternate(a)));

        static bool IsHeld(Keyboard kb, Key k) => k != Key.None && kb[k].isPressed;
        static bool IsDown(Keyboard kb, Key k) => k != Key.None && kb[k].wasPressedThisFrame;

        /// <summary>Подпись для подсказки: «W», «Пробел», «Shift»; с запасной — только основная.</summary>
        public string Label(DriveAction a) { var k = Primary(a); return k == Key.None ? null : KeyLabel(k); }

        public static string KeyLabel(Key k)
        {
            if (k >= Key.A && k <= Key.Z) return k.ToString();
            if (k >= Key.Digit1 && k <= Key.Digit9) return ((int)(k - Key.Digit1) + 1).ToString();
            if (k >= Key.F1 && k <= Key.F12) return k.ToString();
            if (k >= Key.Numpad0 && k <= Key.Numpad9) return "Num " + (int)(k - Key.Numpad0);
            switch (k)
            {
                case Key.Digit0: return "0";
                case Key.Space: return "Пробел";
                case Key.Enter: return "Enter";
                case Key.NumpadEnter: return "Num Enter";
                case Key.Escape: return "Esc";
                case Key.Tab: return "Tab";
                case Key.Backspace: return "Backspace";
                case Key.LeftShift: case Key.RightShift: return "Shift";
                case Key.LeftCtrl: case Key.RightCtrl: return "Ctrl";
                case Key.LeftAlt: case Key.RightAlt: return "Alt";
                case Key.UpArrow: return "↑";
                case Key.DownArrow: return "↓";
                case Key.LeftArrow: return "←";
                case Key.RightArrow: return "→";
                case Key.Comma: return ",";
                case Key.Period: return ".";
                case Key.Slash: return "/";
                case Key.Semicolon: return ";";
                case Key.Quote: return "'";
                case Key.LeftBracket: return "[";
                case Key.RightBracket: return "]";
                case Key.Minus: return "-";
                case Key.Equals: return "=";
                case Key.Backquote: return "`";
                case Key.Home: return "Home";
                case Key.End: return "End";
                case Key.PageUp: return "PgUp";
                case Key.PageDown: return "PgDn";
                case Key.Insert: return "Ins";
                case Key.Delete: return "Del";
                default: return k.ToString();
            }
        }

        public KeyboardProfile FillMissing()
        {
            if (keys == null) keys = new List<KeyBinding>();
            var d = new KeyboardProfile();
            foreach (DriveAction a in Enum.GetValues(typeof(DriveAction)))
                if (Find(a) == null) keys.Add(d.Find(a));
            return this;
        }

        // ---------- файл ----------

        static KeyboardProfile loaded;
        static string overridePath;

        public static string FilePath
        {
            get => overridePath ?? Path.Combine(Application.persistentDataPath, FileName);
            set { overridePath = value; loaded = null; }
        }

        public static KeyboardProfile Current => loaded ?? (loaded = Load());
        public static void Reload() => loaded = null;

        public static KeyboardProfile Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var p = JsonUtility.FromJson<KeyboardProfile>(File.ReadAllText(FilePath));
                    if (p != null && p.version >= CurrentVersion) return p.FillMissing();
                }
            }
            catch (Exception e) { Debug.LogWarning($"[Keys] {FilePath} не читается ({e.Message}) — клавиши по умолчанию"); }
            return new KeyboardProfile();
        }

        public static void Save(KeyboardProfile p)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonUtility.ToJson(p, true));
                loaded = p;
            }
            catch (Exception e) { Debug.LogError($"[Keys] не удалось сохранить {FilePath}: {e.Message}"); }
        }
    }
}
