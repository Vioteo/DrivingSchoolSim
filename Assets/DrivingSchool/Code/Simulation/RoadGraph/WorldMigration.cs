using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DrivingSchool.Contracts;

namespace DrivingSchool.Simulation.RoadGraph
{
    /// <summary>
    /// Deterministic v1 -> v2 migration (T11). v1 has no junction geometry: nodes with three or more segments become
    /// junctions whose lanes are trimmed by half the widest road width, and v1 successors across them become
    /// cubic <see cref="LaneConnection"/>s. The resulting junction geometry is schematic, the topology is exact.
    /// v1 carries no markings, signs or priority: those stay empty and must be authored in v2.
    /// </summary>
    public static class WorldMigration
    {
        public const double MaxSampleStepM = 2.0;

        public static bool CanMigrate(int sourceSchemaVersion) => sourceSchemaVersion == 1;

        public static WorldDocumentV2 MigrateV1ToV2(WorldDocument v1)
        {
            if (v1 == null) throw new ArgumentNullException(nameof(v1));
            if (!CanMigrate(v1.schemaVersion))
                throw new NotSupportedException("Unsupported world schema version: " + v1.schemaVersion);
            ValidateV1(v1);

            var nodes = v1.nodes.ToDictionary(n => n.id);
            var segments = v1.segments.ToDictionary(s => s.id);
            var degree = new Dictionary<string, int>();
            foreach (var s in v1.segments) { Inc(degree, s.fromNode); Inc(degree, s.toNode); }
            var junctionRadius = new Dictionary<string, double>();
            foreach (var s in v1.segments)
                foreach (var n in new[] { s.fromNode, s.toNode })
                    if (degree[n] >= 3)
                        junctionRadius[n] = Math.Max(junctionRadius.TryGetValue(n, out var r) ? r : 0, s.widthM / 2.0);

            var v2 = new WorldDocumentV2
            {
                id = v1.id, name = v1.name, chunkSizeM = v1.chunkSizeM, revision = "migrated-from-v1",
                nodes = v1.nodes.Select(n => new RoadNode { id = n.id, x = n.x, y = n.y, z = n.z }).ToArray(),
                objects = v1.objects.Select(o => new WorldObject { id = o.id, catalogId = o.catalogId, x = o.x, y = o.y, z = o.z, yawDeg = o.yawDeg }).ToArray(),
                districts = v1.districts.Select(d => new District { id = d.id, minX = d.minX, minZ = d.minZ, sizeM = d.sizeM }).ToArray(),
            };

            // Segments.
            var segmentsV2 = new List<RoadSegmentV2>();
            foreach (var s in v1.segments)
            {
                var a = ToVec(nodes[s.fromNode]); var b = ToVec(nodes[s.toNode]);
                var lanesOf = v1.lanes.Where(l => l.segmentId == s.id).ToList();
                segmentsV2.Add(new RoadSegmentV2
                {
                    id = s.id, fromNode = s.fromNode, toNode = s.toNode, speedLimitKph = s.speedLimitKph,
                    laneWidthM = lanesOf.Count > 0 ? lanesOf.Average(l => l.widthM) : s.widthM / s.laneCount,
                    lanesForward = lanesOf.Count(l => l.fromNode == s.fromNode),
                    lanesBackward = lanesOf.Count(l => l.fromNode != s.fromNode),
                    curve = new CubicCurve { p0 = a, p1 = Polyline.Lerp(a, b, 1.0 / 3), p2 = Polyline.Lerp(a, b, 2.0 / 3), p3 = b },
                });
            }
            v2.segments = segmentsV2.ToArray();

            // Lanes: rank inside each direction by v1 index -> |index| 1 is next to the axis.
            var lanes = new List<LaneV2>();
            var laneById = new Dictionary<string, LaneV2>();
            foreach (var s in v1.segments)
            {
                foreach (bool forward in new[] { true, false })
                {
                    var group = v1.lanes.Where(l => l.segmentId == s.id && (l.fromNode == s.fromNode) == forward)
                                        .OrderBy(l => l.index).ToList();
                    for (int rank = 0; rank < group.Count; rank++)
                    {
                        var l = group[rank];
                        var from = ToVec(nodes[l.fromNode]); var to = ToVec(nodes[l.toNode]);
                        double length = Polyline.Distance2D(from, to);
                        double trimStart = junctionRadius.TryGetValue(l.fromNode, out var r0) ? r0 : 0;
                        double trimEnd = junctionRadius.TryGetValue(l.toNode, out var r1) ? r1 : 0;
                        if (trimStart + trimEnd >= length - 1.0)
                            throw new InvalidDataException("Segment too short for its junctions: " + s.id);
                        var chord = new CubicCurve
                        {
                            p0 = Polyline.Lerp(from, to, trimStart / length),
                            p3 = Polyline.Lerp(from, to, 1 - trimEnd / length),
                        };
                        chord.p1 = Polyline.Lerp(chord.p0, chord.p3, 1.0 / 3); chord.p2 = Polyline.Lerp(chord.p0, chord.p3, 2.0 / 3);
                        double offset = (rank + 0.5) * l.widthM;
                        var lane = new LaneV2
                        {
                            id = l.id, segmentId = s.id, index = forward ? rank + 1 : -(rank + 1),
                            widthM = l.widthM, speedLimitKph = s.speedLimitKph,
                            centerline = Polyline.OffsetRight(Polyline.SampleCubic(chord, MaxSampleStepM), offset),
                        };
                        lanes.Add(lane); laneById[lane.id] = lane;
                    }
                }
            }
            // Neighbours inside a segment.
            foreach (var lane in lanes)
            {
                int sign = Math.Sign(lane.index), abs = Math.Abs(lane.index);
                lane.leftNeighborId = FindLane(lanes, lane.segmentId, sign * (abs - 1));
                lane.rightNeighborId = FindLane(lanes, lane.segmentId, sign * (abs + 1));
                lane.oncomingLaneId = abs == 1 ? FindLane(lanes, lane.segmentId, -sign) : null;
            }

            // Successors: plain continuation at degree-2 nodes, connections at junctions.
            var connections = new List<LaneConnection>();
            var junctions = new Dictionary<string, Junction>();
            var v1Lanes = v1.lanes.ToDictionary(l => l.id);
            foreach (var l in v1.lanes)
            {
                var lane = laneById[l.id];
                var direct = new List<string>();
                foreach (var nextId in l.successors)
                {
                    if (!junctionRadius.ContainsKey(l.toNode)) { direct.Add(nextId); continue; }
                    if (!junctions.TryGetValue(l.toNode, out var j))
                        junctions[l.toNode] = j = new Junction { id = "j:" + l.toNode, nodeId = l.toNode };
                    var next = laneById[nextId];
                    connections.Add(BuildConnection(j.id, lane, next, Math.Min(segments[l.segmentId].speedLimitKph, segments[v1Lanes[nextId].segmentId].speedLimitKph)));
                }
                lane.successors = direct.ToArray();
            }
            foreach (var j in junctions.Values)
                j.connectionIds = connections.Where(c => c.junctionId == j.id).Select(c => c.id).ToArray();

            v2.lanes = lanes.ToArray();
            v2.connections = connections.ToArray();
            v2.junctions = junctions.Values.OrderBy(j => j.id, StringComparer.Ordinal).ToArray();
            var zones = new List<ConflictZone>();
            foreach (var j in v2.junctions)
                zones.AddRange(ConflictZoneBuilder.Build(connections.Where(c => c.junctionId == j.id).ToList()));
            v2.conflictZones = zones.ToArray();
            return v2;
        }

