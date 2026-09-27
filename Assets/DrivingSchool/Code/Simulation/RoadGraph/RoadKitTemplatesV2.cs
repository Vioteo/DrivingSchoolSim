using System;
using System.Collections.Generic;
using System.Linq;
using DrivingSchool.Contracts;

namespace DrivingSchool.Simulation.RoadGraph
{
    /// <summary>
    /// Semantic templates of Road Kit v2 (T55): 2+2 roads, 2+2 junctions, a roundabout, a level crossing.
    /// Every number comes from tools/build_road_kit_v2.py (Unity (x, z) = Blender (x, y)); change both together.
    /// Lane ids are ordered from the axis outwards ("1" inner, "2" outer) and so are socket lane lists.
    /// Multi-lane approaches: the inner lane goes straight or left, the outer straight or right (no U-turns);
    /// a turn ends in the nearest lane of the same side (ПДД РФ 8.5/8.6 — редакцию сверить).
    /// </summary>
    public sealed class RoadKitTemplatesV2 : IModuleTemplateSource
    {
        public const string Straight4 = "RK2_Road_Urban4_20m", LaneChange4 = "RK2_Road_Urban4_LaneChange_40m", Cross4x4 = "RK2_Cross_4x4_32m", Cross4x2 = "RK2_Cross_4x2_24x32m",
            Roundabout = "RK2_Roundabout_52m", RailCrossing = "RK2_Road_RailCrossing_20m";
        /// <summary>sourceSha256 of Art/RoadKitV2/catalog-v2.json (tools/build_road_kit_v2.py); the test fails when the kit changes.</summary>
        public const string SourceSha256 = "86de34ccbd10cfb8b8a4d0fa50c46f6ef72b5bac8789d1e4bba650d1523301e9";
        public const float Lane4WidthM = 3.5f;
        public const double InnerLaneM = 2.0, OuterLaneM = 5.5, Sidewalk4M = 9.2;
        public const double RingRadiusM = 11, RingGapDeg = 35, RoundaboutSocketM = 26, RoundaboutGiveWayM = 15.4, RoundaboutCrosswalkM = 20;
        public const float RingSpeedKph = 30, ArmSpeedKph = 40;
        const float Speed = RoadKitTemplates.DefaultSpeedKph;
        const double Lane2M = RoadKitTemplates.LaneOffsetM;

        readonly Dictionary<string, ModuleTemplate> templates;

        public RoadKitTemplatesV2()
        {
            templates = new[] { MakeStraight4(), MakeLaneChange4(), MakeCross4x4(), MakeCross4x2(), MakeRoundabout(), MakeRailCrossing() }.ToDictionary(t => t.CatalogId);
        }

        public IEnumerable<ModuleTemplate> All => templates.Values;
        public bool TryGet(string catalogId, out ModuleTemplate template) => templates.TryGetValue(catalogId, out template);

        // ------------------------------------------------------------------ 2+2 straight

        static ModuleTemplate MakeStraight4()
        {
            var axis = new[] { new Vec3d(0, 0, 0), new Vec3d(0, 0, 20) };
            var back = Polyline.Reversed(axis);
            var f1 = Lane("f1", 1, Polyline.OffsetRight(Resample(axis), InnerLaneM), Lane4WidthM);
            var f2 = Lane("f2", 2, Polyline.OffsetRight(Resample(axis), OuterLaneM), Lane4WidthM);
            var b1 = Lane("b1", -1, Polyline.OffsetRight(Resample(back), InnerLaneM), Lane4WidthM);
            var b2 = Lane("b2", -2, Polyline.OffsetRight(Resample(back), OuterLaneM), Lane4WidthM);
            Pair(f1, f2); Pair(b1, b2); f1.oncomingLaneId = "b1"; b1.oncomingLaneId = "f1";
            var f = new WorldDocumentV2
            {
                lanes = new[] { f1, f2, b1, b2 },
                boundaries = Bounds(f1, MarkingType.DoubleSolid, MarkingType.Dashed).Concat(Bounds(f2, MarkingType.Dashed, MarkingType.Solid))
                    .Concat(Bounds(b1, MarkingType.DoubleSolid, MarkingType.Dashed)).Concat(Bounds(b2, MarkingType.Dashed, MarkingType.Solid)).ToArray(),
                sidewalks = new[] { Walk("walk.L", Polyline.OffsetRight(axis, -Sidewalk4M), 3), Walk("walk.R", Polyline.OffsetRight(axis, Sidewalk4M), 3) },
            };
            return new ModuleTemplate
            {
                CatalogId = Straight4, SourceSha256 = SourceSha256, Fragment = f,
                Sockets = new[]
                {
                    Sock("Socket_Start", new Vec3d(0, 0, 0), 180, new[] { "b1", "b2" }, new[] { "f1", "f2" }, Lane4WidthM),
                    Sock("Socket_End", new Vec3d(0, 0, 20), 0, new[] { "f1", "f2" }, new[] { "b1", "b2" }, Lane4WidthM),
                },
            };
        }

