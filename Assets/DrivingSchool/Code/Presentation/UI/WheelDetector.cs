using DrivingSchool.Input;

namespace DrivingSchool.Presentation.UI
{
    /// <summary>G29 в PC-режиме: точная USB HID identity, без предположений по названию.</summary>
    public static class WheelDetector
    {
        public static bool IsConnected => G29InputSource.FindDevice() != null;
    }
}
