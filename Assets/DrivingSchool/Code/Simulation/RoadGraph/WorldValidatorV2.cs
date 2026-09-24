using System;
using System.Collections.Generic;
using System.IO;
using DrivingSchool.Contracts;

namespace DrivingSchool.Simulation.RoadGraph
{
    /// <summary>
    /// Structural validation of <see cref="WorldDocumentV2"/> (T11, T27). Throws <see cref="InvalidDataException"/>
    /// naming the offending element id. Tolerances are geometric, not legal norms.
    /// </summary>
    public static class WorldValidatorV2
    {
        public const double JoinToleranceM = 0.05;

        /// <param name="knownSignCodes">Sign codes from the sign catalog (T28); null skips the code check.</param>
        public static void Validate(WorldDocumentV2 w, ICollection<string> knownSignCodes = null)
        {
            Require(w != null, "World is null");
            Require(w.schemaVersion == 2, "Unsupported world version: " + w.schemaVersion);
            Require(!string.IsNullOrWhiteSpace(w.id) && w.chunkSizeM == 256, "Invalid world header");
            Require(w.nodes != null && w.segments != null && w.lanes != null && w.junctions != null && w.connections != null
                && w.conflictZones != null && w.stopLines != null && w.crossings != null && w.signalGroups != null
                && w.signalPlans != null && w.signals != null && w.boundaries != null && w.signs != null
                && w.approaches != null && w.sidewalks != null && w.zones != null && w.spawnPoints != null
                && w.objects != null && w.districts != null, "Missing arrays");

            var ids = new HashSet<string>();
            void Unique(string id, string kind) => Require(!string.IsNullOrWhiteSpace(id) && ids.Add(id), "Duplicate/empty " + kind + " id: " + id);

            var nodes = new HashSet<string>();
            foreach (var n in w.nodes)
            {
                Require(n != null, "Null node"); Unique(n.id, "node"); nodes.Add(n.id);
                Require(Polyline.IsFinite(n.x) && Polyline.IsFinite(n.y) && Polyline.IsFinite(n.z), "Invalid node position: " + n.id);
            }
            var segments = new HashSet<string>();
            foreach (var s in w.segments)
            {
                Require(s != null, "Null segment"); Unique(s.id, "segment"); segments.Add(s.id);
                Require(nodes.Contains(s.fromNode) && nodes.Contains(s.toNode) && s.fromNode != s.toNode, "Broken segment: " + s.id);
                Require(Polyline.Distance2D(s.curve.p0, s.curve.p3) > 0.01, "Zero-length segment: " + s.id);
                Require(s.laneWidthM > 0 && s.speedLimitKph > 0 && s.lanesForward >= 0 && s.lanesBackward >= 0, "Invalid segment dimensions: " + s.id);
            }

            // Lanes and connections share one path namespace: agents route over both.
            var lanes = new Dictionary<string, LaneV2>();
            var paths = new Dictionary<string, Polyline>();
            foreach (var l in w.lanes)
            {
                Require(l != null, "Null lane"); Unique(l.id, "lane");
                Require(string.IsNullOrEmpty(l.segmentId) || segments.Contains(l.segmentId), "Lane on unknown segment: " + l.id);
                Require(l.index != 0 && l.widthM > 0 && l.speedLimitKph > 0 && l.successors != null, "Invalid lane dimensions: " + l.id);
                paths[l.id] = Line(l.centerline, "lane " + l.id);
                lanes[l.id] = l;
            }
            foreach (var l in w.lanes)
            {
                foreach (var next in l.successors)
                {
                    Require(next != null && lanes.ContainsKey(next), "Disconnected successor: " + l.id + " -> " + next);
                    Require(Near(paths[l.id].End, paths[next].Start), "Successor does not start at lane end: " + l.id + " -> " + next);
                }
                foreach (var n in new[] { l.leftNeighborId, l.rightNeighborId, l.oncomingLaneId })
                    Require(string.IsNullOrEmpty(n) || lanes.ContainsKey(n), "Unknown neighbour lane: " + l.id + " -> " + n);
            }

            var junctions = new Dictionary<string, Junction>();
            foreach (var j in w.junctions) { Require(j != null, "Null junction"); Unique(j.id, "junction"); junctions[j.id] = j; }
            var connections = new Dictionary<string, LaneConnection>();
            foreach (var c in w.connections)
            {
                Require(c != null, "Null connection"); Unique(c.id, "connection");
                Require(junctions.ContainsKey(c.junctionId), "Connection in unknown junction: " + c.id);
                Require(lanes.ContainsKey(c.fromLaneId) && lanes.ContainsKey(c.toLaneId), "Connection with unknown lane: " + c.id);
                Require(c.maneuver == LaneManeuver.Straight || c.maneuver == LaneManeuver.Left || c.maneuver == LaneManeuver.Right || c.maneuver == LaneManeuver.UTurn, "Connection needs exactly one maneuver: " + c.id);
                Require(c.speedLimitKph > 0, "Invalid connection speed: " + c.id);
                var line = Line(c.centerline, "connection " + c.id);
                Require(Near(paths[c.fromLaneId].End, line.Start), "Connection does not start at lane end: " + c.id);
                Require(Near(line.End, paths[c.toLaneId].Start), "Connection does not end at lane start: " + c.id);
                var allowed = lanes[c.fromLaneId].allowedManeuvers;
                Require(allowed == LaneManeuver.None || (allowed & c.maneuver) != 0, "Connection not allowed by lane maneuvers: " + c.id);
                paths[c.id] = line; connections[c.id] = c;
            }
            foreach (var j in w.junctions)
            {
                Require(j.connectionIds != null, "Missing connection list: " + j.id);
                foreach (var cid in j.connectionIds)
                    Require(connections.TryGetValue(cid, out var c) && c.junctionId == j.id, "Junction lists foreign connection: " + j.id + " -> " + cid);
            }
            foreach (var z in w.conflictZones)
            {
                Require(z != null, "Null conflict zone"); Unique(z.id, "conflict zone");
                Require(connections.ContainsKey(z.connectionA) && connections.ContainsKey(z.connectionB) && z.connectionA != z.connectionB, "Conflict zone with unknown connection: " + z.id);
                Require(InRange(z.fromSA, z.toSA, paths[z.connectionA].Length) && InRange(z.fromSB, z.toSB, paths[z.connectionB].Length), "Conflict zone range outside connection: " + z.id);
            }

            var stopLines = new HashSet<string>();
            foreach (var s in w.stopLines)
            {
                Require(s != null, "Null stop line"); Unique(s.id, "stop line"); stopLines.Add(s.id);
                Require(paths.ContainsKey(s.laneId) && s.s >= 0 && s.s <= paths[s.laneId].Length + JoinToleranceM, "Stop line outside lane: " + s.id);
            }

            var sidewalks = new HashSet<string>();
            foreach (var p in w.sidewalks) { Require(p != null, "Null sidewalk"); Unique(p.id, "sidewalk"); sidewalks.Add(p.id); Line(p.points, "sidewalk " + p.id); Require(p.widthM > 0, "Invalid sidewalk width: " + p.id); }
            var crossings = new HashSet<string>();
            foreach (var c in w.crossings)
            {
                Require(c != null, "Null crossing"); Unique(c.id, "crossing"); crossings.Add(c.id);
                Require(c.widthM > 0 && Polyline.Distance2D(c.a, c.b) > 0.5, "Invalid crossing geometry: " + c.id);
                Require(c.laneIds != null && c.laneIds.Length > 0, "Crossing crosses no lane: " + c.id);
                foreach (var lid in c.laneIds)
                {
                    Require(paths.ContainsKey(lid), "Crossing references unknown lane: " + c.id + " -> " + lid);
                    Require(Crosses(paths[lid], c.a, c.b), "Crossing does not cross lane: " + c.id + " -> " + lid);
                }
            }
            foreach (var p in w.sidewalks)
                foreach (var link in p.linkedIds)
                    Require(sidewalks.Contains(link) || crossings.Contains(link), "Sidewalk link unknown: " + p.id + " -> " + link);
            foreach (var c in w.crossings)
                foreach (var sid in c.sidewalkIds)
                    Require(sidewalks.Contains(sid), "Crossing references unknown sidewalk: " + c.id + " -> " + sid);

            var groups = new Dictionary<string, SignalGroup>();
            foreach (var g in w.signalGroups)
            {
                Require(g != null, "Null signal group"); Unique(g.id, "signal group"); groups[g.id] = g;
                Require(junctions.ContainsKey(g.junctionId), "Signal group in unknown junction: " + g.id);
                foreach (var cid in g.connectionIds) Require(connections.ContainsKey(cid), "Signal group controls unknown connection: " + g.id + " -> " + cid);
                foreach (var cid in g.crossingIds) Require(crossings.Contains(cid), "Signal group controls unknown crossing: " + g.id + " -> " + cid);
            }
            foreach (var c in w.connections) Require(string.IsNullOrEmpty(c.signalGroupId) || groups.ContainsKey(c.signalGroupId), "Connection with unknown signal group: " + c.id);
            foreach (var c in w.crossings) Require(string.IsNullOrEmpty(c.signalGroupId) || groups.ContainsKey(c.signalGroupId), "Crossing with unknown signal group: " + c.id);
            var plans = new HashSet<string>();
            foreach (var p in w.signalPlans)
            {
                Require(p != null, "Null signal plan"); Unique(p.id, "signal plan"); plans.Add(p.id);
                Require(junctions.ContainsKey(p.junctionId) && p.stages != null && p.stages.Length > 0, "Invalid signal plan: " + p.id);
                foreach (var st in p.stages)
                {
                    Require(st.greenSeconds > 0 && st.greenFlashSeconds >= 0 && st.greenFlashSeconds <= st.greenSeconds && st.amberSeconds >= 0 && st.allRedSeconds >= 0 && st.redAmberSeconds >= 0, "Invalid stage timing: " + p.id);
                    foreach (var gid in st.greenGroupIds) Require(groups.ContainsKey(gid), "Stage uses unknown group: " + p.id + " -> " + gid);
                }
            }
            foreach (var j in w.junctions) Require(string.IsNullOrEmpty(j.signalPlanId) || plans.Contains(j.signalPlanId), "Junction with unknown signal plan: " + j.id);
            foreach (var s in w.signals)
            {
                Require(s != null, "Null signal"); Unique(s.id, "signal");
                Require(groups.ContainsKey(s.signalGroupId) && !string.IsNullOrWhiteSpace(s.catalogId), "Signal head with unknown group/catalog: " + s.id);
            }

            // T27: markings, signs, priority, zones, spawn points.
            var boundarySpans = new Dictionary<string, List<(float from, float to)>>();
            foreach (var b in w.boundaries)
            {
                Require(b != null, "Null boundary"); Unique(b.id, "boundary");
                Require(lanes.ContainsKey(b.laneId), "Boundary on unknown lane: " + b.id);
                Require(InRange(b.fromS, b.toS, paths[b.laneId].Length) && b.toS > b.fromS, "Boundary outside lane: " + b.id);
                var key = b.laneId + "/" + b.side;
                if (!boundarySpans.TryGetValue(key, out var list)) boundarySpans[key] = list = new List<(float, float)>();
                foreach (var span in list) Require(b.toS <= span.from + 1e-3f || b.fromS >= span.to - 1e-3f, "Overlapping boundaries: " + b.id);
                list.Add((b.fromS, b.toS));
            }
            var signs = new Dictionary<string, SignPlacement>();
            foreach (var s in w.signs)
            {
                Require(s != null, "Null sign"); Unique(s.id, "sign"); signs[s.id] = s;
                Require(!string.IsNullOrWhiteSpace(s.code), "Sign without code: " + s.id);
                Require(knownSignCodes == null || knownSignCodes.Contains(s.code), "Unknown sign code: " + s.id + " (" + s.code + ")");
                Require(s.laneIds != null && s.laneIds.Length > 0, "Sign applies to no lane: " + s.id);
                foreach (var lid in s.laneIds) Require(paths.ContainsKey(lid), "Sign on unknown lane: " + s.id + " -> " + lid);
                Require(s.atS >= 0 && s.atS <= paths[s.laneIds[0]].Length + JoinToleranceM, "Sign position outside lane: " + s.id);
                Require(string.IsNullOrEmpty(s.zoneEndLaneId) || paths.ContainsKey(s.zoneEndLaneId), "Sign zone ends on unknown lane: " + s.id);
            }
            foreach (var a in w.approaches)
            {
                Require(a != null, "Null approach"); Unique(a.id, "approach");
                Require(junctions.ContainsKey(a.junctionId) && lanes.ContainsKey(a.laneId), "Approach with unknown junction/lane: " + a.id);
                Require(string.IsNullOrEmpty(a.stopLineId) || stopLines.Contains(a.stopLineId), "Approach with unknown stop line: " + a.id);
                foreach (var sid in a.sourceSignIds) Require(signs.ContainsKey(sid), "Approach with unknown sign: " + a.id + " -> " + sid);
                // Priority on a junction comes from signs (or signals), never from the data alone.
                if (a.priority == ApproachPriority.Main)
                    Require(HasSign(a, signs, c => c == "2.1" || c.StartsWith("2.3")), "Main approach without priority sign: " + a.id);
                if (a.priority == ApproachPriority.Secondary)
                    Require(HasSign(a, signs, c => c == "2.4" || c == "2.5"), "Secondary approach without yield sign: " + a.id);
                if (a.priority == ApproachPriority.Signalized)
                    Require(!string.IsNullOrEmpty(junctions[a.junctionId].signalPlanId), "Signalized approach at junction without plan: " + a.id);
            }
            foreach (var z in w.zones)
            {
                Require(z != null, "Null zone"); Unique(z.id, "zone");
                Require(paths.ContainsKey(z.laneId) && InRange(z.fromS, z.toS, paths[z.laneId].Length) && z.toS > z.fromS, "Zone outside lane: " + z.id);
            }
            foreach (var s in w.spawnPoints)
            {
                Require(s != null, "Null spawn point"); Unique(s.id, "spawn point");
                bool onLane = paths.ContainsKey(s.pathId), onWalk = sidewalks.Contains(s.pathId);
                Require(s.role == SpawnRole.Vehicle ? onLane : onWalk, "Spawn point on wrong path type: " + s.id);
                Require(s.s >= 0, "Spawn point before path start: " + s.id);
            }
        }

