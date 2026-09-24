using System;

namespace DrivingSchool.Simulation
{
    public static class SteeringGeometry
    {
        /// <summary>Ackermann angles in degrees; positive = steer right. Inner wheel turns more.</summary>
        public static (float leftDeg, float rightDeg) Ackermann(float normalizedSteer, float wheelbase, float track, float maxSteerDeg)
        {
            if (float.IsNaN(normalizedSteer) || float.IsInfinity(normalizedSteer)) throw new ArgumentOutOfRangeException(nameof(normalizedSteer));
            normalizedSteer = Math.Max(-1f, Math.Min(1f, normalizedSteer));
            if (Math.Abs(normalizedSteer) < 0.001f) return (0f, 0f);
            const double deg = Math.PI / 180.0;
            double centre = Math.Abs(normalizedSteer) * maxSteerDeg * deg;
            double radius = wheelbase / Math.Tan(centre);
            float inner = (float)(Math.Atan(wheelbase / (radius - track * 0.5)) / deg);
            float outer = (float)(Math.Atan(wheelbase / (radius + track * 0.5)) / deg);
            return normalizedSteer < 0f ? (-inner, -outer) : (outer, inner);
        }
    }
}
