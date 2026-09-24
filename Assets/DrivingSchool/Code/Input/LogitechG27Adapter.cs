using System;
using UnityEngine;
using UnityEngine.InputSystem;
using DrivingSchool.Contracts;

namespace DrivingSchool.Input
{
    /// <summary>
    /// Calibration configuration for a single analog axis (steering or pedals).
    /// </summary>
    [Serializable]
    public sealed class AxisCalibration
    {
        public float rawMin;
        public float rawMax;
        public float deadzone;
        public bool inverted;

        public AxisCalibration(float min = -1f, float max = 1f, float deadzone = 0.02f, bool inverted = false)
        {
            this.rawMin = min;
            this.rawMax = max;
            this.deadzone = deadzone;
            this.inverted = inverted;
        }

        /// <summary>
        /// Normalizes unipolar axis (pedals) into [0.0 .. 1.0].
        /// </summary>
        public float Normalize(float raw)
        {
            if (float.IsNaN(raw) || float.IsInfinity(raw)) return 0f;
            float min = Math.Min(rawMin, rawMax);
            float max = Math.Max(rawMin, rawMax);
            float span = max - min;
            if (span < 1e-6f) return 0f;

            float t = (raw - min) / span;
            t = Math.Clamp(t, 0f, 1f);

            if (inverted)
            {
                t = 1f - t;
            }

            if (t <= deadzone) return 0f;
            if (deadzone >= 0.999f) return 0f;

            return Math.Clamp((t - deadzone) / (1f - deadzone), 0f, 1f);
        }

        /// <summary>
        /// Normalizes bipolar axis (steering) into [-1.0 .. +1.0].
        /// </summary>
        public float NormalizeBipolar(float raw)
        {
            if (float.IsNaN(raw) || float.IsInfinity(raw)) return 0f;
            float min = Math.Min(rawMin, rawMax);
            float max = Math.Max(rawMin, rawMax);
            float span = max - min;
            if (span < 1e-6f) return 0f;

            float mid = (min + max) * 0.5f;
            float halfSpan = span * 0.5f;
            float val = (raw - mid) / halfSpan;
            val = Math.Clamp(val, -1f, 1f);

            if (inverted)
            {
                val = -val;
            }

            if (Math.Abs(val) <= deadzone) return 0f;
            if (deadzone >= 0.999f) return 0f;

            float sign = Math.Sign(val);
            float scaled = (Math.Abs(val) - deadzone) / (1f - deadzone);
            return Math.Clamp(scaled * sign, -1f, 1f);
        }
    }

    /// <summary>
    /// Hardware adapter for Logitech G27 Racing Wheel (900° rotation, 3 pedals, 6+R H-shifter).
    /// Implements IInputSource for driver commands and IForceFeedbackOutput for steering wheel forces.
    /// </summary>
    public sealed class LogitechG27Adapter : IInputSource, IForceFeedbackOutput
    {
        // 900° lock-to-lock (±450° from center)
        public const float MaxSteeringAngleDeg = 450.0f;
        public const float MaxSteeringRadians = 7.85398f; // 450° in radians
        public const float DefaultMaxMotorTorqueNm = 2.5f;
        public const float DefaultMaxSlewRate = 10.0f; // Max change in normalized torque per second
        public const float SlewRateLimit = DefaultMaxSlewRate;

        // Axis calibrations
        public AxisCalibration SteeringCalibration { get; } = new AxisCalibration(-1f, 1f, 0.02f, false);
        public AxisCalibration ThrottleCalibration { get; } = new AxisCalibration(-1f, 1f, 0.02f, true);
        public AxisCalibration BrakeCalibration { get; } = new AxisCalibration(-1f, 1f, 0.02f, true);
        public AxisCalibration ClutchCalibration { get; } = new AxisCalibration(-1f, 1f, 0.02f, true);

