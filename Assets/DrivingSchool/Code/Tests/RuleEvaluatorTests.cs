using System;
using NUnit.Framework;
using DrivingSchool.Contracts;
using DrivingSchool.Rules;

namespace DrivingSchool.Tests
{
    [TestFixture]
    public class RuleEvaluatorTests
    {
        [Test]
        public void SpeedLimit_BelowThreshold_DoesNotTrigger()
        {
            var evaluator = new RuleEvaluator();
            // Limit 60 km/h + grace 5 = 65 km/h; vehicle at 15 m/s = 54 km/h
            bool triggered = evaluator.EvaluateSpeedLimit(1.0, 0, 0, 0, 15f, 60f, 5f, 1.5, out var ev);

            Assert.That(triggered, Is.False);
            Assert.That(ev, Is.Null);
            Assert.That(evaluator.TotalPenaltyPoints, Is.Zero);
        }

        [Test]
        public void SpeedLimit_DebounceWindow_RequiresSustainedExcess()
        {
            var evaluator = new RuleEvaluator();
            // 20 m/s = 72 km/h (> 65 km/h threshold)
            bool t1 = evaluator.EvaluateSpeedLimit(1.0, 0, 0, 0, 20f, 60f, 5f, 1.5, out var ev1);
            Assert.That(t1, Is.False, "Transient spike below debounce window must not trigger");

            bool t2 = evaluator.EvaluateSpeedLimit(2.0, 0, 0, 0, 20f, 60f, 5f, 1.5, out var ev2);
            Assert.That(t2, Is.False, "1.0 second excess is still within 1.5s debounce");

            bool t3 = evaluator.EvaluateSpeedLimit(2.6, 0, 0, 0, 20f, 60f, 5f, 1.5, out var ev3);
            Assert.That(t3, Is.True, "Sustained excess beyond 1.5s must trigger violation");
            Assert.That(ev3, Is.Not.Null);
            Assert.That(ev3.ruleId, Is.EqualTo(TrafficRules.RuleSpeedLimit));
            Assert.That(ev3.penalty, Is.EqualTo(TrafficRules.PenaltySpeedMinor)); // 72 - 60 = 12 km/h excess
        }

        [Test]
        public void SpeedLimit_EdgeTriggered_DoesNotSpamEvents()
        {
            var evaluator = new RuleEvaluator();
            evaluator.EvaluateSpeedLimit(1.0, 0, 0, 0, 22f, 60f, 5f, 1.0, out _);
            evaluator.EvaluateSpeedLimit(2.1, 0, 0, 0, 22f, 60f, 5f, 1.0, out var ev);
            Assert.That(ev, Is.Not.Null);

            // Continuing to speed for next 5 seconds
            for (double t = 2.2; t <= 7.0; t += 0.2)
            {
                bool repeat = evaluator.EvaluateSpeedLimit(t, 0, 0, 0, 22f, 60f, 5f, 1.0, out var evRepeat);
                Assert.That(repeat, Is.False, "Must not spam repeated events while continuing to speed");
                Assert.That(evRepeat, Is.Null);
            }

            Assert.That(evaluator.ViolationCount, Is.EqualTo(1));
        }

        [Test]
        public void SpeedLimit_Hysteresis_ResetsAfterSlowingDown()
        {
            var evaluator = new RuleEvaluator();
            evaluator.EvaluateSpeedLimit(1.0, 0, 0, 0, 25f, 60f, 5f, 1.0, out _);
            evaluator.EvaluateSpeedLimit(2.2, 0, 0, 0, 25f, 60f, 5f, 1.0, out _);
            Assert.That(evaluator.ViolationCount, Is.EqualTo(1));

            // Slow down below 65 - 2 = 63 km/h (16 m/s = 57.6 km/h)
            evaluator.EvaluateSpeedLimit(4.0, 0, 0, 0, 16f, 60f, 5f, 1.0, out _);

            // Speed up again
            evaluator.EvaluateSpeedLimit(5.0, 0, 0, 0, 25f, 60f, 5f, 1.0, out _);
            bool secondViolation = evaluator.EvaluateSpeedLimit(6.2, 0, 0, 0, 25f, 60f, 5f, 1.0, out var ev2);

            Assert.That(secondViolation, Is.True, "Subsequent speeding after reset must trigger new violation");
            Assert.That(evaluator.ViolationCount, Is.EqualTo(2));
        }

        [Test]
        public void StopLine_PassingWithoutStopping_TriggersViolation()
        {
            var evaluator = new RuleEvaluator();

            // Vehicle approaches stop line without slowing down and crosses past it
            bool triggered = evaluator.EvaluateStopLine(
                simSeconds: 5.0,
                x: 10.0, y: 0.0, z: 50.0,
                speedMps: 10.0f,
                distanceToStopLine: -1.0, // negative means past the line
                isPastStopLine: true,
                signalRequiresStop: true,
                stopToleranceM: 1.5f,
                requiredStopSeconds: 1.0,
                out var ev);

            Assert.That(triggered, Is.True);
            Assert.That(ev, Is.Not.Null);
            Assert.That(ev.ruleId, Is.EqualTo(TrafficRules.RuleStopLine));
            Assert.That(ev.penalty, Is.EqualTo(TrafficRules.PenaltyStopLineOverrun));
            Assert.That(evaluator.TotalPenaltyPoints, Is.EqualTo(25));
        }

