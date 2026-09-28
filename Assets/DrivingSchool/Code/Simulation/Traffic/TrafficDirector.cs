using System;
using System.Collections.Generic;
using System.Linq;
using DrivingSchool.Contracts;
using DrivingSchool.Simulation.RoadGraph;

namespace DrivingSchool.Simulation.Traffic
{
    /// <summary>
    /// The single traffic control node of a district (T35, ADR-014) with the permit/notice protocol (T40, ADR-015).
    /// Owns participants, signals, the reservation table, spawning and level of detail. Every tick:
    /// player → signals → snapshot → notices → leases → decisions/permits (10 Hz) → kinematics → spawn → publish.
    /// Agents decide from one immutable snapshot, so the result does not depend on the order agents are visited.
    /// </summary>
    public sealed class TrafficDirector
    {
        public const string PlayerId = "player";

        readonly RoadGraphIndex index;
        readonly LaneLocator locator;
        readonly TrafficProfile profile;
        readonly Dictionary<string, SignalController> controllerByGroup = new Dictionary<string, SignalController>();
        readonly List<SignalController> controllers = new List<SignalController>();
        readonly Dictionary<string, SignalAspect> external = new Dictionary<string, SignalAspect>();
        readonly ReservationTable reservations = new ReservationTable();
        readonly List<Agent> agents = new List<Agent>();
        readonly Random rng;
        readonly int seed;
        readonly HashSet<(int, int)> loadedChunks = new HashSet<(int, int)>();
        bool allChunksLoaded = true;
        int spawnCounter;
        long tick;
        double now, decisionClock, spawnClock;
        bool started;
        PlayerSample player;
        LanePosition playerPos;
        List<ManeuverNotice> pending = new List<ManeuverNotice>();
        List<ManeuverPermit> permits = new List<ManeuverPermit>();
        readonly List<TrafficEvent> events = new List<TrafficEvent>();
        readonly List<TrafficEvent> incoming = new List<TrafficEvent>(); // reported between ticks, published by the next one
        readonly PedestrianSimulation pedestrians;
        readonly Dictionary<(string crossing, string path), double> crossingS = new Dictionary<(string, string), double>();
        bool pedestriansPlaced;

        /// <summary>Test hook: visit agents in reverse order; the outcome must not change.</summary>
        public bool ReverseProcessingOrder;

        public TrafficDirector(WorldDocumentV2 graph, TrafficProfile profile, int seed)
        {
            index = new RoadGraphIndex(graph ?? throw new ArgumentNullException(nameof(graph)));
            locator = new LaneLocator(index);
            this.profile = profile ?? new TrafficProfile();
            this.seed = seed;
            rng = new Random(seed);
            foreach (var plan in graph.signalPlans)
            {
                SignalController.ValidatePlan(plan, graph);
                var c = new SignalController(plan, graph.signalGroups);
                controllers.Add(c);
                foreach (var g in c.GroupIds) controllerByGroup[g] = c;
            }
            // Groups without a plan are driven from outside (a level crossing, T56). Until told otherwise the way is
            // open: vehicles see no signal (they drive by the rules), pedestrians may walk.
            foreach (var g in graph.signalGroups)
                if (!controllerByGroup.ContainsKey(g.id))
                    external[g.id] = g.kind == SignalGroupKind.Pedestrian ? SignalAspect.Green : SignalAspect.Off;
            pedestrians = new PedestrianSimulation(index, seed);
            foreach (var c in graph.crossings)
                foreach (var pathId in c.laneIds)
                    if (index.TryPath(pathId, out var path)) crossingS[(c.id, pathId)] = CrossingPoint(path.Line, c);
            Snapshot = new TrafficSnapshot();
        }

        public PedestrianSimulation Pedestrians => pedestrians;

        /// <summary>Arc length on <paramref name="line"/> where it meets the walkway centre line a–b (the nearest point if it does not).</summary>
        public static double CrossingPoint(Polyline line, PedestrianCrossing c)
        {
            var pts = line.Points; double run = 0;
            for (int i = 0; i < pts.Count - 1; i++)
            {
                var p = pts[i]; var q = pts[i + 1];
                double ex = q.x - p.x, ez = q.z - p.z, fx = c.b.x - c.a.x, fz = c.b.z - c.a.z;
                double den = ex * fz - ez * fx, seg = Math.Sqrt(ex * ex + ez * ez);
                if (Math.Abs(den) > 1e-9)
                {
                    double t = ((c.a.x - p.x) * fz - (c.a.z - p.z) * fx) / den;
                    double u = ((c.a.x - p.x) * ez - (c.a.z - p.z) * ex) / den;
                    if (t >= 0 && t <= 1 && u >= 0 && u <= 1) return run + t * seg;
                }
                run += seg;
            }
            var mid = Polyline.Lerp(c.a, c.b, 0.5);
            line.Project(mid.x, mid.z, out double s, out _);
            return s;
        }

        public RoadGraphIndex Graph => index;
        public ReservationTable Reservations => reservations;
        public TrafficSnapshot Snapshot { get; private set; }
        public int VehicleCount => agents.Count;
        public IReadOnlyList<SignalController> SignalControllers => controllers;

        public void SetPlayer(PlayerSample sample) => player = sample;