        static bool HasSign(JunctionApproach a, Dictionary<string, SignPlacement> signs, Func<string, bool> match)
        {
            foreach (var sid in a.sourceSignIds) if (match(signs[sid].code)) return true;
            return false;
        }

        static Polyline Line(Vec3d[] points, string what)
        {
            Require(points != null && points.Length >= 2, "Centerline needs two points: " + what);
            foreach (var p in points) Require(Polyline.IsFinite(p), "Non-finite point: " + what);
            var line = new Polyline(points);
            Require(line.Length > 0.01, "Zero-length path: " + what);
            return line;
        }

        static bool Near(Vec3d a, Vec3d b) => Polyline.Distance2D(a, b) <= JoinToleranceM;
        static bool InRange(float from, float to, double length) =>
            from >= -JoinToleranceM && to >= from && to <= length + JoinToleranceM;

        /// <summary>True if segment a-b intersects the polyline in the XZ plane.</summary>
        static bool Crosses(Polyline line, Vec3d a, Vec3d b)
        {
            var pts = line.Points;
            for (int i = 0; i < pts.Count - 1; i++)
                if (SegmentsIntersect(a, b, pts[i], pts[i + 1])) return true;
            return false;
        }

        static bool SegmentsIntersect(Vec3d p1, Vec3d p2, Vec3d q1, Vec3d q2)
        {
            double d1 = Cross(q1, q2, p1), d2 = Cross(q1, q2, p2), d3 = Cross(p1, p2, q1), d4 = Cross(p1, p2, q2);
            return ((d1 > 0) != (d2 > 0)) && ((d3 > 0) != (d4 > 0));
        }

        static double Cross(Vec3d a, Vec3d b, Vec3d c) => (b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x);

        static void Require(bool ok, string message) { if (!ok) throw new InvalidDataException(message); }
    }
}
