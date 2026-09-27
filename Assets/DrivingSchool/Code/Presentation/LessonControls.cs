using DrivingSchool.Learning;

namespace DrivingSchool.Presentation
{
    /// <summary>
    /// Имена клавиш для текстов уроков ({clutch} → «Shift»). Раскладка — KeyboardInputSource; руль пока не подключён
    /// к машине (VehicleController.Source = клавиатура), поэтому имена только клавиатурные. Когда появится ввод с G29,
    /// сюда добавится второй набор (педали, рычаг КПП) и выбор по активному источнику.
    /// </summary>
    public static class LessonControls
    {
        public static string KeyName(string key)
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

        /// <summary>Текст с клавишами, выделенными как «клавиши» (rich text TMP и IMGUI).</summary>
        public static string Format(string text) => GuidedText.Format(text, k =>
        {
            var n = KeyName(k);
            return n == null ? null : "<b>[" + n + "]</b>";
        });
    }
}
