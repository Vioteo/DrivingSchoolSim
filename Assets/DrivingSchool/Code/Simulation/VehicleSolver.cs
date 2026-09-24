using System;
using DrivingSchool.Contracts;

namespace DrivingSchool.Simulation
{
    /// <summary>Per-wheel ground contact supplied by the physics adapter (car frame, projected on the ground plane).</summary>
    [Serializable] public struct WheelContact
    {
        public bool grounded;
        public float normalForceN;
        public float velocityRightMps, velocityForwardMps;
        public float frictionCoefficient;
    }

    /// <summary>Per-wheel solver output. Forces are in the car frame (x right, z forward), averaged over the step.</summary>
    [Serializable] public struct WheelOutput
    {
        public float forceRightN, forceForwardN;
        public float angularVelocityRadS, spinAngleRad, steerAngleRad;
        public float slipRatio, slipAngleRad;
        public bool grounded;
    }

    /// <summary>
    /// Pure vehicle solver: engine + clutch/torque converter + gearbox + open differential + 4 wheels with tyres,
    /// brakes, handbrake, park pawl, ABS, lights and wipers. It turns a DriverCommand and wheel contacts into
    /// contact forces; chassis integration is done by the caller (Unity Rigidbody or PlanarChassis).
    /// Wheel order: 0 FL, 1 FR, 2 RL, 3 RR.
    /// </summary>
    public sealed class VehicleSolver
    {
        public const float MaxSubstepS = 0.0005f;
        public const float LockSlipRadS = 0.5f;           // ≈5 rpm
        public const float LockupCapacityNm = 350f;       // AT lock-up clutch
        public const float ParkPawlTorqueNm = 4000f;
        public const float AnchorStiffnessNpm = 30000f;
        public const float StarterClutchThreshold = 0.8f;

        public readonly VehicleSpec Spec;
        public readonly EngineModel Engine;
        public readonly GearboxModel Gearbox;
        public readonly VehicleLightsModel Lights = new VehicleLightsModel();
        public readonly WiperModel Wipers = new WiperModel();
        public readonly WheelOutput[] Wheels = new WheelOutput[4];

        public VehicleState State { get; private set; }
        public float DragForceForwardN { get; private set; }
        public float ClutchTorqueNm { get; private set; }
        public bool ClutchLocked { get; private set; }
        public bool StarterInhibited { get; private set; }
        public float SteeringWheelDeg { get; private set; }

        readonly float[] omega = new float[4], spin = new float[4], anchorX = new float[4], anchorY = new float[4];
        readonly float[] fxW = new float[4], fyW = new float[4], brakeT = new float[4], driveT = new float[4];
        readonly float[] sumR = new float[4], sumF = new float[4], steer = new float[4];
        readonly float[] vxW = new float[4], vyW = new float[4];
        readonly int d0, d1;
        long tick; double time;

        public VehicleSolver(VehicleSpec spec)
        {
            Spec = (spec ?? new VehicleSpec()).Clone();
            Spec.Validate();
            Engine = new EngineModel(Spec.idleRpm, Spec.redlineRpm, Math.Max(Spec.redlineRpm, Spec.redlineRpm + 100f), EngineModel.DefaultStallRpm, Spec.engineInertiaKgm2);
            Gearbox = GearboxModel.FromSpec(Spec);
            if (Spec.drive == DriveLayout.FrontWheelDrive) { d0 = 0; d1 = 1; } else { d0 = 2; d1 = 3; }
        }

        public bool IsDriven(int wheel) => wheel == d0 || wheel == d1;

