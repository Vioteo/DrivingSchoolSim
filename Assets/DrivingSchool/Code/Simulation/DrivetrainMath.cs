using System;
namespace DrivingSchool.Simulation
{
    // Reference calculations for future integration, not a completed vehicle solver.
    public static class DrivetrainMath
    {
        public static double AxleTorque(double clutchTorqueNm, double gearRatio, double finalDrive, double efficiency)
        {
            if (double.IsNaN(clutchTorqueNm) || double.IsInfinity(clutchTorqueNm) ||
                double.IsNaN(gearRatio) || double.IsInfinity(gearRatio) ||
                double.IsNaN(finalDrive) || double.IsInfinity(finalDrive) ||
                double.IsNaN(efficiency) || double.IsInfinity(efficiency))
            {
                throw new ArgumentOutOfRangeException("DrivetrainMath.AxleTorque inputs must be finite numbers.");
            }

            if (finalDrive <= 0 || efficiency < 0 || efficiency > 1) throw new ArgumentOutOfRangeException();
            return clutchTorqueNm * gearRatio * finalDrive * efficiency;
        }
        public static double ClutchTorque(double engineRadS, double inputShaftRadS, double pedal, double capacityNm, double coupling)
        {
            if (double.IsNaN(engineRadS) || double.IsInfinity(engineRadS) ||
                double.IsNaN(inputShaftRadS) || double.IsInfinity(inputShaftRadS) ||
                double.IsNaN(pedal) || double.IsInfinity(pedal) ||
                double.IsNaN(capacityNm) || double.IsInfinity(capacityNm) ||
                double.IsNaN(coupling) || double.IsInfinity(coupling))
            {
                throw new ArgumentOutOfRangeException("DrivetrainMath.ClutchTorque inputs must be finite numbers.");
            }

            if (pedal < 0 || pedal > 1 || capacityNm < 0 || coupling < 0) throw new ArgumentOutOfRangeException();
            double cap = capacityNm * (1 - pedal);
            return Math.Max(-cap, Math.Min(cap, (engineRadS - inputShaftRadS) * coupling));
        }
    }
}