        // Simulated raw inputs for testing and headless execution
        public bool UseSimulatedInputs { get; set; } = true;
        public float RawSteering { get; set; } = 0f;
        public float RawThrottle { get; set; } = 1f; // unpressed when inverted
        public float RawBrake { get; set; } = 1f;    // unpressed when inverted
        public float RawClutch { get; set; } = 1f;   // unpressed when inverted
        public int SelectedGear { get; set; } = 0;   // 0 = Neutral
        public bool HandbrakeState { get; set; } = true;
        public bool IgnitionState { get; set; } = false;
        public bool StarterState { get; set; } = false;
        public bool HazardLightsState { get; set; } = false;
        public bool LeftIndicatorState { get; set; } = false;
        public bool RightIndicatorState { get; set; } = false;
        public bool HighBeamState { get; set; } = false;
        public bool LowBeamState { get; set; } = false;
        public bool HornState { get; set; } = false;
        public bool WipersState { get; set; } = false;

        // FFB Configuration and State
        public float MaxMotorTorqueNm { get; set; } = DefaultMaxMotorTorqueNm;
        public float MaxSlewRate { get; set; } = DefaultMaxSlewRate;
        public float CurrentAppliedTorque { get; private set; } = 0f;
        public float TargetTorque { get; private set; } = 0f;
        public bool IsPaused { get; private set; } = false;
        public bool IsStopped { get; private set; } = false;
        public bool Disposed { get; private set; } = false;

        // Status properties
        bool isConnected = true;
        public bool IsConnected
        {
            get => isConnected;
            set
            {
                isConnected = value;
                if (!isConnected)
                {
                    CurrentAppliedTorque = 0f;
                    TargetTorque = 0f;
                    lastTorqueTime = -1.0;
                }
            }
        }
        public bool IsAvailable => IsConnected && !Disposed && !IsStopped && !IsPaused;

        public LogitechG27Adapter()
        {
        }

        #region IInputSource Implementation

        public DriverCommand Read(long tick)
        {
            if (!IsConnected)
            {
                // Safe fail-over on disconnected hardware: neutral gear, zero throttle/brake
                var safeCmd = new DriverCommand
                {
                    sequence = tick,
                    steering = 0f,
                    throttle = 0f,
                    brake = 0f,
                    clutch = 0f,
                    handbrake = true,
                    ignition = false,
                    starter = false,
                    requestedGear = 0
                };
                safeCmd.Validate();
                return safeCmd;
            }

            float steerNorm;
            float throttleNorm;
            float brakeNorm;
            float clutchNorm;
            int gear;
            bool handbrake;
            bool ignition;
            bool starter;

            if (UseSimulatedInputs || Gamepad.current == null)
            {
                steerNorm = SteeringCalibration.NormalizeBipolar(RawSteering);
                throttleNorm = ThrottleCalibration.Normalize(RawThrottle);
                brakeNorm = BrakeCalibration.Normalize(RawBrake);
                clutchNorm = ClutchCalibration.Normalize(RawClutch);
                gear = Math.Clamp(SelectedGear, -1, 6);
                handbrake = HandbrakeState;
                ignition = IgnitionState;
                starter = StarterState;
            }
            else
            {
                var gp = Gamepad.current;
                steerNorm = SteeringCalibration.NormalizeBipolar(gp.leftStick.x.ReadValue());
                throttleNorm = ThrottleCalibration.Normalize(gp.rightTrigger.ReadValue());
                brakeNorm = BrakeCalibration.Normalize(gp.leftTrigger.ReadValue());
                clutchNorm = ClutchCalibration.Normalize(gp.buttonWest.isPressed ? 1f : 0f);
                gear = SelectedGear;
                handbrake = gp.buttonSouth.isPressed || HandbrakeState;
                ignition = IgnitionState;
                starter = StarterState;
            }

            var cmd = new DriverCommand
            {
                sequence = tick,
                steering = Math.Clamp(steerNorm, -1f, 1f),
                throttle = Math.Clamp(throttleNorm, 0f, 1f),
                brake = Math.Clamp(brakeNorm, 0f, 1f),
                clutch = Math.Clamp(clutchNorm, 0f, 1f),
                handbrake = handbrake,
                ignition = ignition,
                starter = starter,
                requestedGear = gear,
                turnSignal = LeftIndicatorState ? TurnSignal.Left : RightIndicatorState ? TurnSignal.Right : TurnSignal.Off,
                hazard = HazardLightsState,
                headlights = LowBeamState || HighBeamState ? HeadlightMode.LowBeam : HeadlightMode.Off,
                highBeam = HighBeamState,
                horn = HornState,
                wipers = WipersState ? WiperMode.Low : WiperMode.Off
            };

            cmd.Validate();
            return cmd;
        }