        public VehicleState Step(DriverCommand cmd, WheelContact[] contacts, float dtSeconds)
        {
            if (float.IsNaN(dtSeconds) || float.IsInfinity(dtSeconds) || dtSeconds <= 0f) throw new ArgumentOutOfRangeException(nameof(dtSeconds));
            if (contacts == null || contacts.Length != 4) throw new ArgumentException("Four wheel contacts required.");
            cmd.Validate();
            tick++; time += dtSeconds;
            float pedal = cmd.throttle;
            cmd.throttle = 1f - (float)Math.Pow(1f - pedal, Spec.throttleProgression); // throttle plate opening
            var s = Spec; float r = s.wheelRadiusM;

            // Forward speed of the body (average over grounded contacts).
            float vBody = 0; int n = 0;
            for (int k = 0; k < 4; k++) if (contacts[k].grounded) { vBody += contacts[k].velocityForwardMps; n++; }
            vBody = n > 0 ? vBody / n : (contacts[0].velocityForwardMps + contacts[2].velocityForwardMps) * 0.5f;

            // Gear / selector once per step.
            int gearBefore = Gearbox.CurrentGear;
            if (Gearbox.Type == TransmissionType.Manual) Gearbox.TryShiftManual(cmd.requestedGear, cmd.clutch);
            else Gearbox.UpdateAutomatic(Engine.OutputTorqueNm, Engine.Rpm, cmd.selector, vBody, dtSeconds, pedal);
            if (Gearbox.CurrentGear != gearBefore) ClutchLocked = false;

            // Starter interlock: clutch pressed or neutral (MT), P/N (AT).
            StarterInhibited = Gearbox.Type == TransmissionType.Manual
                ? !(Gearbox.CurrentGear == 0 || cmd.clutch >= StarterClutchThreshold)
                : !(Gearbox.Selector == AutomaticSelector.P || Gearbox.Selector == AutomaticSelector.N);
            bool starter = cmd.starter && !StarterInhibited;

            // Steering (Ackermann on the front axle).
            var (dl, dr) = SteeringGeometry.Ackermann(cmd.steering, s.wheelbaseM, s.trackM, s.maxSteerDeg);
            const float deg = (float)(Math.PI / 180.0);
            steer[0] = dl * deg; steer[1] = dr * deg; steer[2] = steer[3] = 0f;
            SteeringWheelDeg = cmd.steering * s.steeringWheelDegrees * 0.5f;

            // Contact velocities in each wheel frame; static anchors once per step.
            for (int k = 0; k < 4; k++)
            {
                float sn = (float)Math.Sin(steer[k]), cs = (float)Math.Cos(steer[k]);
                var c = contacts[k];
                vxW[k] = c.velocityRightMps * sn + c.velocityForwardMps * cs;
                vyW[k] = c.velocityRightMps * cs - c.velocityForwardMps * sn;
                bool held = c.grounded && omega[k] == 0f && Math.Abs(vxW[k]) < 0.2f;
                anchorX[k] = held ? anchorX[k] + vxW[k] * dtSeconds : 0f;
                anchorY[k] = c.grounded && Math.Abs(vxW[k]) < 0.3f ? anchorY[k] + vyW[k] * dtSeconds : 0f;
                float lim = c.frictionCoefficient * Math.Max(0f, c.normalForceN) / AnchorStiffnessNpm;
                anchorX[k] = Clamp(anchorX[k], -lim, lim); anchorY[k] = Clamp(anchorY[k], -lim, lim);
                sumR[k] = sumF[k] = 0f;
            }

            // Brake torques (magnitudes).
            float serviceFront = cmd.brake * s.maxBrakeTorqueNm * s.frontBrakeShare * 0.5f;
            float serviceRear = cmd.brake * s.maxBrakeTorqueNm * (1f - s.frontBrakeShare) * 0.5f;
            float hand = cmd.handbrake ? s.handbrakeTorqueNm * 0.5f : 0f;
            float pawl = Gearbox.ParkPawlEngaged ? ParkPawlTorqueNm : 0f;

            int steps = Math.Max(1, (int)Math.Ceiling(dtSeconds / MaxSubstepS));
            float h = dtSeconds / steps;
            float clutchSum = 0f;
            for (int st = 0; st < steps; st++)
            {
                // Tyre forces from current wheel speed.
                for (int k = 0; k < 4; k++)
                {
                    var c = contacts[k];
                    if (!c.grounded) { fxW[k] = fyW[k] = 0f; continue; }
                    var f = TireModel.Force(omega[k] * r - vxW[k], vxW[k], vyW[k], c.normalForceN, c.frictionCoefficient);
                    float fx = f.fx - AnchorStiffnessNpm * anchorX[k];
                    float fy = f.fy - AnchorStiffnessNpm * anchorY[k];
                    float lim = c.frictionCoefficient * c.normalForceN;
                    float mag = (float)Math.Sqrt(fx * fx + fy * fy);
                    if (mag > lim && mag > 0f) { fx *= lim / mag; fy *= lim / mag; }
                    fxW[k] = fx; fyW[k] = fy;
                    Wheels[k].slipRatio = f.slipRatio; Wheels[k].slipAngleRad = f.slipAngle;
                }
                for (int k = 0; k < 4; k++)
                {
                    float b = k < 2 ? serviceFront : serviceRear + hand;
                    if (s.abs && k < 4 && cmd.brake > 0f && Math.Abs(vxW[k]) > 1.5f && Wheels[k].slipRatio < -0.2f) b = (k < 2 ? serviceFront : serviceRear) * 0.4f + (k < 2 ? 0f : hand);
                    if (IsDriven(k)) b += pawl;
                    brakeT[k] = b; driveT[k] = 0f;
                }

                clutchSum += DrivetrainSubstep(cmd, starter, h);

                // Integrate wheel spin; brakes are Coulomb torques that can hold the wheel at rest.
                for (int k = 0; k < 4; k++)
                {
                    float w = omega[k] + h * (driveT[k] - fxW[k] * r) / s.wheelInertiaKgm2;
                    float db = h * brakeT[k] / s.wheelInertiaKgm2;
                    omega[k] = w > 0f ? Math.Max(0f, w - db) : Math.Min(0f, w + db);
                    spin[k] += omega[k] * h;
                    float sn = (float)Math.Sin(steer[k]), cs = (float)Math.Cos(steer[k]);
                    sumR[k] += fxW[k] * sn + fyW[k] * cs;
                    sumF[k] += fxW[k] * cs - fyW[k] * sn;
                }
                AfterWheelIntegration();
            }

            // Averaged forces + rolling resistance.
            for (int k = 0; k < 4; k++)
            {
                var c = contacts[k];
                float rr = c.grounded ? -s.rollingResistance * c.normalForceN * Clamp(vxW[k] / 0.3f, -1f, 1f) : 0f;
                float sn = (float)Math.Sin(steer[k]), cs = (float)Math.Cos(steer[k]);
                Wheels[k].forceRightN = sumR[k] / steps + rr * sn;
                Wheels[k].forceForwardN = sumF[k] / steps + rr * cs;
                Wheels[k].angularVelocityRadS = omega[k];
                Wheels[k].spinAngleRad = spin[k] % (2f * (float)Math.PI);
                Wheels[k].steerAngleRad = steer[k];
                Wheels[k].grounded = c.grounded;
            }
            DragForceForwardN = -0.5f * 1.225f * s.dragCoefficient * s.frontalAreaM2 * vBody * Math.Abs(vBody);
            ClutchTorqueNm = clutchSum / steps;

            float carrier = 0.5f * (omega[d0] + omega[d1]);
            Gearbox.PublishShaft(ClutchTorqueNm * Gearbox.TotalRatio * s.drivetrainEfficiency, carrier * Gearbox.TotalRatio * 60f / (2f * (float)Math.PI));

            bool reverse = Gearbox.CurrentGear == -1;
            Lights.Update(cmd, reverse, dtSeconds);
            Wipers.Update(cmd.wipers, cmd.washer, cmd.ignition, dtSeconds);

            State = new VehicleState
            {
                tick = tick, simulationSeconds = time,
                signedSpeedMps = vBody, engineRpm = Engine.Rpm,
                steeringRadians = 0.5f * (steer[0] + steer[1]),
                clutchTorqueNm = ClutchTorqueNm, gear = Gearbox.CurrentGear, engine = Engine.Phase,
                leftIndicator = Lights.LeftActive, rightIndicator = Lights.RightActive,
                lowBeam = Lights.LowBeam, highBeam = Lights.HighBeam, brakeLight = Lights.Brake,
                indicatorLampOn = Lights.LampOn, hazard = Lights.Hazard, parkingLights = Lights.Parking,
                reverseLight = Lights.Reverse, horn = Lights.Horn, seatbelt = cmd.seatbelt, handbrake = cmd.handbrake,
                transmission = Gearbox.Type, selector = Gearbox.Selector,
                wipers = cmd.wipers, wiperAngle01 = Wipers.Angle01,
                engineTorqueNm = Engine.OutputTorqueNm,
                wheelSpeedFrontRadS = 0.5f * (omega[0] + omega[1]), wheelSpeedRearRadS = 0.5f * (omega[2] + omega[3])
            };
            return State;
        }