        /// <summary>The player on the graph after the last tick (invalid when off road or absent).</summary>
        public LanePosition PlayerLane => playerPos;
        public PlayerSample Player => player;
        public double SimSeconds => now;

        public void OnChunkReady(int cx, int cz) { allChunksLoaded = false; loadedChunks.Add((cx, cz)); }

        public void OnChunkUnloaded(int cx, int cz)
        {
            allChunksLoaded = false;
            loadedChunks.Remove((cx, cz));
            foreach (var a in agents.Where(a => !InLoadedChunks(a)).ToList()) Remove(a);
        }

        public void ReportContact(string agentId, string otherId)
        {
            var a = agents.FirstOrDefault(x => x.Id == agentId);
            if (a == null) return;
            if (!a.Hazard) incoming.Add(new TrafficEvent { Kind = "hazard-on", ParticipantId = agentId, OtherId = otherId, SimSeconds = now });
            a.Hazard = true; a.HazardSince = now;   // a contact that goes on keeps renewing it
            incoming.Add(new TrafficEvent { Kind = "contact", ParticipantId = agentId, OtherId = otherId, SimSeconds = now });
        }

        /// <summary>
        /// A pedestrian was hit (T53): they lie down, cars nearby are warned and keep off them; the event goes to the rules.
        /// The person is removed after <see cref="TrafficProfile.DownedPedestrianSeconds"/>.
        /// </summary>
        public void KnockPedestrian(string pedestrianId, string byId)
        {
            if (!pedestrians.Knock(pedestrianId, now)) return;
            incoming.Add(new TrafficEvent { Kind = "pedestrian-hit", ParticipantId = pedestrianId, OtherId = byId, SimSeconds = now });
        }

        /// <summary>Adds a vehicle on an explicit route (scenarios and tests). Spawning normally happens by itself.</summary>
        public string AddVehicle(IEnumerable<string> route, double s, double speedMps, DriverProfile driver = null, string id = null)
        {
            id = id ?? "car" + (++spawnCounter).ToString("D3");
            var car = new LaneFollowerAgent(id, index, route, s, driver ?? profile.Drivers[0], speedMps);
            car.ExitAtRouteEnd = index.Path(car.Route[car.Route.Count - 1]).Next.Length == 0;
            var agent = new Agent { Id = id, Car = car, Rng = new Random(seed ^ StableHash(id)) };
            agents.Add(agent);
            agents.Sort((x, y) => string.CompareOrdinal(x.Id, y.Id));
            return id;
        }

        public LaneFollowerAgent Vehicle(string id) => agents.First(a => a.Id == id).Car;

        /// <summary>Test hook: the vehicle holds still (e.g. to let a lease expire).</summary>
        public void Freeze(string id, bool frozen) => agents.First(a => a.Id == id).Frozen = frozen;

        public void Tick(long simTick, double simSeconds)
        {
            double dt = started ? Math.Max(0, simSeconds - now) : 0;
            started = true;
            tick = simTick; now = simSeconds;
            events.Clear();
            events.AddRange(incoming); incoming.Clear();

            // 1. Player on the graph.
            playerPos = player.Present ? locator.Locate(player.X, player.Z, player.HeadingRad, playerPos) : default;
            // 2. Signals.
            foreach (var c in controllers) c.Tick(now);
            // 3. One snapshot for all decisions of this tick; pedestrians decide on it too (T51).
            var before = Participants();
            if (profile.MaxPedestrians > 0 && !pedestriansPlaced)
            {
                pedestriansPlaced = true;
                for (int i = 0; i < profile.MaxPedestrians * 3 && pedestrians.People.Count < profile.MaxPedestrians; i++) pedestrians.Spawn(p => Loaded(p) && !VisibleToPlayer(p));
            }
            pedestrians.Tick(dt, before, AspectOf, now);
            // 4. Notices issued last tick are delivered now.
            var delivered = pending; pending = new List<ManeuverNotice>();
            foreach (var a in agents) a.Notices = delivered.Where(n => n.ToId == a.Id).ToList();
            // 5. Leases, the player's observed claim.
            ExpireLeases();
            ClaimForPlayer();
            // 6. Decisions and permits.
            decisionClock += dt;
            double decisionStep = 1.0 / profile.DecisionHz;
            if (decisionClock >= decisionStep - 1e-9 || tick == 0 || dt == 0)
            {
                decisionClock = 0;
                Decide(before);
            }
            // 7. Kinematics.
            foreach (var a in Ordered()) Drive(a, before, dt);
            // Indicators follow the car every tick (T65): off as soon as the turn is done, not up to a decision step later.
            foreach (var a in agents) UpdateIndicators(a);
            ReleaseFinishedPermits();
            // 8. Spawn / despawn / level of detail.
            spawnClock += dt;
            if (spawnClock >= profile.SpawnIntervalSeconds)
            {
                spawnClock = 0; TrySpawn();
                if (pedestrians.People.Count < profile.MaxPedestrians) pedestrians.Spawn(p => Loaded(p) && !VisibleToPlayer(p));
            }
            Despawn();
            foreach (var p in pedestrians.People.Where(x => x.Phase == PedestrianPhase.Down && now - x.DownSince > profile.DownedPedestrianSeconds).ToList())
                pedestrians.Remove(p.Id);
            // 9. Publish.
            Snapshot = new TrafficSnapshot
            {
                Tick = tick, SimSeconds = now, Participants = Participants(),
                Signals = controllerByGroup.Keys.Concat(external.Keys).OrderBy(k => k, StringComparer.Ordinal).Select(g => new SignalState { GroupId = g, Aspect = AspectOf(g) }).ToList(),
                Reservations = reservations.Entries.ToList(), Permits = permits, Notices = delivered, Events = events.ToList(),
            };
        }