        /// <summary>
        /// Semantic-only module, drawn as two <see cref="Straight4"/> meshes: 6 m of lane, a 28 m stretch where a car may
        /// move to the neighbouring lane (ПДД РФ 8.4: the one changing lanes yields — here the conflict zones and
        /// reservations keep them apart), 6 m of lane. Lane changes are ordinary connections of a small junction.
        /// </summary>
        static ModuleTemplate MakeLaneChange4()
        {
            const double L = 40, A = 6;
            var f = new WorldDocumentV2();
            var lanes = new List<LaneV2>(); var connections = new List<LaneConnection>(); var bounds = new List<LaneBoundary>();
            foreach (var (dir, sign) in new[] { ("f", 1), ("b", -1) })
            {
                Vec3d P(double z) => new Vec3d(0, 0, sign > 0 ? z : L - z);
                var before = new List<LaneV2>(); var after = new List<LaneV2>();
                for (int i = 0; i < 2; i++)
                {
                    double o = i == 0 ? InnerLaneM : OuterLaneM;
                    var a = Lane(dir + (i + 1) + "a", sign * (i + 1), Polyline.OffsetRight(new[] { P(0), P(A) }, o), Lane4WidthM);
                    var b = Lane(dir + (i + 1) + "b", sign * (i + 1), Polyline.OffsetRight(new[] { P(L - A), P(L) }, o), Lane4WidthM);
                    before.Add(a); after.Add(b); lanes.Add(a); lanes.Add(b);
                    bounds.AddRange(Bounds(a, i == 0 ? MarkingType.DoubleSolid : MarkingType.Dashed, i == 0 ? MarkingType.Dashed : MarkingType.Solid));
                    bounds.AddRange(Bounds(b, i == 0 ? MarkingType.DoubleSolid : MarkingType.Dashed, i == 0 ? MarkingType.Dashed : MarkingType.Solid));
                }
                Pair(before[0], before[1]); Pair(after[0], after[1]);
                foreach (var from in before) foreach (var to in after)
                    connections.Add(WorldMigration.BuildConnection("lc", from, to, Speed));
            }
            foreach (var x in new[] { "1a", "1b" }) { lanes.First(l => l.id == "f" + x).oncomingLaneId = "b" + (x == "1a" ? "1b" : "1a"); lanes.First(l => l.id == "b" + x).oncomingLaneId = "f" + (x == "1a" ? "1b" : "1a"); }
            var axis = new[] { new Vec3d(0, 0, 0), new Vec3d(0, 0, L) };
            f.nodes = new[] { new RoadNode { id = "node", z = L / 2 } };
            f.lanes = lanes.ToArray(); f.connections = connections.ToArray(); f.boundaries = bounds.ToArray();
            f.junctions = new[] { new Junction { id = "lc", nodeId = "node", connectionIds = connections.Select(c => c.id).ToArray() } };
            f.conflictZones = ConflictZoneBuilder.Build(connections).ToArray();
            f.sidewalks = new[] { Walk("walk.L", Polyline.OffsetRight(axis, -Sidewalk4M), 3), Walk("walk.R", Polyline.OffsetRight(axis, Sidewalk4M), 3) };
            return new ModuleTemplate
            {
                CatalogId = LaneChange4, SourceSha256 = SourceSha256, Fragment = f,
                Sockets = new[]
                {
                    Sock("Socket_Start", new Vec3d(0, 0, 0), 180, new[] { "b1b", "b2b" }, new[] { "f1a", "f2a" }, Lane4WidthM),
                    Sock("Socket_End", new Vec3d(0, 0, L), 0, new[] { "f1b", "f2b" }, new[] { "b1a", "b2a" }, Lane4WidthM),
                },
            };
        }

