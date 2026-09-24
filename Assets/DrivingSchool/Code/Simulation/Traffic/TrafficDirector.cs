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
            Snapshot = new TrafficSnapshot();
        }

        public RoadGraphIndex Graph => index;
        public ReservationTable Reservations => reservations;
        public TrafficSnapshot Snapshot { get; private set; }
        public int VehicleCount => agents.Count;
        public IReadOnlyList<SignalController> SignalControllers => controllers;

        public void SetPlayer(PlayerSample sample) => player = sample;

        public void OnChunkReady(int cx, int cz) { allChunksLoaded = false; loadedChunks.Add((cx, cz)); }

        public void OnChunkUnloaded(int cx, int cz)
        {
            allChunksLoaded = false;
            loadedChunks.Remove((cx, cz));
            foreach (var a in agents.Where(a => !Loaded(a.Car.Position)).ToList()) Remove(a);
        }

        public void ReportContact(string agentId, string otherId)
        {
            var a = agents.FirstOrDefault(x => x.Id == agentId);
            if (a == null) return;
            a.Hazard = true;
            incoming.Add(new TrafficEvent { Kind = "contact", ParticipantId = agentId, OtherId = otherId, SimSeconds = now });
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
            // 3. One snapshot for all decisions of this tick.
            var before = Participants();
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
            ReleaseFinishedPermits();
            // 8. Spawn / despawn / level of detail.
            spawnClock += dt;
            if (spawnClock >= profile.SpawnIntervalSeconds) { spawnClock = 0; TrySpawn(); }
            Despawn();
            // 9. Publish.
            Snapshot = new TrafficSnapshot
            {
                Tick = tick, SimSeconds = now, Participants = Participants(),
                Signals = controllerByGroup.Keys.OrderBy(k => k, StringComparer.Ordinal).Select(g => new SignalState { GroupId = g, Aspect = controllerByGroup[g].GetAspect(g) }).ToList(),
                Reservations = reservations.Entries.ToList(), Permits = permits, Notices = delivered, Events = events.ToList(),
            };
        }

        public SignalAspect AspectOf(string groupId) => controllerByGroup.TryGetValue(groupId ?? "", out var c) ? c.GetAspect(groupId) : SignalAspect.Off;

        // ---------------------------------------------------------------- decisions and permits

        void Decide(List<ParticipantState> snapshot)
        {
            permits = new List<ManeuverPermit>();
            var requests = new List<(Agent agent, ManeuverRequest request, JunctionPolicy.Approach approach)>();
            foreach (var a in Ordered())
            {
                ExtendRoute(a);
                UpdateIndicators(a);
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
            string owner;
            while ((owner = reservations.ConflictingOwner(claim, PlayerId)) != null)
            {
                var a = agents.FirstOrDefault(x => x.Id == owner);
                if (a == null || a.Car.CurrentPathId == a.PermitConnection) break; // inside the junction: cannot be undone
                reservations.Release(a.Id, a.PermitClaim);
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
            a.Car.Update(dt, gap, leadSpeed, stop);
            if (a.PermitConnection == null && a.WaitingSince < 0 && leadId != null && gap < 15) a.Decision = "follow " + leadId;
            else if (a.PermitConnection == null && a.WaitingSince < 0 && next == null) a.Decision = "free";
        }

        (double gap, double speed, string id) Lead(Agent a, List<ParticipantState> snapshot)
        {
            double best = double.PositiveInfinity, speed = 0; string id = null;
            foreach (var p in snapshot)
            {
                if (p.Id == a.Id || p.PathId == null) continue;
                double along = a.Car.DistanceAlongRoute(p.PathId, p.S);
                if (double.IsPositiveInfinity(along) || along <= 0) continue;
                if (Math.Abs(p.D - a.Car.LateralOffset) > (p.WidthM + a.Car.Profile.WidthM) / 2 + 0.2) continue;
                double gap = along - (p.LengthM + a.Car.Profile.LengthM) / 2;
                if (gap < best) { best = gap; speed = p.SpeedMps; id = p.Id; }
            }
            return (best, speed, id);
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
                return snapshot.All(o => Polyline.Distance2D(o.Position, p) > 15);
            }).ToList();
            if (free.Count == 0) return;
            var spawn = free[rng.Next(free.Count)];
            var driver = profile.Drivers[rng.Next(profile.Drivers.Length)];
            double speed = Math.Min(index.SpeedLimitAt(spawn.pathId, spawn.s) / 3.6 * 0.6, 8);
            var id = AddVehicle(new[] { spawn.pathId }, spawn.s, speed, driver);
            ExtendRoute(agents.First(x => x.Id == id));
        }

        void Despawn()
        {
            foreach (var a in agents.ToList())
            {
                if ((a.Car.Finished && !VisibleToPlayer(a.Car.Position)) || !Loaded(a.Car.Position)) Remove(a);
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
                a.Car.AppendRoute(new[] { options[a.Rng.Next(options.Count)].Id });
            }
        }

        void UpdateIndicators(Agent a)
        {
            var next = NextConnection(a, out double distance);
            var m = next != null && distance < 40 || next != null && a.Car.CurrentPathId == next.Id ? next.Connection.maneuver : LaneManeuver.Straight;
            a.Left = m == LaneManeuver.Left || m == LaneManeuver.UTurn;
            a.Right = m == LaneManeuver.Right;
        }

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
                    SpeedMps = car.CurrentSpeedMps, AccelerationMps2 = car.TargetAccelerationMps2, HeadingRad = car.HeadingRad,
                    LengthM = car.Profile.LengthM, WidthM = car.Profile.WidthM, Position = car.Position, SteeringRad = car.TargetSteeringAngleRad,
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
            public double WaitingSince = -1, BackgroundClock;
            public bool Hazard, Frozen, Left, Right;
            public string Decision = "free";
            public List<ManeuverNotice> Notices = new List<ManeuverNotice>();
        }
    }
}