        public SignalAspect AspectOf(string groupId)
        {
            if (external.TryGetValue(groupId ?? "", out var e)) return e;
            return controllerByGroup.TryGetValue(groupId ?? "", out var c) ? c.GetAspect(groupId) : SignalAspect.Off;
        }

        /// <summary>Groups driven from outside (no signal plan), e.g. the lights of a level crossing.</summary>
        public IEnumerable<string> ExternalGroups => external.Keys;

        /// <summary>Sets the aspect of a group without a signal plan (level crossing: Red while it closes or is closed).</summary>
        public void SetExternalAspect(string groupId, SignalAspect aspect)
        {
            if (!external.ContainsKey(groupId ?? "")) throw new ArgumentException("Not an external signal group: " + groupId);
            external[groupId] = aspect;
        }

        // ---------------------------------------------------------------- decisions and permits

        void Decide(List<ParticipantState> snapshot)
        {
            permits = new List<ManeuverPermit>();
            var requests = new List<(Agent agent, ManeuverRequest request, JunctionPolicy.Approach approach)>();
            foreach (var a in Ordered())
            {
                ExtendRoute(a);
                if (a.Hazard) NotifyHazard(a);
                RevokeIfSignalChanged(a);
                var next = NextConnection(a, out double distance);
                if (next == null || a.PermitConnection == next.Id || a.Frozen) continue;
                double reach = Math.Max(profile.JunctionLookaheadM, a.Car.CurrentSpeedMps * a.Car.CurrentSpeedMps / (2 * a.Car.Profile.ComfortDecelerationMps2) + 10);
                if (distance > reach || !IsFrontOfQueue(a, next, snapshot)) continue;
                double v = Math.Max(1, a.Car.CurrentSpeedMps);
                var request = new ManeuverRequest
                {
                    AgentId = a.Id, PathId = next.Id, Kind = ManeuverKind.EnterConflictZone, EtaSeconds = distance / v,
                    DurationSeconds = (next.Length + a.Car.Profile.LengthM) / Math.Max(3, v),
                    Claim = new ReservationClaim { Id = "enter:" + next.Id, Keys = JunctionPolicy.ZoneKeys(index, next.Id) },
                };
                requests.Add((a, request, ApproachOf(a.Id, next, request.EtaSeconds)));
            }
            var playerApproach = PlayerApproach();
            // Deterministic processing: earlier arrival first, then id.
            foreach (var (a, request, approach) in requests.OrderBy(r => r.request.EtaSeconds).ThenBy(r => r.agent.Id, StringComparer.Ordinal))
                permits.Add(Evaluate(a, request, approach, requests.Select(r => r.approach).Where(x => x != approach), playerApproach));
        }

        ManeuverPermit Evaluate(Agent a, ManeuverRequest r, JunctionPolicy.Approach mine, IEnumerable<JunctionPolicy.Approach> others, JunctionPolicy.Approach playerApproach)
        {
            var connection = index.Path(r.PathId).Connection;
            ManeuverPermit Deny(string reason, string owner = null)
            {
                if (a.WaitingSince < 0) a.WaitingSince = now;
                a.Decision = "wait: " + reason + (owner != null ? " (" + owner + ")" : "");
                return new ManeuverPermit { AgentId = a.Id, PathId = r.PathId, Kind = r.Kind, Granted = false, Reason = reason, ConflictOwnerId = owner };
            }
            // Signal.
            if (!string.IsNullOrEmpty(connection.signalGroupId))
            {
                var aspect = AspectOf(connection.signalGroupId);
                bool go = aspect == SignalAspect.Green || aspect == SignalAspect.GreenFlashing || aspect == SignalAspect.AmberFlashing || aspect == SignalAspect.Off;
                if (aspect == SignalAspect.Amber) go = !CanStopComfortably(a, r.PathId);
                if (!go) return Deny("signal " + aspect);
            }
            // Room behind the junction (do not block it in a jam).
            var blocker = ExitBlocker(a, connection);
            if (blocker != null) return Deny("exit blocked", blocker);
            // Priority against other approaching participants whose paths conflict.
            var conflicting = new HashSet<string>(index.ZonesOf(r.PathId).SelectMany(z => new[] { z.connectionA, z.connectionB }));
            bool deadlockBreak = a.WaitingSince >= 0 && now - a.WaitingSince > profile.DeadlockSeconds;
            foreach (var other in others.Concat(playerApproach != null ? new[] { playerApproach } : Array.Empty<JunctionPolicy.Approach>()))
            {
                if (other.ParticipantId == a.Id || !conflicting.Contains(other.ConnectionId)) continue;
                if (other.EtaSeconds > r.EtaSeconds + r.DurationSeconds + 2) continue; // arrives after we are through
                if (!JunctionPolicy.MustYield(mine, other)) continue;
                bool otherIsPlayer = other.ParticipantId == PlayerId;
                if (deadlockBreak && !otherIsPlayer && string.CompareOrdinal(a.Id, other.ParticipantId) < 0 && IsStopped(other.ParticipantId)) continue;
                return Deny("yield", other.ParticipantId);
            }
            // Exclusive zones.
            double until = now + r.EtaSeconds + r.DurationSeconds + 5;
            if (!reservations.TryReserve(a.Id, r.Claim, until, out var owner)) return Deny("zone taken", owner);
            a.PermitConnection = r.PathId; a.PermitClaim = r.Claim.Id; a.WaitingSince = -1;
            a.Decision = "go: " + r.PathId;
            // Tell the ones who have to wait, so they do not start to creep in.
            foreach (var other in others)
                if (conflicting.Contains(other.ConnectionId))
                    pending.Add(new ManeuverNotice { ToId = other.ParticipantId, FromId = a.Id, SubjectId = r.PathId, Kind = NoticeKind.Hold, Value = until, IssuedTick = tick });
            return new ManeuverPermit { AgentId = a.Id, PathId = r.PathId, Kind = r.Kind, Granted = true, UntilSeconds = until, Fallback = "stop-at-line", Reason = "granted" };
        }

