using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DrivingSchool.Contracts;
using DrivingSchool.Rules;
using DrivingSchool.Simulation.RoadGraph;
using DrivingSchool.Simulation.Traffic;
using NUnit.Framework;

namespace DrivingSchool.Tests
{
    /// <summary>
    /// T74 — route vehicles on a closed 2+2 square (no junctions, a lane-change stretch on every side): lanes for route
    /// vehicles in the graph, stops and routes, buses serving their stops, cars kept off the bus lane, the player's
    /// facts and rules (ПДД 18.2, 18.3, 12.4, 12.5).
    /// </summary>
    public sealed class TransitTests
    {
        static readonly RoadKitCatalog Kits = new RoadKitCatalog();
        static ModuleTemplate T(string id) { Kits.TryGet(id, out var t); return t; }
        const string C = RoadKitTemplatesV2.Curve4, LC = RoadKitTemplatesV2.LaneChange4, S = RoadKitTemplatesV2.Straight4;
        // A side of the square: a lane-change stretch (40 m) and a straight (20 m) after a right curve.
        static readonly string[] Pieces = { C, LC, S, C, LC, S, C, LC, S, C, LC, S };
        const string StopA = "stop-a", StopB = "stop-b";

        /// <summary>The square: pieces p0..p11 joined End → Start, the "f" lanes run clockwise; f2 (outer) is a bus lane.</summary>
        internal static DistrictLayout Square(bool busLanes = true, bool stops = true, string busLaneOn = "f2")
        {
            var list = new List<ModuleInstance>(); var joins = new List<SocketJoin>();
            ModuleInstance prev = null;
            for (int i = 0; i < Pieces.Length; i++)
            {
                var m = prev == null ? new ModuleInstance { id = "p0", catalogId = Pieces[0] }
                    : DistrictCompiler.Dock("p" + i, T(Pieces[i]), "Socket_Start", prev, T(prev.catalogId), "Socket_End");
                if (prev != null) joins.Add(new SocketJoin { instanceA = prev.id, socketA = "Socket_End", instanceB = m.id, socketB = "Socket_Start" });
                list.Add(m); prev = m;
            }
            joins.Add(new SocketJoin { instanceA = prev.id, socketA = "Socket_End", instanceB = "p0", socketB = "Socket_Start" });
            var layout = new DistrictLayout { id = "transit-square", name = "Кольцо 2+2", instances = list.ToArray(), joins = joins.ToArray() };
            if (busLanes)
                layout.busLanes = list.SelectMany(m => m.catalogId == LC ? new[] { busLaneOn + "a", busLaneOn + "b" } : new[] { busLaneOn },
                    (m, lane) => new LayoutBusLane { instanceId = m.id, laneId = lane }).ToArray();
            if (stops)
            {
                layout.transitStops = new[]
                {
                    new LayoutTransitStop { id = StopA, name = "А", instanceId = "p2", laneId = "f2", atS = 18, lengthM = 15 },
                    new LayoutTransitStop { id = StopB, name = "Б", instanceId = "p8", laneId = "f2", atS = 18, lengthM = 15 },
                };
                layout.transitRoutes = new[] { new LayoutTransitRoute { id = "r1", name = "1", kind = TransitKind.Bus, stopIds = new[] { StopA, StopB } } };
            }
            return layout;
        }

        internal static WorldDocumentV2 SquareWorld() => DistrictCompiler.Compile(Square(), Kits);

        // ------------------------------------------------------------------ graph

        [Test]
        public void BusLanesAreFlaggedAndFencedOffBySolidLines()
        {
            var w = SquareWorld();
            var bus = w.lanes.Where(l => l.busLane).Select(l => l.id).ToList();
            Assert.That(bus, Has.Member("p2/f2").And.Member("p1/f2a").And.Member("p1/f2b").And.Member("p0/f2"));
            Assert.That(w.lanes.Where(l => l.busLane).All(l => !l.busLaneEntry && string.IsNullOrEmpty(l.rightNeighborId)), "outermost lanes, solid line");
            Assert.That(w.lanes.Any(l => l.id == "p2/f1" && l.busLane), Is.False);
            Assert.That(w.boundaries.Single(b => b.laneId == "p2/f2" && b.side == BoundarySide.Left).type, Is.EqualTo(MarkingType.Solid));
            Assert.That(w.boundaries.Single(b => b.laneId == "p2/f1" && b.side == BoundarySide.Right).type, Is.EqualTo(MarkingType.Solid));
            Assert.That(w.boundaries.Single(b => b.laneId == "p2/b1" && b.side == BoundarySide.Right).type, Is.EqualTo(MarkingType.Dashed), "the other way is untouched");
        }

