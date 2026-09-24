using System;
using System.Collections.Generic;
using DrivingSchool.Contracts;

namespace DrivingSchool.Rules
{
    /// <summary>
    /// Engine for evaluating road traffic regulations (ПДД) and autodrome exercise criteria.
    /// Tracks telemetry over time, enforces debounce and single-edge triggering, accumulates penalty scores,
    /// and flags disqualifying fatal infractions.
    /// Pure C# implementation without engine references.
    /// </summary>
    public sealed class RuleEvaluator
    {
        readonly List<RuleEvent> infractions = new List<RuleEvent>();

        // Penalty scoring state
        public int TotalPenaltyPoints { get; private set; }
        public bool HasFatalViolation { get; private set; }
        public int ViolationCount => infractions.Count;
        public IReadOnlyList<RuleEvent> Infractions => infractions;

        // Speed limit evaluation state
        double speedingStartTime = -1.0;
        bool speedViolationActive = false;

        // Stop line evaluation state
        double stopLineArrivalSeconds = -1.0;
        double stopDurationAccumulated = 0.0;
        bool stopLineCompleted = false;
        bool stopLineViolationActive = false;
        double lastSimSeconds = -1.0;

        // Exercise boundary evaluation state
        bool boundaryViolationActive = false;

        // Turn signal evaluation state
        bool turnSignalViolationActive = false;

        public RuleEvaluator()
        {
            Reset();
        }

        public void Reset()
        {
            infractions.Clear();
            TotalPenaltyPoints = 0;
            HasFatalViolation = false;

            speedingStartTime = -1.0;
            speedViolationActive = false;

            stopLineArrivalSeconds = -1.0;
            stopDurationAccumulated = 0.0;
            stopLineCompleted = false;
            stopLineViolationActive = false;
            lastSimSeconds = -1.0;

            boundaryViolationActive = false;
            turnSignalViolationActive = false;
        }

        public void AddInfraction(RuleEvent ev)
        {
            if (ev == null) return;
            infractions.Add(ev);
            TotalPenaltyPoints += ev.penalty;

            if (ev.penalty >= TrafficRules.PenaltyFatal ||
                ev.ruleId == TrafficRules.RuleExerciseBoundary ||
                ev.ruleId == TrafficRules.RuleRedLight)
            {
                HasFatalViolation = true;
            }
        }

        /// <summary>
        /// Evaluates speed limit adherence with debounce time and reset hysteresis.
        /// Returns true if a new violation was triggered this frame.
        /// </summary>
        public bool EvaluateSpeedLimit(
            double simSeconds,
            double x, double y, double z,
            float speedMps,
            float speedLimitKph,
            out RuleEvent violation)
        {
            return EvaluateSpeedLimit(simSeconds, x, y, z, speedMps, speedLimitKph, TrafficRules.SpeedGraceKph, TrafficRules.SpeedDebounceSeconds, out violation);
        }

        public bool EvaluateSpeedLimit(
            double simSeconds,
            double x, double y, double z,
            float speedMps,
            float speedLimitKph,
            float graceKph,
            double debounceSeconds,
            out RuleEvent violation)
        {
            violation = null;
            if (speedLimitKph <= 0f || float.IsNaN(speedMps) || float.IsInfinity(speedMps))
                return false;

            float currentSpeedKph = Math.Abs(speedMps) * 3.6f;
            float thresholdKph = speedLimitKph + graceKph;
            bool isOverThreshold = currentSpeedKph > thresholdKph;

            if (isOverThreshold)
            {
                if (speedingStartTime < 0.0)
                {
                    speedingStartTime = simSeconds;
                }

                double duration = simSeconds - speedingStartTime;
                if (duration >= debounceSeconds && !speedViolationActive)
                {
                    speedViolationActive = true;
                    float excessKph = currentSpeedKph - speedLimitKph;
                    int penalty = excessKph > 20f ? TrafficRules.PenaltySpeedMajor : TrafficRules.PenaltySpeedMinor;

                    violation = new RuleEvent
                    {
                        id = Guid.NewGuid().ToString("N"),
                        ruleId = TrafficRules.RuleSpeedLimit,
                        ruleRevision = TrafficRules.CurrentRevision,
                        participantId = "player",
                        evidenceId = $"speed-{currentSpeedKph:F1}-limit-{speedLimitKph:F0}",
                        explanationKey = "rule.speed_limit_exceeded",
                        simulationSeconds = simSeconds,
                        x = x,
                        y = y,
                        z = z,
                        penalty = penalty
                    };

                    AddInfraction(violation);
                    return true;
                }
            }
            else
            {
                // Reset debounce counter
                speedingStartTime = -1.0;

                // Hysteresis: require speed to drop at least 2 km/h below threshold before clearing violation state
                if (currentSpeedKph < (thresholdKph - 2.0f))
                {
                    speedViolationActive = false;
                }
            }

            return false;
        }

        public bool EvaluateStopLine(
            double simSeconds,
            double x, double y, double z,
            float speedMps,
            double distanceToStopLine,
            bool isPastStopLine,
            bool signalRequiresStop,
            out RuleEvent violation)
        {
            return EvaluateStopLine(simSeconds, x, y, z, speedMps, distanceToStopLine, isPastStopLine, signalRequiresStop, 1.5f, 1.0, out violation);
        }

