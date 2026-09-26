using System;
using NUnit.Framework;
using DrivingSchool.Contracts;
using DrivingSchool.Simulation;

namespace DrivingSchool.Tests
{
    public sealed class FuelModelTests
    {
        const float Dt = 0.01f;

        [Test] public void OverrunWithoutTorqueBurnsNothing()
        {
            Assert.That(FuelModel.Flow(0f, 3000f, 150f), Is.EqualTo(0f));
            var f = new FuelModel(50f, 10f); f.Update(true, 0f, 3000f, 150f, 1f);
            Assert.That(f.Litres, Is.EqualTo(10f)); Assert.That(f.FlowLitresPerHour, Is.EqualTo(0f));
        }

        [Test] public void PartLoadCostsMorePerKilowattHour()
        {
            Assert.That(FuelModel.SpecificConsumption(0.2f), Is.GreaterThan(FuelModel.SpecificConsumption(0.7f)));
            Assert.That(FuelModel.SpecificConsumption(1f), Is.GreaterThan(FuelModel.SpecificConsumption(0.8f)), "full-load enrichment");
        }

        [Test] public void TankDrainsAndStopsAtZero()
        {
            var f = new FuelModel(50f, 0.01f);
            for (int i = 0; i < 1000; i++) f.Update(true, 140f, 5000f, 150f, 0.1f);
            Assert.That(f.Litres, Is.EqualTo(0f)); Assert.That(f.IsEmpty, Is.True); Assert.That(f.FlowLitresPerHour, Is.EqualTo(0f));
            Assert.That(f.Refill(100f), Is.EqualTo(50f)); Assert.That(f.Level01, Is.EqualTo(1f));
        }

        [Test] public void InvalidInputsAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new FuelModel(0f, 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FuelModel(50f, -1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FuelModel(50f, 10f).Update(true, 10f, 1000f, 100f, 0f));
            Assert.Throws<ArgumentException>(() => new VehicleSolver(new VehicleSpec { initialFuelLitres = 60f }));
        }

        [Test] public void EngineWithoutFuelDoesNotStartAndARunningOneStalls()
        {
            var e = new EngineModel { FuelAvailable = false };
            for (int i = 0; i < 300; i++) e.Update(0f, true, true, 0f, Dt);
            Assert.That(e.Phase, Is.Not.EqualTo(EnginePhase.Running));

            var run = new EngineModel();
            for (int i = 0; i < 100; i++) run.Update(0f, true, true, 0f, Dt);
            for (int i = 0; i < 100; i++) run.Update(0f, true, false, 0f, Dt);
            Assert.That(run.Phase, Is.EqualTo(EnginePhase.Running));
            run.FuelAvailable = false;
            for (int i = 0; i < 300; i++) run.Update(0f, true, false, 0f, Dt);
            Assert.That(run.Phase, Is.EqualTo(EnginePhase.Stalled));
        }

        // ---------------------------------------------------------------- whole car on the planar chassis

        static void Run(VehicleSolver s, PlanarChassis c, Func<DriverCommand> cmd, float seconds)
        {
            for (int i = 0; i < (int)Math.Round(seconds / Dt); i++) c.Step(s, cmd(), Dt);
        }

        static (VehicleSolver, PlanarChassis) StartedCar(float fuel = 40f)
        {
            var s = new VehicleSolver(new VehicleSpec { transmission = TransmissionType.Automatic, initialFuelLitres = fuel });
            var c = new PlanarChassis();
            Run(s, c, () => new DriverCommand { ignition = true, starter = true, selector = AutomaticSelector.P }, 1f);
            Run(s, c, () => new DriverCommand { ignition = true, selector = AutomaticSelector.P }, 1f);
            Assert.That(s.State.engine, Is.EqualTo(EnginePhase.Running));
            return (s, c);
        }

        [Test] public void IdleConsumptionIsAboutOneLitrePerHour()
        {
            var (s, c) = StartedCar();
            Run(s, c, () => new DriverCommand { ignition = true, selector = AutomaticSelector.P }, 5f);
            Assert.That(s.State.fuelFlowLitresPerHour, Is.InRange(0.5f, 1.3f));
        }

        [Test] public void SteadyNinetyKmhUsesSixToNineLitresPerHundredKm()
        {
            var (s, c) = StartedCar();
            const float target = 25f; // m/s
            Func<DriverCommand> cruise = () => new DriverCommand
            {
                ignition = true, selector = AutomaticSelector.D,
                throttle = Math.Max(0f, Math.Min(1f, 0.25f + (target - c.VelocityForward) * 0.4f))
            };
            Run(s, c, cruise, 45f);
            Assert.That(c.VelocityForward, Is.EqualTo(target).Within(1.5f), "cruise speed reached");
            float before = s.Fuel.Litres; double metres = 0;
            for (int i = 0; i < 2000; i++) { c.Step(s, cruise(), Dt); metres += c.VelocityForward * Dt; }
            float per100 = (before - s.Fuel.Litres) / (float)(metres / 100000.0);
            Assert.That(per100, Is.InRange(5f, 9.5f));
        }

        [Test] public void EmptyTankStallsTheCar()
        {
            var (s, c) = StartedCar(0.002f);
            Run(s, c, () => new DriverCommand { ignition = true, selector = AutomaticSelector.P, throttle = 0.6f }, 20f);
            Assert.That(s.State.fuelLitres, Is.EqualTo(0f));
            Assert.That(s.State.engine, Is.Not.EqualTo(EnginePhase.Running));
            Run(s, c, () => new DriverCommand { ignition = true, starter = true, selector = AutomaticSelector.P }, 2f);
            Assert.That(s.State.engine, Is.Not.EqualTo(EnginePhase.Running), "no restart on an empty tank");
        }
    }
}
