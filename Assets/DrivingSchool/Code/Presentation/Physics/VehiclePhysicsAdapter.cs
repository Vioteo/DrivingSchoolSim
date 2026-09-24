using System;
using UnityEngine;
using DrivingSchool.Contracts;
using DrivingSchool.Simulation;

namespace DrivingSchool.Presentation.Physics
{
    /// <summary>
    /// Production vehicle dynamics and drivetrain adapter for Unity.
    /// Bridges deterministic pure C# drivetrain math with Unity physical simulation.
    /// </summary>
    public sealed class VehiclePhysicsAdapter : MonoBehaviour
    {
        [Header("Chassis Specifications (from vehicle.json)")]
        [SerializeField] float massKg = 1350f;
        [SerializeField] float wheelbaseM = 2.72f;
        [SerializeField] float trackM = 1.71f;
        [SerializeField] float wheelRadiusM = 0.327f;
        [SerializeField] Vector3 centreOfMass = new Vector3(0f, 0.51f, -0.1f);

        [Header("Drivetrain Specifications")]
        [SerializeField] float finalDriveRatio = 4.1f;
        [SerializeField] float reverseRatio = -3.5f;
        [SerializeField] float maxClutchTorqueNm = 240f;
        [SerializeField] float maxBrakeTorqueNm = 3500f;
        [SerializeField] float handbrakeTorqueNm = 2500f;
        [SerializeField] float maxSteeringAngleDeg = 32f;

        static readonly float[] ForwardGearRatios = { 3.6f, 2.1f, 1.4f, 1.05f, 0.84f, 0.69f };

        public EngineModel Engine { get; private set; }
        public int CurrentGear { get; private set; } = 0; // -1 R, 0 N, 1..6
        public VehicleState CurrentState { get; private set; }
        public Rigidbody Body { get; private set; }

        long currentTick = 0;
        double simulationSeconds = 0.0;
        float currentSpeedMps = 0f;
        float currentSteerAngleDeg = 0f;

        void Awake()
        {
            InitializeEngine();
            SetupRigidbody();
        }

        public void InitializeEngine()
        {
            if (Engine == null)
            {
                Engine = new EngineModel();
            }
        }

        public void SetupRigidbody()
        {
            Body = GetComponent<Rigidbody>();
            if (Body != null)
            {
                if (GetComponent<Collider>() == null)
                {
                    var box = gameObject.AddComponent<BoxCollider>();
                    box.size = new Vector3(trackM, 1.5f, wheelbaseM);
                }
                Body.mass = massKg;
                Body.centerOfMass = centreOfMass;
            }
        }

