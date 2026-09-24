using System;
using NUnit.Framework;
using DrivingSchool.Contracts;
using DrivingSchool.Simulation;

namespace DrivingSchool.Tests
{
    /// <summary>Whole-vehicle behaviour on the headless PlanarChassis at the game's 100 Hz step.</summary>
    public sealed class VehicleDynamicsTests
    {
        const float Dt = 0.01f;

        sealed class Rig
        {
            public readonly VehicleSolver Solver; public readonly PlanarChassis Chassis = new PlanarChassis();
            public Rig(TransmissionType t = TransmissionType.Manual, SurfaceType surface = SurfaceType.DryAsphalt)
            { Solver = new VehicleSolver(new VehicleSpec { transmission = t }); Chassis.Surface = surface; }
            public VehicleState State => Solver.State;
            public float Speed => Chassis.VelocityForward;
            public void Run(DriverCommand c, float seconds) { for (int i = 0; i < (int)Math.Round(seconds / Dt); i++) Chassis.Step(Solver, c, Dt); }
            public void Run(Func<float, DriverCommand> c, float seconds) { for (int i = 0; i < (int)Math.Round(seconds / Dt); i++) Chassis.Step(Solver, c(i * Dt), Dt); }
            public void StartEngine(AutomaticSelector sel = AutomaticSelector.P)
            {
                Run(new DriverCommand { ignition = true, starter = true, clutch = 1f, selector = sel }, 1f);
                Run(new DriverCommand { ignition = true, clutch = 1f, selector = sel }, 1f);
            }
            public void PullAwayManual(int gear = 1, float throttle = 0.35f)
            {
                Run(new DriverCommand { ignition = true, clutch = 1f, requestedGear = gear }, 0.3f);
                Run(t => new DriverCommand { ignition = true, requestedGear = gear, clutch = Math.Max(0f, 1f - t / 2f), throttle = throttle }, 2.5f);
            }
        }

        [Test] public void StarterIsInhibitedInGearWithClutchReleased()
        {
            var r = new Rig();
            r.Run(new DriverCommand { ignition = true, clutch = 1f, requestedGear = 1 }, 0.1f);
            r.Run(new DriverCommand { ignition = true, starter = true, clutch = 0f, requestedGear = 1 }, 1f);
            Assert.That(r.Solver.StarterInhibited, Is.True);
            Assert.That(r.State.engine, Is.Not.EqualTo(EnginePhase.Running));
        }

        [Test] public void EngineStartsAndIdles()
        {
            var r = new Rig(); r.StartEngine();
            Assert.That(r.State.engine, Is.EqualTo(EnginePhase.Running));
            Assert.That(r.State.engineRpm, Is.InRange(700f, 1100f));
            Assert.That(Math.Abs(r.Speed), Is.LessThan(0.01f));
        }

        [Test] public void SmoothClutchReleasePullsAwayWithoutStalling()
        {
            var r = new Rig(); r.StartEngine(); r.PullAwayManual();
            Assert.That(r.State.engine, Is.EqualTo(EnginePhase.Running));
            Assert.That(r.Speed, Is.GreaterThan(1.5f));
            Assert.That(r.Solver.ClutchLocked, Is.True, "clutch fully released should lock up");
            float wheelRpmSpeed = r.State.wheelSpeedRearRadS * r.Solver.Spec.wheelRadiusM;
            Assert.That(wheelRpmSpeed, Is.EqualTo(r.Speed).Within(0.3f), "driven wheels roll with the road");
        }

        [Test] public void ClutchDumpAtIdleStalls()
        {
            var r = new Rig(); r.StartEngine();
            r.Run(new DriverCommand { ignition = true, clutch = 1f, requestedGear = 1 }, 0.2f);
            r.Run(new DriverCommand { ignition = true, clutch = 0f, requestedGear = 1 }, 1.5f);
            Assert.That(r.State.engine, Is.EqualTo(EnginePhase.Stalled));
        }

