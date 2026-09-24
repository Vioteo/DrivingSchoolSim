using System;
using UnityEngine;
using NUnit.Framework;
using DrivingSchool.Contracts;
using DrivingSchool.Simulation;
using DrivingSchool.Presentation.Physics;

namespace DrivingSchool.Tests
{
    /// <summary>Adapter in headless mode (kinematic body → PlanarChassis): same solver, no PhysX needed.</summary>
    [TestFixture]
    public sealed class VehicleAdapterTests
    {
        const float Dt = 0.01f;
        GameObject carObject;
        VehiclePhysicsAdapter adapter;

        [SetUp]
        public void Setup()
        {
            carObject = new GameObject("TestVehicle");
            var col = carObject.AddComponent<BoxCollider>();
            col.size = new Vector3(1.71f, 1.5f, 2.72f);
            var rb = carObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            adapter = carObject.AddComponent<VehiclePhysicsAdapter>();
            adapter.InitializeEngine();
            adapter.SetupRigidbody();
        }

        [TearDown]
        public void Teardown()
        {
            if (carObject != null) UnityEngine.Object.DestroyImmediate(carObject);
        }

        void Run(DriverCommand c, float seconds) { for (int i = 0; i < Mathf.RoundToInt(seconds / Dt); i++) adapter.Step(c, Dt); }

        void StartEngine()
        {
            Run(new DriverCommand { ignition = true, starter = true, clutch = 1f }, 1f);
            Run(new DriverCommand { ignition = true, clutch = 1f }, 0.5f);
        }

        void PullAway(int gear, float throttle)
        {
            Run(new DriverCommand { ignition = true, clutch = 1f, requestedGear = gear }, 0.2f);
            for (int i = 0; i < 250; i++)
                adapter.Step(new DriverCommand { ignition = true, requestedGear = gear, clutch = Mathf.Clamp01(1f - i / 200f), throttle = throttle }, Dt);
        }

        [Test]
        public void Rigidbody_MassAndCenterOfMass_ConfiguredCorrectly()
        {
            var rb = carObject.GetComponent<Rigidbody>();
            Assert.IsNotNull(rb);
            Assert.AreEqual(1350f, rb.mass);
            Assert.AreEqual(new Vector3(0f, 0.51f, -0.1f), rb.centerOfMass);
            Assert.That(adapter.Headless, Is.True);
        }

        [Test]
        public void StartingEngine_AndDrivingForward_AcceleratesVehicle()
        {
            StartEngine();
            Assert.AreEqual(EnginePhase.Running, adapter.CurrentState.engine);
            PullAway(1, 0.45f);
            Assert.AreEqual(1, adapter.CurrentGear);
            Assert.AreEqual(EnginePhase.Running, adapter.CurrentState.engine);
            Assert.Greater(adapter.CurrentState.signedSpeedMps, 1f);
            Assert.Greater(adapter.CurrentState.engineRpm, 800f);
            Assert.Greater(carObject.transform.position.z, 0.5f, "transform follows the headless chassis");
        }

        [Test]
        public void Brakes_DecelerateAndStopVehicle()
        {
            StartEngine(); PullAway(1, 0.5f);
            Assert.Greater(adapter.CurrentState.signedSpeedMps, 0.5f, "Vehicle should accelerate before brake test.");
            Run(new DriverCommand { ignition = true, clutch = 1f, requestedGear = 1, brake = 1f }, 3f);
            Assert.Less(Mathf.Abs(adapter.CurrentState.signedSpeedMps), 0.05f);
            Assert.IsTrue(adapter.CurrentState.brakeLight);
        }

        [Test]
        public void Handbrake_HoldsVehicleWithoutBrakeLight()
        {
            var state = adapter.Step(new DriverCommand { handbrake = true, clutch = 1.0f }, Dt);
            Assert.AreEqual(0f, state.signedSpeedMps);
            Assert.IsTrue(state.handbrake);
            Assert.IsFalse(state.brakeLight, "the parking brake does not light the stop lamps");
        }

        [Test]
        public void SuddenClutchDumpAtIdle_StallsEngine()
        {
            StartEngine();
            Run(new DriverCommand { ignition = true, clutch = 1f, requestedGear = 1 }, 0.2f);
            Run(new DriverCommand { ignition = true, requestedGear = 1, clutch = 0f }, 1.5f);
            Assert.AreEqual(EnginePhase.Stalled, adapter.CurrentState.engine);
        }

        [Test]
        public void AckermannSteering_ComputesAsymmetricWheelAngles()
        {
            var (lZero, rZero) = VehiclePhysicsAdapter.CalculateAckermann(0f, 2.72f, 1.71f);
            Assert.AreEqual(0f, lZero);
            Assert.AreEqual(0f, rZero);
            var (lLeft, rLeft) = VehiclePhysicsAdapter.CalculateAckermann(-1.0f, 2.72f, 1.71f, 32f);
            Assert.Less(lLeft, 0f); Assert.Less(rLeft, 0f);
            Assert.Greater(Mathf.Abs(lLeft), Mathf.Abs(rLeft), "Left wheel should turn more steeply than right when steering left.");
            var (lRight, rRight) = VehiclePhysicsAdapter.CalculateAckermann(1.0f, 2.72f, 1.71f, 32f);
            Assert.Greater(lRight, 0f); Assert.Greater(rRight, 0f);
            Assert.Greater(Mathf.Abs(rRight), Mathf.Abs(lRight), "Right wheel should turn more steeply than left when steering right.");
        }

