using System;
using System.Collections.Generic;
using System.Linq;
using DrivingSchool.Contracts;
using DrivingSchool.Simulation.RoadGraph;

namespace DrivingSchool.Simulation.Traffic
{
    public enum PedestrianPhase { Walking, Waiting, Crossing, Down }

    /// <summary>
    /// Pedestrians of a district (T36, first version in T51). They walk the sidewalk network of the graph, cross the road only
    /// on pedestrian crossings and decide there: on a signalled crossing they wait for the pedestrian green; on an
    /// unsignalled one for a gap — every approaching vehicle must be able to stop comfortably or be far enough away.
    /// A crossing with a pedestrian on it is <see cref="IsCrossingOccupied">occupied</see>: vehicles stop before it
    /// (ПДД РФ 14.1, 13.1 — the director applies it; редакцию пунктов сверить, CLAUDE.md правило 5).
    /// The network: sidewalks and crossings from the graph plus short straight links between their ends that do not
    /// cross any lane (junction corners, kerb to crossing). Pure C#, deterministic for a seed.
    /// </summary>
    public sealed class PedestrianSimulation
    {
        public const double LinkRadiusM = 11;          // corner of a 24 m cross: sidewalk end → sidewalk end ≈ 9.6 m
        public const double KeepRightM = 0.45;         // walkers keep to the right of their direction on sidewalks
        public const double SafeGapSeconds = 3.0;      // unsignalled crossing: a vehicle closer in time is a threat
        public const double SafeMarginM = 4.0;         // ...unless it can stop comfortably with this margin
        public const double ComfortDecelerationMps2 = 2.5;
        public const double ApproachRangeM = 45;
        public const double MinRoadClearanceM = 1.9;   // lane centre → kerb is 2 m in Road Kit v1

        internal sealed class Walk
        {
            public string Id; public Polyline Line; public PedestrianCrossing Crossing;
            public bool IsCrossing => Crossing != null;
        }

        struct End { public Walk Walk; public bool AtEnd; public Vec3d Point; }

        public sealed class Pedestrian
        {
            public string Id; public PedestrianPhase Phase; public double SpeedMps, WalkSpeedMps;
            public Vec3d Position; public double HeadingRad; public string WalkId, CrossingId;
            public double WaitingSince = -1, DownSince = -1;
            internal Walk Path; internal bool Forward; internal double S;   // along Path in its own direction
            internal Polyline Leg; internal double LegS;                    // current straight link to the next path, if any
            internal Walk NextPath; internal bool NextForward; internal Walk CameFrom;
            internal Random Rng;
        }

        readonly RoadGraphIndex index;
        readonly List<Walk> walks = new List<Walk>();
        readonly Dictionary<Walk, List<End>[]> links = new Dictionary<Walk, List<End>[]>();
        readonly List<Pedestrian> people = new List<Pedestrian>();
        readonly HashSet<string> occupied = new HashSet<string>();
        readonly Random rng;
        int counter;

        public PedestrianSimulation(RoadGraphIndex index, int seed)
        {
            this.index = index ?? throw new ArgumentNullException(nameof(index));
            rng = new Random(seed ^ 0x5eed);
            foreach (var s in index.World.sidewalks)
                if (s.points != null && s.points.Length >= 2) walks.Add(new Walk { Id = s.id, Line = new Polyline(s.points) });
            foreach (var c in index.World.crossings) walks.Add(new Walk { Id = c.id, Line = new Polyline(new[] { c.a, c.b }), Crossing = c });
            BuildLinks();
        }

        public IReadOnlyList<Pedestrian> People => people;
        public int WalkCount => walks.Count;
        public bool IsCrossingOccupied(string crossingId) => occupied.Contains(crossingId);
        public IReadOnlyCollection<string> OccupiedCrossings => occupied;

        /// <summary>Number of links from the ends of a sidewalk or crossing (tests, debug).</summary>
        public int LinkCount(string walkId)
        {
            var w = walks.FirstOrDefault(x => x.Id == walkId);
            return w == null ? 0 : links[w][0].Count + links[w][1].Count;
        }

        // ---------------------------------------------------------------- network