        /// <summary>Connection from the end of one lane to the start of another, tangent-continuous at both ends.</summary>
        public static LaneConnection BuildConnection(string junctionId, LaneV2 from, LaneV2 to, float speedLimitKph)
        {
            var a = new Polyline(from.centerline); var b = new Polyline(to.centerline);
            var p0 = a.End; var p3 = b.Start;
            a.TangentAt(a.Length, out double t0x, out double t0z);
            b.TangentAt(0, out double t1x, out double t1z);
            double k = Math.Max(0.5, Polyline.Distance2D(p0, p3) / 3.0);
            var curve = new CubicCurve
            {
                p0 = p0, p3 = p3,
                p1 = new Vec3d(p0.x + t0x * k, p0.y, p0.z + t0z * k),
                p2 = new Vec3d(p3.x - t1x * k, p3.y, p3.z - t1z * k),
            };
            return new LaneConnection
            {
                id = "c:" + from.id + ">" + to.id, junctionId = junctionId, fromLaneId = from.id, toLaneId = to.id,
                maneuver = ClassifyTurn(Math.Atan2(t0x, t0z), Math.Atan2(t1x, t1z)),
                speedLimitKph = speedLimitKph,
                centerline = Polyline.SampleCubic(curve, 1.0),
            };
        }