        [Test] public void ThirdGearStartFromRestStalls()
        {
            var r = new Rig(); r.StartEngine();
            r.Run(new DriverCommand { ignition = true, clutch = 1f, requestedGear = 3 }, 0.2f);
            r.Run(t => new DriverCommand { ignition = true, requestedGear = 3, clutch = Math.Max(0f, 1f - t / 1.5f), throttle = 0.15f }, 3f);
            Assert.That(r.State.engine, Is.EqualTo(EnginePhase.Stalled));
        }

        [Test] public void GearChangeWithoutClutchIsRefused()
        {
            var r = new Rig(); r.StartEngine(); r.PullAwayManual();
            r.Run(new DriverCommand { ignition = true, clutch = 0f, requestedGear = 2, throttle = 0.3f }, 0.2f);
            Assert.That(r.State.gear, Is.EqualTo(1));
        }

        [Test] public void AcceleratesThroughGearsToCitySpeed()
        {
            var r = new Rig(); r.StartEngine(); r.PullAwayManual(1, 0.5f);
            for (int g = 2; g <= 3; g++)
            {
                r.Run(new DriverCommand { ignition = true, clutch = 1f, requestedGear = g - 1 }, 0.3f);
                r.Run(new DriverCommand { ignition = true, clutch = 1f, requestedGear = g }, 0.3f);
                int gear = g;
                r.Run(t => new DriverCommand { ignition = true, requestedGear = gear, clutch = Math.Max(0f, 1f - t / 0.8f), throttle = 0.8f }, 5f);
            }
            Assert.That(r.State.gear, Is.EqualTo(3));
            Assert.That(r.Speed * 3.6f, Is.GreaterThan(40f));
            Assert.That(r.State.engine, Is.EqualTo(EnginePhase.Running));
        }

        [Test] public void BrakingStopsAndHoldsWithBrakeLight()
        {
            var r = new Rig(); r.StartEngine(); r.PullAwayManual(1, 0.5f);
            Assert.That(r.Speed, Is.GreaterThan(1f));
            r.Run(new DriverCommand { ignition = true, clutch = 1f, requestedGear = 1, brake = 1f }, 3f);
            Assert.That(Math.Abs(r.Speed), Is.LessThan(0.05f));
            Assert.That(r.State.brakeLight, Is.True);
            Assert.That(r.State.engine, Is.EqualTo(EnginePhase.Running), "clutch pressed while braking keeps the engine alive");
        }

        [Test] public void HandbrakeHoldsOnTwelvePercentGrade()
        {
            var r = new Rig(); r.Chassis.GradeRad = (float)Math.Atan(0.12);
            r.Run(new DriverCommand { handbrake = true }, 5f);
            double moved = Math.Sqrt(r.Chassis.X * r.Chassis.X + r.Chassis.Z * r.Chassis.Z);
            Assert.That(moved, Is.LessThan(0.05), "rolled back " + moved + " m");
        }

        [Test] public void WithoutHandbrakeCarRollsBackOnGrade()
        {
            var r = new Rig(); r.Chassis.GradeRad = (float)Math.Atan(0.12);
            r.Run(new DriverCommand(), 3f);
            Assert.That(r.Speed, Is.LessThan(-0.5f));
        }

        [Test] public void ReverseGearDrivesBackwardsWithReverseLight()
        {
            var r = new Rig(); r.StartEngine(); r.PullAwayManual(-1, 0.35f);
            Assert.That(r.Speed, Is.LessThan(-0.8f));
            Assert.That(r.State.reverseLight, Is.True);
        }

        [Test] public void SteeringLeftTurnsLeft()
        {
            var r = new Rig(); r.StartEngine(); r.PullAwayManual(1, 0.4f);
            float yaw0 = r.Chassis.Yaw;
            r.Run(new DriverCommand { ignition = true, requestedGear = 1, throttle = 0.25f, steering = -0.6f }, 3f);
            Assert.That(r.Chassis.Yaw - yaw0, Is.LessThan(-0.4f), "heading must rotate counter-clockwise (left)");
            Assert.That(r.Solver.Wheels[0].steerAngleRad, Is.LessThan(r.Solver.Wheels[1].steerAngleRad), "inner (left) wheel steers more");
        }

