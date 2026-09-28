using System;
using DrivingSchool.Settings;
using UnityEngine.InputSystem;

namespace DrivingSchool.Input
{
    /// <summary>Player-editable driving keys. Indices are stable in settings.json.</summary>
    public static class KeyboardBindings
    {
        public static readonly string[] Labels =
        {
            "Руль влево", "Руль вправо", "Газ", "Тормоз", "Сцепление", "Ручник",
            "Нейтраль", "Задняя", "1-я передача", "2-я передача", "3-я передача",
            "4-я передача", "5-я передача", "6-я передача", "Парковка (АКПП)",
            "Зажигание", "Стартер", "Левый поворотник", "Правый поворотник",
            "Аварийка", "Фары", "Дальний свет", "Мигание дальним", "Сигнал",
            "Дворники", "Омыватель", "Ремень"
        };

        public static readonly Key[] Defaults =
        {
            Key.A, Key.D, Key.W, Key.S, Key.LeftShift, Key.Space,
            Key.N, Key.R, Key.Digit1, Key.Digit2, Key.Digit3,
            Key.Digit4, Key.Digit5, Key.Digit6, Key.P,
            Key.I, Key.Enter, Key.Q, Key.E,
            Key.X, Key.L, Key.K, Key.J, Key.H,
            Key.V, Key.B, Key.T
        };

        public static Key Get(int action, ControlsSection controls)
        {
            if (action < 0 || action >= Defaults.Length) throw new ArgumentOutOfRangeException(nameof(action));
            var names = controls?.keyBindings;
            return names != null && action < names.Length && Enum.TryParse(names[action], true, out Key key)
                && key != Key.None && Enum.IsDefined(typeof(Key), key)
                ? key : Defaults[action];
        }

        public static void Set(int action, Key key, ControlsSection controls)
        {
            if (controls == null || action < 0 || action >= Defaults.Length || key == Key.None)
                throw new ArgumentOutOfRangeException(nameof(action));
            if (controls.keyBindings == null || controls.keyBindings.Length != Defaults.Length)
            {
                var old = controls.keyBindings;
                controls.keyBindings = new string[Defaults.Length];
                if (old != null) Array.Copy(old, controls.keyBindings, Math.Min(old.Length, Defaults.Length));
            }
            Key previous = Get(action, controls);
            // Rebinding to an occupied key swaps the two actions.
            for (int i = 0; i < Defaults.Length; i++)
                if (i != action && Get(i, controls) == key)
                    controls.keyBindings[i] = previous.ToString();
            controls.keyBindings[action] = key.ToString();
        }

        public static bool Held(Keyboard keyboard, ControlsSection controls, int action) =>
            keyboard != null && keyboard[Get(action, controls)].isPressed;

        public static bool Pressed(Keyboard keyboard, ControlsSection controls, int action) =>
            keyboard != null && keyboard[Get(action, controls)].wasPressedThisFrame;
    }
}
