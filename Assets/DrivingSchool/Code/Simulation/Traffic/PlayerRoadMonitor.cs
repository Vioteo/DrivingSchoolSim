using System;
using System.Collections.Generic;
using System.Linq;
using DrivingSchool.Contracts;
using DrivingSchool.Simulation.RoadGraph;

namespace DrivingSchool.Simulation.Traffic
{
    /// <summary>
    /// Facts about the player's car on the district graph for the city rules (T65): lane, axis and marking, speed limit of
    /// the zone, the movement made at an intersection and the lane it was made from, lane changes, where the car stands,
    /// the vehicle ahead. Reads the director after its tick; decides nothing (the rules do, <c>CityRuleMonitor</c>).
    /// A movement is judged by the lanes used: the lane before the intersection and the lane after it, so the
    /// locator switching between connections inside the junction does not matter.
    /// </summary>
    public sealed class PlayerRoadMonitor
    {
        /// <summary>Game parameters: a stop line or an occupied crosswalk this close ahead is a reason to stand.</summary>
        public double StopLineWaitM = 15, SignalWaitM = 60, CrosswalkWaitM = 25, QueueGapM = 12, BeforeCrosswalkM = 5, LeadHorizonM = 80;

        readonly TrafficDirector director;
        readonly RoadGraphIndex index;
        readonly Dictionary<string, List<LaneBoundary>> boundaries = new Dictionary<string, List<LaneBoundary>>();
        bool inJunction; string junctionFrom; double junctionEntered;
        string rankLane; int rank; string lastPath;
        // ПДД 18.3 (T74): route vehicles moving off a stop the player was behind, by agent id.
        readonly Dictionary<string, BusWatch> departing = new Dictionary<string, BusWatch>();
        sealed class BusWatch { public bool Behind, Queued; public double LastSeen; }

        /// <summary>Game parameters (T74): a bus moving off counts as passed when the player gets ahead of its front while its
        /// left signal is on or it pulls away, closer to its side than this, or after queueing right behind it.</summary>
        public double PassLateralM = 3.0, QueueBehindM = 30, QueueLateralM = 1.3;

        public PlayerRoadMonitor(TrafficDirector director)
        {
            this.director = director ?? throw new ArgumentNullException(nameof(director));
            index = director.Graph;
            foreach (var b in index.World.boundaries)
            {
                if (!boundaries.TryGetValue(b.laneId, out var list)) boundaries[b.laneId] = list = new List<LaneBoundary>();
                list.Add(b);
            }
        }

        public DriverRoadFacts Update()
        {
            var p = director.Player;
            var pos = director.PlayerLane;
            var f = new DriverRoadFacts
            {
                Seconds = director.SimSeconds, X = p.X, Z = p.Z, SpeedMps = (float)Math.Abs(p.SpeedMps),
                LeftIndicator = p.LeftIndicator, RightIndicator = p.RightIndicator, Hazard = p.Hazard,
                LeadGapM = float.PositiveInfinity, JunctionFromAllowedLane = true,
            };
            Transit(ref f, p);
            if (!p.Present || !pos.IsValid) { rankLane = null; lastPath = null; return f; }
            var path = index.Path(pos.PathId);
            if (path.IsConnection && lastPath != null && index.TryPath(lastPath, out var before) && !before.IsConnection && !string.IsNullOrEmpty(path.Connection.signalGroupId))
            {
                // Drove in from a lane: what did the signal show (ПДД 6.2, 6.13; переезд — 15.3)?
                var aspect = director.AspectOf(path.Connection.signalGroupId);
                bool red = aspect == SignalAspect.Red || aspect == SignalAspect.RedAmber;
                if (red && index.IsIntersection(path.Connection.junctionId)) f.EnteredOnRed = true;
                else if (red) f.EnteredClosedRailway = true;
            }
            lastPath = path.Id;
            f.OnRoad = true;
            f.SpeedLimitKph = index.SpeedLimitAt(path.Id, pos.S);
            f.AgainstDirection = pos.AgainstDirection;
            f.LateralOffsetM = (float)pos.D;

            if (path.IsConnection)
            {
                bool intersection = index.IsIntersection(path.Connection.junctionId);
                f.InIntersection = intersection;
                if (intersection && !inJunction) { inJunction = true; junctionFrom = path.Connection.fromLaneId; junctionEntered = f.Seconds; }
                var from = index.Path(path.Connection.fromLaneId).Lane;
                f.LanesInDirection = LanesOf(from); f.RightmostLane = string.IsNullOrEmpty(from.rightNeighborId);
            }
            else
            {
                var lane = path.Lane;
                if (inJunction) LeftJunction(ref f, lane);
                else if (!pos.AgainstDirection)
                {
                    // Lane change: the rank (1 next to the axis) of the lane changed without passing an intersection.
                    int r = Math.Abs(lane.index);
                    if (rankLane != null && rankLane != lane.id && r != rank && r > 0 && rank > 0) f.LaneChange = r > rank ? 1 : -1;
                }
                if (!pos.AgainstDirection) { rankLane = lane.id; rank = Math.Abs(lane.index); }
                f.LanesInDirection = LanesOf(lane);
                f.RightmostLane = string.IsNullOrEmpty(lane.rightNeighborId);
                Axis(ref f, lane, pos, p.WidthM);
                // Lane for route vehicles (T74): the centre of the car is on it.
                if (!pos.AgainstDirection && lane.busLane) { f.OnBusLane = !lane.busLaneEntry; f.OnBusLaneEntry = lane.busLaneEntry; }
            }
            Stop(ref f, path, pos, p);
            Lead(ref f, path, pos, p);
            return f;
        }