        // Lock-up bookkeeping between drivetrain solve and wheel integration.
        bool slipping; float slipBefore; bool lockedThisStep;

        float DrivetrainSubstep(DriverCommand cmd, bool starter, float h)
        {
            var s = Spec; float r = s.wheelRadiusM; float eta = s.drivetrainEfficiency;
            float i = Gearbox.TotalRatio;
            slipping = false; lockedThisStep = false;
            bool running = Engine.Phase == EnginePhase.Running;
            bool manual = Gearbox.Type == TransmissionType.Manual;

            if (i == 0f || !running)
            {
                Engine.Update(cmd.throttle, cmd.ignition, starter, 0f, h);
                ClutchLocked = false;
                // A stopped engine in gear holds the driven wheels through the engaged clutch.
                if (i != 0f && manual && Engine.Phase != EnginePhase.Running)
                {
                    float hold = ClutchModel.Capacity(cmd.clutch, s.maxClutchTorqueNm) * Math.Abs(i) * eta * 0.5f;
                    brakeT[d0] += hold; brakeT[d1] += hold;
                }
                return 0f;
            }

            float carrier = 0.5f * (omega[d0] + omega[d1]);
            float we = Engine.Rpm * 2f * (float)Math.PI / 60f;
            float tFree = Engine.NetTorqueAt(cmd.throttle);
            float cap;
            if (manual) cap = ClutchModel.Capacity(cmd.clutch, s.maxClutchTorqueNm);
            else
            {
                float sr = we > 1f ? carrier * i / we : 0f;
                bool lockupAllowed = Gearbox.CurrentGear >= 2 && Gearbox.SecondsSinceShift > 1f && cmd.throttle < 0.95f && sr > 0.85f;
                cap = lockupAllowed ? LockupCapacityNm : 0f;
                if (cap == 0f) ClutchLocked = false;
            }

            if (ClutchLocked && cap > 0f)
            {
                float je = Engine.FlywheelInertia;
                float sumFr = fxW[d0] * r + fxW[d1] * r;
                float sumTb = brakeT[d0] * Math.Sign(omega[d0]) + brakeT[d1] * Math.Sign(omega[d1]);
                float jeq = je * i * i * eta + 2f * s.wheelInertiaKgm2;
                float alpha = (tFree * i * eta - sumFr - sumTb) / jeq;
                float tc = tFree - je * alpha * i;
                if (Math.Abs(tc) <= cap)
                {
                    Engine.Update(cmd.throttle, cmd.ignition, false, tc, h);
                    float td = tc * i * eta;
                    driveT[d0] = driveT[d1] = 0.5f * td;
                    lockedThisStep = true;
                    return tc;
                }
                ClutchLocked = false;
            }

            float tcE, tdWheel;
            float slip = we - carrier * i;
            if (manual || cap > 0f)
            {
                tcE = Math.Abs(slip) < 1e-4f ? 0f : Math.Sign(slip) * cap;
                tdWheel = tcE * i * eta;
                slipping = cap > 0f; slipBefore = slip;
            }
            else
            {
                var (pump, turbine) = TorqueConverterModel.Torques(we, carrier * i);
                tcE = pump; tdWheel = turbine * i * eta;
            }
            Engine.Update(cmd.throttle, cmd.ignition, starter, tcE, h);
            driveT[d0] = driveT[d1] = 0.5f * tdWheel;
            return tcE;
        }

        void AfterWheelIntegration()
        {
            float i = Gearbox.TotalRatio;
            float carrierRpm = 0.5f * (omega[d0] + omega[d1]) * i * 60f / (2f * (float)Math.PI);
            if (lockedThisStep) { Engine.LockToRpm(carrierRpm); if (Engine.Phase != EnginePhase.Running) ClutchLocked = false; return; }
            if (!slipping || Engine.Phase != EnginePhase.Running) return;
            float slipNow = (Engine.Rpm - carrierRpm) * 2f * (float)Math.PI / 60f;
            if (Math.Abs(slipNow) < LockSlipRadS || Math.Sign(slipNow) != Math.Sign(slipBefore)) ClutchLocked = true;
        }

        static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