        /// <summary>Meshes that draw a semantic module (catalog id, local z offset): the lane-change stretch is two straights.</summary>
        public static IEnumerable<(string prefab, double z)> Meshes(string catalogId) =>
            catalogId == LaneChange4 ? new[] { (Straight4, 0.0), (Straight4, 20.0) } : new[] { (catalogId, 0.0) };

        /// <summary>Sideways shift of a connection (right positive): a lane change is a straight connection shifted by about a lane.</summary>
        public static double LateralShift(Vec3d[] line)
        {
            var p = new Polyline(line);
            p.TangentAt(0, out double tx, out double tz);
            double dx = p.End.x - p.Start.x, dz = p.End.z - p.Start.z;
            return dx * tz - dz * tx;
        }

        // ------------------------------------------------------------------ junctions
        // Kerb corners are rounded (R 8 / 7.5 m in build_road_kit_v2.py) so a right turn from the outer lane stays on the
        // carriageway; crosswalk ends (CrossHalf) lie 0.35–0.4 m behind the kerb face where it crosses the crosswalk.

        sealed class Arm
        {
            public string Name; public double Ux, Uz; public float Heading; public double Socket, Inner, StopS;
            public double[] Offsets; public float Width; public double Crosswalk, CrossHalf;
            public List<LaneV2> In = new List<LaneV2>(), Out = new List<LaneV2>();
        }

        static readonly (string name, double ux, double uz, float heading)[] Compass =
            { ("South", 0, -1, 180), ("North", 0, 1, 0), ("East", 1, 0, 90), ("West", -1, 0, 270) };

        static ModuleTemplate MakeCross4x4() => Junction(Cross4x4, Compass.Select(c => new Arm
        {
            Name = c.name, Ux = c.ux, Uz = c.uz, Heading = c.heading, Socket = 16, Inner = 14, StopS = 1.5,
            Offsets = new[] { InnerLaneM, OuterLaneM }, Width = Lane4WidthM, Crosswalk = 11.75, CrossHalf = 8.9,
        }).ToList());

        static ModuleTemplate MakeCross4x2() => Junction(Cross4x2, Compass.Select(c =>
        {
            bool main = c.ux != 0;   // main road along X
            return main
                ? new Arm { Name = c.name, Ux = c.ux, Uz = c.uz, Heading = c.heading, Socket = 12, Inner = 10, StopS = 1.6, Offsets = new[] { InnerLaneM, OuterLaneM }, Width = Lane4WidthM, Crosswalk = 7.75, CrossHalf = 8.95 }
                : new Arm { Name = c.name, Ux = c.ux, Uz = c.uz, Heading = c.heading, Socket = 16, Inner = 13.3, StopS = 2.3, Offsets = new[] { Lane2M }, Width = RoadKitTemplates.LaneWidthM, Crosswalk = 11, CrossHalf = 5.6 };
        }).ToList());

