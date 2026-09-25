using System;
using DrivingSchool.Contracts;

namespace DrivingSchool.Simulation
{
    /// <summary>
    /// Deterministic internal combustion engine (ICE) simulation model.
    /// Pure C# domain model without engine/platform references.
    /// </summary>
    public sealed class EngineModel
    {
        public const float DefaultIdleRpm = 850f;
        public const float DefaultRedlineRpm = 6500f;
        public const float DefaultCutoffRpm = 6600f;
        public const float DefaultStallRpm = 400f;
        public const float DefaultFlywheelInertia = 0.2f; // kg * m^2

        readonly float idleRpm;
        readonly float redlineRpm;
        readonly float cutoffRpm;
        readonly float stallRpm;
        readonly float flywheelInertia;

        public EnginePhase Phase { get; private set; } = EnginePhase.Off;
        public float Rpm { get; private set; } = 0f;
        public float OutputTorqueNm { get; private set; } = 0f;

        public float IdleRpm => idleRpm;
        public float RedlineRpm => redlineRpm;
        public float StallRpm => stallRpm;

        float crankingTimer = 0f;

        public EngineModel(
            float idleRpm = DefaultIdleRpm,
            float redlineRpm = DefaultRedlineRpm,
            float cutoffRpm = DefaultCutoffRpm,
            float stallRpm = DefaultStallRpm,
            float flywheelInertia = DefaultFlywheelInertia)
        {
            if (idleRpm <= stallRpm || redlineRpm <= idleRpm || cutoffRpm < redlineRpm || flywheelInertia <= 0f)
                throw new ArgumentException("Invalid engine physical parameters.");

            this.idleRpm = idleRpm;
            this.redlineRpm = redlineRpm;
            this.cutoffRpm = cutoffRpm;
            this.stallRpm = stallRpm;
            this.flywheelInertia = flywheelInertia;
        }

        public void Reset()
        {
            Phase = EnginePhase.Off;
            Rpm = 0f;
            OutputTorqueNm = 0f;
            crankingTimer = 0f;
        }

        public void Update(float throttle, bool ignition, bool starter, float loadTorqueNm, float dtSeconds)
        {
            ValidateInputs(throttle, loadTorqueNm, dtSeconds);

            if (!ignition)
            {
                Phase = EnginePhase.Off;
                OutputTorqueNm = 0f;
                crankingTimer = 0f;
                Rpm = Math.Max(0f, Rpm - 1800f * dtSeconds);
                return;
            }

            switch (Phase)
            {
                case EnginePhase.Off:
                    Phase = EnginePhase.Ignition;
                    Rpm = 0f;
                    OutputTorqueNm = 0f;
                    crankingTimer = 0f;
                    break;

                case EnginePhase.Ignition:
                case EnginePhase.Stalled:
                    if (starter)
                    {
                        Phase = EnginePhase.Cranking;
                        crankingTimer = 0f;
                        SimulateCranking(starter, dtSeconds);
                    }
                    else
                    {
                        Rpm = 0f;
                        OutputTorqueNm = 0f;
                    }
                    break;

                case EnginePhase.Cranking:
                    SimulateCranking(starter, dtSeconds);
                    break;

                case EnginePhase.Running:
                    SimulateRunning(throttle, loadTorqueNm, dtSeconds);
                    break;
            }
        }

        void SimulateCranking(bool starter, float dtSeconds)
        {
            if (!starter)
            {
                Phase = EnginePhase.Ignition;
                crankingTimer = 0f;
                Rpm = Math.Max(0f, Rpm - 1200f * dtSeconds);
                OutputTorqueNm = 0f;
                return;
            }

            crankingTimer += dtSeconds;
            Rpm = Math.Min(450f, Rpm + 800f * dtSeconds);
            OutputTorqueNm = 20f;

            if (crankingTimer >= 0.4f && Rpm >= 400f)
            {
                Phase = EnginePhase.Running;
                crankingTimer = 0f;
            }
        }

        public float FlywheelInertia => flywheelInertia;

