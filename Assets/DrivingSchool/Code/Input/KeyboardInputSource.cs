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
        public bool automatic;              // AT: gear keys drive the selector
        public Keyboard KeyboardDevice { get; set; }

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
            requestedGear = 0; selector = AutomaticSelector.P; ignition = false; handbrake = true;
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
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) targetSteer -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) targetSteer += 1f;
            // A key is on/off: at speed the virtual steering wheel turns slower and not to full lock, otherwise a tap
            // throws a 1.3-tonne car sideways and it feels weightless.
            float v = Math.Abs(vehicleSpeedMps);
            targetSteer *= Mathf.Lerp(1f, 0.3f, Mathf.Clamp01((v - 5f) / 25f));
            float speedFactor = 1f / (1f + v / 15f);
            steering = Mathf.MoveTowards(steering, targetSteer, (Math.Abs(targetSteer) > 0.01f ? steeringRate * speedFactor : returnRate) * dt);

            bool gas = kb.wKey.isPressed || kb.upArrowKey.isPressed, brk = kb.sKey.isPressed || kb.downArrowKey.isPressed;
            throttle = Mathf.MoveTowards(throttle, gas ? 1f : 0f, (gas ? throttleRiseRate : pedalRate) * dt);
            brake = Mathf.MoveTowards(brake, brk ? 1f : 0f, (brk ? brakeRiseRate : pedalRate) * dt);
            // The clutch is released slower than pressed, like a foot finding the bite point.
            bool clutchDown = kb.leftShiftKey.isPressed || kb.leftCtrlKey.isPressed;
            clutch = Mathf.MoveTowards(clutch, clutchDown ? 1f : 0f, (clutchDown ? pedalRate : 1.6f) * dt);

            if (kb.spaceKey.wasPressedThisFrame) handbrake = !handbrake;

            int gear = -99;
            if (kb.digit0Key.wasPressedThisFrame || kb.nKey.wasPressedThisFrame) gear = 0;
            else if (kb.rKey.wasPressedThisFrame) gear = -1;
            else if (kb.digit1Key.wasPressedThisFrame) gear = 1;
            else if (kb.digit2Key.wasPressedThisFrame) gear = 2;
            else if (kb.digit3Key.wasPressedThisFrame) gear = 3;
            else if (kb.digit4Key.wasPressedThisFrame) gear = 4;
            else if (kb.digit5Key.wasPressedThisFrame) gear = 5;
            else if (kb.digit6Key.wasPressedThisFrame) gear = 6;
            if (gear != -99)
            {
                requestedGear = gear;
                selector = gear > 0 ? AutomaticSelector.D : gear < 0 ? AutomaticSelector.R : AutomaticSelector.N;
            }
            if (kb.pKey.wasPressedThisFrame) { selector = AutomaticSelector.P; requestedGear = 0; }

            if (kb.iKey.wasPressedThisFrame) ignition = !ignition;
            starter = kb.enterKey.isPressed || kb.numpadEnterKey.isPressed;

            if (kb.qKey.wasPressedThisFrame) stalk.Toggle(TurnSignal.Left);
            if (kb.eKey.wasPressedThisFrame) stalk.Toggle(TurnSignal.Right);
            stalk.Update(steering);
            if (kb.xKey.wasPressedThisFrame) hazard = !hazard;
            if (kb.lKey.wasPressedThisFrame) headlights = headlights == HeadlightMode.Off ? HeadlightMode.Parking : headlights == HeadlightMode.Parking ? HeadlightMode.LowBeam : HeadlightMode.Off;
            if (kb.kKey.wasPressedThisFrame) highBeam = !highBeam;
            flash = kb.jKey.isPressed;
            horn = kb.hKey.isPressed;
            if (kb.vKey.wasPressedThisFrame) wipers = (WiperMode)(((int)wipers + 1) % 4);
            washer = kb.bKey.isPressed;
            if (kb.tKey.wasPressedThisFrame) seatbelt = !seatbelt;
        }

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
