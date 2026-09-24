using System;

namespace DrivingSchool.Simulation
{
    /// <summary>
    /// Game-calibrated hydrodynamic torque converter: pump torque ~ C·ω², fading to zero as the speed ratio nears 1;
    /// torque multiplication falls from StallTorqueRatio to 1 at the coupling point.
    /// </summary>
    public static class TorqueConverterModel
    {
        public const float CapacityFactor = 0.0028f;   // Nm / (rad/s)^2, stall ~2200 rpm at full load
        public const float StallTorqueRatio = 2.0f;
        public const float CouplingPoint = 0.85f;

        /// <summary>Returns (pumpTorque on engine, turbineTorque on gearbox input).</summary>
        public const float SlipWindow = 0.3f;          // pump torque fades to zero over the last 30 % of speed ratio

        public static (float pump, float turbine) Torques(float engineRadS, float turbineRadS)
        {
            float slip = engineRadS - turbineRadS;
            float wMax = Math.Max(Math.Abs(engineRadS), Math.Abs(turbineRadS));
            if (wMax < 1e-3f) return (0f, 0f);
            float fill = Math.Min(1f, Math.Abs(slip) / (SlipWindow * wMax));
            float pump = Math.Sign(slip) * CapacityFactor * wMax * wMax * fill;
            float ratio = 1f;
            if (slip > 0f && engineRadS > 1f)
            {
                float sr = Math.Max(0f, turbineRadS / engineRadS);
                ratio = 1f + (StallTorqueRatio - 1f) * Math.Max(0f, 1f - sr / CouplingPoint);
            }
            return (pump, pump * ratio);
        }
    }
}