        /// <summary>
        /// Torque available at the crankshaft flange before any clutch load (gross minus internal friction)
        /// at the current rpm. Zero unless the engine is running. Used by the drivetrain lock-up solve.
        /// </summary>
        public float NetTorqueAt(float throttle)
        {
            if (Phase != EnginePhase.Running) return 0f;
            return GrossTorque(throttle) - InternalFriction(throttle);
        }

        /// <summary>Keeps a running engine on the speed imposed by a locked clutch; stalls below stallRpm.</summary>
        public void LockToRpm(float rpm)
        {
            if (float.IsNaN(rpm) || float.IsInfinity(rpm)) throw new ArgumentOutOfRangeException(nameof(rpm));
            if (Phase != EnginePhase.Running) return;
            Rpm = Math.Min(cutoffRpm, rpm);
            if (Rpm < stallRpm) { Phase = EnginePhase.Stalled; Rpm = 0f; OutputTorqueNm = 0f; }
        }

        /// <summary>
        /// Mechanical friction plus pumping loss. With the throttle closed the engine pumps against manifold vacuum,
        /// which is what makes engine braking noticeable; the loss fades quickly as the throttle opens (≈40 Nm extra at 5000 rpm for a 1.6 l engine).
        /// </summary>
        float InternalFriction(float throttle) => 12f + (Rpm / 1000f) * 4f + (1f - throttle) * (1f - throttle) * (Rpm / 1000f) * 8f;

        float GrossTorque(float throttle)
        {
            float iacTorque = 0f;
            if (Rpm < idleRpm + 150f)
            {
                // Idle air control: proportional around the target so falling revs settle back to idle instead of
                // hanging anywhere between the target and idle + 150 rpm.
                float idleError = (idleRpm + 50f) - Rpm;
                iacTorque = Math.Max(0f, Math.Min(60f, idleError * 0.45f + InternalFriction(throttle)));
            }
            bool fuelCutoff = Rpm >= redlineRpm;
            float combustionTorque = fuelCutoff ? 0f : CalculateMaxTorque(Rpm) * throttle;
            return Math.Max(iacTorque, combustionTorque);
        }

        void SimulateRunning(float throttle, float loadTorqueNm, float dtSeconds)
        {
            float internalFriction = InternalFriction(throttle);
            float grossTorque = GrossTorque(throttle);
            OutputTorqueNm = grossTorque;

            float netTorque = grossTorque - internalFriction - loadTorqueNm;

            const float radSToRpm = 60f / (2f * (float)Math.PI);
            float dRpm = (netTorque / flywheelInertia) * radSToRpm * dtSeconds;

            Rpm += dRpm;

            if (Rpm > cutoffRpm)
            {
                Rpm = cutoffRpm;
            }

            if (Rpm < stallRpm)
            {
                Phase = EnginePhase.Stalled;
                Rpm = 0f;
                OutputTorqueNm = 0f;
            }
        }

        static float CalculateMaxTorque(float rpm)
        {
            if (rpm < 300f) return 60f;
            if (rpm < 1000f) return 95f + (rpm - 300f) / 700f * 20f;
            if (rpm < 2500f) return 115f + (rpm - 1000f) / 1500f * 25f;
            if (rpm < 4200f) return 140f + (rpm - 2500f) / 1700f * 15f;
            if (rpm < 5500f) return 155f - (rpm - 4200f) / 1300f * 15f;
            return Math.Max(70f, 140f - (rpm - 5500f) / 1500f * 40f);
        }

        static void ValidateInputs(float throttle, float loadTorqueNm, float dtSeconds)
        {
            if (float.IsNaN(throttle) || float.IsInfinity(throttle) || throttle < 0f || throttle > 1f)
                throw new ArgumentOutOfRangeException(nameof(throttle), "Throttle must be in range [0, 1].");

            if (float.IsNaN(loadTorqueNm) || float.IsInfinity(loadTorqueNm))
                throw new ArgumentOutOfRangeException(nameof(loadTorqueNm), "Load torque must be a finite number.");

            if (float.IsNaN(dtSeconds) || float.IsInfinity(dtSeconds) || dtSeconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(dtSeconds), "dtSeconds must be greater than zero and finite.");
        }
    }
}
