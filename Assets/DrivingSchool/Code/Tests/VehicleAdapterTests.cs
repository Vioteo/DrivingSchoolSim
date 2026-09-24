using System;
using UnityEngine;
using NUnit.Framework;
using DrivingSchool.Contracts;
using DrivingSchool.Simulation;
using DrivingSchool.Presentation.Physics;

namespace DrivingSchool.Tests
{
    [TestFixture]
    public sealed class VehicleAdapterTests
    {
        GameObject carObject;
        VehiclePhysicsAdapter adapter;

        [SetUp]
        public void Setup()
        {
            carObject = new GameObject("TestVehicle");
            var col = carObject.AddComponent<BoxCollider>();
            col.size = new Vector3(1.71f, 1.5f, 2.72f);
            var rb = carObject.AddComponent<Rigidbody>();
            rb.isKinematic = false;
            adapter = carObject.AddComponent<VehiclePhysicsAdapter>();
            adapter.InitializeEngine();
            adapter.SetupRigidbody();
        }

        [TearDown]
        public void Teardown()
        {
            if (carObject != null)
            {
                UnityEngine.Object.DestroyImmediate(carObject);
            }
        }

        [Test]
        public void Rigidbody_MassAndCenterOfMass_ConfiguredCorrectly()
        {
            var rb = carObject.GetComponent<Rigidbody>();
            Assert.IsNotNull(rb);
            Assert.AreEqual(1350f, rb.mass);
            Assert.AreEqual(new Vector3(0f, 0.51f, -0.1f), rb.centerOfMass);
        }

        [Test]
        public void StartingEngine_AndDrivingForward_AcceleratesVehicle()
        {
            // 1. Crank and start engine
            for (int i = 0; i < 15; i++)
            {
                adapter.Step(new DriverCommand { ignition = true, starter = true, clutch = 1.0f }, 0.05f);
            }
            Assert.AreEqual(EnginePhase.Running, adapter.CurrentState.engine);

            // 2. Select 1st gear while clutch is pressed
            adapter.Step(new DriverCommand { ignition = true, clutch = 1.0f, requestedGear = 1 }, 0.05f);
            Assert.AreEqual(1, adapter.CurrentGear);

            // 3. Smooth clutch release with throttle to initiate drive
            for (int i = 0; i < 50; i++)
            {
                float clutchProgress = Mathf.Clamp01(1.0f - i * 0.02f);
                adapter.Step(new DriverCommand
                {
                    ignition = true,
                    requestedGear = 1,
                    clutch = clutchProgress,
                    throttle = 0.45f
                }, 0.05f);
            }

            Assert.AreEqual(EnginePhase.Running, adapter.CurrentState.engine);
            Assert.Greater(adapter.CurrentState.signedSpeedMps, 0.3f);
            Assert.Greater(adapter.CurrentState.engineRpm, 800f);
        }

        [Test]
        public void Brakes_DecelerateAndStopVehicle()
        {
            // 1. Start engine
            for (int i = 0; i < 15; i++) adapter.Step(new DriverCommand { ignition = true, starter = true, clutch = 1f }, 0.05f);
            adapter.Step(new DriverCommand { ignition = true, clutch = 1f, requestedGear = 1 }, 0.05f);

            // 2. Accelerate forward smoothly
            for (int i = 0; i < 40; i++)
            {
                float c = Mathf.Clamp01(1.0f - i * 0.03f);
                adapter.Step(new DriverCommand { ignition = true, requestedGear = 1, clutch = c, throttle = 0.5f }, 0.05f);
            }

            float movingSpeed = adapter.CurrentState.signedSpeedMps;
            Assert.Greater(movingSpeed, 0.1f, "Vehicle should accelerate before brake test.");

            // 3. Apply full brake with disengaged clutch
            for (int i = 0; i < 40; i++)
            {
                adapter.Step(new DriverCommand { ignition = true, clutch = 1f, brake = 1.0f }, 0.05f);
            }

            Assert.Less(adapter.CurrentState.signedSpeedMps, 0.01f);
            Assert.IsTrue(adapter.CurrentState.brakeLight);
        }

