using System;
using NUnit.Framework;
using DrivingSchool.Contracts;
using DrivingSchool.Input;

namespace DrivingSchool.Tests
{
    [TestFixture]
    public class LogitechG27Tests
    {
        [Test]
        public void AxisCalibration_NormalizesLinearCorrectly()
        {
            var cal = new AxisCalibration(0f, 1f, 0.1f, false);

            Assert.That(cal.Normalize(0.05f), Is.Zero, "Values inside deadzone must return 0");
            Assert.That(cal.Normalize(1.0f), Is.EqualTo(1.0f).Within(0.001f));
            Assert.That(cal.Normalize(0.55f), Is.EqualTo(0.5f).Within(0.01f));
        }

        [Test]
        public void AxisCalibration_HandlesInvertedPedal()
        {
            // Typical DirectInput pedal: 1.0 unpressed, -1.0 fully pressed
            var pedalCal = new AxisCalibration(-1.0f, 1.0f, 0.05f, true);

            Assert.That(pedalCal.Normalize(1.0f), Is.Zero, "Unpressed pedal must return 0.0");
            Assert.That(pedalCal.Normalize(-1.0f), Is.EqualTo(1.0f).Within(0.001f), "Fully pressed pedal must return 1.0");
            Assert.That(pedalCal.Normalize(0.0f), Is.EqualTo(0.473f).Within(0.05f), "Half-pressed pedal is normalized around 0.5");
        }

        [Test]
        public void AxisCalibration_ZeroSpan_ReturnsZeroWithoutNaN()
        {
            var brokenCal = new AxisCalibration(0.5f, 0.5f, 0.02f, false);
            Assert.That(brokenCal.Normalize(0.5f), Is.Zero);
            Assert.That(brokenCal.NormalizeBipolar(0.5f), Is.Zero);
        }

        [Test]
        public void AxisCalibration_RejectsNanAndInfinity()
        {
            var cal = new AxisCalibration(-1f, 1f, 0.02f, false);
            Assert.That(cal.Normalize(float.NaN), Is.Zero);
            Assert.That(cal.Normalize(float.PositiveInfinity), Is.Zero);
            Assert.That(cal.NormalizeBipolar(float.NaN), Is.Zero);
            Assert.That(cal.NormalizeBipolar(float.NegativeInfinity), Is.Zero);
        }

        [Test]
        public void AxisCalibration_BipolarNormalizesSteeringCorrectly()
        {
            var steerCal = new AxisCalibration(-1f, 1f, 0.05f, false);

            Assert.That(steerCal.NormalizeBipolar(0.02f), Is.Zero, "Inside steering deadzone should be 0");
            Assert.That(steerCal.NormalizeBipolar(-1.0f), Is.EqualTo(-1.0f).Within(0.001f));
            Assert.That(steerCal.NormalizeBipolar(1.0f), Is.EqualTo(1.0f).Within(0.001f));
            Assert.That(steerCal.NormalizeBipolar(0.525f), Is.EqualTo(0.5f).Within(0.05f));
        }

        [Test]
        public void LogitechG27_SimulatedInputProducesValidCommand()
        {
            var adapter = new LogitechG27Adapter();
            adapter.RawSteering = 0.5f;
            adapter.RawThrottle = -0.8f; // pressed pedal
            adapter.RawBrake = 1.0f;    // unpressed pedal
            adapter.RawClutch = 1.0f;   // unpressed pedal
            adapter.SelectedGear = 2;
            adapter.HandbrakeState = false;
            adapter.IgnitionState = true;
            adapter.StarterState = false;

            var cmd = adapter.Read(100);
            Assert.DoesNotThrow(() => cmd.Validate());
            Assert.That(cmd.sequence, Is.EqualTo(100));
            Assert.That(cmd.steering, Is.GreaterThan(0.4f).And.LessThan(0.6f));
            Assert.That(cmd.throttle, Is.GreaterThan(0.8f));
            Assert.That(cmd.brake, Is.Zero);
            Assert.That(cmd.requestedGear, Is.EqualTo(2));
            Assert.That(cmd.handbrake, Is.False);
            Assert.That(cmd.ignition, Is.True);
        }