        void BuildLinks()
        {
            var ends = new List<End>();
            foreach (var w in walks)
            {
                links[w] = new[] { new List<End>(), new List<End>() };
                ends.Add(new End { Walk = w, AtEnd = false, Point = w.Line.Start });
                ends.Add(new End { Walk = w, AtEnd = true, Point = w.Line.End });
            }
            var roads = index.Paths.Select(p => p.Line).ToList();
            foreach (var a in ends)
                foreach (var b in ends)
                {
                    if (a.Walk == b.Walk) continue;
                    if (a.Walk.IsCrossing && b.Walk.IsCrossing) continue;   // corner links go through the sidewalk
                    double dist = Polyline.Distance2D(a.Point, b.Point);
                    if (dist > LinkRadiusM) continue;
                    if (dist > 0.35 && roads.Any(r => CrossesLine(r, a.Point, b.Point) || NearInterior(r, a.Point, b.Point))) continue;
                    links[a.Walk][a.AtEnd ? 1 : 0].Add(b);
                }
        }

        static bool CrossesLine(Polyline line, Vec3d a, Vec3d b)
        {
            var pts = line.Points;
            for (int i = 0; i < pts.Count - 1; i++)
            {
                double d1 = Cross(pts[i], pts[i + 1], a), d2 = Cross(pts[i], pts[i + 1], b);
                double d3 = Cross(a, b, pts[i]), d4 = Cross(a, b, pts[i + 1]);
                if ((d1 > 0) != (d2 > 0) && (d3 > 0) != (d4 > 0)) return true;
            }
            return false;
        }

        /// <summary>
        /// A link whose middle part (15–85 %) comes closer than <see cref="MinRoadClearanceM"/> to a lane centre line runs over
        /// the carriageway (e.g. across the open end of a road, where the lanes stop exactly on the link). Its ends may be close:
        /// crossing ends lie on the kerb.
        /// </summary>
        static bool NearInterior(Polyline road, Vec3d a, Vec3d b)
        {
            for (double t = 0.15; t <= 0.851; t += 0.07)
            {
                var q = Polyline.Lerp(a, b, t);
                if (DistanceToLine(road, q) < MinRoadClearanceM) return true;
            }
            return false;
        }

        static double DistanceToLine(Polyline l, Vec3d q)
        {
            double best = double.MaxValue; var pts = l.Points;
            for (int i = 0; i < pts.Count - 1; i++)
            {
                double ex = pts[i + 1].x - pts[i].x, ez = pts[i + 1].z - pts[i].z, len2 = ex * ex + ez * ez;
                double t = len2 > 0 ? Math.Max(0, Math.Min(1, ((q.x - pts[i].x) * ex + (q.z - pts[i].z) * ez) / len2)) : 0;
                double dx = q.x - pts[i].x - ex * t, dz = q.z - pts[i].z - ez * t;
                best = Math.Min(best, dx * dx + dz * dz);
            }
            return Math.Sqrt(best);
        }

        static double Cross(Vec3d a, Vec3d b, Vec3d c) => (b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x);

        // ---------------------------------------------------------------- spawning

        /// <summary>Puts a pedestrian on a random sidewalk point accepted by <paramref name="allowed"/>; null if none found.</summary>
        public string Spawn(Func<Vec3d, bool> allowed = null)
        {
            var sidewalks = walks.Where(w => !w.IsCrossing).ToList();
            if (sidewalks.Count == 0) return null;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                var w = sidewalks[rng.Next(sidewalks.Count)];
                double s = rng.NextDouble() * w.Line.Length;
                bool forward = rng.Next(2) == 0;
                var p = new Pedestrian
                {
                    Id = "ped" + (++counter).ToString("D3"), Path = w, Forward = forward, S = forward ? s : w.Line.Length - s,
                    WalkSpeedMps = 1.15 + rng.NextDouble() * 0.35, Rng = new Random(rng.Next()),
                };
                Place(p);
                if (allowed != null && !allowed(p.Position)) { counter--; continue; }
                people.Add(p);
                return p.Id;
            }
            return null;
        }

        /// <summary>Scenarios and tests: a pedestrian waiting at the kerb of a crossing, at end a (or b), about to cross.</summary>
        public string PlaceAtCrossing(string crossingId, bool fromA = true, double walkSpeedMps = 1.3)
        {
            var w = walks.First(x => x.IsCrossing && x.Id == crossingId);
            var p = new Pedestrian
            {
                Id = "ped" + (++counter).ToString("D3"), Path = w, Forward = fromA, S = 0, WalkSpeedMps = walkSpeedMps, Rng = new Random(rng.Next()),
                Phase = PedestrianPhase.Waiting, NextPath = w, NextForward = fromA, CrossingId = w.Id, WalkId = w.Id,
            };
            Place(p, w.Line.PointAt(fromA ? 0 : w.Line.Length));
            FaceAlong(p, w, fromA);
            people.Add(p);
            return p.Id;
        }

        public Pedestrian Find(string id) => people.FirstOrDefault(p => p.Id == id);

