using System;

namespace DrivingSchool.Simulation.Traffic
{
    // Request -> time-limited permit -> notices to affected participants (T40, ADR-015).

    public enum ManeuverKind { EnterConflictZone, LaneChange, MandatoryLaneChange, Merge, OncomingBypass, PullOut, PedestrianCross }

    public enum NoticeKind { OpenGap, Hold, SlowDown, YieldToPedestrian, HazardAhead, Cancel }

    /// <summary>What a participant asks the director for. Keys are reservation keys (conflict zones, connections, lane spans).</summary>
    public sealed class ManeuverRequest
    {
        public string AgentId, PathId;      // PathId: connection or target lane of the maneuver
        public ManeuverKind Kind;
        public double EtaSeconds;           // time until the maneuver starts
        public double DurationSeconds;      // expected time to complete once started
        public ReservationClaim Claim;
    }

    public sealed class ManeuverPermit
    {
        public string AgentId, PathId, Reason, ConflictOwnerId;
        public ManeuverKind Kind;
        public bool Granted;
        public double UntilSeconds;         // lease end; not finished by then -> released, fallback applies
        public string Fallback;             // what the agent does if the lease ends: "stop-at-line", "return-to-lane"
    }

    public sealed class ManeuverNotice
    {
        public string ToId, FromId, SubjectId; // SubjectId: zone, lane or crossing the notice is about
        public NoticeKind Kind;
        public double Value;                   // decel for OpenGap/SlowDown target speed, s for HazardAhead, time for Hold
        public long IssuedTick;
    }

    /// <summary>A stretch of a lane or connection, [FromS, ToS] along it.</summary>
    public struct PathSpan
    {
        public string PathId; public double FromS, ToS;
        public PathSpan(string pathId, double fromS, double toS) { PathId = pathId; FromS = fromS; ToS = toS; }
        public bool Overlaps(PathSpan o) => PathId == o.PathId && FromS < o.ToS && o.FromS < ToS;
    }

    /// <summary>Everything one maneuver needs exclusively: named keys (conflict zones, crossings) and path spans.</summary>
    public sealed class ReservationClaim
    {
        public string Id;
        public string[] Keys = Array.Empty<string>();
        public PathSpan[] Spans = Array.Empty<PathSpan>();
    }
}
