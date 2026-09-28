using System;
using DrivingSchool.Contracts;
using UnityEngine;

namespace DrivingSchool.Input
{
    /// <summary>Руль/педали/кнопки поверх клавиатуры: <see cref="KeyboardInputSource"/> зовёт их из Poll.</summary>
    public interface IControlOverlay
    {
        bool IsConnected { get; }
        /// <summary>Руль и педали — вместо клавиш W/S/A/D/Shift.</summary>
        void ApplyAxes(KeyboardInputSource target);
        /// <summary>Кнопки и шифтер — после клавиш (клавиатура продолжает работать для кнопок).</summary>
        void ApplyButtons(KeyboardInputSource target);
        /// <summary>Машину переставили (ResetToParked): забыть положение рычага.</summary>
        void Resync();
    }

    /// <summary>Настройки руля из «Управление» (docs/ui-settings.md §4), заполняет DriveSettingsApplier.</summary>
    public sealed class WheelTuning
    {
        /// <summary>Какой угол руля даёт полный поворот колёс, при диапазоне драйвера 900°: 900 → 1 : 1, 450 → вдвое острее.</summary>
        public float steeringLockDeg = 900f;
        public float steeringDeadzone;          // 0…0.1
        public float steeringLinearity;         // 0…1: 0 — линейно, больше — точнее в центре
        public float pedalDeadzone = 0.03f;     // 0…0.15
        public bool invertPedals;
        public const float DriverRangeDeg = 900f;

        public float ShapeSteering(float raw)
        {
            float s = Mathf.Clamp(raw * DriverRangeDeg / Mathf.Clamp(steeringLockDeg, 90f, 1080f), -1f, 1f);
            float a = Mathf.Abs(s);
            if (a <= steeringDeadzone) return 0f;
            a = (a - steeringDeadzone) / Mathf.Max(1e-3f, 1f - steeringDeadzone);
            a = Mathf.Pow(a, 1f + 1.5f * Mathf.Clamp01(steeringLinearity));
            return Mathf.Sign(s) * Mathf.Clamp01(a);
        }

        public float ShapePedal(float fraction)
        {
            float t = invertPedals ? 1f - fraction : fraction;
            if (t <= pedalDeadzone) return 0f;
            return Mathf.Clamp01((t - pedalDeadzone) / Mathf.Max(1e-3f, 1f - pedalDeadzone));
        }
    }

    /// <summary>
    /// Руль Logitech G29 + Driving Force Shifter (T42). Оси и кнопки — из <see cref="WheelProfile"/> (раскладка как в City Car Driving).
    /// Клавиатура при этом не отключается: её кнопки (I, T, Q/E…) работают, а руль и педали берутся только с руля.
    /// Шифтер: на механике положение рычага = передача (нет кнопки — нейтраль), пока рычаг ни разу не двигали —
    /// передачи с клавиатуры; на АКПП рычаг вперёд = D, в R = R, в нейтраль = N, P — отдельной кнопкой, из P — только с тормозом.
    /// </summary>
    public sealed class WheelInputSource : IControlOverlay
    {
        public WheelTuning Tuning { get; } = new WheelTuning();

        /// <summary>Последние прочитанные значения (для оверлея и HUD).</summary>
        public float Steering { get; private set; }
        public float Throttle { get; private set; }
        public float Brake { get; private set; }
        public float Clutch { get; private set; }
        /// <summary>Положение рычага: −1 R, 0 нейтраль, 1…6.</summary>
        public int ShifterPosition { get; private set; }
        /// <summary>Шифтер хотя бы раз включал передачу — значит, он подключён.</summary>
        public bool ShifterSeen { get; private set; }

        int appliedPosition = int.MinValue;

        public bool IsConnected => WheelDevice.Current != null;

        public void Resync() => appliedPosition = int.MinValue;

        public void ApplyAxes(KeyboardInputSource k)
        {
            var p = WheelProfile.Current;
            Steering = Tuning.ShapeSteering(WheelDevice.Bipolar(WheelDevice.Control(p.steering.control), p.steering.releasedEnd));
            Throttle = Tuning.ShapePedal(WheelDevice.PedalFraction(WheelDevice.Control(p.throttle.control), p.throttle.releasedEnd));
            Brake = Tuning.ShapePedal(WheelDevice.PedalFraction(WheelDevice.Control(p.brake.control), p.brake.releasedEnd));
            Clutch = Tuning.ShapePedal(WheelDevice.PedalFraction(WheelDevice.Control(p.clutch.control), p.clutch.releasedEnd));
            k.SetAnalog(Steering, Throttle, Brake, Clutch);
        }

        public void ApplyButtons(KeyboardInputSource k)
        {
            if (Down(WheelAction.Ignition)) k.ToggleIgnition();
            if (Down(WheelAction.Handbrake)) k.ToggleHandbrake();
            if (Down(WheelAction.Belt)) k.ToggleSeatbelt();
            if (Down(WheelAction.LeftSignal)) k.ToggleIndicator(TurnSignal.Left);
            if (Down(WheelAction.RightSignal)) k.ToggleIndicator(TurnSignal.Right);
            if (Down(WheelAction.Hazard)) k.ToggleHazard();
            if (Down(WheelAction.Lights)) k.CycleLights();
            if (Down(WheelAction.HighBeam)) k.ToggleHighBeam();
            if (Down(WheelAction.Wipers)) k.CycleWipers();
            if (Down(WheelAction.Park)) k.SelectPark();
            k.SetHeld(Held(WheelAction.Starter), Held(WheelAction.Horn), Held(WheelAction.Flash), Held(WheelAction.Washer));
            ApplyShifter(k);
        }

        void ApplyShifter(KeyboardInputSource k)
        {
            int pos = ReadShifter();
            ShifterPosition = pos;
            if (pos != 0) ShifterSeen = true;
            if (!ShifterSeen) return;                                  // без шифтера передачи — с клавиатуры
            bool brakeHeld = Brake > 0.1f;
            if (!k.automatic)
            {
                if (k.RequestedGear != pos) k.RequestGear(pos, brakeHeld);
                appliedPosition = pos;
                return;
            }
            // АКПП: действует движение рычага; первое чтение после старта/респауна только запоминает положение.
            if (appliedPosition == int.MinValue) { appliedPosition = pos; return; }
            if (pos == appliedPosition) return;
            int want = pos > 0 ? 1 : pos;                              // любая передача вперёд → D
            if (k.RequestGear(want, brakeHeld)) appliedPosition = pos;  // из P без тормоза — ждём тормоз (подсказка shift-lock)
        }

        static int ReadShifter()
        {
            if (Held(WheelAction.Reverse)) return -1;
            for (int g = 1; g <= 6; g++)
                if (Held(WheelAction.Gear1 + (g - 1))) return g;
            return 0;
        }

        static bool Down(WheelAction a) => WheelDevice.WasPressed(WheelDevice.Control(a));
        static bool Held(WheelAction a) => WheelDevice.IsPressed(WheelDevice.Control(a));
    }
}