        /// <summary>A four-arm junction: in/out lanes per arm, connections by lane discipline, crossings, stop lines.</summary>
        static ModuleTemplate Junction(string id, List<Arm> arms)
        {
            var f = new WorldDocumentV2();
            var lanes = new List<LaneV2>(); var stops = new List<StopLine>(); var bounds = new List<LaneBoundary>(); var sockets = new List<TemplateSocket>();
            foreach (var a in arms)
            {
                var outer = new Vec3d(a.Ux * a.Socket, 0, a.Uz * a.Socket); var inner = new Vec3d(a.Ux * a.Inner, 0, a.Uz * a.Inner);
                for (int i = 0; i < a.Offsets.Length; i++)
                {
                    var inL = Lane(a.Name + ".in" + (i + 1), i + 1, Polyline.OffsetRight(new[] { outer, inner }, a.Offsets[i]), a.Width);
                    var outL = Lane(a.Name + ".out" + (i + 1), -(i + 1), Polyline.OffsetRight(new[] { inner, outer }, a.Offsets[i]), a.Width);
                    if (a.Offsets.Length == 2) inL.allowedManeuvers = i == 0 ? LaneManeuver.Straight | LaneManeuver.Left : LaneManeuver.Straight | LaneManeuver.Right;
                    a.In.Add(inL); a.Out.Add(outL); lanes.Add(inL); lanes.Add(outL);
                    stops.Add(new StopLine { id = "stop." + a.Name + "." + (i + 1), laneId = inL.id, s = (float)a.StopS });
                    bounds.AddRange(Bounds(inL, i == 0 ? MarkingType.DoubleSolid : MarkingType.Solid, MarkingType.Solid));
                    bounds.AddRange(Bounds(outL, i == 0 ? MarkingType.DoubleSolid : MarkingType.Solid, MarkingType.Solid));
                }
                if (a.In.Count == 2) { Pair(a.In[0], a.In[1]); Pair(a.Out[0], a.Out[1]); }
                a.In[0].oncomingLaneId = a.Out[0].id; a.Out[0].oncomingLaneId = a.In[0].id;
                var sock = Sock("Socket_" + a.Name, outer, a.Heading, a.Out.Select(l => l.id).ToArray(), a.In.Select(l => l.id).ToArray(), a.Width);
                sock.StopLineId = "stop." + a.Name + "." + a.In.Count; sock.CrossingId = "crossing." + a.Name;
                sockets.Add(sock);
            }
            var connections = new List<LaneConnection>();
            foreach (var from in arms)
                foreach (var to in arms)
                {
                    if (from == to) continue;
                    var m = WorldMigration.ClassifyTurn(Math.Atan2(-from.Ux, -from.Uz), Math.Atan2(to.Ux, to.Uz));
                    if (m == LaneManeuver.UTurn) continue;
                    foreach (var (inL, outL) in Pairs(from, to, m))
                        connections.Add(WorldMigration.BuildConnection("j", inL, outL, Speed));
                }
            var crossings = new List<PedestrianCrossing>();
            foreach (var a in arms)
            {
                double cx = a.Ux * a.Crosswalk, cz = a.Uz * a.Crosswalk, px = a.Uz, pz = -a.Ux;
                var pa = new Vec3d(cx + px * a.CrossHalf, 0, cz + pz * a.CrossHalf);
                var pb = new Vec3d(cx - px * a.CrossHalf, 0, cz - pz * a.CrossHalf);
                crossings.Add(new PedestrianCrossing
                {
                    id = "crossing." + a.Name, a = pa, b = pb, widthM = 3,
                    laneIds = connections.Where(c => RoadKitTemplates.Crosses(c.centerline, pa, pb)).Select(c => c.id).ToArray(),
                });
            }
            f.nodes = new[] { new RoadNode { id = "node" } };
            f.lanes = lanes.ToArray(); f.connections = connections.ToArray(); f.stopLines = stops.ToArray(); f.boundaries = bounds.ToArray(); f.crossings = crossings.ToArray();
            f.junctions = new[] { new Junction { id = "j", nodeId = "node", connectionIds = connections.Select(c => c.id).ToArray() } };
            f.conflictZones = ConflictZoneBuilder.Build(connections).ToArray();
            return new ModuleTemplate { CatalogId = id, SourceSha256 = SourceSha256, Fragment = f, Sockets = sockets.ToArray() };
        }

        /// <summary>Lane pairs for a movement: same index for straight; a turn uses the lane on its side and ends in the nearest one.</summary>
        static IEnumerable<(LaneV2, LaneV2)> Pairs(Arm from, Arm to, LaneManeuver m)
        {
            int nIn = from.In.Count, nOut = to.Out.Count;
            if (m == LaneManeuver.Straight)
            {
                for (int i = 0; i < Math.Min(nIn, nOut); i++) yield return (from.In[i], to.Out[i]);
                if (nIn > nOut) for (int i = nOut; i < nIn; i++) yield return (from.In[i], to.Out[nOut - 1]);
                if (nOut > nIn) yield return (from.In[nIn - 1], to.Out[nOut - 1]);   // 1+1 into 2+2: keep right
            }
            else if (m == LaneManeuver.Right) yield return (from.In[nIn - 1], to.Out[nOut - 1]);
            else if (m == LaneManeuver.Left) yield return (from.In[0], to.Out[0]);
        }

        // ------------------------------------------------------------------ roundabout