        void LeftJunction(ref DriverRoadFacts f, LaneV2 exit)
        {
            inJunction = false;
            rankLane = exit.id; rank = Math.Abs(exit.index);
            if (!index.TryPath(junctionFrom, out var entryPath) || entryPath.Lane == null) return;
            var entry = entryPath.Lane;
            if (entry.roundabout && exit.roundabout) return;   // round the ring past an exit: no movement to judge
            var exitLine = index.Path(exit.id).Line;
            var m = WorldMigration.ClassifyTurn(entryPath.Line.HeadingAt(entryPath.Length), exitLine.HeadingAt(0));
            f.JunctionManeuver = m;
            f.JunctionEnteredSeconds = junctionEntered;
            f.JunctionLaneAllowed = entry.allowedManeuvers;
            f.JunctionFromAllowedLane = entry.allowedManeuvers == LaneManeuver.None || (entry.allowedManeuvers & m) != 0;
            f.JunctionFromBusLane = entry.busLane;
            f.EnteredRoundabout = !entry.roundabout && exit.roundabout;
            f.LeftRoundabout = entry.roundabout && !exit.roundabout;
        }

        int LanesOf(LaneV2 lane)
        {
            int n = 1;
            for (var l = lane; n < 8 && index.TryPath(l.leftNeighborId, out var q) && q.Lane != null; n++) l = q.Lane;
            for (var l = lane; n < 8 && index.TryPath(l.rightNeighborId, out var q) && q.Lane != null; n++) l = q.Lane;
            return n;
        }

        /// <summary>Is the car over the road axis? Only the lane next to the axis (it has an oncoming lane) borders it.</summary>
        void Axis(ref DriverRoadFacts f, LaneV2 lane, LanePosition pos, double carWidth)
        {
            if (string.IsNullOrEmpty(lane.oncomingLaneId)) return;
            f.AxisMarking = MarkingAt(lane.id, BoundarySide.Left, pos.S);
            if (pos.AgainstDirection) { f.LeftSideOverAxis = true; return; }
            // Left side: D grows to the right, the axis is half a lane to the left of the lane centre.
            f.LeftSideOverAxis = pos.D - carWidth / 2 < -lane.widthM / 2 - 0.1;
        }

        MarkingType MarkingAt(string laneId, BoundarySide side, double s)
        {
            if (!boundaries.TryGetValue(laneId, out var list)) return MarkingType.None;
            foreach (var b in list) if (b.side == side && s >= b.fromS - 0.5 && s <= b.toS + 0.5) return b.type;
            return MarkingType.None;
        }