        [Test] public void AutomaticCreepsInDriveAndHoldsInPark()
        {
            var r = new Rig(TransmissionType.Automatic); r.StartEngine(AutomaticSelector.P);
            Assert.That(r.State.engine, Is.EqualTo(EnginePhase.Running));
            r.Run(new DriverCommand { ignition = true, selector = AutomaticSelector.P, throttle = 0.3f }, 2f);
            Assert.That(Math.Abs(r.Speed), Is.LessThan(0.02f), "park pawl holds");
            r.Run(new DriverCommand { ignition = true, selector = AutomaticSelector.D }, 4f);
            Assert.That(r.Speed, Is.GreaterThan(0.5f), "torque converter creeps");
            Assert.That(r.State.engine, Is.EqualTo(EnginePhase.Running));
        }

        [Test] public void AutomaticUpshiftsUnderAcceleration()
        {
            var r = new Rig(TransmissionType.Automatic); r.StartEngine(AutomaticSelector.P);
            r.Run(new DriverCommand { ignition = true, selector = AutomaticSelector.D, throttle = 0.5f }, 15f);
            Assert.That(r.State.gear, Is.GreaterThanOrEqualTo(3));
            Assert.That(r.Speed * 3.6f, Is.GreaterThan(50f));
            r.Run(new DriverCommand { ignition = true, selector = AutomaticSelector.P, throttle = 0f }, 0.2f);
            Assert.That(r.State.selector, Is.EqualTo(AutomaticSelector.D), "P refused at speed");
        }

        [Test] public void AutomaticStopsWithBrakeInDriveWithoutStalling()
        {
            var r = new Rig(TransmissionType.Automatic); r.StartEngine(AutomaticSelector.P);
            r.Run(new DriverCommand { ignition = true, selector = AutomaticSelector.D, throttle = 0.4f }, 5f);
            r.Run(new DriverCommand { ignition = true, selector = AutomaticSelector.D, brake = 0.8f }, 5f);
            Assert.That(Math.Abs(r.Speed), Is.LessThan(0.05f));
            Assert.That(r.State.engine, Is.EqualTo(EnginePhase.Running));
        }

        [Test] public void WetRoadBrakingIsLongerThanDry()
        {
            float Distance(SurfaceType s)
            {
                var r = new Rig(TransmissionType.Automatic, s);
                r.Chassis.VelocityForward = 60f / 3.6f;
                r.Run(new DriverCommand { brake = 1f, selector = AutomaticSelector.N }, 15f);
                Assert.That(Math.Abs(r.Speed), Is.LessThan(0.1f));
                return (float)r.Chassis.Z;
            }
            float dry = Distance(SurfaceType.DryAsphalt), wet = Distance(SurfaceType.WetAsphalt), ice = Distance(SurfaceType.BlackIce);
            Assert.That(dry, Is.InRange(12f, 25f), "dry " + dry);
            Assert.That(wet, Is.GreaterThan(dry * 1.2f), "wet " + wet);
            Assert.That(ice, Is.GreaterThan(wet * 2f), "ice " + ice);
        }

        [Test] public void EngineBrakingSlowsCoastingCarInGear()
        {
            var inGear = new Rig(); inGear.StartEngine(); inGear.PullAwayManual(1, 0.5f);
            float v0 = inGear.Speed;
            inGear.Run(new DriverCommand { ignition = true, requestedGear = 1 }, 2f);
            Assert.That(inGear.Speed, Is.LessThan(v0 - 0.3f));
            Assert.That(inGear.State.engine, Is.EqualTo(EnginePhase.Running));
        }

        [Test] public void InvalidInputsAreRejected()
        {
            var s = new VehicleSolver(new VehicleSpec());
            Assert.Throws<ArgumentException>(() => s.Step(new DriverCommand(), new WheelContact[3], Dt));
            Assert.Throws<ArgumentOutOfRangeException>(() => s.Step(new DriverCommand(), new WheelContact[4], 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => s.Step(new DriverCommand { steering = 2f }, new WheelContact[4], Dt));
            Assert.Throws<ArgumentException>(() => new VehicleSolver(new VehicleSpec { massKg = -1 }));
        }
    }
}