        [Test]
        public void ABusLaneMustBeTheOutermostLane()
        {
            var e = Assert.Throws<InvalidDataException>(() => DistrictCompiler.Compile(Square(busLaneOn: "f1"), Kits));
            StringAssert.Contains("outermost", e.Message);
        }

        [Test]
        public void StopsGetTheirSignAndTheRouteIsAClosedWayThroughThem()
        {
            var w = SquareWorld();
            Assert.That(w.transitStops.Select(s => s.id), Is.EquivalentTo(new[] { StopA, StopB }));
            var a = w.transitStops.Single(s => s.id == StopA);
            Assert.That(a.laneId, Is.EqualTo("p2/f2"));
            var sign = w.signs.Single(s => s.id == a.signId);
            Assert.That(sign.code, Is.EqualTo("5.16"));
            Assert.That(sign.catalogId, Is.EqualTo("PT_Sign_5_16_BusStop"));
            var route = w.transitRoutes.Single();
            Assert.That(route.pathIds, Has.Member("p2/f2").And.Member("p8/f2"));
            // Closed: every path leads into the next, the last into the first (the validator checks it too).
            var index = new RoadGraphIndex(w);
            for (int i = 0; i < route.pathIds.Length; i++)
                Assert.That(index.Path(route.pathIds[i]).Next, Has.Member(route.pathIds[(i + 1) % route.pathIds.Length]), route.pathIds[i]);
            // It keeps to the bus lane: on the lane-change stretches it stays on f2.
            Assert.That(route.pathIds.Where(id => !index.Path(id).IsConnection).All(id => id.EndsWith("/f2") || id.EndsWith("/f2a") || id.EndsWith("/f2b")),
                string.Join(" ", route.pathIds));
        }

        [Test]
        public void ABrokenRouteIsRejectedByTheValidator()
        {
            var w = SquareWorld();
            var route = w.transitRoutes.Single();
            route.pathIds = route.pathIds.Where(id => id != "p5/f2").ToArray();
            var e = Assert.Throws<InvalidDataException>(() => WorldValidatorV2.Validate(w, SignCatalog.Codes));
            StringAssert.Contains("not a closed way", e.Message);
        }

        [Test]
        public void NoStoppingZoneRunsFifteenMetresRoundTheMarkedPlace()
        {
            var index = new RoadGraphIndex(SquareWorld());
            // The place: p2/f2 s 3..18 (marked 1.17.1); the zone 15 m either side, over into the lanes before and after.
            Assert.That(index.TransitStopZoneAt("p2/f2", 10), Is.EqualTo(StopA));
            Assert.That(index.TransitStopZoneAt("p2/f2", 0.5), Is.EqualTo(StopA));
            Assert.That(index.TransitStopZoneAt("p1/f2b", 3), Is.EqualTo(StopA), "12 m back: the short lane after the lane-change stretch");
            Assert.That(index.TransitStopZoneAt("p3/f2", 12), Is.EqualTo(StopA), "15 m past the sign at 18");
            Assert.That(index.TransitStopZoneAt("p3/f2", 14), Is.Null);
            Assert.That(index.TransitStopZoneAt("p2/f1", 10), Is.Null, "only the stop's lane");
        }

        // ------------------------------------------------------------------ traffic

        [Test]
        public void BusesServeTheirStopsAndCarsKeepOffTheBusLane()
        {
            var w = SquareWorld();
            var index = new RoadGraphIndex(w);
            var run = new TrafficRun(w, new TrafficProfile { MaxVehicles = 5, TransitVehiclesPerRoute = 2, SpawnIntervalSeconds = 1, LaneChangeShare = 1.0 }, seed: 3);
            var boarded = new HashSet<string>(); var buses = new HashSet<string>();
            bool doors = false, rightIntoStop = false; int departingTicks = 0, carTicks = 0;
            run.Run(240, r =>
            {
                TrafficRun.AssertNoOverlaps(r.Director.Snapshot);
                foreach (var p in r.Director.Snapshot.Participants.Where(x => x.Kind == ParticipantKind.Vehicle))
                {
                    var path = index.Path(p.PathId);
                    var lane = path.IsConnection ? index.Path(path.Connection.toLaneId).Lane : path.Lane;
                    if (p.RouteId == null)
                    {
                        carTicks++;
                        Assert.That(lane.busLane, Is.False, $"{p.Id} is on the bus lane: {p.PathId}");
                        continue;
                    }
                    buses.Add(p.Id);
                    if (p.Transit == TransitPhase.Boarding) { boarded.Add(p.StopId); doors |= p.DoorsOpen; }
                    if (p.Transit == TransitPhase.Arriving && !path.IsConnection) rightIntoStop |= p.RightIndicator && !p.LeftIndicator;
                    if (p.Transit == TransitPhase.Departing && !path.IsConnection)
                    {
                        departingTicks++;
                        Assert.That(p.LeftIndicator && !p.RightIndicator, $"{p.Id} moves off its stop without the left signal");
                    }
                }
            });
            Assert.That(buses.Count, Is.GreaterThanOrEqualTo(2), "two buses on the route");
            Assert.That(boarded, Is.EquivalentTo(new[] { StopA, StopB }), "both stops served");
            Assert.That(doors, "doors opened at a stop");
            Assert.That(rightIntoStop, "right signal pulling in");
            Assert.That(departingTicks, Is.GreaterThan(0));
            Assert.That(carTicks, Is.GreaterThan(1000), "cars drove too (with every lane change they could take)");
        }

