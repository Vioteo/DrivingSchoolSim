using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DrivingSchool.Contracts;

namespace DrivingSchool.Simulation.RoadGraph
{
    /// <summary>
    /// Compiles a <see cref="DistrictLayout"/> into one <see cref="WorldDocumentV2"/> (T29, ADR-011).
    /// Deterministic: the same layout gives the same graph (ids are "instanceId/localId", order follows the layout).
    /// Modules are never moved to close a gap: a socket mismatch is an error that reports the gap.
    /// </summary>
    public static class DistrictCompiler
    {
        public const double JoinPositionToleranceM = 0.01;
        public const double JoinHeadingToleranceDeg = 0.1;
        public const double SidewalkLinkToleranceM = 0.3;
        // Road Kit v1: lane centre 1.825 m from the axis, sidewalk centre 5.2 m -> signs stand 3.4 m right of the lane centre.
        public const double SignLateralOffsetM = 3.4;

        public static WorldDocumentV2 Compile(DistrictLayout layout, IModuleTemplateSource templates)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            Require(layout.schemaVersion == 1, "Unsupported layout version: " + layout.schemaVersion);
            Require(!string.IsNullOrWhiteSpace(layout.id), "Layout without id");
            Require(layout.instances != null && layout.instances.Length > 0, "Layout without instances");

            var world = new WorldDocumentV2 { id = layout.id, name = layout.name, revision = layout.revision };
            var acc = new Accumulator();
            var instances = new Dictionary<string, (ModuleInstance inst, ModuleTemplate tpl)>();
            foreach (var inst in layout.instances)
            {
                Require(inst != null && !string.IsNullOrWhiteSpace(inst.id) && !inst.id.Contains("/") && !instances.ContainsKey(inst.id), "Duplicate/invalid instance id: " + inst?.id);
                Require(templates.TryGet(inst.catalogId, out var tpl), "No semantic template for " + inst.catalogId + " (instance " + inst.id + ")");
                instances.Add(inst.id, (inst, tpl));
                AddInstance(acc, inst, tpl);
            }

            // Joins: sockets must meet exactly and carry the same profile.
            var used = new HashSet<string>();
            foreach (var j in layout.joins)
            {
                var (a, sa) = Resolve(instances, j.instanceA, j.socketA);
                var (b, sb) = Resolve(instances, j.instanceB, j.socketB);
                Require(used.Add(j.instanceA + ":" + j.socketA), "Socket joined twice: " + j.instanceA + ":" + j.socketA);
                Require(used.Add(j.instanceB + ":" + j.socketB), "Socket joined twice: " + j.instanceB + ":" + j.socketB);
                double gap = Polyline.Distance2D(SocketPosition(a, sa), SocketPosition(b, sb));
                Require(gap <= JoinPositionToleranceM, string.Format(CultureInfo.InvariantCulture,
                    "Sockets do not meet: {0}:{1} and {2}:{3}, gap {4:F2} m", j.instanceA, j.socketA, j.instanceB, j.socketB, gap));
                double turn = Math.Abs(NormalizeDeg(SocketHeading(a, sa) - SocketHeading(b, sb) - 180));
                Require(turn <= JoinHeadingToleranceDeg, string.Format(CultureInfo.InvariantCulture,
                    "Sockets are not opposite: {0}:{1} and {2}:{3}, off by {4:F2} deg", j.instanceA, j.socketA, j.instanceB, j.socketB, turn));
                Require(sa.OutLaneIds.Length == sb.InLaneIds.Length && sa.InLaneIds.Length == sb.OutLaneIds.Length && Math.Abs(sa.LaneWidthM - sb.LaneWidthM) < 0.01f,
                    "Socket profiles differ: " + j.instanceA + ":" + j.socketA + " and " + j.instanceB + ":" + j.socketB);
                for (int i = 0; i < sa.OutLaneIds.Length; i++) acc.Successor(P(a, sa.OutLaneIds[i]), P(b, sb.InLaneIds[i]));
                for (int i = 0; i < sb.OutLaneIds.Length; i++) acc.Successor(P(b, sb.OutLaneIds[i]), P(a, sa.InLaneIds[i]));
            }
            foreach (var o in layout.openSockets)
            {
                var (inst, s) = Resolve(instances, o.instanceId, o.socket);
                Require(used.Add(o.instanceId + ":" + o.socket), "Open socket is also joined: " + o.instanceId + ":" + o.socket);
                foreach (var lane in s.InLaneIds)
                    acc.Spawns.Add(new SpawnPoint { id = "spawn/" + inst.id + "/" + lane, pathId = P(inst, lane), role = SpawnRole.Vehicle, edge = SpawnEdge.DistrictEdge, s = 0.5f });
            }
            foreach (var (inst, tpl) in instances.Values)
                foreach (var s in tpl.Sockets)
                    Require(used.Contains(inst.id + ":" + s.Name), "Dangling socket: " + inst.id + ":" + s.Name + " (join it or list it in openSockets)");

