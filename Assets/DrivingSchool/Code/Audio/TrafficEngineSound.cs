using System;

namespace DrivingSchool.Audio
{
    /// <summary>
    /// Engine note of an AI car from its motion only (the bots have no drivetrain): a virtual six-speed automatic
    /// that shifts up earlier when cruising and later when accelerating hard.
    /// </summary>
    public static class TrafficEngineSound
    {
        public const float IdleRpm = 850f, WheelRadiusM = 0.32f, FinalDrive = 4.1f;
        static readonly float[] Ratios = { 3.6f, 2.1f, 1.4f, 1.05f, 0.84f, 0.69f };

        /// <summary>Crank rpm in a gear at a road speed.</summary>
        public static float RpmInGear(float speedMps, int gear)
        {
            int g = Math.Max(1, Math.Min(Ratios.Length, gear));
            return Math.Abs(speedMps) / WheelRadiusM * Ratios[g - 1] * FinalDrive * 60f / (2f * MathF.PI);
        }

        /// <summary>Upshift point, rpm: 2200 when cruising … 3700 at full acceleration.</summary>
        public static float UpshiftRpm(float accelMps2) => 2200f + 1500f * Dsp.Clamp01(accelMps2 / 2.5f);

        public static int Gear(float speedMps, float accelMps2)
        {
            float up = UpshiftRpm(accelMps2);
            for (int g = 1; g < Ratios.Length; g++) if (RpmInGear(speedMps, g) <= up) return g;
            return Ratios.Length;
        }

        public static float Rpm(float speedMps, float accelMps2)
        {
            if (!Dsp.Finite(speedMps) || !Dsp.Finite(accelMps2)) return IdleRpm;
            float rpm = RpmInGear(speedMps, Gear(speedMps, accelMps2));
            // Pulling away: the torque converter lets the engine run above the road speed.
            float launch = IdleRpm + 900f * Dsp.Clamp01(accelMps2 / 2f) * Dsp.Clamp01(1f - Math.Abs(speedMps) / 6f);
            return Math.Max(Math.Max(IdleRpm, launch), rpm);
        }

        /// <summary>Engine load from acceleration: 0.15 cruising, 1 at ≈2 m/s², 0 when slowing down.</summary>
        public static float Load(float accelMps2) => !Dsp.Finite(accelMps2) ? 0f : Dsp.Clamp01(0.15f + accelMps2 / 2.35f);
    }
}