        void ExpireLeases()
        {
            foreach (var e in reservations.ExpireUntil(now))
            {
                var a = agents.FirstOrDefault(x => x.Id == e.OwnerId);
                if (a == null) continue;
                if (a.PermitConnection != null && a.Car.CurrentPathId == a.PermitConnection)
                {
                    // Already inside: it cannot vanish, the lease is extended until it leaves.
                    reservations.TryReserve(a.Id, e.Claim, now + 5, out _);
                    continue;
                }
                pending.Add(new ManeuverNotice { ToId = a.Id, FromId = "director", SubjectId = a.PermitConnection, Kind = NoticeKind.Cancel, IssuedTick = tick });
                a.PermitConnection = null; a.PermitClaim = null;
            }
        }

        void RevokeIfSignalChanged(Agent a)
        {
            if (a.PermitConnection == null || a.Car.CurrentPathId == a.PermitConnection) return;
            var group = index.Path(a.PermitConnection).Connection.signalGroupId;
            if (string.IsNullOrEmpty(group)) return;
            var aspect = AspectOf(group);
            bool stop = aspect == SignalAspect.Red || aspect == SignalAspect.RedAmber || (aspect == SignalAspect.Amber && CanStopComfortably(a, a.PermitConnection));
            if (!stop) return;
            reservations.Release(a.Id, a.PermitClaim);
            a.PermitConnection = null; a.PermitClaim = null;
        }

        void ReleaseFinishedPermits()
        {
            foreach (var a in agents)
            {
                if (a.PermitConnection == null) continue;
                int at = IndexOnRoute(a, a.PermitConnection);
                if (at >= 0 && at < a.Car.RouteIndex)
                {
                    reservations.Release(a.Id, a.PermitClaim);
                    a.PermitConnection = null; a.PermitClaim = null;
                }
            }
        }

        /// <summary>The player cannot be given permits; the director claims what the player is visibly about to take.</summary>
        void ClaimForPlayer()
        {
            reservations.ReleaseAll(PlayerId);
            var connection = PlayerConnection(out _);
            if (connection == null) return;
            var claim = new ReservationClaim { Id = "player:" + connection, Keys = JunctionPolicy.ZoneKeys(index, connection) };
            // Safety first: AI permits not yet used give way to the player.
            // Every owner is handled once: a lease can outlive the permit it was made for (two junctions close together,
            // T55), and releasing it by the permit's claim id then freed nothing — this loop spun for ever (T60).
            string owner; var handled = new HashSet<string>();
            while ((owner = reservations.ConflictingOwner(claim, PlayerId)) != null && handled.Add(owner))
            {
                var a = agents.FirstOrDefault(x => x.Id == owner);
                if (a == null || a.Car.CurrentPathId == a.PermitConnection) break; // inside the junction: cannot be undone
                reservations.ReleaseConflicting(a.Id, claim);
                if (a.PermitClaim != null) reservations.Release(a.Id, a.PermitClaim);
                pending.Add(new ManeuverNotice { ToId = a.Id, FromId = PlayerId, SubjectId = a.PermitConnection, Kind = NoticeKind.Cancel, IssuedTick = tick });
                a.PermitConnection = null; a.PermitClaim = null;
            }
            reservations.TryReserve(PlayerId, claim, now + 1, out _);
        }

        /// <summary>Connection the player is on or about to enter (by indicator, straight otherwise), null if none.</summary>
        string PlayerConnection(out double eta)
        {
            eta = 0;
            if (!player.Present || !playerPos.IsValid) return null;
            var path = index.Path(playerPos.PathId);
            if (path.IsConnection) return path.Id;
            double toEnd = path.Length - playerPos.S - player.LengthM / 2;
            var exits = path.Next.Select(index.Path).Where(p => p.IsConnection).ToList();
            if (exits.Count == 0 || toEnd > 25 || player.SpeedMps < 2 && toEnd > 3) return null;
            var wanted = player.LeftIndicator ? LaneManeuver.Left : player.RightIndicator ? LaneManeuver.Right : LaneManeuver.Straight;
            var chosen = exits.FirstOrDefault(p => p.Connection.maneuver == wanted) ?? exits.FirstOrDefault(p => p.Connection.maneuver == LaneManeuver.Straight) ?? exits[0];
            eta = toEnd / Math.Max(1, player.SpeedMps);
            return chosen.Id;
        }