        void Stop(ref DriverRoadFacts f, PathInfo path, LanePosition pos, PlayerSample p)
        {
            double fx = Math.Sin(p.HeadingRad), fz = Math.Cos(p.HeadingRad), half = p.LengthM / 2;
            var place = RoadStopPlace.Lane; bool signalled = false, occupiedAhead = false;
            foreach (var c in index.World.crossings)
            {
                var mid = Polyline.Lerp(c.a, c.b, 0.5);
                double dx = mid.x - p.X, dz = mid.z - p.Z;
                if (dx * dx + dz * dz > 40 * 40) continue;
                if (director.Pedestrians.IsCrossingOccupied(c.id) && dx * fx + dz * fz > 0 && dx * dx + dz * dz < CrosswalkWaitM * CrosswalkWaitM) occupiedAhead = true;
                bool on = false, before = false;
                for (double o = -half + 0.3; o <= half - 0.3 + 1e-6; o += 0.5) on |= OnCrosswalk(c, p.X + fx * o, p.Z + fz * o);
                if (!on)
                    for (double o = half; o <= half + BeforeCrosswalkM + 1e-6; o += 0.5) before |= OnCrosswalk(c, p.X + fx * o, p.Z + fz * o);
                if (on) { place = RoadStopPlace.Crosswalk; signalled = !string.IsNullOrEmpty(c.signalGroupId); break; }
                if (before && place != RoadStopPlace.BeforeCrosswalk) { place = RoadStopPlace.BeforeCrosswalk; signalled = !string.IsNullOrEmpty(c.signalGroupId); }
            }
            // A stopping place of route vehicles and 15 m round it (ПДД 12.4), a lane for them (ПДД 18.2), the zone of 3.27.
            string stopZone = place == RoadStopPlace.Lane ? index.TransitStopZoneAt(path.Id, pos.S) : null;
            if (stopZone != null) place = RoadStopPlace.TransitStop;
            else if (place == RoadStopPlace.Lane && !path.IsConnection && path.Lane.busLane && !pos.AgainstDirection) place = RoadStopPlace.BusLane;
            if (place == RoadStopPlace.Lane && index.SignsAt(path.Id, pos.S).Any(c => index.World.signs.First(s => s.id == c.SignId).code == "3.27"))
                place = RoadStopPlace.NoStoppingZone;
            f.StopPlace = place; f.CrosswalkSignalled = signalled; f.TransitStopId = stopZone;

            // The stop line lies on the short entry lane of the junction, so look along the lanes ahead:
            // near the line — waiting to go in; a red or amber ahead — waiting for the signal (ПДД 6.13).
            bool atLine = false;
            if (!path.IsConnection && Ahead(path, pos.S + half, out double toLine, out bool signalStop))
                atLine = toLine > -2 && toLine < StopLineWaitM || signalStop && toLine > -2 && toLine < SignalWaitM;
            f.WaitingForTraffic = atLine || occupiedAhead || f.InIntersection;
        }

        /// <summary>Distance from <paramref name="s"/> on a lane to where the lanes ahead enter an intersection (its stop
        /// line, else the lane end), following the only way on; whether a signal there shows stop.</summary>
        bool Ahead(PathInfo path, double s, out double distance, out bool signalStop)
        {
            distance = 0; signalStop = false;
            double offset = -s;
            for (int guard = 0; guard < 16 && offset < SignalWaitM; guard++)
            {
                var stop = index.StopLineOf(path.Id);
                var into = path.Next.Select(n => index.Path(n))
                    .Where(n => n.IsConnection && index.IsIntersection(n.Connection.junctionId)).ToList();
                if (stop != null || into.Count > 0)
                {
                    distance = offset + (stop != null ? stop.s : path.Length);
                    foreach (var c in into)
                    {
                        if (string.IsNullOrEmpty(c.Connection.signalGroupId)) continue;
                        var a = director.AspectOf(c.Connection.signalGroupId);
                        if (a == SignalAspect.Red || a == SignalAspect.RedAmber || a == SignalAspect.Amber) signalStop = true;
                    }
                    return true;
                }
                var lanes = path.Next.Select(n => index.Path(n)).Where(n => !n.IsConnection || !index.IsIntersection(n.Connection.junctionId)).ToList();
                if (lanes.Count != 1) return false;
                offset += path.Length;
                path = lanes[0];
            }
            return false;
        }

