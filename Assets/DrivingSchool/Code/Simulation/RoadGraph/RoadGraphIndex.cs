using System;
using System.Collections.Generic;
using System.Linq;
using DrivingSchool.Contracts;

namespace DrivingSchool.Simulation.RoadGraph
{
    /// <summary>One drivable path: a lane or a junction connection.</summary>
    public sealed class PathInfo
    {
        public string Id;
        public Polyline Line;
        public LaneV2 Lane;             // set for lanes
        public LaneConnection Connection; // set for connections
        public float WidthM, SpeedLimitKph;
        public string[] Next = Array.Empty<string>();     // lanes: successors and connections leaving it; connections: target lane
        public string[] Previous = Array.Empty<string>();
        public bool IsConnection => Connection != null;
        public double Length => Line.Length;
    }

    /// <summary>A stretch of one path covered by a sign's zone.</summary>
    public struct SignCoverage { public string SignId, PathId; public double FromS, ToS; }

    /// <summary>
    /// Immutable runtime view of a compiled graph: paths by id, adjacency, a uniform grid over path geometry and
    /// sign coverage. Built once per graph revision; shared by the locator, the director and the agents.
    /// </summary>
    public sealed class RoadGraphIndex
    {
        public const double CellSizeM = 16;
        public readonly WorldDocumentV2 World;
        readonly Dictionary<string, PathInfo> paths = new Dictionary<string, PathInfo>();
        readonly Dictionary<(int, int), List<string>> grid = new Dictionary<(int, int), List<string>>();
        readonly Dictionary<string, List<SignCoverage>> coverageByPath = new Dictionary<string, List<SignCoverage>>();
        readonly Dictionary<string, StopLine> stopLineByLane = new Dictionary<string, StopLine>();
        readonly Dictionary<string, List<ConflictZone>> zonesByConnection = new Dictionary<string, List<ConflictZone>>();
        readonly Dictionary<string, List<PedestrianCrossing>> crossingsByPath = new Dictionary<string, List<PedestrianCrossing>>();
        readonly Dictionary<string, List<SignPlacement>> signsByPath = new Dictionary<string, List<SignPlacement>>();
        readonly HashSet<string> intersections = new HashSet<string>();

        public RoadGraphIndex(WorldDocumentV2 world)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
            foreach (var l in world.lanes)
                paths[l.id] = new PathInfo { Id = l.id, Line = new Polyline(l.centerline), Lane = l, WidthM = l.widthM, SpeedLimitKph = l.speedLimitKph };
            foreach (var c in world.connections)
            {
                var from = world.lanes.First(l => l.id == c.fromLaneId);
                paths[c.id] = new PathInfo { Id = c.id, Line = new Polyline(c.centerline), Connection = c, WidthM = from.widthM, SpeedLimitKph = c.speedLimitKph };
            }
            var next = paths.Keys.ToDictionary(k => k, k => new List<string>());
            var prev = paths.Keys.ToDictionary(k => k, k => new List<string>());
            foreach (var l in world.lanes) foreach (var s in l.successors) { next[l.id].Add(s); prev[s].Add(l.id); }
            foreach (var c in world.connections)
            {
                next[c.fromLaneId].Add(c.id); prev[c.id].Add(c.fromLaneId);
                next[c.id].Add(c.toLaneId); prev[c.toLaneId].Add(c.id);
            }
            foreach (var p in paths.Values) { p.Next = next[p.Id].ToArray(); p.Previous = prev[p.Id].ToArray(); }

            foreach (var p in paths.Values)
            {
                var pts = p.Line.Points;
                for (int i = 0; i < pts.Count - 1; i++) AddToGrid(p.Id, pts[i], pts[i + 1], p.WidthM / 2 + 1);
            }
            foreach (var s in world.stopLines) stopLineByLane[s.laneId] = s;
            foreach (var z in world.conflictZones)
            {
                Add(zonesByConnection, z.connectionA, z);
                Add(zonesByConnection, z.connectionB, z);
            }
            foreach (var c in world.crossings) foreach (var id in c.laneIds) Add(crossingsByPath, id, c);
            // An intersection has turns; a lane-change stretch or a level crossing only goes straight on (T65).
            foreach (var c in world.connections) if (c.maneuver != LaneManeuver.Straight) intersections.Add(c.junctionId ?? "");
            foreach (var sign in world.signs) foreach (var id in sign.laneIds) if (paths.ContainsKey(id)) Add(signsByPath, id, sign);
            foreach (var sign in world.signs) BuildCoverage(sign);
        }

