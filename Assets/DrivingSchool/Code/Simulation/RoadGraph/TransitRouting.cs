using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DrivingSchool.Contracts;

namespace DrivingSchool.Simulation.RoadGraph
{
    /// <summary>
    /// Ways of route vehicles over a compiled graph (T74): the shortest way from one stop to the next, then back to the
    /// first, joined into a closed loop of paths. Deterministic (ties by ordinal id). Lanes for route vehicles are
    /// preferred where there is a choice; U-turns are never taken.
    /// </summary>
    public static class TransitRouting
    {
        /// <summary>A metre of a lane for route vehicles costs this much (they keep to their lane where they can).</summary>
        public const double BusLaneCost = 0.8;

        /// <summary>Fills <see cref="TransitRoute.pathIds"/> of every route of <paramref name="world"/>.</summary>
        public static void CompileRoutes(WorldDocumentV2 world)
        {
            if (world.transitRoutes == null || world.transitRoutes.Length == 0) return;
            var index = new RoadGraphIndex(world);
            var stops = world.transitStops.ToDictionary(s => s.id);
            foreach (var r in world.transitRoutes)
            {
                Require(r.stopIds != null && r.stopIds.Length > 0, "Transit route without stops: " + r.id);
                foreach (var id in r.stopIds) Require(stops.ContainsKey(id), "Transit route " + r.id + " has unknown stop " + id);
                var loop = new List<string>();
                for (int i = 0; i < r.stopIds.Length; i++)
                {
                    var from = stops[r.stopIds[i]]; var to = stops[r.stopIds[(i + 1) % r.stopIds.Length]];
                    bool single = r.stopIds.Length == 1;
                    var way = Way(index, from.laneId, from.s, to.laneId, to.s, forceLoop: single);
                    Require(way != null, "No way for route " + r.id + " from stop " + from.id + " to " + to.id);
                    // Every leg starts on the lane where the previous one ended: keep it once.
                    loop.AddRange(loop.Count == 0 ? way : way.Skip(1));
                }
                // The last leg ends on the first stop's lane, where the loop begins.
                if (loop.Count > 1 && loop[loop.Count - 1] == loop[0]) loop.RemoveAt(loop.Count - 1);
                Require(loop.Count > 1, "Transit route too short: " + r.id);
                r.pathIds = loop.ToArray();
            }
        }

        /// <summary>
        /// Paths from lane <paramref name="fromLane"/> (at s <paramref name="fromS"/>) to lane <paramref name="toLane"/>
        /// (at <paramref name="toS"/>), both included; null if there is no way. One lane when the target lies ahead on it.
        /// </summary>
        public static List<string> Way(RoadGraphIndex index, string fromLane, double fromS, string toLane, double toS, bool forceLoop = false)
        {
            if (fromLane == toLane && toS > fromS && !forceLoop) return new List<string> { fromLane };
            var cost = new Dictionary<string, double>();
            var previous = new Dictionary<string, string>();
            var open = new SortedSet<(double cost, string id)>();
            foreach (var n in Moves(index, index.Path(fromLane)))
            {
                double c = Cost(index.Path(n));
                if (!cost.TryGetValue(n, out var old) || c < old) { if (cost.ContainsKey(n)) open.Remove((old, n)); cost[n] = c; previous[n] = fromLane; open.Add((c, n)); }
            }
            var done = new HashSet<string>();
            while (open.Count > 0)
            {
                var (c, id) = open.Min; open.Remove(open.Min);
                if (!done.Add(id)) continue;
                if (id == toLane) return Unwind(previous, fromLane, toLane);
                foreach (var n in Moves(index, index.Path(id)))
                {
                    if (done.Contains(n)) continue;
                    double nc = c + Cost(index.Path(n));
                    if (cost.TryGetValue(n, out var old))
                    {
                        if (nc >= old - 1e-9) continue;
                        open.Remove((old, n));
                    }
                    cost[n] = nc; previous[n] = id; open.Add((nc, n));
                }
            }
            return null;
        }

        static List<string> Unwind(Dictionary<string, string> previous, string fromLane, string toLane)
        {
            var way = new List<string> { toLane };
            for (var at = previous[toLane]; ; at = previous[at])
            {
                way.Add(at);
                if (at == fromLane) break;
                if (way.Count > 100000) throw new InvalidDataException("Transit routing loop");
            }
            way.Reverse();
            return way;
        }

        static IEnumerable<string> Moves(RoadGraphIndex index, PathInfo p) =>
            p.Next.Where(n => { var q = index.Path(n); return !q.IsConnection || q.Connection.maneuver != LaneManeuver.UTurn; })
                  .OrderBy(n => n, StringComparer.Ordinal);

        static double Cost(PathInfo p) => p.Length * (p.Lane != null && p.Lane.busLane ? BusLaneCost : 1.0);

        static void Require(bool ok, string message) { if (!ok) throw new InvalidDataException(message); }
    }
}