        [Test]
        public void ReverseGear_DrivesVehicleBackwards()
        {
            StartEngine();
            PullAway(-1, 0.4f);
            Assert.AreEqual(-1, adapter.CurrentGear);
            Assert.Less(adapter.CurrentState.signedSpeedMps, -0.5f, "Vehicle should move backwards in reverse gear.");
            Assert.IsTrue(adapter.CurrentState.reverseLight);
        }

        [Test]
        public void AutomaticTransmission_CanBeSelected()
        {
            adapter.SetTransmission(TransmissionType.Automatic);
            Run(new DriverCommand { ignition = true, starter = true, selector = AutomaticSelector.P }, 1f);
            Run(new DriverCommand { ignition = true, selector = AutomaticSelector.D, throttle = 0.4f }, 4f);
            Assert.AreEqual(TransmissionType.Automatic, adapter.CurrentState.transmission);
            Assert.Greater(adapter.CurrentState.signedSpeedMps, 2f);
        }
    }

    /// <summary>Raycast suspension with real PhysX stepping (Physics.Simulate in EditMode).</summary>
    [TestFixture]
    public sealed class VehicleAdapterPhysXTests
    {
        GameObject ground, car;
        VehiclePhysicsAdapter adapter;
#if UNITY_2022_2_OR_NEWER
        SimulationMode previousMode;
#else
        bool previousAuto;
#endif

        [SetUp]
        public void Setup()
        {
#if UNITY_2022_2_OR_NEWER
            previousMode = Physics.simulationMode; Physics.simulationMode = SimulationMode.Script;
#else
            previousAuto = Physics.autoSimulation; Physics.autoSimulation = false;
#endif
            ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.transform.position = new Vector3(0, -0.5f, 50); ground.transform.localScale = new Vector3(40, 1, 200);
            car = new GameObject("PhysXCar") { layer = 2 }; // Ignore Raycast: the wheels never hit the own hull
            car.transform.position = new Vector3(0, 0.05f, 0);
            var hull = car.AddComponent<BoxCollider>(); hull.center = new Vector3(0, 0.85f, 0); hull.size = new Vector3(1.7f, 1.0f, 4.3f);
            var rb = car.AddComponent<Rigidbody>(); rb.isKinematic = false;
            adapter = car.AddComponent<VehiclePhysicsAdapter>();
            adapter.measureFromModel = false; // no model: wheels from wheelbase/track
            adapter.groundMask = ~(1 << 2);
            adapter.InitializeEngine(); adapter.SetupRigidbody();
            Physics.SyncTransforms();
        }

        [TearDown]
        public void Teardown()
        {
            UnityEngine.Object.DestroyImmediate(car); UnityEngine.Object.DestroyImmediate(ground);
#if UNITY_2022_2_OR_NEWER
            Physics.simulationMode = previousMode;
#else
            Physics.autoSimulation = previousAuto;
#endif
        }

        void Run(DriverCommand c, float seconds)
        {
            for (int i = 0; i < Mathf.RoundToInt(seconds / 0.01f); i++) { adapter.Step(c, 0.01f); Physics.Simulate(0.01f); }
        }

        [Test]
        public void CarSettlesOnSuspensionAndStaysLevel()
        {
            Run(new DriverCommand { handbrake = true }, 3f);
            for (int k = 0; k < 4; k++) Assert.That(adapter.Grounded[k], Is.True, "wheel " + k);
            Assert.That(car.transform.position.y, Is.InRange(-0.08f, 0.12f), "rest height");
            Assert.That(Vector3.Angle(car.transform.up, Vector3.up), Is.LessThan(2f), "level");
            Assert.That(car.GetComponent<Rigidbody>().GetPointVelocity(car.transform.position).magnitude, Is.LessThan(0.05f), "at rest");
        }

        [Test]
        public void CarDrivesStraightAndStops()
        {
            Run(new DriverCommand { handbrake = true }, 1f);
            Run(new DriverCommand { ignition = true, starter = true, clutch = 1f }, 1f);
            Run(new DriverCommand { ignition = true, clutch = 1f, requestedGear = 1 }, 0.2f);
            for (int i = 0; i < 300; i++) { adapter.Step(new DriverCommand { ignition = true, requestedGear = 1, clutch = Mathf.Clamp01(1f - i / 200f), throttle = 0.4f }, 0.01f); Physics.Simulate(0.01f); }
            Assert.That(adapter.CurrentState.engine, Is.EqualTo(EnginePhase.Running));
            Assert.That(car.transform.position.z, Is.GreaterThan(1.5f));
            Assert.That(Mathf.Abs(car.transform.position.x), Is.LessThan(0.3f), "keeps straight");
            Run(new DriverCommand { ignition = true, clutch = 1f, requestedGear = 1, brake = 1f }, 3f);
            Assert.That(Mathf.Abs(adapter.CurrentState.signedSpeedMps), Is.LessThan(0.05f));
        }
    }
}
