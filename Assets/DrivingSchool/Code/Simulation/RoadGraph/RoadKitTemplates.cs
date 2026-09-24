using System;
using System.Collections.Generic;
using System.Linq;
using DrivingSchool.Contracts;

namespace DrivingSchool.Simulation.RoadGraph
{
    /// <summary>A module socket: where neighbours attach. Lane ids are ordered from the axis outwards.</summary>
    public sealed class TemplateSocket
    {
        public string Name;
        public Vec3d Position;
        public float OutHeadingDeg;  // Unity yaw of the direction leaving the module through this socket
        public string[] OutLaneIds;  // lanes that leave the module here
        public string[] InLaneIds;   // lanes that enter the module here
        public float LaneWidthM;
        public string StopLineId, CrossingId; // junction arms only
    }

    /// <summary>Semantic template of one road kit module in prefab-local coordinates (T28).</summary>
    public sealed class ModuleTemplate
    {
        public string CatalogId, SourceSha256;
        public TemplateSocket[] Sockets;
        public WorldDocumentV2 Fragment; // local ids, local coordinates
        public bool IsJunction => Fragment.junctions.Length > 0;
        public TemplateSocket Socket(string name) =>
            Sockets.FirstOrDefault(s => s.Name == name) ?? throw new ArgumentException(CatalogId + " has no socket " + name);
    }

    public interface IModuleTemplateSource
    {
        bool TryGet(string catalogId, out ModuleTemplate template);
    }

    /// <summary>
    /// Templates for Road Kit v1. Every number is taken from tools/build_road_kit.py (source SHA below), axes as in
    /// docs/road-kit.md: Unity (x, z) = Blender (x, y). Update together with the Blender script; the test compares
    /// <see cref="SourceSha256"/> with Art/RoadKit/catalog.json and fails when the kit changes.
    /// </summary>
    public sealed class RoadKitTemplates : IModuleTemplateSource
    {
        public const string SourceSha256 = "e2403f0859318136f3528cc433475d91fa51ec6e84374541a3ff32fc69fd45ec";

        // straight(): axis dashes box(x=0, w=.12), edge lines box(x=±3.65, w=.12) -> lane between 0.06 and 3.59.
        public const double LaneOffsetM = 1.825;
        public const float LaneWidthM = 3.65f;
        // straight(): sidewalk box(x=±5.2, w=2).
        public const double SidewalkOffsetM = 5.2;
        public const float SidewalkWidthM = 2f;
        public const float StraightLengthM = 20f;
        // curve(): arc centre (14, 0), axis radius 14, sidewalks r 7.8..9.8 and 18.2..20.2.
        public const double CurveRadiusM = 14;
        // junction(): sockets at ±12, stop bars box(±1.9, ±10, 3.5, .4), crosswalks centred at ±7.5 over road ±4.
        public const double CrossSocketM = 12, CrossStopBarM = 10, CrossStopBarDepthM = 0.4, CrossCrosswalkM = 7.5, CrossRoadHalfWidthM = 4;
        public const float DefaultSpeedKph = 60;

        readonly Dictionary<string, ModuleTemplate> templates;

        public RoadKitTemplates()
        {
            templates = new[] { Straight(), Curve(), Cross() }.ToDictionary(t => t.CatalogId);
        }

        public IEnumerable<ModuleTemplate> All => templates.Values;

        /// <summary>Throws when the templates were measured on a different Road Kit source than the one in catalog.json.</summary>
        public static void RequireSource(string catalogSourceSha256)
        {
            if (!string.Equals(catalogSourceSha256, SourceSha256, StringComparison.OrdinalIgnoreCase))
                throw new System.IO.InvalidDataException("Road kit templates are stale: measured on " + SourceSha256 + ", catalog has " + catalogSourceSha256);
        }
        public bool TryGet(string catalogId, out ModuleTemplate template) => templates.TryGetValue(catalogId, out template);