        [Test]
        public void LogitechG27_DisconnectedDeviceReturnsNeutralSafeCommand()
        {
            var adapter = new LogitechG27Adapter { IsConnected = false };
            var cmd = adapter.Read(42);

            Assert.DoesNotThrow(() => cmd.Validate());
            Assert.That(cmd.steering, Is.Zero);
            Assert.That(cmd.throttle, Is.Zero);
            Assert.That(cmd.brake, Is.Zero);
            Assert.That(cmd.requestedGear, Is.Zero);
            Assert.That(cmd.handbrake, Is.True);
        }

        [Test]
        public void LogitechG27_CenteringSpringIncreasesWithSpeed()
        {
            float springLow = LogitechG27Adapter.CalculateCenteringSpring(0.5f, 0f);
            float springHigh = LogitechG27Adapter.CalculateCenteringSpring(0.5f, 15f);

            Assert.That(springLow, Is.LessThan(0f), "Centering spring must oppose positive steering");
            Assert.That(Math.Abs(springHigh), Is.GreaterThan(Math.Abs(springLow)), "High speed centering spring must be stiffer");
        }

        [Test]
        public void LogitechG27_EndStopAppliesRepulsiveTorqueBeyond450Deg()
        {
            float withinLimit = LogitechG27Adapter.CalculateEndStop(400f, 450f);
            Assert.That(withinLimit, Is.Zero, "No end-stop torque within 450 degrees");

            float beyondRight = LogitechG27Adapter.CalculateEndStop(460f, 450f);
            Assert.That(beyondRight, Is.LessThan(0f), "End stop must push left when turned past +450 deg");

            float beyondLeft = LogitechG27Adapter.CalculateEndStop(-470f, 450f);
            Assert.That(beyondLeft, Is.GreaterThan(0f), "End stop must push right when turned past -450 deg");
        }

        [Test]
        public void LogitechG27_DampingAndFrictionOpposeAngularVelocity()
        {
            float damp = LogitechG27Adapter.CalculateDamping(2.0f);
            Assert.That(damp, Is.LessThan(0f), "Damping must oppose positive velocity");

            float fricPos = LogitechG27Adapter.CalculateFriction(0.5f);
            float fricNeg = LogitechG27Adapter.CalculateFriction(-0.5f);
            Assert.That(fricPos, Is.LessThan(0f));
            Assert.That(fricNeg, Is.GreaterThan(0f));

            float fricZero = LogitechG27Adapter.CalculateFriction(0.001f);
            Assert.That(fricZero, Is.Zero, "Friction deadband near zero velocity");
        }

        [Test]
        public void LogitechG27_GripLossVibrationActivatesOnFrontSlip()
        {
            float noSlip = LogitechG27Adapter.CalculateGripLossVibration(0.02f, 10f, 0.1);
            Assert.That(noSlip, Is.Zero, "No vibration below slip angle threshold");

            float zeroSpeed = LogitechG27Adapter.CalculateGripLossVibration(0.15f, 0f, 0.1);
            Assert.That(zeroSpeed, Is.Zero, "No vibration when stationary");

            float slipping = LogitechG27Adapter.CalculateGripLossVibration(0.15f, 10f, 0.015);
            Assert.That(slipping, Is.Not.Zero, "Slip vibration must generate periodic signal");
        }

        [Test]
        public void LogitechG27_FFBRateLimiterConstrainsTorqueDelta()
        {
            // With max rate 10.0/s and dt = 0.01s, max allowed delta is 0.10
            float limited = LogitechG27Adapter.ApplyRateLimiter(1.0f, 0.0f, 0.01f, 10.0f);
            Assert.That(limited, Is.EqualTo(0.10f).Within(0.001f));

            float clamped = LogitechG27Adapter.ApplyRateLimiter(5.0f, 0.95f, 0.1f, 10.0f);
            Assert.That(clamped, Is.EqualTo(1.0f), "Total torque must remain clamped within [-1, 1]");

            float nanSafe = LogitechG27Adapter.ApplyRateLimiter(float.NaN, 0.5f, 0.01f, 10.0f);
            Assert.That(nanSafe, Is.Zero, "NaN target must safely return 0");
        }

