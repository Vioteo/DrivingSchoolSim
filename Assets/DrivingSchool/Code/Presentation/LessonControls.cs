using DrivingSchool.Input;
using DrivingSchool.Learning;

namespace DrivingSchool.Presentation
{
    /// <summary>
    /// Имена органов управления для текстов уроков ({clutch} → «Shift» или «педаль сцепления»).
    /// Если в настройках выбран руль и он подключён (<see cref="WheelDevice.Active"/>), подсказки — для руля:
    /// подписи кнопок берутся из раскладки <see cref="WheelProfile"/> (её можно поправить в wheel-g29.json).
    /// Иначе — клавиатура: имена клавиш из <see cref="KeyboardProfile"/> (keyboard.json).
    /// </summary>
    public static class LessonControls
    {
        /// <summary>Подсказки для руля (руль выбран в настройках и подключён).</summary>
        public static bool UsingWheel => WheelDevice.Active;

        public static string KeyName(string key) => KeyName(key, UsingWheel);

        public static string KeyName(string key, bool wheel) => wheel ? WheelName(key, WheelProfile.Current) : KeyboardName(key);

        public static string KeyboardName(string key) => KeyboardName(key, KeyboardProfile.Current);

        /// <summary>Имена клавиш из раскладки клавиатуры (keyboard.json).</summary>
        public static string KeyboardName(string key, KeyboardProfile k)
        {
            switch (key)
            {
                case "belt": return k.Label(DriveAction.Belt) ?? "—";
                case "ignition": return k.Label(DriveAction.Ignition) ?? "—";
                case "starter": return k.Label(DriveAction.Starter) ?? "—";
                case "clutch": return k.Label(DriveAction.Clutch) ?? "—";
                case "gas": return k.Label(DriveAction.Gas) ?? "—";
                case "brake": return k.Label(DriveAction.Brake) ?? "—";
                case "handbrake": return k.Label(DriveAction.Handbrake) ?? "—";
                case "left": return k.Label(DriveAction.LeftSignal) ?? "—";
                case "right": return k.Label(DriveAction.RightSignal) ?? "—";
                case "lights": { var l = k.Label(DriveAction.Lights); return l == null ? "—" : l + " ×2"; }
                case "gear1": return k.Label(DriveAction.Gear1) ?? "—";
                case "gear2": return k.Label(DriveAction.Gear2) ?? "—";
                case "reverse": return k.Label(DriveAction.Reverse) ?? "—";
                case "neutral": return k.Label(DriveAction.Neutral) ?? "—";
                case "drive": return k.Label(DriveAction.Gear1) ?? "—";
                case "park": return k.Label(DriveAction.Park) ?? "—";
                case "steer": { var l = k.Label(DriveAction.SteerLeft); var r = k.Label(DriveAction.SteerRight); return (l ?? "—") + " / " + (r ?? "—"); }
                case "steer_left": return k.Label(DriveAction.SteerLeft) ?? "—";
                case "steer_right": return k.Label(DriveAction.SteerRight) ?? "—";
                case "horn": return k.Label(DriveAction.Horn) ?? "—";
                case "hazard": return k.Label(DriveAction.Hazard) ?? "—";
                default: return null;
            }
        }

        /// <summary>Имена для руля G29 + шифтер; кнопки — из раскладки, педали и рычаг — словами.</summary>
        public static string WheelName(string key, WheelProfile p)
        {
            switch (key)
            {
                case "belt": return p.Label(DriveAction.Belt) ?? KeyboardName(key);
                case "ignition": return p.Label(DriveAction.Ignition) ?? KeyboardName(key);
                case "starter": return p.Label(DriveAction.Starter) ?? KeyboardName(key);
                case "handbrake": return p.Label(DriveAction.Handbrake) ?? KeyboardName(key);
                case "left": return p.Label(DriveAction.LeftSignal) ?? KeyboardName(key);
                case "right": return p.Label(DriveAction.RightSignal) ?? KeyboardName(key);
                case "lights": { var l = p.Label(DriveAction.Lights); return l == null ? KeyboardName(key) : l + " ×2"; }
                case "horn": return p.Label(DriveAction.Horn) ?? KeyboardName(key);
                case "hazard": return p.Label(DriveAction.Hazard) ?? KeyboardName(key);
                case "park": return p.Label(DriveAction.Park) ?? KeyboardName(key);
                case "clutch": return p.clutch?.label ?? "педаль сцепления";
                case "gas": return p.throttle?.label ?? "педаль газа";
                case "brake": return p.brake?.label ?? "педаль тормоза";
                case "gear1": return p.Label(DriveAction.Gear1) ?? KeyboardName(key);
                case "gear2": return p.Label(DriveAction.Gear2) ?? KeyboardName(key);
                case "reverse": return p.Label(DriveAction.Reverse) ?? KeyboardName(key);
                case "neutral": return p.Label(DriveAction.Neutral) ?? "рычаг КПП в нейтраль";
                case "drive": { var g = p.Label(DriveAction.Gear1); return g == null ? KeyboardName(key) : g + " (D)"; }
                case "steer": return p.steering?.label ?? "руль";
                case "steer_left": return "руль влево";
                case "steer_right": return "руль вправо";
                default: return null;
            }
        }

        /// <summary>Текст с клавишами, выделенными как «клавиши» (rich text TMP и IMGUI).</summary>
        public static string Format(string text) => Format(text, UsingWheel);

        public static string Format(string text, bool wheel) => GuidedText.Format(text, k =>
        {
            var n = KeyName(k, wheel);
            return n == null ? null : "<b>[" + n + "]</b>";
        });
    }
}