        #endregion

        #region Force Feedback Calculations

        /// <summary>
        /// Centering spring force that pulls the steering wheel toward 0.
        /// Increases smoothly with vehicle forward speed.
        /// </summary>
        public static float CalculateCenteringSpring(float steeringNormalized, float speedMps, float baseStiffness = 0.15f, float maxStiffness = 0.65f)
        {
            if (float.IsNaN(steeringNormalized) || float.IsInfinity(steeringNormalized) ||
                float.IsNaN(speedMps) || float.IsInfinity(speedMps)) return 0f;
            float speedFactor = Math.Clamp(Math.Abs(speedMps) / 15.0f, 0f, 1f);
            float kSpring = baseStiffness + (maxStiffness - baseStiffness) * speedFactor;
            return -steeringNormalized * kSpring;
        }

        /// <summary>
        /// Viscous damping opposing steering rotation velocity.
        /// </summary>
        public static float CalculateDamping(float steeringAngularVelocityRadS, float dampingCoeff = 0.12f)
        {
            if (float.IsNaN(steeringAngularVelocityRadS) || float.IsInfinity(steeringAngularVelocityRadS)) return 0f;
            return -steeringAngularVelocityRadS * dampingCoeff;
        }

        /// <summary>
        /// Mechanical friction in the steering rack opposing direction of movement.
        /// </summary>
        public static float CalculateFriction(float steeringAngularVelocityRadS, float frictionTorque = 0.05f)
        {
            if (float.IsNaN(steeringAngularVelocityRadS) || float.IsInfinity(steeringAngularVelocityRadS)) return 0f;
            if (Math.Abs(steeringAngularVelocityRadS) < 0.01f) return 0f;
            return -Math.Sign(steeringAngularVelocityRadS) * frictionTorque;
        }

        /// <summary>
        /// Mechanical end-stop bumper simulating hard mechanical stops at ±450° (900° lock-to-lock).
        /// Returns strong repulsive torque opposing penetration beyond the limit.
        /// </summary>
        public static float CalculateEndStop(float wheelAngleDeg, float maxAngleDeg = 450.0f, float stopStiffness = 0.10f)
        {
            if (float.IsNaN(wheelAngleDeg) || float.IsInfinity(wheelAngleDeg)) return 0f;
            float absAngle = Math.Abs(wheelAngleDeg);
            if (absAngle <= maxAngleDeg) return 0f;

            float penetration = absAngle - maxAngleDeg;
            float stopForce = Math.Clamp(penetration * stopStiffness, 0f, 1.0f);
            return -Math.Sign(wheelAngleDeg) * stopForce;
        }

        /// <summary>
        /// Road texture and grip loss vibration modulated by front tire slip angle and speed.
        /// </summary>
        public static float CalculateGripLossVibration(float frontSlipAngleRad, float speedMps, double simSeconds, float frequency = 22.0f, float amplitude = 0.25f)
        {
            if (float.IsNaN(frontSlipAngleRad) || float.IsInfinity(frontSlipAngleRad)) return 0f;
            if (Math.Abs(speedMps) < 1.0f) return 0f;

            float absSlip = Math.Abs(frontSlipAngleRad);
            const float slipThreshold = 0.09f; // ~5.15 degrees
            if (absSlip <= slipThreshold) return 0f;

            float excess = Math.Clamp((absSlip - slipThreshold) / 0.10f, 0f, 1f);
            float sine = (float)Math.Sin(2.0 * Math.PI * frequency * simSeconds);
            return sine * amplitude * excess;
        }

        /// <summary>
        /// Combines all physics components (self-aligning torque, centering, damping, end-stops, vibration).
        /// </summary>
        public float CalculateTotalTorque(
            float selfAligningTorqueNm,
            float steeringNorm,
            float steeringAngularVelocityRadS,
            float speedMps,
            float frontSlipAngleRad,
            double simSeconds)
        {
            if (!IsAvailable) return 0f;

            float normalizedAligning = MaxMotorTorqueNm > 0f ? (selfAligningTorqueNm / MaxMotorTorqueNm) : 0f;
            float centering = CalculateCenteringSpring(steeringNorm, speedMps);
            float damping = CalculateDamping(steeringAngularVelocityRadS);
            float friction = CalculateFriction(steeringAngularVelocityRadS);
            float endStop = CalculateEndStop(steeringNorm * MaxSteeringAngleDeg, MaxSteeringAngleDeg);
            float gripLoss = CalculateGripLossVibration(frontSlipAngleRad, speedMps, simSeconds);

            float total = normalizedAligning + centering + damping + friction + endStop + gripLoss;
            if (float.IsNaN(total) || float.IsInfinity(total)) return 0f;

            return Math.Clamp(total, -1f, 1f);
        }