        static ModuleTemplate MakeRoundabout()
        {
            var f = new WorldDocumentV2();
            // Arms in counter-clockwise order (right-hand traffic goes round anticlockwise): E, N, W, S; theta = math angle of the arm.
            var arms = new[] { ("East", 0.0, 90f), ("North", 90.0, 0f), ("West", 180.0, 270f), ("South", 270.0, 180f) };
            Vec3d OnRing(double deg) => new Vec3d(RingRadiusM * Math.Cos(deg * Math.PI / 180), 0, RingRadiusM * Math.Sin(deg * Math.PI / 180));
            Vec3d[] Arc(double from, double to) { int n = Math.Max(2, (int)Math.Ceiling((to - from) / 2)); return Enumerable.Range(0, n + 1).Select(i => OnRing(from + (to - from) * i / n)).ToArray(); }
            var lanes = new List<LaneV2>(); var ring = new LaneV2[4]; var inL = new LaneV2[4]; var outL = new LaneV2[4];
            var stops = new List<StopLine>(); var sockets = new List<TemplateSocket>(); var crossings = new List<PedestrianCrossing>(); var walks = new List<SidewalkPath>();
            for (int k = 0; k < 4; k++)
            {
                var (name, theta, heading) = arms[k];
                double ux = Math.Cos(theta * Math.PI / 180), uz = Math.Sin(theta * Math.PI / 180);
                var outer = new Vec3d(ux * RoundaboutSocketM, 0, uz * RoundaboutSocketM); var inner = new Vec3d(ux * 15.0, 0, uz * 15.0);
                inL[k] = Lane(name + ".in", 1, Polyline.OffsetRight(Resample(new[] { outer, inner }), Lane2M), RoadKitTemplates.LaneWidthM, ArmSpeedKph);
                outL[k] = Lane(name + ".out", -1, Polyline.OffsetRight(Resample(new[] { inner, outer }), Lane2M), RoadKitTemplates.LaneWidthM, ArmSpeedKph);
                inL[k].oncomingLaneId = outL[k].id; outL[k].oncomingLaneId = inL[k].id;
                ring[k] = Lane("ring." + name, 1, Arc(theta + RingGapDeg, theta + 90 - RingGapDeg), 5.5f, RingSpeedKph);
                lanes.Add(inL[k]); lanes.Add(outL[k]); lanes.Add(ring[k]);
                stops.Add(new StopLine { id = "giveway." + name, laneId = inL[k].id, s = (float)(RoundaboutSocketM - RoundaboutGiveWayM) });
                var sock = Sock("Socket_" + name, outer, heading, new[] { outL[k].id }, new[] { inL[k].id }, RoadKitTemplates.LaneWidthM);
                sock.StopLineId = "giveway." + name; sock.CrossingId = "crossing." + name;
                sockets.Add(sock);
                double px = uz, pz = -ux;   // right of the outward direction
                var ca = new Vec3d(ux * RoundaboutCrosswalkM + px * 4, 0, uz * RoundaboutCrosswalkM + pz * 4);
                var cb = new Vec3d(ux * RoundaboutCrosswalkM - px * 4, 0, uz * RoundaboutCrosswalkM - pz * 4);
                crossings.Add(new PedestrianCrossing { id = "crossing." + name, a = ca, b = cb, widthM = 3, laneIds = new[] { inL[k].id, outL[k].id } });
                foreach (var side in new[] { 1, -1 })
                    walks.Add(Walk("walk." + name + (side > 0 ? ".R" : ".L"), new[] { new Vec3d(ux * RoundaboutSocketM + px * 5.2 * side, 0, uz * RoundaboutSocketM + pz * 5.2 * side), new Vec3d(ux * 14.6 + px * 5.2 * side, 0, uz * 14.6 + pz * 5.2 * side) }, 2));
                var ringWalk = Enumerable.Range(0, 13).Select(i => theta + 20 + 50.0 * i / 12).Select(d => new Vec3d(15.2 * Math.Cos(d * Math.PI / 180), 0, 15.2 * Math.Sin(d * Math.PI / 180))).ToArray();
                walks.Add(Walk("walk.ring." + name, ringWalk, 2));
            }
            var connections = new List<LaneConnection>(); var junctions = new List<Junction>(); var approaches = new List<JunctionApproach>();
            for (int k = 0; k < 4; k++)
            {
                var (name, theta, _) = arms[k];
                var prev = ring[(k + 3) % 4];
                var jid = "j." + name;
                var through = new LaneConnection
                {
                    id = "c:" + prev.id + ">" + ring[k].id, junctionId = jid, fromLaneId = prev.id, toLaneId = ring[k].id,
                    maneuver = LaneManeuver.Straight, speedLimitKph = RingSpeedKph, centerline = Arc(theta - RingGapDeg, theta + RingGapDeg),
                };
                var exit = WorldMigration.BuildConnection(jid, prev, outL[k], RingSpeedKph);
                var entry = WorldMigration.BuildConnection(jid, inL[k], ring[k], RingSpeedKph);
                var mine = new List<LaneConnection> { through, exit, entry };
                connections.AddRange(mine);
                junctions.Add(new Junction { id = jid, nodeId = "node." + name, connectionIds = mine.Select(c => c.id).ToArray() });
                // Those on the ring go first; entering traffic gives way (signs 4.3 + 2.4 in the layout).
                approaches.Add(new JunctionApproach { id = "approach.ring." + name, junctionId = jid, laneId = prev.id, priority = ApproachPriority.Main });
                approaches.Add(new JunctionApproach { id = "approach." + name, junctionId = jid, laneId = inL[k].id, stopLineId = "giveway." + name, priority = ApproachPriority.Secondary });
            }
            f.nodes = arms.Select(a => new RoadNode { id = "node." + a.Item1, x = RingRadiusM * Math.Cos(a.Item2 * Math.PI / 180), z = RingRadiusM * Math.Sin(a.Item2 * Math.PI / 180) }).ToArray();
            f.lanes = lanes.ToArray(); f.connections = connections.ToArray(); f.junctions = junctions.ToArray(); f.stopLines = stops.ToArray();
            f.crossings = crossings.ToArray(); f.sidewalks = walks.ToArray(); f.approaches = approaches.ToArray();
            f.conflictZones = junctions.SelectMany(j => ConflictZoneBuilder.Build(connections.Where(c => c.junctionId == j.id).ToList())).ToArray();
            f.boundaries = lanes.SelectMany(l => Bounds(l, l.id.StartsWith("ring") ? MarkingType.None : MarkingType.Solid, MarkingType.Solid)).ToArray();
            return new ModuleTemplate { CatalogId = Roundabout, SourceSha256 = SourceSha256, Fragment = f, Sockets = sockets.ToArray() };
        }

