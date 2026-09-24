using System;
using System.Collections.Generic;
using System.Linq;
using DrivingSchool.Contracts;
using DrivingSchool.Simulation.RoadGraph;

namespace DrivingSchool.Simulation.Traffic
{
    /// <summary>
    /// Who goes first at a junction (T33 core). Order: signal state (checked by the director) -> approach priority
    /// from signs (main before secondary, ПДД РФ 13.9) -> a left turn yields to oncoming straight/right traffic
    /// (13.4 on green, 13.12 on equal roads) -> yield to traffic from the right (13.11, and 13.10 between main-road
    /// approaches) -> earlier arrival -> id. Rule references must be checked against the current edition before the
    /// rules module (T18) uses them for scoring; here they only order AI traffic.
    /// </summary>
    public static class JunctionPolicy
    {
        public const double SideToleranceDeg = 45;

        public sealed class Approach
        {
            public string ParticipantId, ConnectionId;
            public LaneManeuver Maneuver;
            public ApproachPriority Priority;
            public double EntryHeadingRad;   // heading at the start of the connection
            public double EtaSeconds;
            public bool SignalGreen;         // regulated and currently green for this approach
        }

        /// <summary>True if <paramref name="a"/> must let <paramref name="b"/> go first.</summary>
        public static bool MustYield(Approach a, Approach b)
        {
            if (a.SignalGreen && b.SignalGreen)
            {
                // Same green: turning left or U-turning yields to oncoming straight and right.
                if (IsTurnAcross(a.Maneuver) && !IsTurnAcross(b.Maneuver) && Opposite(a, b)) return true;
                if (IsTurnAcross(b.Maneuver) && !IsTurnAcross(a.Maneuver) && Opposite(a, b)) return false;
                return Earlier(b, a);
            }
            int ra = Rank(a.Priority), rb = Rank(b.Priority);
            if (ra != rb) return rb < ra;
            if (IsTurnAcross(a.Maneuver) && !IsTurnAcross(b.Maneuver) && Opposite(a, b)) return true;
            if (IsTurnAcross(b.Maneuver) && !IsTurnAcross(a.Maneuver) && Opposite(a, b)) return false;
            if (FromRight(a, b)) return true;
            if (FromRight(b, a)) return false;
            return Earlier(b, a);
        }

        /// <summary>b approaches from a's right: b travels at about -90 degrees relative to a (Unity yaw).</summary>
        public static bool FromRight(Approach a, Approach b) => Near(Relative(a, b), -90);

        public static bool Opposite(Approach a, Approach b) => Math.Abs(Relative(a, b)) >= 180 - SideToleranceDeg;

        static double Relative(Approach a, Approach b)
        {
            double deg = (b.EntryHeadingRad - a.EntryHeadingRad) * 180 / Math.PI;
            return DistrictCompiler.NormalizeDeg(deg);
        }

        static bool Near(double deg, double target) => Math.Abs(DistrictCompiler.NormalizeDeg(deg - target)) <= SideToleranceDeg;
        static bool IsTurnAcross(LaneManeuver m) => m == LaneManeuver.Left || m == LaneManeuver.UTurn;
        static bool Earlier(Approach x, Approach y) => x.EtaSeconds < y.EtaSeconds - 1e-6 || (Math.Abs(x.EtaSeconds - y.EtaSeconds) <= 1e-6 && string.CompareOrdinal(x.ParticipantId, y.ParticipantId) < 0);

        static int Rank(ApproachPriority p) => p == ApproachPriority.Main ? 0 : p == ApproachPriority.Secondary ? 2 : 1;

        /// <summary>Reservation keys of a connection: every conflict zone it takes part in, tagged with its side.</summary>
        public static string[] ZoneKeys(RoadGraphIndex index, string connectionId) =>
            index.ZonesOf(connectionId).Select(z => z.id + "#" + (z.connectionA == connectionId ? "A" : "B")).ToArray();

        /// <summary>Priority of the approach lane of a connection (Equal when the graph has no approach record).</summary>
        public static ApproachPriority PriorityOf(WorldDocumentV2 world, string fromLaneId)
        {
            foreach (var a in world.approaches) if (a.laneId == fromLaneId) return a.priority;
            return ApproachPriority.Equal;
        }
    }
}
