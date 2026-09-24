using System;
using UnityEngine;
using UnityEngine.InputSystem;
using DrivingSchool.Contracts;

namespace DrivingSchool.Input
{
    public sealed class KeyboardInputSource : IInputSource
    {
        public float steeringRate = 2.5f;
        public float returnRate = 3.5f;
        public float pedalRate = 5.0f;

        float steering;
        float throttle;
        float brake;
        float clutch;
        int requestedGear = 0; // 0 = Neutral
        bool ignition = false;
        bool starter = false;
        bool handbrake = true;

        public bool IsConnected => Keyboard.current != null;

        public void Reset()
        {
            steering = 0f;
            throttle = 0f;
            brake = 0f;
            clutch = 0f;
            starter = false;
        }

        public DriverCommand Read(long tick)
        {
            var kb = Keyboard.current;
            float dt = Time.deltaTime > 0f && Time.deltaTime < 0.2f ? Time.deltaTime : 0.01f;

            if (kb != null)
            {
                // Steering
                float targetSteer = 0f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) targetSteer -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) targetSteer += 1f;

                if (Math.Abs(targetSteer) > 0.01f)
                    steering = Mathf.MoveTowards(steering, targetSteer, steeringRate * dt);
                else
                    steering = Mathf.MoveTowards(steering, 0f, returnRate * dt);

                // Throttle
                float targetThrottle = (kb.wKey.isPressed || kb.upArrowKey.isPressed) ? 1f : 0f;
                throttle = Mathf.MoveTowards(throttle, targetThrottle, pedalRate * dt);

                // Brake
                float targetBrake = (kb.sKey.isPressed || kb.downArrowKey.isPressed) ? 1f : 0f;
                brake = Mathf.MoveTowards(brake, targetBrake, pedalRate * dt);

                // Clutch
                float targetClutch = (kb.leftShiftKey.isPressed || kb.leftCtrlKey.isPressed) ? 1f : 0f;
                clutch = Mathf.MoveTowards(clutch, targetClutch, pedalRate * dt);

                // Handbrake toggle
                if (kb.spaceKey.wasPressedThisFrame) handbrake = !handbrake;

                // Gears
                if (kb.digit0Key.wasPressedThisFrame || kb.nKey.wasPressedThisFrame) requestedGear = 0;
                else if (kb.rKey.wasPressedThisFrame) requestedGear = -1;
                else if (kb.digit1Key.wasPressedThisFrame) requestedGear = 1;
                else if (kb.digit2Key.wasPressedThisFrame) requestedGear = 2;
                else if (kb.digit3Key.wasPressedThisFrame) requestedGear = 3;
                else if (kb.digit4Key.wasPressedThisFrame) requestedGear = 4;
                else if (kb.digit5Key.wasPressedThisFrame) requestedGear = 5;
                else if (kb.digit6Key.wasPressedThisFrame) requestedGear = 6;

                // Ignition & Starter
                if (kb.iKey.wasPressedThisFrame) ignition = !ignition;
                starter = kb.eKey.isPressed;
            }

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
                requestedGear = Math.Clamp(requestedGear, -1, 6)
            };

            cmd.Validate();
            return cmd;
        }
    }
}