        [Test]
        public void StopLine_StoppingCorrectly_PassesWithoutViolation()
        {
            var evaluator = new RuleEvaluator();

            // Vehicle stops in front of the line (distance 0.8m) for 1.2 seconds (24 ticks of 0.05s)
            for (int i = 0; i < 25; i++)
            {
                evaluator.EvaluateStopLine(
                    simSeconds: 1.0 + i * 0.05,
                    x: 10.0, y: 0.0, z: 49.2,
                    speedMps: 0.05f, // stationary
                    distanceToStopLine: 0.8,
                    isPastStopLine: false,
                    signalRequiresStop: true,
                    stopToleranceM: 1.5f,
                    requiredStopSeconds: 1.0,
                    out _);
            }

            // Vehicle proceeds past the line
            bool triggered = evaluator.EvaluateStopLine(
                simSeconds: 3.0,
                x: 10.0, y: 0.0, z: 51.0,
                speedMps: 3.0f,
                distanceToStopLine: -1.0,
                isPastStopLine: true,
                signalRequiresStop: true,
                stopToleranceM: 1.5f,
                requiredStopSeconds: 1.0,
                out var ev);

            Assert.That(triggered, Is.False, "Completed stop must not generate penalty upon crossing");
            Assert.That(ev, Is.Null);
            Assert.That(evaluator.TotalPenaltyPoints, Is.Zero);
        }

        [Test]
        public void ExerciseBoundary_InsideBoundary_DoesNotTrigger()
        {
            var evaluator = new RuleEvaluator();
            // Parallel bay bounds: X from 0 to 20, Z from 0 to 15
            bool triggered = evaluator.EvaluateExerciseBoundary(1.0, 10.0, 0.0, 8.0, 0, 20, 0, 15, "parallel_bay", out var ev);

            Assert.That(triggered, Is.False);
            Assert.That(ev, Is.Null);
            Assert.That(evaluator.HasFatalViolation, Is.False);
        }

        [Test]
        public void ExerciseBoundary_CrossingBoundary_TriggersFatalInfraction()
        {
            var evaluator = new RuleEvaluator();
            // Car strays outside boundary: X = 22.5 > 20.0
            bool triggered = evaluator.EvaluateExerciseBoundary(2.5, 22.5, 0.0, 8.0, 0, 20, 0, 15, "parallel_bay", out var ev);

            Assert.That(triggered, Is.True);
            Assert.That(ev, Is.Not.Null);
            Assert.That(ev.ruleId, Is.EqualTo(TrafficRules.RuleExerciseBoundary));
            Assert.That(ev.penalty, Is.EqualTo(TrafficRules.PenaltyFatal));
            Assert.That(evaluator.HasFatalViolation, Is.True, "Crossing exercise boundary must trigger fatal failure");
            Assert.That(evaluator.TotalPenaltyPoints, Is.EqualTo(100));
        }

        [Test]
        public void TurnSignal_OmissionTriggersPenalty()
        {
            var evaluator = new RuleEvaluator();
            // Vehicle turning left (steer = -0.45) at 5 m/s with indicators off
            bool triggered = evaluator.EvaluateTurnSignal(1.0, 0, 0, 0, -0.45f, 5.0f, leftIndicator: false, rightIndicator: false, out var ev);

            Assert.That(triggered, Is.True);
            Assert.That(ev, Is.Not.Null);
            Assert.That(ev.ruleId, Is.EqualTo(TrafficRules.RuleTurnSignal));
            Assert.That(ev.penalty, Is.EqualTo(TrafficRules.PenaltyTurnSignal));
        }

        [Test]
        public void TurnSignal_ProperIndicatorUsage_PassesCleanly()
        {
            var evaluator = new RuleEvaluator();
            // Vehicle turning right (steer = 0.40) at 4 m/s with rightIndicator on
            bool triggered = evaluator.EvaluateTurnSignal(1.0, 0, 0, 0, 0.40f, 4.0f, leftIndicator: false, rightIndicator: true, out var ev);

            Assert.That(triggered, Is.False);
            Assert.That(ev, Is.Null);
            Assert.That(evaluator.TotalPenaltyPoints, Is.Zero);
        }

