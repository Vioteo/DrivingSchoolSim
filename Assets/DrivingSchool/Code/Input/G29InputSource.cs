using System;
using DrivingSchool.Contracts;
using DrivingSchool.Settings;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace DrivingSchool.Input
{
    /// <summary>G29 HID input. No axis or H-shifter button number is assumed; the player captures each control.</summary>
    public sealed class G29InputSource : IInputSource
    {
        [Serializable] sealed class HidIdentity { public int vendorId, productId; }
        public const int VendorId = 0x046d, ProductId = 0xc24f;
        public ControlsSection Settings { get; set; }
        public KeyboardInputSource KeyboardSource { get; }

        public G29InputSource(KeyboardInputSource keyboard) { KeyboardSource = keyboard ?? throw new ArgumentNullException(nameof(keyboard)); }

        public static InputDevice FindDevice()
        {
            foreach (var device in InputSystem.devices)
            {
                if (device is Keyboard || device is Mouse) continue;
                try
                {
                    var ids = JsonUtility.FromJson<HidIdentity>(device.description.capabilities);
                    if (ids != null && ids.vendorId == VendorId && ids.productId == ProductId) return device;
                }
                catch (ArgumentException) { /* Not a HID capability descriptor. */ }
            }
            return null;
        }

        public static string RelativePath(InputDevice device, InputControl control)
        {
            if (device == null || control == null || control.device != device) return "";
            string prefix = device.path + "/";
            return control.path.StartsWith(prefix, StringComparison.Ordinal) ? control.path.Substring(prefix.Length) : "";
        }

        public static InputControl FindControl(InputDevice device, string relativePath)
        {
            if (device == null || string.IsNullOrWhiteSpace(relativePath)) return null;
            foreach (var control in device.allControls)
                if (RelativePath(device, control) == relativePath) return control;
            return null;
        }

        public bool IsConnected
        {
            get
            {
                var device = FindDevice(); var p = Settings?.wheel;
                return device != null && Calibrated
                    && AxisReady(device, p.steering) && AxisReady(device, p.throttle)
                    && AxisReady(device, p.brake) && AxisReady(device, p.clutch);
            }
        }

        static bool AxisReady(InputDevice device, WheelAxisBinding binding) =>
            binding != null && binding.max - binding.min >= 0.1f && FindControl(device, binding.control) is AxisControl;
        public bool Calibrated
        {
            get
            {
                var p = Settings?.wheel;
                return p != null && !string.IsNullOrEmpty(p.steering?.control)
                    && !string.IsNullOrEmpty(p.throttle?.control)
                    && !string.IsNullOrEmpty(p.brake?.control)
                    && !string.IsNullOrEmpty(p.clutch?.control);
            }
        }

        static float ReadAxis(InputDevice device, WheelAxisBinding binding, bool bipolar, float deadzone)
        {
            if (!(FindControl(device, binding?.control) is AxisControl axis)) return 0f;
            float raw = axis.ReadValue();
            if (float.IsNaN(raw) || float.IsInfinity(raw) || binding.max - binding.min < 0.1f) return 0f;
            float value;
            if (bipolar)
            {
                float span = raw >= binding.center ? binding.max - binding.center : binding.center - binding.min;
                value = span > 0.05f ? (raw - binding.center) / span : 0f;
                if (binding.inverted) value = -value;
                value = Mathf.Clamp(value, -1f, 1f);
                return Mathf.Abs(value) < deadzone ? 0f : value;
            }
            value = Mathf.Clamp01((raw - binding.min) / (binding.max - binding.min));
            if (binding.inverted) value = 1f - value;
            return value < deadzone ? 0f : value;
        }

        public DriverCommand Read(long tick)
        {
            var device = FindDevice();
            if (device == null || !IsConnected)
                return new DriverCommand { sequence = tick, handbrake = true, clutch = 1f, requestedGear = 0, selector = AutomaticSelector.P };

            var cmd = KeyboardSource.Read(tick); // keyboard retains switches, lights and starter
            var p = Settings.wheel;
            cmd.steering = Mathf.Clamp(ReadAxis(device, p.steering, true, Settings.steeringDeadzone / 100f)
                * 900f / Mathf.Max(180f, Settings.steeringLock), -1f, 1f);
            float exponent = 1f + Settings.steeringLinearity / 100f;
            cmd.steering = Mathf.Sign(cmd.steering) * Mathf.Pow(Mathf.Abs(cmd.steering), exponent);
            cmd.throttle = ReadAxis(device, p.throttle, false, Settings.pedalDeadzone / 100f);
            cmd.brake = ReadAxis(device, p.brake, false, Settings.pedalDeadzone / 100f);
            cmd.clutch = ReadAxis(device, p.clutch, false, Settings.pedalDeadzone / 100f);
            if (Settings.invertPedals)
            { cmd.throttle = 1f - cmd.throttle; cmd.brake = 1f - cmd.brake; cmd.clutch = 1f - cmd.clutch; }
            if (!KeyboardSource.automatic)
            {
                cmd.requestedGear = 0;
                if (p.gears != null)
                    for (int i = 0; i < Math.Min(7, p.gears.Length); i++)
                        if (FindControl(device, p.gears[i]) is ButtonControl button && button.isPressed)
                        { cmd.requestedGear = i == 0 ? -1 : i; break; }
            }
            cmd.Validate();
            return cmd;
        }
    }
}