        public IEnumerable<PathInfo> Paths => paths.Values;
        public PathInfo Path(string id) => paths.TryGetValue(id, out var p) ? p : throw new KeyNotFoundException("Unknown path " + id);
        public bool TryPath(string id, out PathInfo p) => paths.TryGetValue(id ?? "", out p);
        public StopLine StopLineOf(string laneId) => stopLineByLane.TryGetValue(laneId ?? "", out var s) ? s : null;
        public IReadOnlyList<ConflictZone> ZonesOf(string connectionId) =>
            zonesByConnection.TryGetValue(connectionId ?? "", out var z) ? (IReadOnlyList<ConflictZone>)z : Array.Empty<ConflictZone>();
        public IReadOnlyList<PedestrianCrossing> CrossingsOn(string pathId) =>
            crossingsByPath.TryGetValue(pathId ?? "", out var c) ? (IReadOnlyList<PedestrianCrossing>)c : Array.Empty<PedestrianCrossing>();

        public IReadOnlyList<SignCoverage> SignsAt(string pathId, double s)
        {
            if (!coverageByPath.TryGetValue(pathId ?? "", out var list)) return Array.Empty<SignCoverage>();
            return list.Where(c => s >= c.FromS && s <= c.ToS).ToList();
        }

        public IReadOnlyList<SignCoverage> CoverageOn(string pathId) =>
            coverageByPath.TryGetValue(pathId ?? "", out var list) ? (IReadOnlyList<SignCoverage>)list : Array.Empty<SignCoverage>();