        /// <summary>
        /// Deterministic simulation step. Can be called directly in unit tests or in FixedUpdate.
        /// </summary>
        public VehicleState Step(DriverCommand cmd, float dtSeconds)
        {
            if (dtSeconds <= 0f || float.IsNaN(dtSeconds) || float.IsInfinity(dtSeconds))
                throw new ArgumentOutOfRangeException(nameof(dtSeconds), "dtSeconds must be positive and finite.");

            cmd.Validate();
            InitializeEngine();

            currentTick++;
            simulationSeconds += dtSeconds;

            // 1. Process Gear Shifting
            ProcessGearShift(cmd.requestedGear, cmd.clutch);

            // 2. Wheel & Input Shaft Angular Velocities
            float gearRatio = GetGearRatio(CurrentGear);
            float wheelAngularVel = currentSpeedMps / wheelRadiusM;
            double inputShaftRadS = (CurrentGear != 0) ? (wheelAngularVel * gearRatio * finalDriveRatio) : 0.0;
            double engineRadS = Engine.Rpm * (2.0 * Math.PI / 60.0);

            // 3. Progressive Clutch Torque Calculation
            // Clutch pedal: 1.0 = fully disengaged, 0.0 = fully engaged
            // Progressive bite curve between 0.8 and 0.0 pedal position
            double clutchTorque = 0.0;
            if (CurrentGear != 0)
            {
                float normalizedPedal = Mathf.Clamp01(cmd.clutch);
                float engagement = Mathf.Clamp01((1.0f - normalizedPedal) / 0.85f);
                float progressiveCapacity = maxClutchTorqueNm * (engagement * engagement);

                double slipRadS = engineRadS - inputShaftRadS;
                double rawTorque = slipRadS * 8.0;
                clutchTorque = Math.Max(-progressiveCapacity, Math.Min(progressiveCapacity, rawTorque));
            }

            // 4. Update Engine with Clutch Load
            Engine.Update(
                throttle: cmd.throttle,
                ignition: cmd.ignition,
                starter: cmd.starter,
                loadTorqueNm: (float)Math.Abs(clutchTorque),
                dtSeconds: dtSeconds
            );

            // 5. Axle Drive Torque
            double axleTorque = 0.0;
            if (CurrentGear != 0 && Engine.Phase == EnginePhase.Running)
            {
                axleTorque = DrivetrainMath.AxleTorque(
                    clutchTorqueNm: clutchTorque,
                    gearRatio: gearRatio,
                    finalDrive: finalDriveRatio,
                    efficiency: 0.9
                );
            }

            // 6. Brake System (65% Front, 35% Rear + 100% Handbrake on Rear)
            float totalServiceBrake = cmd.brake * maxBrakeTorqueNm;
            float frontBrakeTorque = totalServiceBrake * 0.65f;
            float rearBrakeTorque = totalServiceBrake * 0.35f + (cmd.handbrake ? handbrakeTorqueNm : 0f);
            float totalBrakeTorque = frontBrakeTorque + rearBrakeTorque;

            // 7. Vehicle Acceleration & Velocity Integration
            float driveForce = (float)(axleTorque / wheelRadiusM);
            float brakeForce = totalBrakeTorque / wheelRadiusM;

            // Rolling resistance and aerodynamic drag
            float rollingResistance = massKg * 9.81f * 0.015f * Math.Sign(currentSpeedMps);
            float aeroDrag = 0.5f * 1.225f * 0.32f * 2.2f * currentSpeedMps * Math.Abs(currentSpeedMps);
            float opposingForce = rollingResistance + aeroDrag;

            // Net longitudinal force
            float netForce = driveForce - opposingForce;

            // Apply braking against current velocity
            if (brakeForce > 0f)
            {
                float brakeDecelForce = brakeForce * (Math.Abs(currentSpeedMps) > 0.05f ? Math.Sign(currentSpeedMps) : (driveForce >= 0f ? 1f : -1f));
                if (Math.Abs(currentSpeedMps) < 0.1f && Math.Abs(driveForce) < brakeForce)
                {
                    // Full stop lock
                    currentSpeedMps = 0f;
                    netForce = 0f;
                }
                else
                {
                    netForce -= brakeDecelForce;
                }
            }

            float accel = netForce / massKg;
            currentSpeedMps += accel * dtSeconds;

            // Rigidbody sync if active in scene
            if (Body != null && !Body.isKinematic)
            {
                Vector3 forwardVelocity = transform.forward * currentSpeedMps;
                Body.linearVelocity = forwardVelocity;
            }

            // 8. Steering with Ackermann Geometry
            currentSteerAngleDeg = cmd.steering * maxSteeringAngleDeg;
            var (leftAngle, rightAngle) = CalculateAckermann(cmd.steering, wheelbaseM, trackM, maxSteeringAngleDeg);

            // 9. Publish VehicleState
            CurrentState = new VehicleState
            {
                tick = currentTick,
                simulationSeconds = simulationSeconds,
                signedSpeedMps = currentSpeedMps,
                engineRpm = Engine.Rpm,
                steeringRadians = currentSteerAngleDeg * Mathf.Deg2Rad,
                clutchTorqueNm = (float)clutchTorque,
                gear = CurrentGear,
                engine = Engine.Phase,
                leftIndicator = cmd.steering < -0.3f,
                rightIndicator = cmd.steering > 0.3f,
                lowBeam = cmd.ignition,
                highBeam = false,
                brakeLight = cmd.brake > 0.05f || cmd.handbrake
            };

            return CurrentState;
        }

        void ProcessGearShift(int requestedGear, float clutchPedal)
        {
            if (requestedGear == CurrentGear) return;

            // Shifting allowed when clutch is pressed (> 0.6) or shifting to neutral (0)
            if (clutchPedal >= 0.6f || requestedGear == 0)
            {
                CurrentGear = requestedGear;
            }
        }

        float GetGearRatio(int gear)
        {
            if (gear == 0) return 0f;
            if (gear == -1) return reverseRatio;
            if (gear >= 1 && gear <= ForwardGearRatios.Length) return ForwardGearRatios[gear - 1];
            return 0f;
        }

        /// <summary>
        /// Computes exact Ackermann steering geometry angles for front wheels.
        /// Inner wheel turns more sharply than outer wheel.
        /// </summary>
        public static (float leftSteerDeg, float rightSteerDeg) CalculateAckermann(
            float normalizedSteer, float wheelbase, float track, float maxSteerDeg = 32f)
        {
            if (Mathf.Abs(normalizedSteer) < 0.001f) return (0f, 0f);

            float centerAngleRad = Mathf.Abs(normalizedSteer) * maxSteerDeg * Mathf.Deg2Rad;
            float turningRadius = wheelbase / Mathf.Tan(centerAngleRad);

            float innerAngleRad = Mathf.Atan(wheelbase / (turningRadius - track * 0.5f));
            float outerAngleRad = Mathf.Atan(wheelbase / (turningRadius + track * 0.5f));

            float innerDeg = innerAngleRad * Mathf.Rad2Deg;
            float outerDeg = outerAngleRad * Mathf.Rad2Deg;

            if (normalizedSteer < 0f)
            {
                // Turning Left: Left wheel is inner (larger angle), Right wheel is outer
                return (-innerDeg, -outerDeg);
            }
            else
            {
                // Turning Right: Right wheel is inner (larger angle), Left wheel is outer
                return (outerDeg, innerDeg);
            }
        }
    }
}