        /// <summary>Hit by a car (T53): the person lies where they are and no longer moves; a crossing they lie on stays occupied.</summary>
        public bool Knock(string id, double now)
        {
            var p = Find(id);
            if (p == null || p.Phase == PedestrianPhase.Down) return false;
            p.Phase = PedestrianPhase.Down; p.DownSince = now; p.SpeedMps = 0; p.Leg = null;
            if (!p.Path.IsCrossing) p.CrossingId = null;
            return true;
        }

        public void Remove(string id) => people.RemoveAll(p => p.Id == id);

        // ---------------------------------------------------------------- tick

        /// <param name="vehicles">Vehicles and the player from the director's snapshot.</param>
        /// <param name="aspectOf">Aspect of a signal group.</param>
        public void Tick(double dt, IReadOnlyList<ParticipantState> vehicles, Func<string, SignalAspect> aspectOf, double now)
        {
            if (dt < 0) throw new ArgumentOutOfRangeException(nameof(dt));
            foreach (var p in people) Step(p, dt, vehicles, aspectOf, now);
            occupied.Clear();
            foreach (var p in people) if ((p.Phase == PedestrianPhase.Crossing || p.Phase == PedestrianPhase.Down) && p.CrossingId != null) occupied.Add(p.CrossingId);
        }

        void Step(Pedestrian p, double dt, IReadOnlyList<ParticipantState> vehicles, Func<string, SignalAspect> aspectOf, double now)
        {
            if (p.Phase == PedestrianPhase.Down) { p.SpeedMps = 0; return; }
            if (p.Phase == PedestrianPhase.Waiting)
            {
                p.SpeedMps = 0;
                if (!MayEnter(p.NextPath.Crossing, vehicles, aspectOf)) return;
                p.Phase = PedestrianPhase.Crossing; p.WaitingSince = -1;
                Enter(p, p.NextPath, p.NextForward);
            }
            double budget = p.WalkSpeedMps * (p.Phase == PedestrianPhase.Crossing ? 1.15 : 1.0) * dt;
            p.SpeedMps = dt > 0 ? budget / dt : 0;
            for (int guard = 0; guard < 8 && budget > 1e-9; guard++)
            {
                if (p.Leg != null)
                {
                    double left = p.Leg.Length - p.LegS;
                    if (budget < left) { p.LegS += budget; budget = 0; break; }
                    budget -= left; p.Leg = null;
                    if (p.NextPath.IsCrossing)
                    {
                        // At the kerb: stop and decide next tick.
                        p.Phase = PedestrianPhase.Waiting; p.WaitingSince = now; p.CrossingId = p.NextPath.Id;
                        Place(p, p.NextPath.Line.PointAt(p.NextForward ? 0 : p.NextPath.Line.Length));
                        FaceAlong(p, p.NextPath, p.NextForward);
                        return;
                    }
                    Enter(p, p.NextPath, p.NextForward);
                    continue;
                }
                double remaining = p.Path.Line.Length - p.S;
                if (budget < remaining) { p.S += budget; budget = 0; break; }
                budget -= remaining; p.S = p.Path.Line.Length;
                ChooseNext(p);
            }
            if (p.Leg == null && p.Phase == PedestrianPhase.Crossing && !p.Path.IsCrossing) p.Phase = PedestrianPhase.Walking;
            Place(p);
        }

        void Enter(Pedestrian p, Walk w, bool forward)
        {
            p.CameFrom = p.Path; p.Path = w; p.Forward = forward; p.S = 0; p.Leg = null;
            p.WalkId = w.Id;
            p.Phase = w.IsCrossing ? PedestrianPhase.Crossing : PedestrianPhase.Walking;
            p.CrossingId = w.IsCrossing ? w.Id : null;
        }

        void ChooseNext(Pedestrian p)
        {
            var here = PointOnPath(p.Path, p.Forward, p.Path.Line.Length, 0);
            var options = links[p.Path][p.Forward ? 1 : 0].Where(e => e.Walk != p.CameFrom || links[p.Path][p.Forward ? 1 : 0].Count == 1).ToList();
            if (options.Count == 0)
            {
                // Dead end (edge of the district): turn round.
                p.CameFrom = p.Path; p.Forward = !p.Forward; p.S = 0;
                return;
            }
            var next = options[p.Rng.Next(options.Count)];
            p.NextPath = next.Walk; p.NextForward = !next.AtEnd;      // enter at the end we linked to
            var entry = next.Point;
            if (Polyline.Distance2D(here, entry) < 0.05 && !next.Walk.IsCrossing) { Enter(p, next.Walk, p.NextForward); return; }
            p.Leg = new Polyline(new[] { p.Position, entry }); p.LegS = 0;
            if (p.Leg.Length < 1e-3)
            {
                p.Leg = new Polyline(new[] { entry, new Vec3d(entry.x + 1e-3, entry.y, entry.z) }); p.LegS = p.Leg.Length;
            }
        }

