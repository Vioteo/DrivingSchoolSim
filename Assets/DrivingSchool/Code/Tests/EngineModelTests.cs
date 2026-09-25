using System;
using NUnit.Framework;
using DrivingSchool.Contracts;
using DrivingSchool.Simulation;

namespace DrivingSchool.Tests
{
    [TestFixture]
    public sealed class EngineModelTests
    {
        EngineModel engine;

        [SetUp]
        public void Setup()
        {
            engine = new EngineModel();
        }

        [Test]
        public void InitialState_IsOff_ZeroRpm()
        {
            Assert.AreEqual(EnginePhase.Off, engine.Phase);
            Assert.AreEqual(0f, engine.Rpm);
            Assert.AreEqual(0f, engine.OutputTorqueNm);
        }

        [Test]
        public void IgnitionOnly_TransitionsToIgnitionPhase_ZeroRpm()
        {
            engine.Update(throttle: 0f, ignition: true, starter: false, loadTorqueNm: 0f, dtSeconds: 0.05f);
            Assert.AreEqual(EnginePhase.Ignition, engine.Phase);
            Assert.AreEqual(0f, engine.Rpm);
        }

        [Test]
        public void Starter_TransitionsToCranking_AndSpinsEngine()
        {
            // Turn on ignition
            engine.Update(0f, ignition: true, starter: false, 0f, 0.05f);

            // Crank for 0.1s
            engine.Update(0f, ignition: true, starter: true, 0f, 0.1f);
            Assert.AreEqual(EnginePhase.Cranking, engine.Phase);
            Assert.Greater(engine.Rpm, 0f);
            Assert.LessOrEqual(engine.Rpm, 450f);
        }

        [Test]
        public void SuccessfulStart_EntersRunning_AndMaintainsIdleRpm()
        {
            // Crank until running (over ~0.5s)
            for (int i = 0; i < 15; i++)
            {
                engine.Update(0f, ignition: true, starter: true, 0f, 0.05f);
                if (engine.Phase == EnginePhase.Running) break;
            }

            Assert.AreEqual(EnginePhase.Running, engine.Phase);

            // Release starter, let idle stabilize over 2 seconds
            for (int i = 0; i < 40; i++)
            {
                engine.Update(throttle: 0f, ignition: true, starter: false, loadTorqueNm: 0f, dtSeconds: 0.05f);
            }

            // Should be near 850 RPM (idle)
            Assert.AreEqual(EnginePhase.Running, engine.Phase);
            Assert.That(engine.Rpm, Is.InRange(800f, 950f));
            Assert.Greater(engine.OutputTorqueNm, 0f);
        }

        [Test]
        public void FullThrottleInNeutral_ReachesRedline_DoesNotExceedCutoff()
        {
            // Start engine
            for (int i = 0; i < 15; i++) engine.Update(0f, true, true, 0f, 0.05f);

            // Apply full throttle for 3 seconds
            for (int i = 0; i < 60; i++)
            {
                engine.Update(throttle: 1.0f, ignition: true, starter: false, loadTorqueNm: 0f, dtSeconds: 0.05f);
            }

            Assert.AreEqual(EnginePhase.Running, engine.Phase);
            Assert.GreaterOrEqual(engine.Rpm, 6500f);
            Assert.LessOrEqual(engine.Rpm, 6600f);
        }

        [Test]
        public void ExcessiveLoad_CausesEngineToStall()
        {
            // Start engine and let idle
            for (int i = 0; i < 15; i++) engine.Update(0f, true, true, 0f, 0.05f);
            for (int i = 0; i < 20; i++) engine.Update(0f, true, false, 0f, 0.05f);

            Assert.AreEqual(EnginePhase.Running, engine.Phase);

            // Apply heavy braking/clutch load without throttle
            for (int i = 0; i < 10; i++)
            {
                engine.Update(throttle: 0f, ignition: true, starter: false, loadTorqueNm: 250f, dtSeconds: 0.05f);
                if (engine.Phase == EnginePhase.Stalled) break;
            }

            Assert.AreEqual(EnginePhase.Stalled, engine.Phase);
            Assert.AreEqual(0f, engine.Rpm);
            Assert.AreEqual(0f, engine.OutputTorqueNm);

            // Without starter, engine does not restart on its own
            engine.Update(throttle: 1.0f, ignition: true, starter: false, loadTorqueNm: 0f, dtSeconds: 0.05f);
            Assert.AreEqual(EnginePhase.Stalled, engine.Phase);
            Assert.AreEqual(0f, engine.Rpm);
        }

        [Test]
        public void TurningOffIgnition_StopsEngine()
        {
            // Start engine
            for (int i = 0; i < 15; i++) engine.Update(0f, true, true, 0f, 0.05f);
            Assert.AreEqual(EnginePhase.Running, engine.Phase);

            // Switch off ignition
            engine.Update(throttle: 0f, ignition: false, starter: false, loadTorqueNm: 0f, dtSeconds: 0.05f);
            Assert.AreEqual(EnginePhase.Off, engine.Phase);
            Assert.AreEqual(0f, engine.OutputTorqueNm);
        }

        [Test]
        public void InvalidInputs_ThrowArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => engine.Update(-0.1f, true, false, 0f, 0.05f));
            Assert.Throws<ArgumentOutOfRangeException>(() => engine.Update(1.1f, true, false, 0f, 0.05f));
            Assert.Throws<ArgumentOutOfRangeException>(() => engine.Update(0.5f, true, false, 0f, -0.01f));
            Assert.Throws<ArgumentOutOfRangeException>(() => engine.Update(0.5f, true, false, 0f, 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => engine.Update(float.NaN, true, false, 0f, 0.05f));
            Assert.Throws<ArgumentOutOfRangeException>(() => engine.Update(0.5f, true, false, float.NaN, 0.05f));
            Assert.Throws<ArgumentOutOfRangeException>(() => engine.Update(0.5f, true, false, 0f, float.NaN));
        }

        [Test]
        public void ClosedThrottle_RevsDropQuickly_EngineBraking()
        {
            for (int i = 0; i < 15; i++) engine.Update(0f, true, true, 0f, 0.05f);
            for (int i = 0; i < 300; i++) engine.Update(1f, true, false, 0f, 0.01f);   // to the limiter
            Assert.GreaterOrEqual(engine.Rpm, 6400f);
            for (int i = 0; i < 100; i++) engine.Update(0f, true, false, 0f, 0.01f);   // 1 s off throttle, no load
            // Friction plus pumping loss: a free-revving 1.6 l engine falls ~3000 rpm per second from the limiter.
            Assert.Less(engine.Rpm, 3500f);
            Assert.AreEqual(EnginePhase.Running, engine.Phase);
            for (int i = 0; i < 300; i++) engine.Update(0f, true, false, 0f, 0.01f);
            Assert.That(engine.Rpm, Is.InRange(800f, 950f), "idle control catches the falling revs");
        }
    }
}