        [Test]
        public void Handbrake_PreventsRollbackAndHoldsVehicle()
        {
            // Apply handbrake from stationary
            var state = adapter.Step(new DriverCommand { handbrake = true, clutch = 1.0f }, 0.05f);
            Assert.AreEqual(0f, state.signedSpeedMps);
            Assert.IsTrue(state.brakeLight);
        }

        [Test]
        public void SuddenClutchDumpAtIdle_StallsEngine()
        {
            // Start engine and idle
            for (int i = 0; i < 15; i++) adapter.Step(new DriverCommand { ignition = true, starter = true, clutch = 1f }, 0.05f);
            for (int i = 0; i < 20; i++) adapter.Step(new DriverCommand { ignition = true, clutch = 1f }, 0.05f);
            Assert.AreEqual(EnginePhase.Running, adapter.CurrentState.engine);

            // Shift to 1st gear
            adapter.Step(new DriverCommand { ignition = true, clutch = 1f, requestedGear = 1 }, 0.05f);

            // Abruptly dump clutch to 0 with 0 throttle while stationary
            for (int i = 0; i < 10; i++)
            {
                adapter.Step(new DriverCommand { ignition = true, requestedGear = 1, clutch = 0f, throttle = 0f }, 0.05f);
                if (adapter.CurrentState.engine == EnginePhase.Stalled) break;
            }

            Assert.AreEqual(EnginePhase.Stalled, adapter.CurrentState.engine);
        }

        [Test]
        public void AckermannSteering_ComputesAsymmetricWheelAngles()
        {
            // Straight
            var (lZero, rZero) = VehiclePhysicsAdapter.CalculateAckermann(0f, 2.72f, 1.71f);
            Assert.AreEqual(0f, lZero);
            Assert.AreEqual(0f, rZero);

            // Turn Left: Left wheel (inner) angle must be greater than Right wheel (outer)
            var (lLeft, rLeft) = VehiclePhysicsAdapter.CalculateAckermann(-1.0f, 2.72f, 1.71f, 32f);
            Assert.Less(lLeft, 0f);
            Assert.Less(rLeft, 0f);
            Assert.Greater(Mathf.Abs(lLeft), Mathf.Abs(rLeft), "Left wheel should turn more steeply than right when steering left.");

            // Turn Right: Right wheel (inner) angle must be greater than Left wheel (outer)
            var (lRight, rRight) = VehiclePhysicsAdapter.CalculateAckermann(1.0f, 2.72f, 1.71f, 32f);
            Assert.Greater(lRight, 0f);
            Assert.Greater(rRight, 0f);
            Assert.Greater(Mathf.Abs(rRight), Mathf.Abs(lRight), "Right wheel should turn more steeply than left when steering right.");
        }

        [Test]
        public void ReverseGear_DrivesVehicleBackwards()
        {
            // 1. Start engine
            for (int i = 0; i < 15; i++) adapter.Step(new DriverCommand { ignition = true, starter = true, clutch = 1f }, 0.05f);

            // 2. Shift to Reverse (-1)
            adapter.Step(new DriverCommand { ignition = true, clutch = 1f, requestedGear = -1 }, 0.05f);
            Assert.AreEqual(-1, adapter.CurrentGear);

            // 3. Smooth clutch release with throttle backwards
            for (int i = 0; i < 40; i++)
            {
                float c = Mathf.Clamp01(1.0f - i * 0.03f);
                adapter.Step(new DriverCommand
                {
                    ignition = true,
                    requestedGear = -1,
                    clutch = c,
                    throttle = 0.5f
                }, 0.05f);
            }

            Assert.Less(adapter.CurrentState.signedSpeedMps, -0.2f, "Vehicle should move backwards in reverse gear.");
        }
    }
}