        [Test]
        public void LogitechG27_StopWatchdogIsIdempotentAndZeroesTorque()
        {
            var adapter = new LogitechG27Adapter();
            adapter.SetNormalizedTorque(0.8f);
            Assert.That(adapter.CurrentAppliedTorque, Is.EqualTo(0.8f));

            adapter.Stop();
            Assert.That(adapter.CurrentAppliedTorque, Is.Zero);
            Assert.That(adapter.IsStopped, Is.True);

            // Safe to call repeated times
            Assert.DoesNotThrow(() => adapter.Stop());
            Assert.DoesNotThrow(() => adapter.Stop());
            Assert.That(adapter.CurrentAppliedTorque, Is.Zero);
        }

        [Test]
        public void LogitechG27_PauseAndResumeZeroesAndRestoresFFB()
        {
            var adapter = new LogitechG27Adapter();
            adapter.SetNormalizedTorque(0.75f);
            Assert.That(adapter.CurrentAppliedTorque, Is.EqualTo(0.75f));

            adapter.Pause();
            Assert.That(adapter.CurrentAppliedTorque, Is.Zero);
            Assert.That(adapter.IsPaused, Is.True);
            Assert.That(adapter.IsAvailable, Is.False);

            // While paused, setting torque remains 0
            adapter.SetNormalizedTorque(0.5f);
            Assert.That(adapter.CurrentAppliedTorque, Is.Zero);

            adapter.Resume();
            Assert.That(adapter.IsPaused, Is.False);
            Assert.That(adapter.IsAvailable, Is.True);

            adapter.SetNormalizedTorque(0.5f);
            Assert.That(adapter.CurrentAppliedTorque, Is.EqualTo(0.5f));
        }

        [Test]
        public void LogitechG27_DisposeCleansUpGracefully()
        {
            var adapter = new LogitechG27Adapter();
            adapter.SetNormalizedTorque(0.9f);
            Assert.DoesNotThrow(() => adapter.Dispose());
            Assert.That(adapter.CurrentAppliedTorque, Is.Zero);
            Assert.DoesNotThrow(() => adapter.Dispose()); // Idempotent dispose
        }

        [Test]
        public void LogitechG27_DisconnectionImmediatelyZeroesTorque()
        {
            var adapter = new LogitechG27Adapter();
            adapter.SetNormalizedTorque(0.8f);
            Assert.That(adapter.CurrentAppliedTorque, Is.EqualTo(0.8f));

            // Hardware disconnection
            adapter.IsConnected = false;
            Assert.That(adapter.CurrentAppliedTorque, Is.Zero, "Disconnecting must immediately zero out CurrentAppliedTorque");
            Assert.That(adapter.TargetTorque, Is.Zero, "Disconnecting must immediately zero out TargetTorque");
            Assert.That(adapter.IsAvailable, Is.False);
        }

        [Test]
        public void LogitechG27_SetNormalizedTorque_RespectsSlewRateLimit()
        {
            var adapter = new LogitechG27Adapter();
            adapter.SetNormalizedTorque(1.0f);
            Assert.That(adapter.CurrentAppliedTorque, Is.EqualTo(1.0f));

            // Immediate reversal in zero/sub-millisecond time
            adapter.SetNormalizedTorque(-1.0f);
            Assert.That(adapter.CurrentAppliedTorque, Is.Not.EqualTo(-1.0f), "SetNormalizedTorque must not jump instantaneously by 2.0");
            Assert.That(adapter.CurrentAppliedTorque, Is.GreaterThan(0.9f), "Sub-millisecond delta must be heavily bounded by rate limiter");

            // Explicit dt step test: with dt = 0.01s and SlewRateLimit = 10.0f, max delta is 0.10f
            adapter.SetNormalizedTorque(1.0f);
            adapter.SetNormalizedTorque(0.0f, 0.01f);
            Assert.That(adapter.CurrentAppliedTorque, Is.EqualTo(0.90f).Within(0.001f), "0.01s step from 1.0 toward 0.0 must change by exactly 0.10 at 10.0/s slew rate");
        }
    }
}