        static ModuleTemplate Straight()
        {
            var axis = new[] { new Vec3d(0, 0, 0), new Vec3d(0, 0, StraightLengthM) };
            var f = new WorldDocumentV2();
            var fwd = Lane("f", 1, Polyline.OffsetRight(Resample(axis, 1.0), LaneOffsetM));
            var bwd = Lane("b", -1, Polyline.OffsetRight(Resample(Polyline.Reversed(axis), 1.0), LaneOffsetM));
            fwd.oncomingLaneId = "b"; bwd.oncomingLaneId = "f";
            f.lanes = new[] { fwd, bwd };
            f.boundaries = RoadBoundaries(fwd, MarkingType.Dashed).Concat(RoadBoundaries(bwd, MarkingType.Dashed)).ToArray();
            f.sidewalks = new[]
            {
                Walk("walk.L", Polyline.OffsetRight(axis, -SidewalkOffsetM)),
                Walk("walk.R", Polyline.OffsetRight(axis, SidewalkOffsetM)),
            };
            return new ModuleTemplate
            {
                CatalogId = "RK_Road_Urban_20m", SourceSha256 = SourceSha256, Fragment = f,
                Sockets = new[]
                {
                    Sock("Socket_Start", new Vec3d(0, 0, 0), 180, outLane: "b", inLane: "f"),
                    Sock("Socket_End", new Vec3d(0, 0, StraightLengthM), 0, outLane: "f", inLane: "b"),
                },
            };
        }

        static ModuleTemplate Curve()
        {
            // point(r, a) = (14 - r cos a, r sin a): a = 0 at the start socket (heading +Z), a = pi/2 at the end (heading +X).
            Vec3d[] Arc(double r, bool reverse)
            {
                const int n = 32;
                var pts = Enumerable.Range(0, n + 1).Select(i => i * Math.PI / 2 / n)
                    .Select(a => new Vec3d(CurveRadiusM - r * Math.Cos(a), 0, r * Math.Sin(a))).ToArray();
                return reverse ? Polyline.Reversed(pts) : pts;
            }
            var f = new WorldDocumentV2();
            var fwd = Lane("f", 1, Arc(CurveRadiusM - LaneOffsetM, false));
            var bwd = Lane("b", -1, Arc(CurveRadiusM + LaneOffsetM, true));
            fwd.oncomingLaneId = "b"; bwd.oncomingLaneId = "f";
            f.lanes = new[] { fwd, bwd };
            f.boundaries = RoadBoundaries(fwd, MarkingType.Dashed).Concat(RoadBoundaries(bwd, MarkingType.Dashed)).ToArray();
            f.sidewalks = new[]
            {
                Walk("walk.L", Arc(CurveRadiusM + SidewalkOffsetM, false)),
                Walk("walk.R", Arc(CurveRadiusM - SidewalkOffsetM, false)),
            };
            return new ModuleTemplate
            {
                CatalogId = "RK_Road_Curve90_R14", SourceSha256 = SourceSha256, Fragment = f,
                Sockets = new[]
                {
                    Sock("Socket_Start", new Vec3d(0, 0, 0), 180, outLane: "b", inLane: "f"),
                    Sock("Socket_End", new Vec3d(CurveRadiusM, 0, CurveRadiusM), 90, outLane: "f", inLane: "b"),
                },
            };
        }

