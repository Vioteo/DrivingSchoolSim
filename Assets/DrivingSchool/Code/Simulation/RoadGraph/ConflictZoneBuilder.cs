using System;
using System.Collections.Generic;
using DrivingSchool.Contracts;

namespace DrivingSchool.Simulation.RoadGraph
{
    /// <summary>
    /// Derives conflict zones between connections of one junction from their centerlines.
    /// Two connections conflict where their centerlines come closer than <see cref="DefaultClearanceM"/>
    /// (roughly one car width plus margin — a game parameter, not a legal norm) or when they end in the same lane.
    /// Connections that leave the same lane never conflict: those agents are ordered by car-following.
    /// </summary>
    public static class ConflictZoneBuilder
    {
        public const double DefaultClearanceM = 2.4;
        const double SampleStepM = 0.5;

        public static List<ConflictZone> Build(IReadOnlyList<LaneConnection> connections, double clearanceM = DefaultClearanceM)
        {
            var zones = new List<ConflictZone>();
            var lines = new Polyline[connections.Count];
            for (int i = 0; i < connections.Count; i++) lines[i] = new Polyline(connections[i].centerline);

            for (int i = 0; i < connections.Count; i++)
            for (int j = i + 1; j < connections.Count; j++)
            {
                var a = connections[i]; var b = connections[j];
                if (a.junctionId != b.junctionId || a.fromLaneId == b.fromLaneId) continue;
                bool merge = a.toLaneId == b.toLaneId;
                if (!Overlap(lines[i], lines[j], clearanceM, out float a0, out float a1))
                {
                    if (!merge) continue;
                    a0 = a1 = (float)lines[i].Length;
                }
                Overlap(lines[j], lines[i], clearanceM, out float b0, out float b1);
                if (merge)
                {
                    // Merging paths share the exit: the zone extends to both ends.
                    a1 = (float)lines[i].Length; b1 = (float)lines[j].Length;
                    if (b0 > b1) b0 = b1;
                }
                zones.Add(new ConflictZone
                {
                    id = "cz:" + a.id + "|" + b.id, junctionId = a.junctionId,
                    connectionA = a.id, connectionB = b.id,
                    fromSA = a0, toSA = a1, fromSB = b0, toSB = b1, merge = merge,
                });
            }
            return zones;
        }

        /// <summary>Arc-length range on <paramref name="line"/> that lies within clearance of <paramref name="other"/>.</summary>
        static bool Overlap(Polyline line, Polyline other, double clearance, out float from, out float to)
        {
            from = float.MaxValue; to = float.MinValue;
            int n = Math.Max(1, (int)Math.Ceiling(line.Length / SampleStepM));
            for (int k = 0; k <= n; k++)
            {
                double s = line.Length * k / n;
                var p = line.PointAt(s);
                other.Project(p.x, p.z, out double os, out _);
                var q = other.PointAt(os);
                if (Polyline.Distance2D(p, q) < clearance)
                {
                    from = Math.Min(from, (float)s); to = Math.Max(to, (float)s);
                }
            }
            return from <= to;
        }
    }
}