            LinkSidewalks(acc);
            AddSigns(acc, layout, instances);
            AddApproachesAndSignals(acc, layout, instances);

            world.nodes = acc.Nodes.ToArray();
            world.lanes = acc.Lanes.ToArray();
            world.junctions = acc.Junctions.ToArray();
            world.connections = acc.Connections.ToArray();
            world.conflictZones = acc.Zones.ToArray();
            world.stopLines = acc.StopLines.ToArray();
            world.crossings = acc.Crossings.ToArray();
            world.boundaries = acc.Boundaries.ToArray();
            world.sidewalks = acc.Sidewalks.ToArray();
            world.signs = acc.Signs.ToArray();
            world.approaches = acc.Approaches.ToArray();
            world.signalGroups = acc.Groups.ToArray();
            world.signalPlans = acc.Plans.ToArray();
            world.signals = acc.Heads.ToArray();
            world.spawnPoints = acc.Spawns.ToArray();
            WorldValidatorV2.Validate(world, SignCatalog.Codes);
            return world;
        }

        /// <summary>Pose for a new module so that its socket meets (opposite) a socket of an already placed module.</summary>
        public static ModuleInstance Dock(string id, ModuleTemplate newTemplate, string newSocket, ModuleInstance placed, ModuleTemplate placedTemplate, string placedSocket)
        {
            var target = placedTemplate.Socket(placedSocket);
            var own = newTemplate.Socket(newSocket);
            float yaw = (float)NormalizeDeg(SocketHeading(placed, target) + 180 - own.OutHeadingDeg);
            var at = SocketPosition(placed, target);
            var offset = Rotate(own.Position, yaw);
            return new ModuleInstance { id = id, catalogId = newTemplate.CatalogId, x = at.x - offset.x, y = at.y - offset.y, z = at.z - offset.z, yawDeg = yaw };
        }

        public static Vec3d SocketPosition(ModuleInstance inst, TemplateSocket s) => ToWorld(inst, s.Position);
        public static double SocketHeading(ModuleInstance inst, TemplateSocket s) => NormalizeDeg(s.OutHeadingDeg + inst.yawDeg);

        public static Vec3d ToWorld(ModuleInstance inst, Vec3d local)
        {
            var r = Rotate(local, inst.yawDeg);
            return new Vec3d(r.x + inst.x, r.y + inst.y, r.z + inst.z);
        }

        /// <summary>Unity yaw rotation about +Y: yaw 90 turns +Z into +X.</summary>
        public static Vec3d Rotate(Vec3d p, double yawDeg)
        {
            double a = yawDeg * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
            return new Vec3d(p.x * c + p.z * s, p.y, -p.x * s + p.z * c);
        }

