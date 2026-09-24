using System;
using DrivingSchool.Contracts;

namespace DrivingSchool.Rules
{
    public sealed class SpeedLimitEvaluator
    {
        public const string RuleId = "pdd-10.2";
        public const string RuleRevision = "2026-01";
        public const string ExplanationKey = "rule.speed_limit_exceeded";

        public float GraceKph { get; set; } = 20.0f;
        public int PenaltyPoints { get; set; } = 500;

        bool wasViolating;

        public void Reset()
        {
            wasViolating = false;
        }

        public bool Evaluate(double simulationSeconds, double x, double y, double z, float speedMps, float speedLimitKph, out RuleEvent violationEvent)
        {
            violationEvent = null;
            if (speedLimitKph <= 0f || float.IsNaN(speedMps) || float.IsInfinity(speedMps))
                return false;

            float currentSpeedKph = Math.Abs(speedMps) * 3.6f;
            bool isViolating = currentSpeedKph > (speedLimitKph + GraceKph);

            if (isViolating && !wasViolating)
            {
                wasViolating = true;
                violationEvent = new RuleEvent
                {
                    id = Guid.NewGuid().ToString("N"),
                    ruleId = RuleId,
                    ruleRevision = RuleRevision,
                    participantId = "player",
                    evidenceId = $"speed-{currentSpeedKph:F1}-limit-{speedLimitKph:F0}",
                    explanationKey = ExplanationKey,
                    simulationSeconds = simulationSeconds,
                    x = x,
                    y = y,
                    z = z,
                    penalty = PenaltyPoints
                };
                return true;
            }

            if (!isViolating)
            {
                wasViolating = false;
            }

            return false;
        }
    }
}