        /// <summary>Heading in Unity yaw (0 = +Z, positive towards +X): a positive change is a right turn.</summary>
        public static LaneManeuver ClassifyTurn(double headingIn, double headingOut)
        {
            double delta = headingOut - headingIn;
            while (delta > Math.PI) delta -= 2 * Math.PI;
            while (delta < -Math.PI) delta += 2 * Math.PI;
            double deg = delta * 180 / Math.PI;
            if (Math.Abs(deg) <= 30) return LaneManeuver.Straight;
            if (Math.Abs(deg) >= 150) return LaneManeuver.UTurn;
            return deg > 0 ? LaneManeuver.Right : LaneManeuver.Left;
        }

        static void ValidateV1(WorldDocument w)
        {
            void Require(bool ok, string message) { if (!ok) throw new InvalidDataException(message); }
            Require(w.nodes != null && w.segments != null && w.lanes != null && w.objects != null && w.districts != null, "Missing arrays");
            var nodes = new Dictionary<string, RoadNode>();
            foreach (var n in w.nodes)
            {
                Require(n != null && !string.IsNullOrWhiteSpace(n.id) && !nodes.ContainsKey(n.id), "Duplicate/empty node");
                Require(Polyline.IsFinite(n.x) && Polyline.IsFinite(n.y) && Polyline.IsFinite(n.z), "Invalid node position: " + n.id);
                nodes.Add(n.id, n);
            }
            var segments = new Dictionary<string, RoadSegment>();
            foreach (var s in w.segments)
            {
                Require(s != null && !string.IsNullOrWhiteSpace(s.id) && !segments.ContainsKey(s.id), "Duplicate segment");
                Require(nodes.ContainsKey(s.fromNode) && nodes.ContainsKey(s.toNode) && s.fromNode != s.toNode, "Broken segment: " + s.id);
                Require(Polyline.Distance2D(ToVec(nodes[s.fromNode]), ToVec(nodes[s.toNode])) > 0.01, "Zero-length segment: " + s.id);
                Require(s.widthM > 0 && s.laneCount > 0 && s.speedLimitKph > 0, "Invalid dimensions: " + s.id);
                segments.Add(s.id, s);
            }
            var lanes = new Dictionary<string, Lane>();
            foreach (var l in w.lanes)
            {
                Require(l != null && !string.IsNullOrWhiteSpace(l.id) && !lanes.ContainsKey(l.id) && segments.ContainsKey(l.segmentId), "Invalid lane");
                var s = segments[l.segmentId];
                Require((l.fromNode == s.fromNode && l.toNode == s.toNode) || (l.fromNode == s.toNode && l.toNode == s.fromNode), "Lane ends outside segment: " + l.id);
                Require(l.widthM > 0 && l.successors != null, "Invalid lane width: " + l.id);
                lanes.Add(l.id, l);
            }
            foreach (var l in w.lanes)
                foreach (var next in l.successors)
                    Require(next != null && lanes.ContainsKey(next) && lanes[next].fromNode == l.toNode, "Disconnected successor: " + l.id + " -> " + next);
        }

        static string FindLane(List<LaneV2> lanes, string segmentId, int index)
        {
            if (index == 0) return null;
            foreach (var l in lanes) if (l.segmentId == segmentId && l.index == index) return l.id;
            return null;
        }

        static void Inc(Dictionary<string, int> d, string k) => d[k] = d.TryGetValue(k, out var v) ? v + 1 : 1;
        static Vec3d ToVec(RoadNode n) => new Vec3d(n.x, n.y, n.z);
    }
}
