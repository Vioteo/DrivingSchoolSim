using System;

namespace DrivingSchool.Rules
{
    /// <summary>
    /// Constants, standard penalty scores, and regulatory definitions for Russian Traffic Rules (ПДД)
    /// and Autodrome educational criteria.
    /// Pure C# domain model without engine references.
    /// </summary>
    public static class TrafficRules
    {
        public const string CurrentRevision = "2026.01";

        // Regulatory Rule Identifiers
        public const string RuleSpeedLimit = "PDD_10.2_SPEED_LIMIT";
        public const string RuleStopLine = "PDD_6.13_STOP_LINE";
        public const string RuleExerciseBoundary = "AUTODROME_EXERCISE_BOUNDARY";
        public const string RuleTurnSignal = "PDD_8.1_TURN_SIGNAL";
        public const string RuleRedLight = "PDD_6.2_RED_LIGHT";

        // Standard penalty points scale according to examination methodology
        public const int PenaltyTurnSignal = 5;       // Minor violation (e.g. failure to indicate maneuver)
        public const int PenaltySpeedMinor = 15;      // Exceeding speed limit by 5..20 km/h
        public const int PenaltySpeedMajor = 25;      // Exceeding speed limit by > 20 km/h
        public const int PenaltyStopLineOverrun = 25;  // Overrunning stop line without complete stop
        public const int PenaltyFatal = 100;          // Disqualifying fatal violation (boundary crossing, red signal, collision)

        // Thresholds
        public const float SpeedStopThresholdMps = 0.14f; // ~0.5 km/h regarded as stationary
        public const float SpeedGraceKph = 5.0f;          // Permissible grace margin
        public const double SpeedDebounceSeconds = 1.5;   // Debounce window to reject momentary transient spikes
        public const float TurnSteeringThreshold = 0.20f; // Wheel deflection indicating active turn maneuver
        public const float TurnSpeedThresholdMps = 1.5f;  // Minimum forward speed where turn signal is required
    }

    /// <summary>
    /// Rectangular bounding box defining an autodrome exercise perimeter.
    /// </summary>
    public sealed class ExerciseBoundary
    {
        public string ExerciseId { get; set; }
        public double MinX { get; set; }
        public double MaxX { get; set; }
        public double MinZ { get; set; }
        public double MaxZ { get; set; }
        public bool IsFatalOnExit { get; set; } = true;

        public ExerciseBoundary(string exerciseId, double minX, double maxX, double minZ, double maxZ, bool isFatal = true)
        {
            ExerciseId = exerciseId ?? "exercise";
            MinX = Math.Min(minX, maxX);
            MaxX = Math.Max(minX, maxX);
            MinZ = Math.Min(minZ, maxZ);
            MaxZ = Math.Max(minZ, maxZ);
            IsFatalOnExit = isFatal;
        }

        public bool Contains(double x, double z)
        {
            return x >= MinX && x <= MaxX && z >= MinZ && z <= MaxZ;
        }
    }

    /// <summary>
    /// Stop line specification along an approach lane.
    /// </summary>
    public sealed class StopLineDefinition
    {
        public string Id { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public float StopToleranceM { get; set; } = 1.5f;
        public double RequiredStopSeconds { get; set; } = 1.0;

        public StopLineDefinition(string id, double x, double y, double z, float toleranceM = 1.5f, double requiredStopSec = 1.0)
        {
            Id = id;
            X = x;
            Y = y;
            Z = z;
            StopToleranceM = toleranceM;
            RequiredStopSeconds = requiredStopSec;
        }
    }
}