        static ModuleTemplate Cross()
        {
            var arms = new (string name, double ux, double uz, float heading)[]
            {
                ("South", 0, -1, 180), ("North", 0, 1, 0), ("East", 1, 0, 90), ("West", -1, 0, 270),
            };
            var f = new WorldDocumentV2();
            var lanes = new List<LaneV2>(); var stops = new List<StopLine>(); var boundaries = new List<LaneBoundary>();
            var sockets = new List<TemplateSocket>();
            foreach (var arm in arms)
            {
                var outer = new Vec3d(arm.ux * CrossSocketM, 0, arm.uz * CrossSocketM);
                var inner = new Vec3d(arm.ux * CrossStopBarM, 0, arm.uz * CrossStopBarM);
                var inLane = Lane(arm.name + ".in", 1, Polyline.OffsetRight(new[] { outer, inner }, LaneOffsetM));
                var outLane = Lane(arm.name + ".out", -1, Polyline.OffsetRight(new[] { inner, outer }, LaneOffsetM));
                inLane.oncomingLaneId = outLane.id; outLane.oncomingLaneId = inLane.id;
                lanes.Add(inLane); lanes.Add(outLane);
                // Near edge of the stop bar as seen by the arriving car.
                stops.Add(new StopLine { id = "stop." + arm.name, laneId = inLane.id, s = (float)(CrossSocketM - CrossStopBarM - CrossStopBarDepthM / 2) });
                boundaries.AddRange(RoadBoundaries(inLane, MarkingType.Solid));
                boundaries.AddRange(RoadBoundaries(outLane, MarkingType.Solid));
                var socket = Sock("Socket_" + arm.name, outer, arm.heading, outLane: outLane.id, inLane: inLane.id);
                socket.StopLineId = "stop." + arm.name; socket.CrossingId = "crossing." + arm.name;
                sockets.Add(socket);
            }
            var connections = new List<LaneConnection>();
            foreach (var from in arms)
                foreach (var to in arms)
                    if (from.name != to.name)
                        connections.Add(WorldMigration.BuildConnection("j", lanes.Single(l => l.id == from.name + ".in"),
                            lanes.Single(l => l.id == to.name + ".out"), DefaultSpeedKph));
            var crossings = new List<PedestrianCrossing>();
            foreach (var arm in arms)
            {
                double cx = arm.ux * CrossCrosswalkM, cz = arm.uz * CrossCrosswalkM, px = arm.uz, pz = -arm.ux;
                var a = new Vec3d(cx + px * CrossRoadHalfWidthM, 0, cz + pz * CrossRoadHalfWidthM);
                var b = new Vec3d(cx - px * CrossRoadHalfWidthM, 0, cz - pz * CrossRoadHalfWidthM);
                crossings.Add(new PedestrianCrossing
                {
                    id = "crossing." + arm.name, a = a, b = b, widthM = 3,
                    laneIds = connections.Where(c => Crosses(c.centerline, a, b)).Select(c => c.id).ToArray(),
                });
            }
            f.nodes = new[] { new RoadNode { id = "node" } };
            f.lanes = lanes.ToArray();
            f.connections = connections.ToArray();
            f.junctions = new[] { new Junction { id = "j", nodeId = "node", connectionIds = connections.Select(c => c.id).ToArray() } };
            f.conflictZones = ConflictZoneBuilder.Build(connections).ToArray();
            f.stopLines = stops.ToArray();
            f.boundaries = boundaries.ToArray();
            f.crossings = crossings.ToArray();
            return new ModuleTemplate { CatalogId = "RK_Road_Cross_24m", SourceSha256 = SourceSha256, Fragment = f, Sockets = sockets.ToArray() };
        }

        static LaneV2 Lane(string id, int index, Vec3d[] centerline) =>
            new LaneV2 { id = id, index = index, widthM = LaneWidthM, speedLimitKph = DefaultSpeedKph, centerline = centerline };

        static IEnumerable<LaneBoundary> RoadBoundaries(LaneV2 lane, MarkingType axis)
        {
            float len = (float)new Polyline(lane.centerline).Length;
            yield return new LaneBoundary { id = "bl." + lane.id, laneId = lane.id, side = BoundarySide.Left, type = axis, fromS = 0, toS = len };
            yield return new LaneBoundary { id = "br." + lane.id, laneId = lane.id, side = BoundarySide.Right, type = MarkingType.Solid, fromS = 0, toS = len };
        }

        static SidewalkPath Walk(string id, Vec3d[] points) => new SidewalkPath { id = id, widthM = SidewalkWidthM, points = points };

        static TemplateSocket Sock(string name, Vec3d pos, float heading, string outLane, string inLane) => new TemplateSocket
        {
            Name = name, Position = pos, OutHeadingDeg = heading, OutLaneIds = new[] { outLane }, InLaneIds = new[] { inLane }, LaneWidthM = LaneWidthM,
        };

        static Vec3d[] Resample(Vec3d[] line, double step)
        {
            var p = new Polyline(line);
            int n = Math.Max(1, (int)Math.Ceiling(p.Length / step));
            return Enumerable.Range(0, n + 1).Select(i => p.PointAt(p.Length * i / n)).ToArray();
        }

        internal static bool Crosses(Vec3d[] line, Vec3d a, Vec3d b)
        {
            for (int i = 0; i < line.Length - 1; i++)
            {
                double d1 = Cross(line[i], line[i + 1], a), d2 = Cross(line[i], line[i + 1], b);
                double d3 = Cross(a, b, line[i]), d4 = Cross(a, b, line[i + 1]);
                if ((d1 > 0) != (d2 > 0) && (d3 > 0) != (d4 > 0)) return true;
            }
            return false;
        }

        static double Cross(Vec3d a, Vec3d b, Vec3d c) => (b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x);
    }
}