        static bool OnCrosswalk(PedestrianCrossing c, double x, double z)
        {
            double ex = c.b.x - c.a.x, ez = c.b.z - c.a.z, len2 = ex * ex + ez * ez;
            if (len2 < 1e-9) return false;
            double t = ((x - c.a.x) * ex + (z - c.a.z) * ez) / len2;
            if (t < 0 || t > 1) return false;
            double px = c.a.x + ex * t - x, pz = c.a.z + ez * t - z;
            return px * px + pz * pz <= (c.widthM / 2) * (c.widthM / 2);
        }

        /// <summary>
        /// ПДД РФ 18.3 (T74): in a town the others give way to a bus moving off its stop. Game reading: the player did not
        /// give way if, while the bus signals left or pulls away, the player gets ahead of its front close beside it — or
        /// after waiting right behind it in its lane (then went round it instead of letting it go).
        /// </summary>
        void Transit(ref DriverRoadFacts f, PlayerSample p)
        {
            var seen = new HashSet<string>();
            foreach (var o in director.Snapshot.Participants)
            {
                if (o.RouteId == null) continue;
                if (o.Transit != TransitPhase.Departing) continue;   // signalling to move off, or pulling away
                seen.Add(o.Id);
                if (!departing.TryGetValue(o.Id, out var w)) departing[o.Id] = w = new BusWatch();
                w.LastSeen = f.Seconds;
                if (!p.Present) continue;
                double fx = Math.Sin(o.HeadingRad), fz = Math.Cos(o.HeadingRad);
                double dx = p.X - o.Position.x, dz = p.Z - o.Position.z;
                double along = dx * fx + dz * fz, lateral = dx * fz - dz * fx;   // right of the bus positive
                bool sameWay = Math.Cos(p.HeadingRad - o.HeadingRad) > 0.7;
                double half = o.LengthM / 2, mine = p.LengthM / 2;
                if (!sameWay) { w.Behind = false; continue; }
                if (along + mine < -half + 0.5)
                {
                    w.Behind = true;
                    if (along > -half - QueueBehindM && Math.Abs(lateral) < QueueLateralM) w.Queued = true;
                }
                else if (w.Behind && along - mine > half - 0.5)
                {
                    // Got ahead of it: beside it on the left (or after queueing behind it).
                    if (w.Queued || Math.Abs(lateral) < PassLateralM) { f.PassedDepartingBus = true; f.PassedBusRoute = o.RouteName; }
                    w.Behind = false; w.Queued = false;
                }
            }
            double now = f.Seconds;   // a ref parameter cannot be used in the lambda
            foreach (var id in departing.Keys.Where(k => !seen.Contains(k) && now - departing[k].LastSeen > 1).ToList()) departing.Remove(id);
        }

        /// <summary>The nearest vehicle ahead along the graph (the lane, then every way on from it), bumper to bumper.</summary>
        void Lead(ref DriverRoadFacts f, PathInfo path, LanePosition pos, PlayerSample p)
        {
            var byPath = new Dictionary<string, List<ParticipantState>>();
            foreach (var o in director.Snapshot.Participants)
            {
                if (o.Kind != ParticipantKind.Vehicle || o.PathId == null) continue;
                if (!byPath.TryGetValue(o.PathId, out var list)) byPath[o.PathId] = list = new List<ParticipantState>();
                list.Add(o);
            }
            double best = double.PositiveInfinity, speed = 0;
            var queue = new Queue<(string id, double offset)>();
            var seen = new HashSet<string>();
            queue.Enqueue((path.Id, -pos.S));
            while (queue.Count > 0)
            {
                var (id, offset) = queue.Dequeue();
                if (!seen.Add(id) || offset > LeadHorizonM) continue;
                if (byPath.TryGetValue(id, out var cars))
                    foreach (var o in cars)
                    {
                        double along = offset + o.S;
                        if (along <= 0.1) continue;
                        if (id == path.Id && Math.Abs(o.D - pos.D) > (o.WidthM + p.WidthM) / 2) continue;
                        double gap = along - (o.LengthM + p.LengthM) / 2;
                        if (gap < best) { best = gap; speed = o.SpeedMps; }
                    }
                var info = index.Path(id);
                foreach (var n in info.Next) queue.Enqueue((n, offset + info.Length));
            }
            f.LeadGapM = (float)best; f.LeadSpeedMps = (float)speed;
            if (best < QueueGapM) f.WaitingForTraffic = true;
        }
    }
}
