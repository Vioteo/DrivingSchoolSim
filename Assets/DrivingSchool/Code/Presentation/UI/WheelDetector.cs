using UnityEngine.InputSystem;

namespace DrivingSchool.Presentation.UI
{
    /// <summary>Подключён ли игровой руль (Logitech G29/G27/G920 и похожие) — по описанию устройства Input System.</summary>
    public static class WheelDetector
    {
        static readonly string[] Hints = { "G29", "G27", "G920", "G923", "Driving Force", "Wheel" };

        public static bool IsConnected
        {
            get
            {
                foreach (var d in InputSystem.devices)
                {
                    if (d is Keyboard || d is Mouse || d is Gamepad) continue;
                    string name = (d.description.product ?? "") + " " + (d.displayName ?? "");
                    foreach (var h in Hints)
                        if (name.IndexOf(h, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
                return false;
            }
        }
    }
}