        /// <summary>
        /// Speed limit at a point: the 3.24 zone covering it (the lowest if several), else the path limit (the town default
        /// of the module). A zone ends at the next intersection, at 3.25 or at the next 3.24 (T65).
        /// </summary>
        public float SpeedLimitAt(string pathId, double s)
        {
            float zone = float.PositiveInfinity;
            foreach (var c in SignsAt(pathId, s))
            {
                var sign = SignById(c.SignId);
                if (sign.code == "3.24" && float.TryParse(sign.value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
                    zone = Math.Min(zone, v);
            }
            return float.IsPositiveInfinity(zone) ? Path(pathId).SpeedLimitKph : zone;
        }

        /// <summary>True for a junction with turns (a real intersection); lane-change stretches and level crossings are not.</summary>
        public bool IsIntersection(string junctionId) => intersections.Contains(junctionId ?? "");

        /// <summary>Signs standing on a path (their <see cref="SignPlacement.laneIds"/> include it).</summary>
        public IReadOnlyList<SignPlacement> SignsOn(string pathId) =>
            signsByPath.TryGetValue(pathId ?? "", out var list) ? (IReadOnlyList<SignPlacement>)list : Array.Empty<SignPlacement>();

        readonly Dictionary<string, SignPlacement> signById = new Dictionary<string, SignPlacement>();
        SignPlacement SignById(string id)
        {
            if (!signById.TryGetValue(id, out var sign)) signById[id] = sign = World.signs.First(x => x.id == id);
            return sign;
        }

        /// <summary>Paths whose geometry passes within the grid cells around (x, z).</summary>
        public IEnumerable<string> Candidates(double x, double z, double radius)
        {
            var seen = new HashSet<string>();
            int x0 = Cell(x - radius), x1 = Cell(x + radius), z0 = Cell(z - radius), z1 = Cell(z + radius);
            for (int i = x0; i <= x1; i++)
                for (int k = z0; k <= z1; k++)
                    if (grid.TryGetValue((i, k), out var list))
                        foreach (var id in list) if (seen.Add(id)) yield return id;
        }

        /// <summary>Signs whose zone runs to the next intersection whatever the layout says (ПДД РФ, Приложение 1, раздел 3 — редакцию сверить).</summary>
        static bool IsZone(string code) => code == "3.24" || code == "3.27" || code == "3.28";

        /// <summary>Signs that end a zone of <paramref name="code"/> before the intersection.</summary>
        static bool Ends(string code, SignPlacement other) =>
            code == "3.24" && (other.code == "3.24" || other.code == "3.25") || (code == "3.27" || code == "3.28") && other.code == code;

        void BuildCoverage(SignPlacement sign)
        {
            if (sign.laneIds.Length == 0) return;
            var start = sign.laneIds[0];
            bool zone = IsZone(sign.code) || sign.untilNextJunction;
            if (!zone && string.IsNullOrEmpty(sign.zoneEndLaneId))
            {
                foreach (var id in sign.laneIds) AddCoverage(sign.id, id, id == start ? sign.atS : 0, Path(id).Length);
                return;
            }
            if (!zone)
            {
                // Explicit end: walk plain continuations from the sign to it.
                var current = start; double from = sign.atS;
                for (int guard = 0; guard < 10000; guard++)
                {
                    bool end = current == sign.zoneEndLaneId;
                    AddCoverage(sign.id, current, from, end ? sign.zoneEndS : Path(current).Length);
                    if (end) return;
                    var lane = Path(current).Lane;
                    if (lane == null || lane.successors.Length != 1) return;
                    current = lane.successors[0]; from = 0;
                }
                return;
            }
            // Zone to the next intersection (T65): every lane of the direction, through lane-change stretches and level
            // crossings (not intersections), cut short by a sign that ends it (3.25, another 3.24).
            foreach (var laneId in sign.laneIds)
            {
                if (!paths.ContainsKey(laneId)) continue;
                var p = Path(laneId);
                double from = p.Lane != null ? Math.Min(sign.atS, p.Length) : 0;
                var seen = new HashSet<string>();
                for (int guard = 0; guard < 10000 && seen.Add(p.Id); guard++)
                {
                    double to = p.Length;
                    var ender = SignsOn(p.Id).Where(o => o.id != sign.id && Ends(sign.code, o) && o.atS > from + 1e-6 && (p.Id != laneId || o.atS > sign.atS)).OrderBy(o => o.atS).FirstOrDefault();
                    if (ender != null) to = ender.atS;
                    AddCoverage(sign.id, p.Id, from, to);
                    if (ender != null) break;
                    var next = ZoneContinuation(p, sign.id);
                    if (next == null) break;
                    p = next; from = 0;
                }
            }
        }

        /// <summary>Where a zone goes on after <paramref name="p"/>: the direct successor, or through a junction that is not an
        /// intersection on the connection that keeps the lane (it is covered too). Null at an intersection or a dead end.</summary>
        PathInfo ZoneContinuation(PathInfo p, string signId)
        {
            if (p.IsConnection) return Path(p.Connection.toLaneId);
            if (p.Lane.successors.Length == 1) return Path(p.Lane.successors[0]);
            var through = p.Next.Select(Path).Where(n => n.IsConnection && !IsIntersection(n.Connection.junctionId)).ToList();
            if (through.Count == 0) return null;
            // A car changing lanes on the stretch is still in the zone.
            foreach (var c in through.Where(n => n.Connection.laneChange)) AddCoverage(signId, c.Id, 0, c.Length);
            var keep = through.FirstOrDefault(n => !n.Connection.laneChange) ?? through[0];
            return keep;
        }

        void AddCoverage(string signId, string pathId, double from, double to) =>
            Add(coverageByPath, pathId, new SignCoverage { SignId = signId, PathId = pathId, FromS = from, ToS = to });

        void AddToGrid(string id, Vec3d a, Vec3d b, double pad)
        {
            int x0 = Cell(Math.Min(a.x, b.x) - pad), x1 = Cell(Math.Max(a.x, b.x) + pad);
            int z0 = Cell(Math.Min(a.z, b.z) - pad), z1 = Cell(Math.Max(a.z, b.z) + pad);
            for (int i = x0; i <= x1; i++)
                for (int k = z0; k <= z1; k++)
                {
                    if (!grid.TryGetValue((i, k), out var list)) grid[(i, k)] = list = new List<string>();
                    if (list.Count == 0 || list[list.Count - 1] != id) list.Add(id);
                }
        }

        static int Cell(double v) => (int)Math.Floor(v / CellSizeM);

        static void Add<T>(Dictionary<string, List<T>> d, string key, T value)
        {
            if (!d.TryGetValue(key, out var list)) d[key] = list = new List<T>();
            list.Add(value);
        }
    }
}
