using DrivingSchool.Input;
using DrivingSchool.Learning;

namespace DrivingSchool.Presentation
{
    /// <summary>
    /// Имена органов управления для текстов уроков ({clutch} → «Shift» или «педаль сцепления»).
    /// Если в настройках выбран руль и он подключён (<see cref="WheelDevice.Active"/>), подсказки — для руля:
    /// подписи кнопок берутся из раскладки <see cref="WheelProfile"/> (её можно поправить в wheel-g29.json).
    /// Иначе — клавиатура (KeyboardInputSource).
    /// </summary>
    public static class LessonControls
    {
        /// <summary>Подсказки для руля (руль выбран в настройках и подключён).</summary>
        public static bool UsingWheel => WheelDevice.Active;

        public static string KeyName(string key) => KeyName(key, UsingWheel);

        public static string KeyName(string key, bool wheel) => wheel ? WheelName(key, WheelProfile.Current) : KeyboardName(key);

        public static string KeyboardName(string key)
        {
            switch (key)
            {
                case "belt": return "T";
                case "ignition": return "I";
                case "starter": return "Enter";
                case "clutch": return "Shift";
                case "gas": return "W";
                case "brake": return "S";
                case "handbrake": return "Пробел";
                case "left": return "Q";
                case "right": return "E";
                case "lights": return "L ×2";
                case "gear1": return "1";
                case "gear2": return "2";
                case "reverse": return "R";
                case "neutral": return "N";
                case "drive": return "1";
                case "park": return "P";
                case "steer": return "A / D";
                case "steer_left": return "A";
                case "steer_right": return "D";
                case "horn": return "H";
                case "hazard": return "X";
                default: return null;
            }
        }

        /// <summary>Имена для руля G29 + шифтер; кнопки — из раскладки, педали и рычаг — словами.</summary>
        public static string WheelName(string key, WheelProfile p)
        {
            switch (key)
            {
                case "belt": return p.Label(WheelAction.Belt) ?? KeyboardName(key);
                case "ignition": return p.Label(WheelAction.Ignition) ?? KeyboardName(key);
                case "starter": return p.Label(WheelAction.Starter) ?? KeyboardName(key);
                case "handbrake": return p.Label(WheelAction.Handbrake) ?? KeyboardName(key);
                case "left": return p.Label(WheelAction.LeftSignal) ?? KeyboardName(key);
                case "right": return p.Label(WheelAction.RightSignal) ?? KeyboardName(key);
                case "lights": { var l = p.Label(WheelAction.Lights); return l == null ? KeyboardName(key) : l + " ×2"; }
                case "horn": return p.Label(WheelAction.Horn) ?? KeyboardName(key);
                case "hazard": return p.Label(WheelAction.Hazard) ?? KeyboardName(key);
                case "park": return p.Label(WheelAction.Park) ?? KeyboardName(key);
                case "clutch": return p.clutch?.label ?? "педаль сцепления";
                case "gas": return p.throttle?.label ?? "педаль газа";
                case "brake": return p.brake?.label ?? "педаль тормоза";
                case "gear1": return p.Label(WheelAction.Gear1) ?? KeyboardName(key);
                case "gear2": return p.Label(WheelAction.Gear2) ?? KeyboardName(key);
                case "reverse": return p.Label(WheelAction.Reverse) ?? KeyboardName(key);
                case "neutral": return "рычаг КПП в нейтраль";
                case "drive": { var g = p.Label(WheelAction.Gear1); return g == null ? KeyboardName(key) : g + " (D)"; }
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
