using System;
using DrivingSchool.Contracts;

namespace DrivingSchool.Simulation
{
    /// <summary>
    /// T09: manual (−1/0/1..6) and automatic (P/R/N/D) gearbox. Ratios exclude the final drive;
    /// AxleTorqueNm and InputShaftRpm include it.
    /// </summary>
    public sealed class GearboxModel
    {
        public const float ShiftClutchThreshold = 0.6f;  // pedal travel needed to change a manual gear
        public const float SelectorLockSpeedMps = 2f;    // P and R refused above this forward speed
        public const float BaseUpshiftRpm = 2500f, BaseDownshiftRpm = 1300f;
        public const float MinShiftIntervalS = 0.6f;

        readonly float[] ratios; readonly float reverse, finalDrive, efficiency;

        public TransmissionType Type { get; }
        public int CurrentGear { get; private set; }
        public AutomaticSelector Selector { get; private set; } = AutomaticSelector.P;
        public float CurrentRatio => RatioOf(CurrentGear);
        public float TotalRatio => CurrentRatio * finalDrive;
        public float Efficiency => efficiency;
        public float AxleTorqueNm { get; private set; }
        public float InputShaftRpm { get; private set; }
        public bool LastRequestRefused { get; private set; }
        public float SecondsSinceShift { get; private set; } = 10f;
        public int MaxGear => ratios.Length;

        public GearboxModel(float[] forwardRatios, float reverseRatio, float finalDrive, float efficiency, TransmissionType type)
        {
            if (forwardRatios == null || forwardRatios.Length == 0 || forwardRatios.Length > 6 || reverseRatio >= 0 || !(finalDrive > 0) || efficiency <= 0 || efficiency > 1)
                throw new ArgumentException("Invalid gearbox ratios.");
            ratios = (float[])forwardRatios.Clone(); reverse = reverseRatio; this.finalDrive = finalDrive; this.efficiency = efficiency; Type = type;
        }

        public static GearboxModel FromSpec(VehicleSpec s) => new GearboxModel(s.gearRatios, s.reverseRatio, s.finalDrive, s.drivetrainEfficiency, s.transmission);

        public float RatioOf(int gear)
        {
            if (gear < -1 || gear > ratios.Length) throw new ArgumentOutOfRangeException(nameof(gear));
            return gear == 0 ? 0f : gear == -1 ? reverse : ratios[gear - 1];
        }

        /// <summary>Manual shift through the clutch: neutral is always reachable, a gear only with the pedal pressed.</summary>
        public bool TryShiftManual(int requestedGear, float clutchPedal)
        {
            if (requestedGear < -1 || requestedGear > ratios.Length) throw new ArgumentOutOfRangeException(nameof(requestedGear));
            LastRequestRefused = false;
            if (requestedGear == CurrentGear) return true;
            if (requestedGear != 0 && clutchPedal < ShiftClutchThreshold) { LastRequestRefused = true; return false; }
            CurrentGear = requestedGear; SecondsSinceShift = 0f;
            return true;
        }

        /// <summary>Pure torque/speed mapping of the card contract; gear selection is taken as given.</summary>
        public void UpdateManual(float clutchTorqueNm, int requestedGear, float wheelAngularVelocityRadSec)
        {
            if (requestedGear < -1 || requestedGear > ratios.Length) throw new ArgumentOutOfRangeException(nameof(requestedGear));
            if (float.IsNaN(clutchTorqueNm) || float.IsInfinity(clutchTorqueNm) || float.IsNaN(wheelAngularVelocityRadSec) || float.IsInfinity(wheelAngularVelocityRadSec))
                throw new ArgumentOutOfRangeException(nameof(clutchTorqueNm));
            CurrentGear = requestedGear;
            AxleTorqueNm = clutchTorqueNm * TotalRatio * efficiency;
            InputShaftRpm = wheelAngularVelocityRadSec * TotalRatio * 60f / (2f * (float)Math.PI);
        }

        /// <summary>
        /// Automatic: applies the selector (P/R refused above SelectorLockSpeedMps forward), shifts by engine rpm
        /// with throttle-dependent thresholds, and maps turbine torque to the axle.
        /// </summary>
        public void UpdateAutomatic(float engineTorqueNm, float engineRpm, AutomaticSelector selector, float vehicleSpeedMps, float dtSeconds)
        {
            UpdateAutomatic(engineTorqueNm, engineRpm, selector, vehicleSpeedMps, dtSeconds, 0.3f);
        }

        public void UpdateAutomatic(float engineTorqueNm, float engineRpm, AutomaticSelector selector, float vehicleSpeedMps, float dtSeconds, float throttle)
        {
            if (float.IsNaN(engineTorqueNm) || float.IsInfinity(engineTorqueNm) || float.IsNaN(engineRpm) || float.IsInfinity(engineRpm) ||
                float.IsNaN(vehicleSpeedMps) || float.IsInfinity(vehicleSpeedMps) || float.IsNaN(dtSeconds) || float.IsInfinity(dtSeconds) || dtSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(engineTorqueNm));
            if (!Enum.IsDefined(typeof(AutomaticSelector), selector)) throw new ArgumentOutOfRangeException(nameof(selector));
            SecondsSinceShift += dtSeconds;
            LastRequestRefused = false;
            if (selector != Selector)
            {
                bool refuse = (selector == AutomaticSelector.P || selector == AutomaticSelector.R) && vehicleSpeedMps > SelectorLockSpeedMps;
                if (refuse) LastRequestRefused = true; else Selector = selector;
            }
            switch (Selector)
            {
                case AutomaticSelector.P: case AutomaticSelector.N: SetGear(0); break;
                case AutomaticSelector.R: SetGear(-1); break;
                case AutomaticSelector.D:
                    if (CurrentGear < 1) SetGear(1);
                    else if (SecondsSinceShift >= MinShiftIntervalS)
                    {
                        float t = Math.Max(0f, Math.Min(1f, (throttle - 0.3f) / 0.7f));
                        float up = BaseUpshiftRpm + t * (5800f - BaseUpshiftRpm);
                        float down = BaseDownshiftRpm + t * (2600f - BaseDownshiftRpm);
                        if (engineRpm > up && CurrentGear < ratios.Length) SetGear(CurrentGear + 1);
                        else if (engineRpm < down && CurrentGear > 1) SetGear(CurrentGear - 1);
                    }
                    break;
            }
            AxleTorqueNm = engineTorqueNm * TotalRatio * efficiency;
        }

        /// <summary>Solver publishes the gearbox input speed derived from the driven wheels.</summary>
        public void PublishShaft(float axleTorqueNm, float inputShaftRpm) { AxleTorqueNm = axleTorqueNm; InputShaftRpm = inputShaftRpm; }

        public bool ParkPawlEngaged => Type == TransmissionType.Automatic && Selector == AutomaticSelector.P;

        void SetGear(int g) { if (g != CurrentGear) { CurrentGear = g; SecondsSinceShift = 0f; } }
    }
}