        JunctionPolicy.Approach PlayerApproach()
        {
            var c = PlayerConnection(out double eta);
            if (c == null) return null;
            var conn = index.Path(c).Connection;
            var aspect = AspectOf(conn.signalGroupId);
            return new JunctionPolicy.Approach
            {
                ParticipantId = PlayerId, ConnectionId = c, Maneuver = conn.maneuver, EtaSeconds = eta,
                Priority = JunctionPolicy.PriorityOf(index.World, conn.fromLaneId), EntryHeadingRad = index.Path(c).Line.HeadingAt(0),
                SignalGreen = aspect == SignalAspect.Green || aspect == SignalAspect.GreenFlashing,
            };
        }

        JunctionPolicy.Approach ApproachOf(string id, PathInfo connection, double eta)
        {
            var aspect = AspectOf(connection.Connection.signalGroupId);
            return new JunctionPolicy.Approach
            {
                ParticipantId = id, ConnectionId = connection.Id, Maneuver = connection.Connection.maneuver, EtaSeconds = eta,
                Priority = JunctionPolicy.PriorityOf(index.World, connection.Connection.fromLaneId),
                EntryHeadingRad = connection.Line.HeadingAt(0),
                SignalGreen = aspect == SignalAspect.Green || aspect == SignalAspect.GreenFlashing,
            };
        }

        void NotifyHazard(Agent a)
        {
            foreach (var other in agents)
            {
                if (other == a) continue;
                double dist = other.Car.DistanceAlongRoute(a.Car.CurrentPathId, a.Car.CurrentDistanceAlongLane);
                if (dist > 0 && dist <= profile.HazardNoticeRangeM)
                    pending.Add(new ManeuverNotice { ToId = other.Id, FromId = a.Id, SubjectId = a.Car.CurrentPathId, Kind = NoticeKind.HazardAhead, Value = a.Car.CurrentDistanceAlongLane, IssuedTick = tick });
            }
        }

        // ---------------------------------------------------------------- kinematics

        void Drive(Agent a, List<ParticipantState> snapshot, double dt)
        {
            if (dt <= 0) return;
            bool background = Lod(a) == SimulationLod.Background;
            if (background)
            {
                a.BackgroundClock += dt;
                if (a.BackgroundClock < 1.0 / profile.BackgroundHz) return;
                dt = a.BackgroundClock; a.BackgroundClock = 0;
            }
            // After a light knock the driver waits with the hazard lights on, then carries on (T52); a contact that goes on
            // (the player is still touching) renews the time in ReportContact.
            if (a.Hazard && now - a.HazardSince > profile.HazardHoldSeconds) { a.Hazard = false; a.Decision = "resume after contact"; }
            if (a.Frozen || a.Hazard)
            {
                a.Car.Update(dt, double.PositiveInfinity, 0, 0.0); // brake to a standstill where it is
                if (a.Hazard) a.Decision = "hazard";
                return;
            }
            var (gap, leadSpeed, leadId) = Lead(a, snapshot);
            double stop = double.PositiveInfinity;
            var next = NextConnection(a, out _);
            if (next != null && a.PermitConnection != next.Id)
            {
                int i = IndexOnRoute(a, next.Id);
                var before = a.Car.Route[i - 1];
                var line = index.StopLineOf(before);
                double stopS = line != null ? line.s : index.Path(before).Length;
                stop = a.Car.DistanceAlongRoute(before, stopS) - a.Car.Profile.LengthM / 2;
                if (stop < -0.5) stop = double.PositiveInfinity; // already past the line: never brake inside the junction
            }
            double yieldStop = PedestrianStop(a);
            if (yieldStop < stop) { stop = yieldStop; a.Decision = "yield: pedestrian"; }
            a.Car.Update(dt, gap, leadSpeed, stop);
            if (a.PermitConnection == null && a.WaitingSince < 0 && leadId != null && gap < 15) a.Decision = "follow " + leadId;
            else if (a.PermitConnection == null && a.WaitingSince < 0 && next == null) a.Decision = "free";
        }

        /// <summary>Distance from the front bumper to the stop point before the nearest crossing ahead with a pedestrian on it.</summary>
        double PedestrianStop(Agent a)
        {
            if (pedestrians.OccupiedCrossings.Count == 0) return double.PositiveInfinity;
            double best = double.PositiveInfinity, half = a.Car.Profile.LengthM / 2;
            var route = a.Car.Route;
            for (int i = a.Car.RouteIndex; i < route.Count; i++)
            {
                if (a.Car.DistanceAlongRoute(route[i], 0) > 60 && i > a.Car.RouteIndex) break;
                foreach (var c in index.CrossingsOn(route[i]))
                {
                    if (!pedestrians.IsCrossingOccupied(c.id) || !crossingS.TryGetValue((c.id, route[i]), out double sc)) continue;
                    double at = Math.Max(0, sc - c.widthM / 2 - 1.0);
                    double gap = a.Car.DistanceAlongRoute(route[i], at) - half;
                    if (gap >= -0.3 && gap < best) best = Math.Max(0, gap);
                }
            }
            return best;
        }