        // ------------------------------------------------------------------ level crossing

        static ModuleTemplate MakeRailCrossing()
        {
            var f = new WorldDocumentV2();
            double o = Lane2M;
            var fIn = Lane("f.in", 1, new[] { new Vec3d(o, 0, 0), new Vec3d(o, 0, 6) }, RoadKitTemplates.LaneWidthM);
            var fOut = Lane("f.out", 1, new[] { new Vec3d(o, 0, 14), new Vec3d(o, 0, 20) }, RoadKitTemplates.LaneWidthM);
            var bIn = Lane("b.in", -1, new[] { new Vec3d(-o, 0, 20), new Vec3d(-o, 0, 14) }, RoadKitTemplates.LaneWidthM);
            var bOut = Lane("b.out", -1, new[] { new Vec3d(-o, 0, 6), new Vec3d(-o, 0, 0) }, RoadKitTemplates.LaneWidthM);
            fIn.oncomingLaneId = bOut.id; bOut.oncomingLaneId = fIn.id; fOut.oncomingLaneId = bIn.id; bIn.oncomingLaneId = fOut.id;
            var cf = WorldMigration.BuildConnection("rail", fIn, fOut, Speed); cf.signalGroupId = "rail.f";
            var cb = WorldMigration.BuildConnection("rail", bIn, bOut, Speed); cb.signalGroupId = "rail.b";
            var walks = new List<SidewalkPath>(); var crossings = new List<PedestrianCrossing>();
            foreach (var (side, x) in new[] { ("L", -RoadKitTemplates.SidewalkOffsetM), ("R", RoadKitTemplates.SidewalkOffsetM) })
            {
                walks.Add(Walk("walk." + side + "1", new[] { new Vec3d(x, 0, 0), new Vec3d(x, 0, 7.5) }, 2));
                walks.Add(Walk("walk." + side + "2", new[] { new Vec3d(x, 0, 12.5), new Vec3d(x, 0, 20) }, 2));
                // The sidewalk over the tracks: pedestrians wait while the crossing is closed (signal group rail.ped).
                crossings.Add(new PedestrianCrossing { id = "rail.walk." + side, a = new Vec3d(x, 0, 7.5), b = new Vec3d(x, 0, 12.5), widthM = 2, signalGroupId = "rail.ped", sidewalkIds = new[] { "walk." + side + "1", "walk." + side + "2" } });
            }
            f.nodes = new[] { new RoadNode { id = "node", z = 10 } };
            f.lanes = new[] { fIn, fOut, bIn, bOut }; f.connections = new[] { cf, cb };
            f.junctions = new[] { new Junction { id = "rail", nodeId = "node", connectionIds = new[] { cf.id, cb.id } } };
            f.conflictZones = ConflictZoneBuilder.Build(f.connections).ToArray();
            f.stopLines = new[] { new StopLine { id = "stop.f", laneId = fIn.id, s = 3.4f }, new StopLine { id = "stop.b", laneId = bIn.id, s = 3.4f } };
            f.signalGroups = new[]
            {
                new SignalGroup { id = "rail.f", junctionId = "rail", kind = SignalGroupKind.Vehicle, connectionIds = new[] { cf.id } },
                new SignalGroup { id = "rail.b", junctionId = "rail", kind = SignalGroupKind.Vehicle, connectionIds = new[] { cb.id } },
                new SignalGroup { id = "rail.ped", junctionId = "rail", kind = SignalGroupKind.Pedestrian, crossingIds = crossings.Select(c => c.id).ToArray() },
            };
            f.sidewalks = walks.ToArray(); f.crossings = crossings.ToArray();
            f.boundaries = new[] { fIn, fOut, bIn, bOut }.SelectMany(l => Bounds(l, MarkingType.Solid, MarkingType.Solid)).ToArray();
            var start = Sock("Socket_Start", new Vec3d(0, 0, 0), 180, new[] { bOut.id }, new[] { fIn.id }, RoadKitTemplates.LaneWidthM); start.StopLineId = "stop.f";
            var end = Sock("Socket_End", new Vec3d(0, 0, 20), 0, new[] { fOut.id }, new[] { bIn.id }, RoadKitTemplates.LaneWidthM); end.StopLineId = "stop.b";
            return new ModuleTemplate { CatalogId = RailCrossing, SourceSha256 = SourceSha256, Fragment = f, Sockets = new[] { start, end } };
        }