        /// <summary>
        /// Limits rate of change of torque to prevent violent wheel kicks.
        /// </summary>
        public static float ApplyRateLimiter(float targetTorque, float currentTorque, float dtSeconds, float maxSlewRate = 10.0f)
        {
            if (float.IsNaN(targetTorque) || float.IsInfinity(targetTorque)) return 0f;
            if (float.IsNaN(currentTorque) || float.IsInfinity(currentTorque)) currentTorque = 0f;
            if (dtSeconds <= 0f) return currentTorque;

            float maxDelta = maxSlewRate * dtSeconds;
            float delta = targetTorque - currentTorque;
            float clampedDelta = Math.Clamp(delta, -maxDelta, maxDelta);
            return Math.Clamp(currentTorque + clampedDelta, -1f, 1f);
        }

        #endregion

        #region IForceFeedbackOutput Implementation

        double lastTorqueTime = -1.0;
        readonly System.Diagnostics.Stopwatch torqueStopwatch = System.Diagnostics.Stopwatch.StartNew();

        public void SetNormalizedTorque(float torque)
        {
            SetNormalizedTorque(torque, -1f);
        }

        public void SetNormalizedTorque(float torque, float dtSeconds)
        {
            if (!IsAvailable)
            {
                TargetTorque = 0f;
                CurrentAppliedTorque = 0f;
                return;
            }

            if (float.IsNaN(torque) || float.IsInfinity(torque))
            {
                Stop();
                return;
            }

            TargetTorque = Math.Clamp(torque, -1f, 1f);

            if (dtSeconds >= 0f)
            {
                CurrentAppliedTorque = ApplyRateLimiter(TargetTorque, CurrentAppliedTorque, dtSeconds, MaxSlewRate);
                lastTorqueTime = torqueStopwatch.Elapsed.TotalSeconds;
            }
            else
            {
                double now = torqueStopwatch.Elapsed.TotalSeconds;
                if (lastTorqueTime < 0.0)
                {
                    CurrentAppliedTorque = TargetTorque;
                }
                else
                {
                    float dt = (float)(now - lastTorqueTime);
                    CurrentAppliedTorque = ApplyRateLimiter(TargetTorque, CurrentAppliedTorque, dt, MaxSlewRate);
                }
                lastTorqueTime = now;
            }
        }

        /// <summary>
        /// Updates FFB torque using slew rate limiting over the specified time delta.
        /// </summary>
        public void UpdateTorque(float targetTorque, float dtSeconds)
        {
            if (!IsAvailable)
            {
                CurrentAppliedTorque = 0f;
                TargetTorque = 0f;
                return;
            }

            TargetTorque = Math.Clamp(targetTorque, -1f, 1f);
            CurrentAppliedTorque = ApplyRateLimiter(TargetTorque, CurrentAppliedTorque, dtSeconds, MaxSlewRate);
        }

        /// <summary>
        /// Pauses the simulation and immediately zeroes FFB torque.
        /// </summary>
        public void Pause()
        {
            IsPaused = true;
            Stop();
        }

        /// <summary>
        /// Resumes FFB torque delivery after a pause.
        /// </summary>
        public void Resume()
        {
            IsPaused = false;
            IsStopped = false;
        }

        /// <summary>
        /// Idempotent safety watchdog stop. Zeroes all motor output immediately.
        /// Safe to call repeatedly during pause, focus loss, disconnect, or dispose.
        /// </summary>
        public void Stop()
        {
            IsStopped = true;
            TargetTorque = 0f;
            CurrentAppliedTorque = 0f;
            lastTorqueTime = -1.0;
        }

        public void Dispose()
        {
            if (!Disposed)
            {
                Stop();
                Disposed = true;
            }
        }

        #endregion
    }
}