        [Test]
        public void PenaltyScore_AccumulatesAcrossMultipleViolations()
        {
            var evaluator = new RuleEvaluator();

            // 1. Turn signal omission (5 points)
            evaluator.EvaluateTurnSignal(1.0, 0, 0, 0, 0.35f, 3.0f, false, false, out _);

            // 2. Minor speeding (15 points)
            evaluator.EvaluateSpeedLimit(1.0, 0, 0, 0, 20f, 60f, 5f, 0.5, out _);
            evaluator.EvaluateSpeedLimit(1.6, 0, 0, 0, 20f, 60f, 5f, 0.5, out _);

            // 3. Stop line overrun (25 points)
            evaluator.EvaluateStopLine(3.0, 0, 0, 0, 5f, -1.0, true, true, 1.5f, 1.0, out _);

            Assert.That(evaluator.ViolationCount, Is.EqualTo(3));
            Assert.That(evaluator.TotalPenaltyPoints, Is.EqualTo(5 + 15 + 25)); // 45 points
            Assert.That(evaluator.HasFatalViolation, Is.False);
        }

        [Test]
        public void Reset_ClearsAllInfractionsAndFatalStatus()
        {
            var evaluator = new RuleEvaluator();
            evaluator.EvaluateExerciseBoundary(1.0, 50, 0, 50, 0, 10, 0, 10, "slalom", out _);
            Assert.That(evaluator.HasFatalViolation, Is.True);

            evaluator.Reset();
            Assert.That(evaluator.ViolationCount, Is.Zero);
            Assert.That(evaluator.TotalPenaltyPoints, Is.Zero);
            Assert.That(evaluator.HasFatalViolation, Is.False);
            Assert.That(evaluator.Infractions.Count, Is.Zero);
        }

        [Test]
        public void StopLine_DynamicDt_IsTickRateIndependent()
        {
            var evaluator = new RuleEvaluator();
            double requiredStop = 1.0;

            // At 100 Hz simulation (dt = 0.01s), 20 ticks = 0.20s
            for (int i = 0; i < 20; i++)
            {
                evaluator.EvaluateStopLine(
                    simSeconds: 1.0 + i * 0.01,
                    x: 0, y: 0, z: 0,
                    speedMps: 0f,
                    distanceToStopLine: 0.5,
                    isPastStopLine: false,
                    signalRequiresStop: true,
                    stopToleranceM: 1.5f,
                    requiredStopSeconds: requiredStop,
                    out _);
            }

            // At 0.20s, the 1.0s requirement must NOT be completed yet
            evaluator.EvaluateStopLine(
                simSeconds: 1.25,
                x: 0, y: 0, z: 0,
                speedMps: 5f,
                distanceToStopLine: -1.0,
                isPastStopLine: true,
                signalRequiresStop: true,
                stopToleranceM: 1.5f,
                requiredStopSeconds: requiredStop,
                out var earlyOverrunViolation);

            Assert.That(earlyOverrunViolation, Is.Not.Null, "0.20s stop must not satisfy a 1.0s requirement");

            // Reset and simulate full 100 ticks (1.0s)
            evaluator.Reset();
            for (int i = 0; i <= 100; i++)
            {
                evaluator.EvaluateStopLine(
                    simSeconds: 2.0 + i * 0.01,
                    x: 0, y: 0, z: 0,
                    speedMps: 0f,
                    distanceToStopLine: 0.5,
                    isPastStopLine: false,
                    signalRequiresStop: true,
                    stopToleranceM: 1.5f,
                    requiredStopSeconds: requiredStop,
                    out _);
            }

            // Cross after full stop: must pass cleanly
            evaluator.EvaluateStopLine(
                simSeconds: 3.5,
                x: 0, y: 0, z: 0,
                speedMps: 5f,
                distanceToStopLine: -1.0,
                isPastStopLine: true,
                signalRequiresStop: true,
                stopToleranceM: 1.5f,
                requiredStopSeconds: requiredStop,
                out var cleanPassViolation);

            Assert.That(cleanPassViolation, Is.Null, "Full 1.0s stop must pass without violation");
        }

        [Test]
        public void ExerciseBoundary_SpatialHysteresis_SuppressesJitterCascade()
        {
            var evaluator = new RuleEvaluator();
            double minX = 0.0, maxX = 3.0;
            double minZ = 0.0, maxZ = 25.0;

            int violationsTriggered = 0;
            // 50 ticks of boundary oscillation across X = 3.0m
            for (int tick = 0; tick < 50; tick++)
            {
                double t = tick * 0.02;
                double x = (tick % 2 == 1) ? 3.001 : 2.999;

                bool triggered = evaluator.EvaluateExerciseBoundary(
                    simSeconds: t,
                    x: x, y: 0.0, z: 10.0,
                    minX: minX, maxX: maxX,
                    minZ: minZ, maxZ: maxZ,
                    exerciseId: "reverse_corridor",
                    out _);

                if (triggered)
                {
                    violationsTriggered++;
                }
            }

            Assert.That(violationsTriggered, Is.EqualTo(1), "Spatial hysteresis must latch violation and avoid duplicate spam");
            Assert.That(evaluator.TotalPenaltyPoints, Is.EqualTo(TrafficRules.PenaltyFatal));
        }
    }
}