        // ------------------------------------------------------------------ the player and ПДД 18.3

        /// <summary>Runs one bus until it signals to move off its stop; the player stands still far away meanwhile.</summary>
        static (TrafficRun run, PlayerRoadMonitor monitor) BusAboutToMoveOff()
        {
            var w = SquareWorld();
            var run = new TrafficRun(w, new TrafficProfile { MaxVehicles = 0, TransitVehiclesPerRoute = 1, SpawnIntervalSeconds = 1 }, seed: 5);
            var monitor = new PlayerRoadMonitor(run.Director);
            for (int i = 0; i < 20000; i++)
            {
                run.Step();
                monitor.Update();
                if (Bus(run).Transit == TransitPhase.Departing) return (run, monitor);
            }
            Assert.Fail("the bus never got to move off a stop");
            return default;
        }

        static ParticipantState Bus(TrafficRun run) =>
            run.Director.Snapshot.Participants.FirstOrDefault(p => p.RouteId != null) ?? new ParticipantState { Transit = TransitPhase.None };

        /// <summary>The player beside the bus: <paramref name="along"/> metres ahead of its centre, <paramref name="left"/> to its left.</summary>
        static PlayerSample Beside(ParticipantState bus, double along, double left, double speed)
        {
            double fx = Math.Sin(bus.HeadingRad), fz = Math.Cos(bus.HeadingRad);
            return new PlayerSample
            {
                Present = true, X = bus.Position.x + fx * along - fz * left, Z = bus.Position.z + fz * along + fx * left, Y = bus.Position.y,
                HeadingRad = bus.HeadingRad, SpeedMps = speed, LengthM = 4.4, WidthM = 1.8,
            };
        }

        static List<string> Drive(TrafficRun run, PlayerRoadMonitor monitor, CityRuleMonitor rules, Func<double, (double along, double left, double speed)> path, double seconds,
            Action<double, ParticipantState> each = null)
        {
            var events = new List<string>();
            var bus = Bus(run);
            for (double t = 0; t < seconds; t += TrafficRun.Dt)
            {
                var (along, left, speed) = path(t);
                run.Director.SetPlayer(Beside(bus, along, left, speed));
                run.Step();
                events.AddRange(rules.Update(monitor.Update()).Select(e => e.ruleId));
                each?.Invoke(along, Bus(run));
            }
            return events;
        }

        [Test]
        public void GoingRoundABusThatMovesOffIsNotGivingWay()
        {
            var (run, monitor) = BusAboutToMoveOff();
            var rules = new CityRuleMonitor();
            // Waits right behind it in its lane, then swerves left and drives past it.
            double busSpeedAlongside = 0;
            var events = Drive(run, monitor, rules, t => t < 1 ? (-11.0, 0.0, 0.0) : (-11.0 + (t - 1) * 5, Math.Min(3.2, (t - 1) * 2), 5.0), 6,
                (along, bus) => { if (along > -6 && along < 6) busSpeedAlongside = Math.Max(busSpeedAlongside, bus.SpeedMps); });
            Assert.That(events, Has.Member(CityRuleMonitor.RuleYieldBus));
            Assert.That(busSpeedAlongside, Is.LessThan(0.5), "the bus waited while the car went past it (ПДД 18.3: it moves off when the others give way)");
        }

        [Test]
        public void PassingInTheNextLaneWithoutQueueingIsFine()
        {
            var (run, monitor) = BusAboutToMoveOff();
            var rules = new CityRuleMonitor();
            // In the next lane all the way (3.9 m to the left of the bus that stands 0.4 m towards the kerb).
            var events = Drive(run, monitor, rules, t => (-40 + t * 8, 3.9, 8.0), 9);
            Assert.That(events, Has.No.Member(CityRuleMonitor.RuleYieldBus));
        }

