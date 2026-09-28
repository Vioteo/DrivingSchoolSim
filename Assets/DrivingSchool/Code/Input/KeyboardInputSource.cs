using System;
using UnityEngine;
using UnityEngine.InputSystem;
using DrivingSchool.Contracts;
using DrivingSchool.Settings;

namespace DrivingSchool.Input
{
    /// <summary>
    /// Keyboard driver input. Call Poll() once per rendered frame (edge-triggered toggles need a frame) and
    /// Read() from FixedUpdate. If Poll() is never called, Read() polls itself (legacy single-call use).
    /// Map (see docs/vehicle-test-range.md): W/S/A/D pedals and steering, Shift clutch, Space handbrake,
    /// I ignition, Enter starter, 1–6/R/N gears (АКПП: 1–6 → D, P → P), Q/E indicators, X hazard,
    /// L lights, K high beam, J flash, H horn, V wipers, B washer, T seat belt.
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
        public ControlsSection Bindings { get; set; }
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
        bool Held(Keyboard kb, int action) => KeyboardBindings.Held(kb, Bindings, action);
        bool Pressed(Keyboard kb, int action) => KeyboardBindings.Pressed(kb, Bindings, action);
        public bool IsConnected => Kb != null;

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
            if (kb == null) return;
            if (!(dt > 0f && dt < 0.2f)) dt = 0.01f;

            float targetSteer = 0f;
            if (Held(kb, 0) || kb.leftArrowKey.isPressed) targetSteer -= 1f;
            if (Held(kb, 1) || kb.rightArrowKey.isPressed) targetSteer += 1f;
            // A key is on/off: at speed the virtual steering wheel turns slower and not to full lock, otherwise a tap
            // throws a 1.3-tonne car sideways and it feels weightless.
            float v = Math.Abs(vehicleSpeedMps);
            targetSteer *= MaxKeyboardSteer(v, wheelbaseM, maxSteerDeg, keyboardLateralMps2);
            float speedFactor = 1f / (1f + v / 15f);
            steering = Mathf.MoveTowards(steering, targetSteer, (Math.Abs(targetSteer) > 0.01f ? steeringRate * speedFactor : returnRate) * dt);

            bool gas = Held(kb, 2) || kb.upArrowKey.isPressed, brk = Held(kb, 3) || kb.downArrowKey.isPressed;
            throttle = Mathf.MoveTowards(throttle, gas ? 1f : 0f, (gas ? throttleRiseRate : pedalRate) * dt);
            brake = Mathf.MoveTowards(brake, brk ? 1f : 0f, (brk ? brakeRiseRate : pedalRate) * dt);
            // The clutch is released slower than pressed, like a foot finding the bite point.
            bool clutchDown = Held(kb, 4) || kb.leftCtrlKey.isPressed;
            clutch = Mathf.MoveTowards(clutch, clutchDown ? 1f : 0f, (clutchDown ? pedalRate : 1.6f) * dt);

            if (Pressed(kb, 5)) handbrake = !handbrake;

            int gear = -99;
            if (kb.digit0Key.wasPressedThisFrame || Pressed(kb, 6)) gear = 0;
            else if (Pressed(kb, 7)) gear = -1;
            else if (Pressed(kb, 8)) gear = 1;
            else if (Pressed(kb, 9)) gear = 2;
            else if (Pressed(kb, 10)) gear = 3;
            else if (Pressed(kb, 11)) gear = 4;
            else if (Pressed(kb, 12)) gear = 5;
            else if (Pressed(kb, 13)) gear = 6;
            if (gear != -99 && ShiftLocked(automatic, selector, brk)) { ShiftLockRefused = true; gear = -99; }
            if (gear != -99)
            {
                ShiftLockRefused = false;
                requestedGear = gear;
                selector = gear > 0 ? AutomaticSelector.D : gear < 0 ? AutomaticSelector.R : AutomaticSelector.N;
            }
            if (Pressed(kb, 14)) { selector = AutomaticSelector.P; requestedGear = 0; }

            if (Pressed(kb, 15)) ignition = !ignition;
            starter = Held(kb, 16) || kb.numpadEnterKey.isPressed;

            if (Pressed(kb, 17)) stalk.Toggle(TurnSignal.Left);
            if (Pressed(kb, 18)) stalk.Toggle(TurnSignal.Right);
            stalk.Update(steering);
            if (Pressed(kb, 19)) hazard = !hazard;
            if (Pressed(kb, 20)) headlights = headlights == HeadlightMode.Off ? HeadlightMode.Parking : headlights == HeadlightMode.Parking ? HeadlightMode.LowBeam : HeadlightMode.Off;
            if (Pressed(kb, 21)) highBeam = !highBeam;
            flash = Held(kb, 22);
            horn = Held(kb, 23);
            if (Pressed(kb, 24)) wipers = (WiperMode)(((int)wipers + 1) % 4);
            washer = Held(kb, 25);
            if (Pressed(kb, 26)) seatbelt = !seatbelt;
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