        (double gap, double speed, string id) Lead(Agent a, List<ParticipantState> snapshot)
        {
            double best = double.PositiveInfinity, speed = 0; string id = null;
            foreach (var p in snapshot)
            {
                if (p.Id == a.Id || p.PathId == null) continue;
                double along = a.Car.DistanceAlongRoute(p.PathId, p.S);
                if (double.IsPositiveInfinity(along))
                {
                    // Not on my route, but maybe standing across it: a car that stopped inside the junction on another
                    // connection from my lane (e.g. turning right and waiting for pedestrians) is in my way (T51).
                    bool obstacle = p.Kind == ParticipantKind.Vehicle || p.Kind == ParticipantKind.Player || p.Kind == ParticipantKind.Pedestrian && p.Decision == "down";
                    if (!obstacle) continue;
                    along = AlongMyRoute(a, p);
                    if (double.IsPositiveInfinity(along)) continue;
                }
                else if (Math.Abs(p.D - a.Car.LateralOffset) > (p.WidthM + a.Car.Profile.WidthM) / 2 + 0.2) continue;
                if (along <= 0) continue;
                double gap = along - (p.LengthM + a.Car.Profile.LengthM) / 2;
                if (gap < best) { best = gap; speed = p.SpeedMps; id = p.Id; }
            }
            return (best, speed, id);
        }

        /// <summary>Distance along the agent's route to the point where participant <paramref name="p"/> overlaps it, +inf if it does not (next 40 m).</summary>
        double AlongMyRoute(Agent a, ParticipantState p)
        {
            var route = a.Car.Route;
            double half = (p.WidthM + a.Car.Profile.WidthM) / 2 + 0.2;
            // Centre, rear and front of the other body: a car turning off my path still blocks it with its rear (T55,
            // two connections from one lane of a 2+2 approach). Result: where its centre would be, as for a car on my route.
            double fx = Math.Sin(p.HeadingRad), fz = Math.Cos(p.HeadingRad);
            double best = double.PositiveInfinity;
            foreach (double o in new[] { 0.0, -p.LengthM / 2 + 0.3, p.LengthM / 2 - 0.3 })
            {
                double x = p.Position.x + fx * o, z = p.Position.z + fz * o;
                for (int i = a.Car.RouteIndex; i < route.Count; i++)
                {
                    var path = index.Path(route[i]);
                    double start = a.Car.DistanceAlongRoute(route[i], i == a.Car.RouteIndex ? a.Car.CurrentDistanceAlongLane : 0);
                    if (start > 40) break;
                    path.Line.Project(x, z, out double s, out double d);
                    if (s <= 1e-6 || s >= path.Length - 1e-6 || Math.Abs(d) > half) continue;
                    double along = a.Car.DistanceAlongRoute(route[i], s);
                    if (double.IsPositiveInfinity(along)) continue;
                    best = Math.Min(best, along - o);
                    break;
                }
            }
            return best;
        }

        // ---------------------------------------------------------------- spawning, level of detail

        void TrySpawn()
        {
            if (agents.Count >= profile.MaxVehicles) return;
            var candidates = index.World.spawnPoints.Where(s => s.role == SpawnRole.Vehicle).OrderBy(s => s.id, StringComparer.Ordinal).ToList();
            var snapshot = Participants();
            var free = candidates.Where(sp =>
            {
                var p = index.Path(sp.pathId).Line.PointAt(sp.s);
                if (!Loaded(p) || VisibleToPlayer(p)) return false;
                return snapshot.All(o => o.Kind == ParticipantKind.Pedestrian || Polyline.Distance2D(o.Position, p) > 15);
            }).ToList();
            if (free.Count == 0) return;
            var spawn = free[rng.Next(free.Count)];
            var driver = profile.Drivers[rng.Next(profile.Drivers.Length)];
            double speed = Math.Min(index.SpeedLimitAt(spawn.pathId, spawn.s) / 3.6 * 0.6, 8);
            var id = AddVehicle(new[] { spawn.pathId }, spawn.s, speed, driver);
            ExtendRoute(agents.First(x => x.Id == id));
        }

        /// <summary>Both the point on the path and the body (which cuts corners, T52) must be in loaded chunks.</summary>
        bool InLoadedChunks(Agent a) => Loaded(a.Car.Position) && Loaded(a.Car.BodyPosition);

        void Despawn()
        {
            foreach (var a in agents.ToList())
            {
                if (a.Car.Finished && a.FinishedAt < 0) a.FinishedAt = now;
                bool lingered = a.Car.Finished && now - a.FinishedAt > profile.FinishedLingerSeconds;
                if ((a.Car.Finished && !VisibleToPlayer(a.Car.Position)) || lingered || !InLoadedChunks(a)) Remove(a);
            }
        }

        void Remove(Agent a)
        {
            reservations.ReleaseAll(a.Id);
            agents.Remove(a);
        }

