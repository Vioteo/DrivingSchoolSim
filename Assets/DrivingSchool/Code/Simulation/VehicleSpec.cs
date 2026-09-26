using System;
using DrivingSchool.Contracts;

namespace DrivingSchool.Simulation
{
    public enum DriveLayout { FrontWheelDrive, RearWheelDrive }

    /// <summary>
    /// Vehicle calibration. Field names match StreamingAssets/Examples/vehicle.json so a Unity adapter can
    /// overwrite defaults with JsonUtility. Values are design targets, not measurements.
    /// </summary>
    [Serializable] public sealed class VehicleSpec
    {
        public float massKg = 1350f;
        public float wheelbaseM = 2.72f;
        public float trackM = 1.71f;
        public float wheelRadiusM = 0.327f;
        public float[] centreOfMassM = { 0f, 0.51f, -0.1f };
        public float steeringWheelDegrees = 900f;
        public float maxSteerDeg = 32f;
        public float[] gearRatios = { 3.6f, 2.1f, 1.4f, 1.05f, 0.84f, 0.69f };
        public float reverseRatio = -3.5f;
        public float finalDrive = 4.1f;
        public float drivetrainEfficiency = 0.9f;
        public float idleRpm = 850f;
        public float redlineRpm = 6500f;
        public float engineInertiaKgm2 = 0.2f;
        public float maxClutchTorqueNm = 240f;
        public float maxBrakeTorqueNm = 3500f;   // whole car at full pedal
        public float frontBrakeShare = 0.65f;
        public float handbrakeTorqueNm = 2500f;  // rear axle
        public float wheelInertiaKgm2 = 1.5f;    // wheel + hub + brake share
        public float yawInertiaKgm2 = 2200f;
        public float rollingResistance = 0.015f;
        public float dragCoefficient = 0.32f;
        public float frontalAreaM2 = 2.2f;
        public float throttleProgression = 2f;  // plate = 1 − (1 − pedal)^k; 1 = linear
        public float fuelTankLitres = 50f;
        public float initialFuelLitres = 40f;
        public bool abs = true;
        public DriveLayout drive = DriveLayout.RearWheelDrive;
        public TransmissionType transmission = TransmissionType.Manual;

        public float CentreOfMassHeightM => centreOfMassM != null && centreOfMassM.Length > 1 ? centreOfMassM[1] : 0.5f;
        public float CentreOfMassForwardM => centreOfMassM != null && centreOfMassM.Length > 2 ? centreOfMassM[2] : 0f;

        public void Validate()
        {
            if (!(massKg > 0) || !(wheelbaseM > 0) || !(trackM > 0) || !(wheelRadiusM > 0) || !(engineInertiaKgm2 > 0) ||
                !(wheelInertiaKgm2 > 0) || !(finalDrive > 0) || throttleProgression < 1f || drivetrainEfficiency <= 0 || drivetrainEfficiency > 1 ||
                gearRatios == null || gearRatios.Length == 0 || reverseRatio >= 0 || maxClutchTorqueNm < 0 || maxBrakeTorqueNm < 0 ||
                frontBrakeShare < 0 || frontBrakeShare > 1 || maxSteerDeg <= 0 || maxSteerDeg >= 60 ||
                !(fuelTankLitres > 0) || initialFuelLitres < 0 || initialFuelLitres > fuelTankLitres)
                throw new ArgumentException("Invalid vehicle spec.");
            foreach (var r in gearRatios) if (!(r > 0)) throw new ArgumentException("Gear ratios must be positive.");
        }

        public VehicleSpec Clone()
        {
            var c = (VehicleSpec)MemberwiseClone();
            c.gearRatios = (float[])gearRatios.Clone();
            c.centreOfMassM = (float[])centreOfMassM.Clone();
            return c;
        }
    }
}