        static void AddInstance(Accumulator acc, ModuleInstance inst, ModuleTemplate tpl)
        {
            var f = tpl.Fragment;
            float speed(float local) => inst.speedLimitKph > 0 ? inst.speedLimitKph : local;
            Vec3d[] W(Vec3d[] pts) => pts.Select(p => ToWorld(inst, p)).ToArray();
            foreach (var n in f.nodes)
                acc.Nodes.Add(new RoadNode { id = P(inst, n.id), x = inst.x, y = inst.y, z = inst.z });
            foreach (var l in f.lanes)
                acc.Lanes.Add(new LaneV2
                {
                    id = P(inst, l.id), segmentId = null, index = l.index, widthM = l.widthM, speedLimitKph = speed(l.speedLimitKph),
                    centerline = W(l.centerline), successors = l.successors.Select(x => P(inst, x)).ToArray(),
                    leftNeighborId = P(inst, l.leftNeighborId), rightNeighborId = P(inst, l.rightNeighborId), oncomingLaneId = P(inst, l.oncomingLaneId),
                    allowedManeuvers = l.allowedManeuvers,
                });
            foreach (var j in f.junctions)
                acc.Junctions.Add(new Junction { id = P(inst, j.id), nodeId = P(inst, j.nodeId), connectionIds = j.connectionIds.Select(x => P(inst, x)).ToArray() });
            foreach (var c in f.connections)
                acc.Connections.Add(new LaneConnection
                {
                    id = P(inst, c.id), junctionId = P(inst, c.junctionId), fromLaneId = P(inst, c.fromLaneId), toLaneId = P(inst, c.toLaneId),
                    maneuver = c.maneuver, speedLimitKph = speed(c.speedLimitKph), centerline = W(c.centerline),
                });
            foreach (var z in f.conflictZones)
                acc.Zones.Add(new ConflictZone
                {
                    id = P(inst, z.id), junctionId = P(inst, z.junctionId), connectionA = P(inst, z.connectionA), connectionB = P(inst, z.connectionB),
                    fromSA = z.fromSA, toSA = z.toSA, fromSB = z.fromSB, toSB = z.toSB, merge = z.merge,
                });
            foreach (var s in f.stopLines) acc.StopLines.Add(new StopLine { id = P(inst, s.id), laneId = P(inst, s.laneId), s = s.s });
            foreach (var c in f.crossings)
                acc.Crossings.Add(new PedestrianCrossing
                {
                    id = P(inst, c.id), a = ToWorld(inst, c.a), b = ToWorld(inst, c.b), widthM = c.widthM,
                    laneIds = c.laneIds.Select(x => P(inst, x)).ToArray(), sidewalkIds = c.sidewalkIds.Select(x => P(inst, x)).ToArray(),
                });
            foreach (var b in f.boundaries)
                acc.Boundaries.Add(new LaneBoundary { id = P(inst, b.id), laneId = P(inst, b.laneId), side = b.side, type = b.type, fromS = b.fromS, toS = b.toS });
            foreach (var w in f.sidewalks)
                acc.Sidewalks.Add(new SidewalkPath { id = P(inst, w.id), widthM = w.widthM, points = W(w.points), linkedIds = w.linkedIds.Select(x => P(inst, x)).ToArray() });
        }

        static void LinkSidewalks(Accumulator acc)
        {
            var links = acc.Sidewalks.ToDictionary(s => s.id, s => new List<string>(s.linkedIds));
            for (int i = 0; i < acc.Sidewalks.Count; i++)
            for (int j = i + 1; j < acc.Sidewalks.Count; j++)
            {
                var a = acc.Sidewalks[i]; var b = acc.Sidewalks[j];
                if (Owner(a.id) == Owner(b.id)) continue;
                var ends = new[] { a.points[0], a.points[a.points.Length - 1] };
                var others = new[] { b.points[0], b.points[b.points.Length - 1] };
                if (ends.Any(e => others.Any(o => Polyline.Distance2D(e, o) <= SidewalkLinkToleranceM)))
                {
                    links[a.id].Add(b.id); links[b.id].Add(a.id);
                }
            }
            foreach (var s in acc.Sidewalks)
            {
                s.linkedIds = links[s.id].ToArray();
                // A sidewalk end that touches nothing is where pedestrians appear and leave.
                var start = s.points[0]; var end = s.points[s.points.Length - 1];
                bool startFree = !acc.Sidewalks.Any(o => o != s && Owner(o.id) != Owner(s.id) && Touches(o, start));
                bool endFree = !acc.Sidewalks.Any(o => o != s && Owner(o.id) != Owner(s.id) && Touches(o, end));
                if (startFree) acc.Spawns.Add(new SpawnPoint { id = "spawn/" + s.id + "/start", pathId = s.id, role = SpawnRole.Pedestrian, edge = SpawnEdge.DistrictEdge, s = 0.2f });
                if (endFree) acc.Spawns.Add(new SpawnPoint { id = "spawn/" + s.id + "/end", pathId = s.id, role = SpawnRole.Pedestrian, edge = SpawnEdge.DistrictEdge, s = (float)new Polyline(s.points).Length - 0.2f });
            }
        }

