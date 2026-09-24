using System;
using DrivingSchool.Contracts;

namespace DrivingSchool.Simulation
{
    /// <summary>T19: gameplay friction per surface. Coefficients are calibration targets, not measured road data.</summary>
    public static class SurfaceFrictionModel
    {
        public const float Gravity = 9.81f;

        public static float GetFrictionCoefficient(SurfaceType surface)
        {
            switch (surface)
            {
                case SurfaceType.DryAsphalt: return 0.95f;
                case SurfaceType.WetAsphalt: return 0.65f;
                case SurfaceType.PackedSnow: return 0.30f;
                case SurfaceType.BlackIce: return 0.15f;
                default: throw new ArgumentOutOfRangeException(nameof(surface));
            }
        }

        public static float CalculateMaxTireForce(float normalForceN, SurfaceType surface)
        {
            if (float.IsNaN(normalForceN) || float.IsInfinity(normalForceN)) throw new ArgumentOutOfRangeException(nameof(normalForceN));
            return Math.Max(0f, normalForceN) * GetFrictionCoefficient(surface);
        }

        /// <summary>Ideal braking distance v²/(2μg), no reaction time.</summary>
        public static float CalculateStoppingDistanceM(float initialSpeedMps, SurfaceType surface)
        {
            if (float.IsNaN(initialSpeedMps) || float.IsInfinity(initialSpeedMps) || initialSpeedMps < 0) throw new ArgumentOutOfRangeException(nameof(initialSpeedMps));
            return initialSpeedMps * initialSpeedMps / (2f * GetFrictionCoefficient(surface) * Gravity);
        }

        /// <summary>C1-continuous blend between two surfaces, t in [0,1] (smoothstep).</summary>
        public static float Blend(SurfaceType from, SurfaceType to, float t)
        {
            if (float.IsNaN(t)) throw new ArgumentOutOfRangeException(nameof(t));
            t = Math.Max(0f, Math.Min(1f, t));
            float s = t * t * (3f - 2f * t);
            float a = GetFrictionCoefficient(from), b = GetFrictionCoefficient(to);
            return a + (b - a) * s;
        }
    }
}
