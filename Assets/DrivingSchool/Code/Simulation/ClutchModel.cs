using System;

namespace DrivingSchool.Simulation
{
    /// <summary>
    /// Dry clutch (T08). Pedal 1 = disengaged (0 Nm), 0 = fully engaged (max capacity); capacity is linear in travel.
    /// Update() is the stand-alone Coulomb model; the vehicle solver uses Capacity() together with its own lock-up
    /// solve because a lock-up needs the inertias on both sides.
    /// </summary>
    public sealed class ClutchModel
    {
        public const float LockThresholdRpm = 5f;

        public float TransmittedTorqueNm { get; private set; }
        public bool IsLockedUp { get; private set; }
        public float SlipSpeedRpm { get; private set; }
        public float CapacityNm { get; private set; }

        public static float Capacity(float clutchPedal, float maxCapacityNm)
        {
            if (float.IsNaN(clutchPedal) || float.IsInfinity(clutchPedal) || float.IsNaN(maxCapacityNm) || float.IsInfinity(maxCapacityNm) || maxCapacityNm < 0)
                throw new ArgumentOutOfRangeException(nameof(clutchPedal));
            float p = Math.Max(0f, Math.Min(1f, clutchPedal));
            return maxCapacityNm * (1f - p);
        }

        /// <summary>Positive torque accelerates the gearbox input shaft and brakes the crankshaft.</summary>
        public void Update(float clutchPedal, float engineRpm, float gearboxInputRpm, float maxCapacityNm, float dtSeconds)
        {
            if (float.IsNaN(engineRpm) || float.IsInfinity(engineRpm) || float.IsNaN(gearboxInputRpm) || float.IsInfinity(gearboxInputRpm) ||
                float.IsNaN(dtSeconds) || float.IsInfinity(dtSeconds) || dtSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(engineRpm));
            CapacityNm = Capacity(clutchPedal, maxCapacityNm);
            SlipSpeedRpm = engineRpm - gearboxInputRpm;
            if (CapacityNm <= 0f) { IsLockedUp = false; TransmittedTorqueNm = 0f; return; }
            if (Math.Abs(SlipSpeedRpm) < LockThresholdRpm)
            {
                // Static friction: the shafts turn together, torque is whatever the load demands up to capacity.
                IsLockedUp = true; TransmittedTorqueNm = 0f; return;
            }
            IsLockedUp = false;
            TransmittedTorqueNm = Math.Sign(SlipSpeedRpm) * CapacityNm;
        }
    }
}