        static bool Touches(SidewalkPath p, Vec3d point) =>
            Polyline.Distance2D(p.points[0], point) <= SidewalkLinkToleranceM || Polyline.Distance2D(p.points[p.points.Length - 1], point) <= SidewalkLinkToleranceM;

        static void AddSigns(Accumulator acc, DistrictLayout layout, Dictionary<string, (ModuleInstance inst, ModuleTemplate tpl)> instances)
        {
            var lanes = acc.Lanes.ToDictionary(l => l.id);
            var ids = new HashSet<string>();
            foreach (var s in layout.signs)
            {
                Require(s != null && !string.IsNullOrWhiteSpace(s.id) && ids.Add(s.id), "Duplicate/empty layout sign id: " + s?.id);
                Require(instances.ContainsKey(s.instanceId), "Sign on unknown instance: " + s.id);
                Require(SignCatalog.TryGet(s.code, out var entry), "Unknown sign code: " + s.id + " (" + s.code + ")");
                Require(!entry.NeedsValue || !string.IsNullOrWhiteSpace(s.value), "Sign needs a value: " + s.id);
                var prefab = SignCatalog.PrefabFor(s.code, s.value);
                Require(prefab != null, "No prefab for sign " + s.id + " (" + s.code + " " + s.value + ")");
                var inst = instances[s.instanceId].inst;
                var laneId = P(inst, s.laneId);
                Require(lanes.ContainsKey(laneId), "Sign on unknown lane: " + s.id + " -> " + s.laneId);
                var line = new Polyline(lanes[laneId].centerline);
                Require(s.atS >= 0 && s.atS <= line.Length, "Sign position outside lane: " + s.id);
                var pos = line.OffsetPoint(s.atS, SignLateralOffsetM);
                var placement = new SignPlacement
                {
                    id = s.id, code = s.code, value = s.value, catalogId = prefab, plaques = s.plaques ?? Array.Empty<string>(),
                    x = pos.x, y = pos.y, z = pos.z, yawDeg = (float)NormalizeDeg(line.HeadingAt(s.atS) * 180 / Math.PI + 180),
                    laneIds = new[] { laneId }, atS = s.atS, untilNextJunction = s.untilNextJunction,
                };
                if (s.untilNextJunction)
                {
                    // Follow plain continuations to the lane that ends at a junction (no direct successor).
                    var current = lanes[laneId];
                    for (int guard = 0; current.successors.Length == 1 && guard < 10000; guard++) current = lanes[current.successors[0]];
                    Require(current.successors.Length == 0, "Sign zone branches before a junction: " + s.id);
                    var stop = acc.StopLines.FirstOrDefault(x => x.laneId == current.id);
                    placement.zoneEndLaneId = current.id;
                    placement.zoneEndS = stop != null ? stop.s : (float)new Polyline(current.centerline).Length;
                }
                acc.Signs.Add(placement);
            }
        }

