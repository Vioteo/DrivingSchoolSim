using System;
using UnityEngine;
using UnityEngine.InputSystem;
using DrivingSchool.Contracts;

namespace DrivingSchool.Input
{
    /// <summary>
    /// Keyboard driver input. Call Poll() once per rendered frame (edge-triggered toggles need a frame) and
    /// Read() from FixedUpdate. If Poll() is never called, Read() polls itself (legacy single-call use).
    /// Map (see docs/vehicle-test-range.md): W/S/A/D pedals and steering, Shift clutch, Space handbrake,
    /// I ignition, Enter starter, 1–6/R/N gears (АКПП: 1–6 → D, P → P), Q/E indicators, X hazard,
    /// L lights, K high beam, J flash, H horn, V wipers, B washer, T seat belt.
    /// Клавиши берутся из <see cref="KeyboardProfile"/> (keyboard.json, экран «Переназначение кнопок»), выше — раскладка по умолчанию.
    /// Руль (T42): <see cref="Overlay"/> — руль и педали заменяют W/S/A/D/Shift, кнопки руля работают вместе с клавишами.
    /// </summary>
    public sealed class KeyboardInputSource : IInputSource
    {
        public float steeringRate = 2.5f;
        public float returnRate = 3.5f;
        public float pedalRate = 5.0f;
        [Tooltip("Keyboard throttle is pressed gradually (a key is not a pedal): 0→1 in ~0.7 s, released quickly.")]
        public float throttleRiseRate = 1.4f, brakeRiseRate = 2.5f;
        [Tooltip("Forward speed of the car, set by the vehicle controller: steering gets slower and shorter at speed.")]
        public float vehicleSpeedMps;
        [Tooltip("Car geometry for the keyboard's steering limit, set by the vehicle controller.")]
        public float wheelbaseM = 2.7f, maxSteerDeg = 32f;
        [Tooltip("A held key steers no further than a turn with this lateral acceleration, m/s² (T66: a key used to ask " +
                 "for 24° at 50 km/h — far past the tyres — and the car slid and leaned).")]
        public float keyboardLateralMps2 = 6.5f;
        public bool automatic;              // AT: gear keys drive the selector
        public Keyboard KeyboardDevice { get; set; }
        /// <summary>Клавиши; null — <see cref="KeyboardProfile.Current"/>.</summary>
        public KeyboardProfile Keys { get; set; }
        KeyboardProfile Map => Keys ?? KeyboardProfile.Current;
        /// <summary>Последняя попытка вывести селектор АКПП из P была без тормоза и не сработала (для подсказки).</summary>
        public bool ShiftLockRefused { get; private set; }

        float steering;
        float throttle;
        float brake;
        float clutch;
        int requestedGear = 0; // 0 = Neutral
        AutomaticSelector selector = AutomaticSelector.P;
        bool ignition = false;
        bool starter = false;
        bool handbrake = true;
        bool hazard, highBeam, flash, horn, seatbelt, washer;
        HeadlightMode headlights = HeadlightMode.Off;
        WiperMode wipers = WiperMode.Off;
        readonly TurnSignalStalk stalk = new TurnSignalStalk();
        int lastPolledFrame = -1;
        bool externallyPolled;

        Keyboard Kb => KeyboardDevice ?? Keyboard.current;
        /// <summary>Руль поверх клавиатуры (WheelInputSource) или null.</summary>
        public IControlOverlay Overlay { get; set; }
        public bool OverlayActive => Overlay != null && Overlay.IsConnected;
        public bool IsConnected => Kb != null || OverlayActive;
        public int RequestedGear => requestedGear;
        bool extStarter, extHorn, extFlash, extWasher;

        public void Reset()
        {
            steering = 0f;
            throttle = 0f;
            brake = 0f;
            clutch = 0f;
            starter = false;
            stalk.Cancel();
        }

        /// <summary>Forces the engine-off parked state (used when the car is respawned).</summary>
        public void ResetToParked()
        {
            Reset();
            requestedGear = 0; selector = AutomaticSelector.P; ignition = false; handbrake = true; ShiftLockRefused = false;
            hazard = highBeam = flash = horn = washer = false; headlights = HeadlightMode.Off; wipers = WiperMode.Off;
            extStarter = extHorn = extFlash = extWasher = false;
            Overlay?.Resync();
        }

        // ---------- действия (клавиши и кнопки руля) ----------

        /// <summary>Руль и педали с внешнего устройства: руль −1…1, педали 0…1 (сцепление 1 = выжато).</summary>
        public void SetAnalog(float steer, float gas, float brakePedal, float clutchPedal)
        {
            steering = Mathf.Clamp(steer, -1f, 1f); throttle = Mathf.Clamp01(gas); brake = Mathf.Clamp01(brakePedal); clutch = Mathf.Clamp01(clutchPedal);
        }