        bool VisibleToPlayer(Vec3d p)
        {
            if (!player.Present) return false;
            double dx = p.x - player.X, dz = p.z - player.Z, dist = Math.Sqrt(dx * dx + dz * dz);
            if (dist < profile.HiddenSpawnDistanceM) return true;
            if (dist > profile.MinSpawnDistanceM) return false;
            double angle = Math.Atan2(dx, dz) - player.HeadingRad;
            while (angle > Math.PI) angle -= 2 * Math.PI;
            while (angle <= -Math.PI) angle += 2 * Math.PI;
            return Math.Abs(angle) * 180 / Math.PI <= profile.ViewConeDeg / 2;
        }

        bool Loaded(Vec3d p) => allChunksLoaded || loadedChunks.Contains(((int)Math.Floor(p.x / profile.ChunkSizeM), (int)Math.Floor(p.z / profile.ChunkSizeM)));

        SimulationLod Lod(Agent a)
        {
            if (!player.Present) return SimulationLod.Near;
            var p = a.Car.Position;
            return Math.Sqrt((p.x - player.X) * (p.x - player.X) + (p.z - player.Z) * (p.z - player.Z)) > profile.NearRadiusM ? SimulationLod.Background : SimulationLod.Near;
        }

        // ---------------------------------------------------------------- helpers

        void ExtendRoute(Agent a)
        {
            for (int guard = 0; guard < 64 && a.Car.RemainingRouteM < profile.RouteHorizonM; guard++)
            {
                var last = index.Path(a.Car.Route[a.Car.Route.Count - 1]);
                var options = last.Next.Select(index.Path).Where(p => !p.IsConnection || p.Connection.maneuver != LaneManeuver.UTurn).OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
                if (options.Count == 0) { a.Car.ExitAtRouteEnd = true; return; }
                // Lane changes (T55) are optional connections: take one about a third of the time, else keep the lane.
                var changes = options.Where(IsLaneChange).ToList();
                var keep = options.Except(changes).ToList();
                var pool = changes.Count > 0 && (keep.Count == 0 || a.Rng.NextDouble() < profile.LaneChangeShare) ? changes : keep;
                a.Car.AppendRoute(new[] { pool[a.Rng.Next(pool.Count)].Id });
            }
        }

        void UpdateIndicators(Agent a)
        {
            int side = IndicatorSide(a.Car);
            a.Left = side < 0; a.Right = side > 0;
        }

        /// <summary>
        /// Which indicator the car shows (−1 left, +1 right, 0 none), T65. ПДД РФ 8.1–8.2 (редакцию сверить): a turn is
        /// signalled in advance and until it is done, a lane change from shortly before it until the car is in the new lane.
        /// Only the next intersection counts: a car going straight through an intersection shows nothing there, even if it
        /// turns at the one after (that would look like a turn here). Lane keeping on a lane-change stretch and level
        /// crossings are not intersections: the look ahead passes them. Straight ring sections of a roundabout: no signal.
        /// </summary>
        public int IndicatorSide(LaneFollowerAgent car)
        {
            var current = index.Path(car.CurrentPathId);
            if (current.IsConnection)
            {
                if (current.Connection.laneChange) return Math.Sign(LaneShift(current));
                int turning = TurnSide(current.Connection.maneuver);
                if (turning != 0) return turning;
                if (index.IsIntersection(current.Connection.junctionId)) return 0;   // straight through: nothing until out of it
            }
            double turnLookahead = Math.Min(profile.TurnSignalMaxM, Math.Max(profile.TurnSignalMinM, car.CurrentSpeedMps * profile.TurnSignalSeconds));
            var route = car.Route;
            for (int i = car.RouteIndex + 1; i < route.Count; i++)
            {
                var p = index.Path(route[i]);
                if (!p.IsConnection) continue;
                double distance = car.DistanceAlongRoute(p.Id, 0) - car.Profile.LengthM / 2;
                if (distance > profile.TurnSignalMaxM) return 0;
                if (p.Connection.laneChange) return distance < profile.LaneChangeSignalM ? Math.Sign(LaneShift(p)) : 0;
                if (!index.IsIntersection(p.Connection.junctionId)) continue;
                int side = TurnSide(p.Connection.maneuver);
                return side != 0 && distance < turnLookahead ? side : 0;
            }
            return 0;
        }

        static int TurnSide(LaneManeuver m) => m == LaneManeuver.Left || m == LaneManeuver.UTurn ? -1 : m == LaneManeuver.Right ? 1 : 0;

        readonly Dictionary<string, double> laneShift = new Dictionary<string, double>();

        double LaneShift(PathInfo p)
        {
            if (!laneShift.TryGetValue(p.Id, out var v)) laneShift[p.Id] = v = p.IsConnection ? RoadKitTemplatesV2.LateralShift(p.Connection.centerline) : 0;
            return v;
        }

        /// <summary>A connection that moves to the neighbouring lane of the same road (marked in the graph, T65). Straight
        /// connections through a junction can shift sideways too (1+1 into 2+2, a roundabout arc) — those are no lane change.</summary>
        static bool IsLaneChange(PathInfo p) => p.IsConnection && p.Connection.laneChange;