        static void AddApproachesAndSignals(Accumulator acc, DistrictLayout layout, Dictionary<string, (ModuleInstance inst, ModuleTemplate tpl)> instances)
        {
            var plans = new Dictionary<string, LayoutSignalPlan>();
            foreach (var p in layout.signalPlans)
            {
                Require(instances.TryGetValue(p.instanceId, out var x) && x.tpl.IsJunction, "Signal plan on non-junction instance: " + p.instanceId);
                Require(plans.TryAdd(p.instanceId, p), "Two signal plans for " + p.instanceId);
            }
            var given = new Dictionary<string, LayoutApproach>();
            foreach (var a in layout.approaches)
            {
                Require(instances.TryGetValue(a.instanceId, out var x) && x.tpl.IsJunction, "Approach on non-junction instance: " + a.instanceId);
                x.tpl.Socket(a.socket);
                Require(given.TryAdd(a.instanceId + ":" + a.socket, a), "Approach given twice: " + a.instanceId + ":" + a.socket);
            }
            foreach (var (inst, tpl) in instances.Values)
            {
                if (!tpl.IsJunction) continue;
                var junctionId = P(inst, tpl.Fragment.junctions[0].id);
                plans.TryGetValue(inst.id, out var plan);
                foreach (var s in tpl.Sockets)
                {
                    given.TryGetValue(inst.id + ":" + s.Name, out var a);
                    var priority = plan != null ? ApproachPriority.Signalized : a?.priority ?? ApproachPriority.Equal;
                    Require(plan == null || a == null || a.priority == ApproachPriority.Signalized, "Approach priority conflicts with signal plan: " + inst.id + ":" + s.Name);
                    acc.Approaches.Add(new JunctionApproach
                    {
                        id = "approach/" + inst.id + "/" + s.Name, junctionId = junctionId, laneId = P(inst, s.InLaneIds[0]),
                        stopLineId = P(inst, s.StopLineId), priority = priority, sourceSignIds = a?.signIds ?? Array.Empty<string>(),
                    });
                }
                if (plan != null) AddSignalPlan(acc, inst, tpl, junctionId, plan);
            }
        }

        static void AddSignalPlan(Accumulator acc, ModuleInstance inst, ModuleTemplate tpl, string junctionId, LayoutSignalPlan plan)
        {
            Require(plan.stages.Length > 0, "Signal plan without stages: " + inst.id);
            var connections = acc.Connections.Where(c => c.junctionId == junctionId).ToList();
            var green = plan.stages.SelectMany(st => st.greenSockets).Distinct().ToList();
            foreach (var s in tpl.Sockets)
                Require(green.Contains(s.Name), "Signal plan never gives green to " + inst.id + ":" + s.Name);
            foreach (var s in tpl.Sockets)
            {
                var gid = P(inst, "sg." + s.Name);
                var inLane = P(inst, s.InLaneIds[0]);
                var mine = connections.Where(c => c.fromLaneId == inLane).ToList();
                foreach (var c in mine) c.signalGroupId = gid;
                acc.Groups.Add(new SignalGroup { id = gid, junctionId = junctionId, kind = SignalGroupKind.Vehicle, connectionIds = mine.Select(c => c.id).ToArray() });
                var lane = new Polyline(acc.Lanes.First(l => l.id == inLane).centerline);
                var stop = acc.StopLines.First(x => x.id == P(inst, s.StopLineId));
                var head = lane.OffsetPoint(stop.s, SignLateralOffsetM);
                acc.Heads.Add(new TrafficSignalAttachment
                {
                    id = P(inst, "head." + s.Name), signalGroupId = gid, catalogId = "DS_Signal_Vehicle",
                    x = head.x, y = head.y, z = head.z, yawDeg = (float)NormalizeDeg(lane.HeadingAt(stop.s) * 180 / Math.PI + 180),
                });
            }
            foreach (var walk in plan.stages.SelectMany(st => st.walkSockets).Distinct())
            {
                var s = tpl.Socket(walk);
                Require(!string.IsNullOrEmpty(s.CrossingId), "Socket has no crossing: " + inst.id + ":" + walk);
                var gid = P(inst, "pg." + walk);
                var crossing = acc.Crossings.First(c => c.id == P(inst, s.CrossingId));
                crossing.signalGroupId = gid;
                acc.Groups.Add(new SignalGroup { id = gid, junctionId = junctionId, kind = SignalGroupKind.Pedestrian, crossingIds = new[] { crossing.id } });
                // Heads stand at both ends, each facing the pedestrians waiting at the other end.
                foreach (var (at, from, tag) in new[] { (crossing.b, crossing.a, "b"), (crossing.a, crossing.b, "a") })
                {
                    Polyline.Direction(at, from, out double tx, out double tz);
                    acc.Heads.Add(new TrafficSignalAttachment
                    {
                        id = P(inst, "phead." + walk + "." + tag), signalGroupId = gid, catalogId = "DS_Signal_Pedestrian",
                        x = at.x, y = at.y, z = at.z, yawDeg = (float)NormalizeDeg(Math.Atan2(tx, tz) * 180 / Math.PI),
                    });
                }
            }
            string G(string socket) => P(inst, "sg." + socket);
            string W(string socket) => P(inst, "pg." + socket);
            acc.Plans.Add(new SignalPlan
            {
                id = P(inst, "plan"), junctionId = junctionId, offsetSeconds = plan.offsetSeconds,
                stages = plan.stages.Select(st => new SignalStage
                {
                    greenGroupIds = st.greenSockets.Select(G).Concat(st.walkSockets.Select(W)).ToArray(),
                    greenSeconds = st.greenSeconds, greenFlashSeconds = st.greenFlashSeconds, amberSeconds = st.amberSeconds,
                    allRedSeconds = st.allRedSeconds, redAmberSeconds = st.redAmberSeconds,
                }).ToArray(),
            });
            acc.Junctions.First(j => j.id == junctionId).signalPlanId = P(inst, "plan");
        }