        /// <summary>Удерживаемые кнопки внешнего устройства (складываются с клавишами).</summary>
        public void SetHeld(bool starterHeld, bool hornHeld, bool flashHeld, bool washerHeld)
        {
            extStarter = starterHeld; extHorn = hornHeld; extFlash = flashHeld; extWasher = washerHeld;
        }

        public void ToggleIgnition() => ignition = !ignition;
        public void ToggleHandbrake() => handbrake = !handbrake;
        public void ToggleSeatbelt() => seatbelt = !seatbelt;
        public void ToggleHazard() => hazard = !hazard;
        public void ToggleHighBeam() => highBeam = !highBeam;
        public void ToggleIndicator(TurnSignal side) => stalk.Toggle(side);
        public void CycleLights() => headlights = headlights == HeadlightMode.Off ? HeadlightMode.Parking : headlights == HeadlightMode.Parking ? HeadlightMode.LowBeam : HeadlightMode.Off;
        public void CycleWipers() => wipers = (WiperMode)(((int)wipers + 1) % 4);
        public void SelectPark() { selector = AutomaticSelector.P; requestedGear = 0; }

        /// <summary>Передача −1/0/1…6 (на АКПП: &gt;0 → D, −1 → R, 0 → N). false — селектор АКПП заблокирован в P без тормоза.</summary>
        public bool RequestGear(int gear, bool brakeHeld)
        {
            if (ShiftLocked(automatic, selector, brakeHeld)) { ShiftLockRefused = true; return false; }
            ShiftLockRefused = false;
            requestedGear = Math.Clamp(gear, -1, 6);
            selector = gear > 0 ? AutomaticSelector.D : gear < 0 ? AutomaticSelector.R : AutomaticSelector.N;
            return true;
        }

        public void Poll(float dt)
        {
            externallyPolled = true;
            PollInternal(dt);
        }

        void PollInternal(float dt)
        {
            if (Time.frameCount == lastPolledFrame) return;
            lastPolledFrame = Time.frameCount;
            var kb = Kb;
            var overlay = OverlayActive ? Overlay : null;
            if (kb == null && overlay == null) return;
            if (!(dt > 0f && dt < 0.2f)) dt = 0.01f;

            if (overlay != null) overlay.ApplyAxes(this);
            else PollKeyboardAxes(kb, dt);
            if (kb != null) PollKeyboardButtons(kb);
            else starter = horn = flash = washer = false;
            if (overlay != null) overlay.ApplyButtons(this);
            else extStarter = extHorn = extFlash = extWasher = false;
            stalk.Update(steering);
            starter |= extStarter; horn |= extHorn; flash |= extFlash; washer |= extWasher;
        }

        void PollKeyboardAxes(Keyboard kb, float dt)
        {
            var map = Map;
            int steerKeys = 0;
            if (map.Held(kb, DriveAction.SteerLeft)) steerKeys -= 1;
            if (map.Held(kb, DriveAction.SteerRight)) steerKeys += 1;
            steering = StepKeyboardSteer(steering, steerKeys, vehicleSpeedMps, dt, steeringRate, returnRate, wheelbaseM, maxSteerDeg, keyboardLateralMps2);

            bool gas = map.Held(kb, DriveAction.Gas), brk = map.Held(kb, DriveAction.Brake);
            throttle = Mathf.MoveTowards(throttle, gas ? 1f : 0f, (gas ? throttleRiseRate : pedalRate) * dt);
            brake = Mathf.MoveTowards(brake, brk ? 1f : 0f, (brk ? brakeRiseRate : pedalRate) * dt);
            // The clutch is released slower than pressed, like a foot finding the bite point.
            bool clutchDown = map.Held(kb, DriveAction.Clutch);
            clutch = Mathf.MoveTowards(clutch, clutchDown ? 1f : 0f, (clutchDown ? pedalRate : 1.6f) * dt);
        }