        public bool EvaluateStopLine(
            double simSeconds,
            double x, double y, double z,
            float speedMps,
            double distanceToStopLine,
            bool isPastStopLine,
            bool signalRequiresStop,
            float stopToleranceM,
            double requiredStopSeconds,
            out RuleEvent violation)
        {
            violation = null;
            if (!signalRequiresStop)
            {
                // Green signal or clear priority; reset tracking
                stopDurationAccumulated = 0.0;
                stopLineCompleted = false;
                stopLineViolationActive = false;
                lastSimSeconds = -1.0;
                return false;
            }

            double dt = lastSimSeconds > 0 ? Math.Max(0.0, simSeconds - lastSimSeconds) : 0.01;
            lastSimSeconds = simSeconds;

            bool isStationary = Math.Abs(speedMps) <= TrafficRules.SpeedStopThresholdMps;
            bool isInStopZone = distanceToStopLine >= -0.5 && distanceToStopLine <= stopToleranceM;

            if (isInStopZone && isStationary)
            {
                stopDurationAccumulated += dt;
                if (stopDurationAccumulated >= requiredStopSeconds)
                {
                    stopLineCompleted = true;
                }
            }

            if (isPastStopLine && !stopLineCompleted && !stopLineViolationActive)
            {
                stopLineViolationActive = true;
                violation = new RuleEvent
                {
                    id = Guid.NewGuid().ToString("N"),
                    ruleId = TrafficRules.RuleStopLine,
                    ruleRevision = TrafficRules.CurrentRevision,
                    participantId = "player",
                    evidenceId = $"stopline-violation-dist-{distanceToStopLine:F2}",
                    explanationKey = "rule.stop_line_overrun",
                    simulationSeconds = simSeconds,
                    x = x,
                    y = y,
                    z = z,
                    penalty = TrafficRules.PenaltyStopLineOverrun
                };

                AddInfraction(violation);
                return true;
            }

            return false;
        }

        public bool EvaluateExerciseBoundary(
            double simSeconds,
            double x, double y, double z,
            double minX, double maxX,
            double minZ, double maxZ,
            out RuleEvent violation)
        {
            return EvaluateExerciseBoundary(simSeconds, x, y, z, minX, maxX, minZ, maxZ, "autodrome_exercise", out violation);
        }

        public bool EvaluateExerciseBoundary(
            double simSeconds,
            double x, double y, double z,
            double minX, double maxX,
            double minZ, double maxZ,
            string exerciseId,
            out RuleEvent violation)
        {
            violation = null;
            double lowX = Math.Min(minX, maxX);
            double highX = Math.Max(minX, maxX);
            double lowZ = Math.Min(minZ, maxZ);
            double highZ = Math.Max(minZ, maxZ);

            bool isInside = x >= lowX && x <= highX && z >= lowZ && z <= highZ;

            if (!isInside && !boundaryViolationActive)
            {
                boundaryViolationActive = true;
                violation = new RuleEvent
                {
                    id = Guid.NewGuid().ToString("N"),
                    ruleId = TrafficRules.RuleExerciseBoundary,
                    ruleRevision = TrafficRules.CurrentRevision,
                    participantId = "player",
                    evidenceId = $"boundary-out-{exerciseId}-pos-{x:F1}_{z:F1}",
                    explanationKey = "rule.exercise_boundary_crossed",
                    simulationSeconds = simSeconds,
                    x = x,
                    y = y,
                    z = z,
                    penalty = TrafficRules.PenaltyFatal
                };

                AddInfraction(violation);
                return true;
            }

            // Spatial hysteresis: to clear violation, vehicle must return safely inside boundary
            // by at least the hysteresis margin (default 0.2m, capped at 10% of span)
            double hx = Math.Min(0.2, Math.Max(0.0, (highX - lowX) * 0.1));
            double hz = Math.Min(0.2, Math.Max(0.0, (highZ - lowZ) * 0.1));
            bool isSafelyInside = x >= (lowX + hx) && x <= (highX - hx) && z >= (lowZ + hz) && z <= (highZ - hz);

            if (isSafelyInside)
            {
                boundaryViolationActive = false;
            }

            return false;
        }

        /// <summary>
        /// Evaluates turn signal compliance during turns or changes of direction.
        /// If vehicle initiates a turn without appropriate indicator, generates a minor penalty.
        /// </summary>
        public bool EvaluateTurnSignal(
            double simSeconds,
            double x, double y, double z,
            float steering,
            float speedMps,
            bool leftIndicator,
            bool rightIndicator,
            out RuleEvent violation)
        {
            violation = null;
            if (float.IsNaN(steering) || float.IsInfinity(steering)) return false;

            bool isMovingForward = speedMps >= TrafficRules.TurnSpeedThresholdMps;
            bool isTurningLeft = steering <= -TrafficRules.TurnSteeringThreshold;
            bool isTurningRight = steering >= TrafficRules.TurnSteeringThreshold;

            if (isMovingForward && (isTurningLeft || isTurningRight))
            {
                bool signalActive = isTurningLeft ? leftIndicator : rightIndicator;

                if (!signalActive && !turnSignalViolationActive)
                {
                    turnSignalViolationActive = true;
                    violation = new RuleEvent
                    {
                        id = Guid.NewGuid().ToString("N"),
                        ruleId = TrafficRules.RuleTurnSignal,
                        ruleRevision = TrafficRules.CurrentRevision,
                        participantId = "player",
                        evidenceId = $"turn-signal-missing-steer-{steering:F2}",
                        explanationKey = "rule.turn_signal_omitted",
                        simulationSeconds = simSeconds,
                        x = x,
                        y = y,
                        z = z,
                        penalty = TrafficRules.PenaltyTurnSignal
                    };

                    AddInfraction(violation);
                    return true;
                }
            }
            else
            {
                // Once steering straightens out, clear turn signal violation latch
                if (Math.Abs(steering) < 0.10f)
                {
                    turnSignalViolationActive = false;
                }
            }

            return false;
        }
    }
}
