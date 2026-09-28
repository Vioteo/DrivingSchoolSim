using DrivingSchool.Input;

namespace DrivingSchool.Presentation.UI
{
    /// <summary>
    /// Подключён ли руль с раскладкой G29 (G29, G923 для PlayStation) — по VID/PID из WheelProfile (T42),
    /// а не по похожему имени: чужому устройству раскладку G29 не применяем.
    /// </summary>
    public static class WheelDetector
    {
        public static bool IsConnected => WheelDevice.Current != null;
    }
}
