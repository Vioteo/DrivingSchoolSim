using System;
using DrivingSchool.Contracts;

namespace DrivingSchool.Simulation
{
    /// <summary>
    /// Electrical logic of exterior lights. Indicators and headlamps need ignition; hazard, parking and brake
    /// lights work without it. Flasher: 90 periods/min, 50 % duty — game setting inside the 90 ± 30 periods/min
    /// band of UN Regulation No. 48 (§6.5.7); verify against the edition in force before citing in lessons.
    /// </summary>
    public sealed class VehicleLightsModel
    {
        public const float FlashHz = 1.5f;

        float flasherTime; bool flasherWasActive;

        public bool LeftActive { get; private set; }
        public bool RightActive { get; private set; }
        public bool Hazard { get; private set; }
        public bool LampOn { get; private set; }       // indicator bulbs lit in this instant
        public bool Parking { get; private set; }
        public bool LowBeam { get; private set; }
        public bool HighBeam { get; private set; }
        public bool Brake { get; private set; }
        public bool Reverse { get; private set; }
        public bool Horn { get; private set; }

        public void Reset() { flasherTime = 0; flasherWasActive = false; LeftActive = RightActive = Hazard = LampOn = Parking = LowBeam = HighBeam = Brake = Reverse = Horn = false; }

        public void Update(DriverCommand cmd, bool reverseEngaged, float dtSeconds)
        {
            if (float.IsNaN(dtSeconds) || float.IsInfinity(dtSeconds) || dtSeconds < 0) throw new ArgumentOutOfRangeException(nameof(dtSeconds));
            bool ign = cmd.ignition;
            Hazard = cmd.hazard;
            LeftActive = Hazard || (ign && cmd.turnSignal == TurnSignal.Left);
            RightActive = Hazard || (ign && cmd.turnSignal == TurnSignal.Right);

            bool active = LeftActive || RightActive;
            if (active && !flasherWasActive) flasherTime = 0f; // first flash starts immediately
            flasherWasActive = active;
            if (active)
            {
                flasherTime = (flasherTime + dtSeconds) % (1f / FlashHz);
                LampOn = flasherTime < 0.5f / FlashHz;
            }
            else LampOn = false;

            Parking = cmd.headlights != HeadlightMode.Off;
            LowBeam = ign && cmd.headlights == HeadlightMode.LowBeam;
            HighBeam = ign && ((LowBeam && cmd.highBeam) || cmd.flashHighBeam);
            Brake = cmd.brake > 0.05f;
            Reverse = ign && reverseEngaged;
            Horn = cmd.horn;
        }
    }
}