        void PollKeyboardButtons(Keyboard kb)
        {
            var map = Map;
            bool brk = map.Held(kb, DriveAction.Brake) || brake > 0.1f;
            if (map.Down(kb, DriveAction.Handbrake)) ToggleHandbrake();

            int gear = -99;
            if (map.Down(kb, DriveAction.Neutral)) gear = 0;
            else if (map.Down(kb, DriveAction.Reverse)) gear = -1;
            else
                for (int g = 1; g <= 6; g++)
                    if (map.Down(kb, DriveAction.Gear1 + (g - 1))) { gear = g; break; }
            if (gear != -99) RequestGear(gear, brk);
            if (map.Down(kb, DriveAction.Park)) SelectPark();

            if (map.Down(kb, DriveAction.Ignition)) ToggleIgnition();
            starter = map.Held(kb, DriveAction.Starter);

            if (map.Down(kb, DriveAction.LeftSignal)) ToggleIndicator(TurnSignal.Left);
            if (map.Down(kb, DriveAction.RightSignal)) ToggleIndicator(TurnSignal.Right);
            if (map.Down(kb, DriveAction.Hazard)) ToggleHazard();
            if (map.Down(kb, DriveAction.Lights)) CycleLights();
            if (map.Down(kb, DriveAction.HighBeam)) ToggleHighBeam();
            flash = map.Held(kb, DriveAction.Flash);
            horn = map.Held(kb, DriveAction.Horn);
            if (map.Down(kb, DriveAction.Wipers)) CycleWipers();
            washer = map.Held(kb, DriveAction.Washer);
            if (map.Down(kb, DriveAction.Belt)) ToggleSeatbelt();
        }

        /// <summary>Блокировка селектора АКПП (как в настоящей машине): из P рычаг выходит только с нажатым тормозом.</summary>
        /// <summary>
        /// Share of full lock a held key gives at <paramref name="speedMps"/>: the wheel angle of a turn whose lateral
        /// acceleration is <paramref name="lateralMps2"/> (radius v²/a), never less than 3 % and full lock at parking speeds.
        /// </summary>
        public static float MaxKeyboardSteer(float speedMps, float wheelbaseM, float maxSteerDeg, float lateralMps2)
        {
            float v = Math.Abs(speedMps);
            if (v < 1f || maxSteerDeg <= 0f || wheelbaseM <= 0f || lateralMps2 <= 0f) return 1f;
            float deg = Mathf.Atan(wheelbaseM * lateralMps2 / (v * v)) * Mathf.Rad2Deg;
            return Mathf.Clamp(deg / maxSteerDeg, 0.03f, 1f);
        }

        /// <summary>Share of the speed's limit the key turns the wheel by per second, per unit of steeringRate (T71).</summary>
        public const float KeyRiseShare = 0.8f;
        /// <summary>Same for the return to centre, per unit of returnRate (T71).</summary>
        public const float KeyReturnShare = 1f;

        /// <summary>
        /// One step of the virtual steering wheel turned by keys (T71). <paramref name="keyDirection"/>: −1 left, 0 none, 1 right.
        /// A key is on/off, so at speed the wheel goes no further than <see cref="MaxKeyboardSteer"/> and turns at a rate
        /// proportional to that limit: the limit is reached in ≈0.5 s and a released key centres in ≈0.3 s (at 100 %),
        /// at 20 km/h and at 90 km/h alike. Before, the rate fell only as 1/(1 + v/15) while the limit falls as 1/v²:
        /// at 60 km/h a 0.1 s tap already asked for 0.7 of the limit and 0.2 s for all of it — no small corrections.
        /// Parking speeds (limit = full lock) keep nearly the old rate.
        /// </summary>
        public static float StepKeyboardSteer(float steering, int keyDirection, float speedMps, float dt,
            float steeringRate, float returnRate, float wheelbaseM, float maxSteerDeg, float lateralMps2)
        {
            float v = Math.Abs(speedMps);
            float limit = MaxKeyboardSteer(v, wheelbaseM, maxSteerDeg, lateralMps2);
            float target = Math.Sign(keyDirection) * limit;
            bool towardCentre = Math.Abs(target) < Math.Abs(steering) || target * steering < 0f;
            float rate = towardCentre
                ? returnRate * Math.Min(1f, Math.Max(limit, Math.Abs(steering)) * KeyReturnShare)
                : steeringRate * Math.Min(1f / (1f + v / 15f), limit * KeyRiseShare);
            return Mathf.MoveTowards(steering, target, rate * dt);
        }

        public static bool ShiftLocked(bool automatic, AutomaticSelector current, bool brakeHeld) =>
            automatic && current == AutomaticSelector.P && !brakeHeld;

        public DriverCommand Read(long tick)
        {
            if (!externallyPolled)
                PollInternal(Time.deltaTime);

            var cmd = new DriverCommand
            {
                sequence = tick,
                steering = Mathf.Clamp(steering, -1f, 1f),
                throttle = Mathf.Clamp01(throttle),
                brake = Mathf.Clamp01(brake),
                clutch = Mathf.Clamp01(clutch),
                handbrake = handbrake,
                ignition = ignition,
                starter = starter,
                requestedGear = Math.Clamp(requestedGear, -1, 6),
                selector = selector,
                turnSignal = stalk.Position,
                hazard = hazard,
                headlights = headlights,
                highBeam = highBeam,
                flashHighBeam = flash,
                horn = horn,
                seatbelt = seatbelt,
                washer = washer,
                wipers = wipers
            };

            cmd.Validate();
            return cmd;
        }
    }
}