        /// <summary>
        /// Signalled: pedestrian green only (not flashing — do not start on a flashing green).
        /// Unsignalled: nothing on the crossing and every vehicle heading for it either far enough in time or able to stop.
        /// </summary>
        bool MayEnter(PedestrianCrossing c, IReadOnlyList<ParticipantState> vehicles, Func<string, SignalAspect> aspectOf)
        {
            bool signalled = !string.IsNullOrEmpty(c.signalGroupId);
            if (signalled && aspectOf(c.signalGroupId) != SignalAspect.Green) return false;
            var centre = Polyline.Lerp(c.a, c.b, 0.5);
            Polyline.Direction(c.a, c.b, out double wx, out double wz);
            double halfLength = Polyline.Distance2D(c.a, c.b) / 2;
            foreach (var v in vehicles)
            {
                if (v.Kind == ParticipantKind.Pedestrian) continue;
                double fx = Math.Sin(v.HeadingRad), fz = Math.Cos(v.HeadingRad);
                double rx = centre.x - v.Position.x, rz = centre.z - v.Position.z;
                double along = rx * fx + rz * fz;                     // distance ahead of the vehicle's centre
                double across = Math.Abs(rx * wx + rz * wz);          // how far along the walkway the vehicle's line meets it
                // Standing on the crossing: never step in front of it.
                double onWalkway = Math.Abs(rx * fz - rz * fx);
                if (Math.Abs(along) < v.LengthM / 2 + c.widthM / 2 && across < halfLength + 0.5) return false;
                if (signalled) continue;                               // on green, turning cars yield (the director stops them)
                if (along <= 0 || along > ApproachRangeM || across > halfLength + 1 || onWalkway > ApproachRangeM) continue;
                if (Math.Abs(fx * wx + fz * wz) > 0.7) continue;       // driving along the crossing line, not towards it
                double gap = along - v.LengthM / 2 - c.widthM / 2;
                double speed = Math.Max(0, v.SpeedMps);
                if (speed < 0.5 && gap > 1) continue;                  // stopped in front of it: it is waiting for us
                double stopDistance = speed * speed / (2 * ComfortDecelerationMps2);
                if (gap > stopDistance + SafeMarginM && gap / Math.Max(speed, 0.1) > SafeGapSeconds) continue;
                return false;
            }
            return true;
        }

        // ---------------------------------------------------------------- pose

        void Place(Pedestrian p)
        {
            if (p.Leg != null)
            {
                var q = p.Leg.PointAt(p.LegS);
                p.Position = q; p.HeadingRad = p.Leg.HeadingAt(p.LegS);
                return;
            }
            p.Position = PointOnPath(p.Path, p.Forward, p.S, p.Path.IsCrossing ? 0.35 : KeepRightM);
            FaceAlong(p, p.Path, p.Forward, p.S);
            p.WalkId = p.Path.Id;
        }

        static void Place(Pedestrian p, Vec3d at) => p.Position = at;

        static void FaceAlong(Pedestrian p, Walk w, bool forward, double s = 0)
        {
            double sw = forward ? s : w.Line.Length - s;
            p.HeadingRad = w.Line.HeadingAt(Math.Min(w.Line.Length, Math.Max(0, sw))) + (forward ? 0 : Math.PI);
        }

        static Vec3d PointOnPath(Walk w, bool forward, double s, double keepRight)
        {
            double sw = forward ? s : w.Line.Length - s;
            sw = Math.Min(w.Line.Length, Math.Max(0, sw));
            return w.Line.OffsetPoint(sw, forward ? keepRight : -keepRight);
        }

        public IEnumerable<ParticipantState> Participants() => people.Select(p => new ParticipantState
        {
            Id = p.Id, Kind = ParticipantKind.Pedestrian, PathId = null, SpeedMps = p.SpeedMps, HeadingRad = p.HeadingRad,
            Position = p.Position, LengthM = 0.5, WidthM = 0.6,
            Decision = p.Phase == PedestrianPhase.Down ? "down" : p.Phase == PedestrianPhase.Waiting ? "wait: " + p.CrossingId : p.Phase == PedestrianPhase.Crossing ? "cross: " + p.CrossingId : "walk: " + p.WalkId,
        });
    }
}