        static (ModuleInstance, TemplateSocket) Resolve(Dictionary<string, (ModuleInstance inst, ModuleTemplate tpl)> instances, string id, string socket)
        {
            Require(id != null && instances.ContainsKey(id), "Unknown instance: " + id);
            var (inst, tpl) = instances[id];
            var s = tpl.Sockets.FirstOrDefault(x => x.Name == socket);
            Require(s != null, "Unknown socket: " + id + ":" + socket);
            return (inst, s);
        }

        static string P(ModuleInstance inst, string localId) => string.IsNullOrEmpty(localId) ? null : inst.id + "/" + localId;
        static string Owner(string id) => id.Substring(0, id.IndexOf('/'));

        public static double NormalizeDeg(double deg)
        {
            deg %= 360;
            if (deg > 180) deg -= 360;
            if (deg <= -180) deg += 360;
            return deg;
        }

        static void Require(bool ok, string message) { if (!ok) throw new InvalidDataException(message); }

        sealed class Accumulator
        {
            public readonly List<RoadNode> Nodes = new List<RoadNode>();
            public readonly List<LaneV2> Lanes = new List<LaneV2>();
            public readonly List<Junction> Junctions = new List<Junction>();
            public readonly List<LaneConnection> Connections = new List<LaneConnection>();
            public readonly List<ConflictZone> Zones = new List<ConflictZone>();
            public readonly List<StopLine> StopLines = new List<StopLine>();
            public readonly List<PedestrianCrossing> Crossings = new List<PedestrianCrossing>();
            public readonly List<LaneBoundary> Boundaries = new List<LaneBoundary>();
            public readonly List<SidewalkPath> Sidewalks = new List<SidewalkPath>();
            public readonly List<SignPlacement> Signs = new List<SignPlacement>();
            public readonly List<JunctionApproach> Approaches = new List<JunctionApproach>();
            public readonly List<SignalGroup> Groups = new List<SignalGroup>();
            public readonly List<SignalPlan> Plans = new List<SignalPlan>();
            public readonly List<TrafficSignalAttachment> Heads = new List<TrafficSignalAttachment>();
            public readonly List<SpawnPoint> Spawns = new List<SpawnPoint>();

            public void Successor(string from, string to)
            {
                var lane = Lanes.First(l => l.id == from);
                lane.successors = lane.successors.Concat(new[] { to }).ToArray();
            }
        }
    }
}