        [Test]
        public void WaitingBehindTheBusLetsItGo()
        {
            var (run, monitor) = BusAboutToMoveOff();
            var rules = new CityRuleMonitor();
            var bus = Bus(run);
            var events = new List<string>();
            var stand = Beside(bus, -11, 0, 0);
            for (int i = 0; i < 600; i++)
            {
                run.Director.SetPlayer(stand);
                run.Step();
                events.AddRange(rules.Update(monitor.Update()).Select(e => e.ruleId));
            }
            Assert.That(events, Has.No.Member(CityRuleMonitor.RuleYieldBus));
            Assert.That(Bus(run).Transit, Is.Not.EqualTo(TransitPhase.Departing), "nobody went past: the bus moved off");
        }

        // ------------------------------------------------------------------ rules on facts

        static DriverRoadFacts F(double t, float speed = 10f) => new DriverRoadFacts
        {
            Seconds = t, OnRoad = true, SpeedMps = speed, SpeedLimitKph = 60, RightmostLane = true, LanesInDirection = 2,
            LeadGapM = float.PositiveInfinity, JunctionFromAllowedLane = true,
        };

        static List<string> Run(IEnumerable<DriverRoadFacts> facts)
        {
            var m = new CityRuleMonitor();
            return facts.SelectMany(f => m.Update(f)).Select(e => e.ruleId).ToList();
        }

        static IEnumerable<DriverRoadFacts> Seconds(double from, double to, Func<double, DriverRoadFacts> make)
        {
            for (double t = from; t < to; t += 0.1) yield return make(t);
        }

        [Test]
        public void DrivingOnABusLaneBehindASolidLineIsReportedOnce()
        {
            var events = Run(Seconds(0, 5, t => { var f = F(t); f.OnBusLane = true; return f; }));
            Assert.That(events, Is.EqualTo(new[] { CityRuleMonitor.RuleBusLane }));
            Assert.That(Run(Seconds(0, 0.8, t => { var f = F(t); f.OnBusLane = true; return f; })), Is.Empty, "crossing it for a moment");
            Assert.That(Run(Seconds(0, 5, t => { var f = F(t); f.OnBusLaneEntry = true; return f; })), Is.Empty, "behind a broken line it is allowed");
        }

        [Test]
        public void FromTheBusLaneOnlyARightTurn()
        {
            DriverRoadFacts Out(LaneManeuver m) { var f = F(10); f.JunctionManeuver = m; f.JunctionEnteredSeconds = 8; f.JunctionFromBusLane = true; f.RightIndicator = m == LaneManeuver.Right; return f; }
            var signal = Seconds(5, 10, t => { var f = F(t); f.RightIndicator = true; return f; }).ToList();
            Assert.That(Run(signal.Concat(new[] { Out(LaneManeuver.Right) })), Has.No.Member(CityRuleMonitor.RuleBusLane));
            Assert.That(Run(new[] { Out(LaneManeuver.Straight) }), Has.Member(CityRuleMonitor.RuleBusLane));
        }

        [Test]
        public void StoppingAndParkingAtABusStop()
        {
            DriverRoadFacts At(double t, bool waiting = false)
            {
                var f = F(t, 0f); f.StopPlace = RoadStopPlace.TransitStop; f.TransitStopId = StopA; f.WaitingForTraffic = waiting; return f;
            }
            Assert.That(Run(Seconds(0, 4, t => At(t))), Is.EqualTo(new[] { CityRuleMonitor.RuleTransitStopZone }));
            Assert.That(Run(Seconds(0, 10, t => At(t, waiting: true))), Is.Empty, "queueing behind a bus at the stop");
            var parked = Run(Seconds(0, CityRuleMonitor.ParkingSeconds + 1, t => At(t)));
            Assert.That(parked, Is.EqualTo(new[] { CityRuleMonitor.RuleTransitStopZone, CityRuleMonitor.RuleTransitStopParking }));
            var bus = Run(Seconds(0, 4, t => { var f = F(t, 0f); f.StopPlace = RoadStopPlace.BusLane; return f; }));
            Assert.That(bus, Is.EqualTo(new[] { CityRuleMonitor.RuleBusLaneStop }));
        }

        [Test]
        public void PassingADepartingBusIsAViolation()
        {
            var f = F(1); f.PassedDepartingBus = true; f.PassedBusRoute = "42";
            Assert.That(Run(new[] { F(0), f }), Is.EqualTo(new[] { CityRuleMonitor.RuleYieldBus }));
        }
    }
}
