using System;

namespace DrivingSchool.Simulation
{
    /// <summary>
    /// Combined-slip tyre with a smooth friction-circle limit. Game calibration, not Pacejka-fitted data.
    /// A minimum speed in the slip denominators keeps parking-speed manoeuvres stable at a 100 Hz physics step.
    /// </summary>
    public static class TireModel
    {
        public const float LowSpeedDenominatorMps = 3f;
        public const float LongitudinalStiffnessPerN = 16f;  // dFx/dκ per newton of load
        public const float CorneringStiffnessPerN = 12f;     // dFy/dα per newton of load, per radian

        /// <summary>
        /// Forces in the wheel frame. fx along the wheel heading, fy to the wheel's right.
        /// slipVelocity = ω·r − vx.
        /// </summary>
        public static (float fx, float fy, float slipRatio, float slipAngle) Force(float slipVelocity, float vx, float vy, float normalForceN, float mu)
        {
            if (normalForceN <= 0f || mu <= 0f) return (0f, 0f, 0f, 0f);
            float den = Math.Max(Math.Abs(vx), LowSpeedDenominatorMps);
            float kappa = slipVelocity / den;
            float tanAlpha = vy / den;
            float fx0 = LongitudinalStiffnessPerN * normalForceN * kappa;
            float fy0 = -CorneringStiffnessPerN * normalForceN * tanAlpha;
            float f0 = (float)Math.Sqrt(fx0 * fx0 + fy0 * fy0);
            float fmax = mu * normalForceN;
            if (f0 < 1e-6f) return (0f, 0f, kappa, (float)Math.Atan(tanAlpha));
            float x = f0 / fmax;
            float scale = fmax * x / (float)Math.Pow(1.0 + x * x * x * x, 0.25) / f0;
            return (fx0 * scale, fy0 * scale, kappa, (float)Math.Atan(tanAlpha));
        }
    }
}