        PathInfo NextConnection(Agent a, out double distance)
        {
            var route = a.Car.Route;
            for (int i = a.Car.RouteIndex + 1; i < route.Count; i++)
            {
                var p = index.Path(route[i]);
                if (!p.IsConnection) continue;
                distance = a.Car.DistanceAlongRoute(p.Id, 0) - a.Car.Profile.LengthM / 2;
                return p;
            }
            distance = double.PositiveInfinity;
            return null;
        }

        /// <summary>Only the first vehicle of an approach asks; the ones behind follow it.</summary>
        bool IsFrontOfQueue(Agent a, PathInfo connection, List<ParticipantState> snapshot)
        {
            double mine = a.Car.DistanceAlongRoute(connection.Id, 0);
            foreach (var p in snapshot)
            {
                if (p.Id == a.Id || p.PathId == null || p.Kind != ParticipantKind.Vehicle && p.Kind != ParticipantKind.Player) continue;
                double along = a.Car.DistanceAlongRoute(p.PathId, p.S);
                if (along > 0 && along < mine) return false;
            }
            return true;
        }

        string ExitBlocker(Agent a, LaneConnection connection)
        {
            double need = a.Car.Profile.LengthM + a.Car.Profile.MinGapM;
            foreach (var p in Participants())
            {
                if (p.Id == a.Id || p.PathId != connection.toLaneId) continue;
                if (p.S - p.LengthM / 2 < need && p.SpeedMps < 2) return p.Id;
            }
            return null;
        }

        bool CanStopComfortably(Agent a, string connectionId)
        {
            int i = IndexOnRoute(a, connectionId);
            if (i <= 0) return false;
            var before = a.Car.Route[i - 1];
            var line = index.StopLineOf(before);
            double dist = a.Car.DistanceAlongRoute(before, line != null ? line.s : index.Path(before).Length) - a.Car.Profile.LengthM / 2;
            double v = a.Car.CurrentSpeedMps;
            if (dist <= 0.5) return v < 0.5;
            return v * v / (2 * dist) <= a.Car.Profile.ComfortDecelerationMps2 * 1.5;
        }

        bool IsStopped(string id)
        {
            var a = agents.FirstOrDefault(x => x.Id == id);
            return a != null && a.Car.CurrentSpeedMps < 0.5;
        }

        static int IndexOnRoute(Agent a, string pathId)
        {
            var route = a.Car.Route;
            for (int i = a.Car.RouteIndex > 0 ? a.Car.RouteIndex - 1 : 0; i < route.Count; i++) if (route[i] == pathId) return i;
            for (int i = 0; i < route.Count; i++) if (route[i] == pathId) return i;
            return -1;
        }

        IEnumerable<Agent> Ordered() => ReverseProcessingOrder ? Enumerable.Reverse(agents).ToList() : agents.ToList();

        List<ParticipantState> Participants()
        {
            var list = new List<ParticipantState>(agents.Count + 1);
            foreach (var a in agents)
            {
                var car = a.Car;
                list.Add(new ParticipantState
                {
                    Id = a.Id, Kind = ParticipantKind.Vehicle, PathId = car.CurrentPathId, S = car.CurrentDistanceAlongLane, D = car.LateralOffset,
                    SpeedMps = car.CurrentSpeedMps, AccelerationMps2 = car.TargetAccelerationMps2, HeadingRad = car.BodyHeadingRad,
                    LengthM = car.Profile.LengthM, WidthM = car.Profile.WidthM, Position = car.BodyPosition, SteeringRad = (float)car.WheelSteerRad,
                    LeftIndicator = a.Left || a.Hazard, RightIndicator = a.Right || a.Hazard, Hazard = a.Hazard,
                    BrakeLight = car.TargetAccelerationMps2 < -0.5 || car.CurrentSpeedMps < 0.1,
                    Lod = Lod(a), DriverProfileId = car.Profile.Id, Decision = a.Decision,
                });
            }
            if (player.Present)
                list.Add(new ParticipantState
                {
                    Id = PlayerId, Kind = ParticipantKind.Player, PathId = playerPos.IsValid ? playerPos.PathId : null, S = playerPos.S, D = playerPos.D,
                    SpeedMps = player.SpeedMps, HeadingRad = player.HeadingRad, LengthM = player.LengthM, WidthM = player.WidthM,
                    Position = new Vec3d(player.X, player.Y, player.Z), LeftIndicator = player.LeftIndicator, RightIndicator = player.RightIndicator, Hazard = player.Hazard,
                });
            if (pedestrians != null) list.AddRange(pedestrians.Participants());
            return list;
        }

        static int StableHash(string s)
        {
            unchecked
            {
                int h = (int)2166136261;
                foreach (char c in s) h = (h ^ c) * 16777619;
                return h;
            }
        }

        sealed class Agent
        {
            public string Id;
            public LaneFollowerAgent Car;
            public Random Rng;
            public string PermitConnection, PermitClaim;
            public double WaitingSince = -1, BackgroundClock, FinishedAt = -1, HazardSince = -1;
            public bool Hazard, Frozen, Left, Right;
            public string Decision = "free";
            public List<ManeuverNotice> Notices = new List<ManeuverNotice>();
        }
    }
}