        // ------------------------------------------------------------------ helpers

        static LaneV2 Lane(string id, int index, Vec3d[] centerline, float width, float speed = Speed) =>
            new LaneV2 { id = id, index = index, widthM = width, speedLimitKph = speed, centerline = centerline };

        static void Pair(LaneV2 inner, LaneV2 outer) { inner.rightNeighborId = outer.id; outer.leftNeighborId = inner.id; }

        static IEnumerable<LaneBoundary> Bounds(LaneV2 lane, MarkingType left, MarkingType right)
        {
            float len = (float)new Polyline(lane.centerline).Length;
            yield return new LaneBoundary { id = "bl." + lane.id, laneId = lane.id, side = BoundarySide.Left, type = left, fromS = 0, toS = len };
            yield return new LaneBoundary { id = "br." + lane.id, laneId = lane.id, side = BoundarySide.Right, type = right, fromS = 0, toS = len };
        }

        static SidewalkPath Walk(string id, Vec3d[] points, float width) => new SidewalkPath { id = id, widthM = width, points = points };

        static TemplateSocket Sock(string name, Vec3d pos, float heading, string[] outLanes, string[] inLanes, float width) => new TemplateSocket
        {
            Name = name, Position = pos, OutHeadingDeg = heading, OutLaneIds = outLanes, InLaneIds = inLanes, LaneWidthM = width,
        };

        static Vec3d[] Resample(Vec3d[] line)
        {
            var p = new Polyline(line);
            int n = Math.Max(1, (int)Math.Ceiling(p.Length));
            return Enumerable.Range(0, n + 1).Select(i => p.PointAt(p.Length * i / n)).ToArray();
        }
    }

    /// <summary>All road kits, v1 and v2 (T55): the compiler takes a module from whichever kit has it.</summary>
    public sealed class RoadKitCatalog : IModuleTemplateSource
    {
        readonly IModuleTemplateSource[] kits;
        public RoadKitCatalog() : this(new RoadKitTemplates(), new RoadKitTemplatesV2()) { }
        public RoadKitCatalog(params IModuleTemplateSource[] kits) { this.kits = kits; }
        public bool TryGet(string catalogId, out ModuleTemplate template)
        {
            foreach (var k in kits) if (k.TryGet(catalogId, out template)) return true;
            template = null; return false;
        }
    }
}
