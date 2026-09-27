using System;
using System.Collections.Generic;
using System.Linq;
using DrivingSchool.Contracts;
using DrivingSchool.Simulation.RoadGraph;
using DrivingSchool.Simulation.Traffic;
using NUnit.Framework;

namespace DrivingSchool.Tests
{
    /// <summary>Pedestrians on the sidewalk network and at crossings; vehicles yield to them (T51).</summary>
    public sealed class PedestrianSimulationTests
    {
        const string North = "c/crossing.North";

        static WorldDocumentV2 EqualCross() => CityLayouts.Compile(CityLayouts.CrossWithArms(2));

        static WorldDocumentV2 SignalCross()
        {
            var layout = CityLayouts.CrossWithArms(2);
            layout.signalPlans = new[] { CityLayouts.TwoPhasePlan() };
            return CityLayouts.Compile(layout);
        }

        static TrafficProfile Quiet(int pedestrians = 0) => new TrafficProfile { MaxVehicles = 0, MaxPedestrians = pedestrians };

        /// <summary>north1/f → north0/f → c/North.in → straight across → c/South.out → south arm.</summary>
        static List<string> RouteNorthToSouth(RoadGraphIndex g)
        {
            var route = new List<string> { "north1/f" };
            for (int guard = 0; guard < 8; guard++)
            {
                var last = g.Path(route[route.Count - 1]);
                if (last.Next.Length == 0) break;
                var next = last.Next.Select(g.Path).FirstOrDefault(p => !p.IsConnection || p.Connection.maneuver == LaneManeuver.Straight);
                if (next == null) break;
                route.Add(next.Id);
            }
            return route;
        }

        [Test]
        public void EveryCrossingIsReachableFromTheSidewalksAtBothEnds()
        {
            var sim = new PedestrianSimulation(new RoadGraphIndex(EqualCross()), 1);
            foreach (var arm in CityLayouts.Arms)
                Assert.That(sim.LinkCount("c/crossing." + arm), Is.GreaterThanOrEqualTo(2), arm);
        }

        [Test]
        public void WalkersStayOffTheRoadwayExceptOnCrossings()
        {
            var world = EqualCross();
            var run = new TrafficRun(world, Quiet(10), seed: 3);
            var lines = run.Director.Graph.Paths.Select(p => p.Line).ToList();
            var kerbs = world.crossings.SelectMany(c => new[] { c.a, c.b }).ToList();
            int crossed = 0;
            run.Run(240, r =>
            {
                foreach (var p in r.Director.Pedestrians.People)
                {
                    if (p.Phase == PedestrianPhase.Crossing) { crossed++; continue; }
                    // The kerb at a crossing end is the road edge; turning lanes pass close to it at a junction corner.
                    if (kerbs.Any(k => Polyline.Distance2D(k, p.Position) < 1.0)) continue;
                    double nearest = lines.Min(l => DistanceTo(l, p.Position));
                    Assert.That(nearest, Is.GreaterThan(1.5), p.Id + " walks on the road at " + p.Position.x.ToString("F1") + ", " + p.Position.z.ToString("F1") + " (" + p.WalkId + ")");
                }
            });
            Assert.That(run.Director.Pedestrians.People.Count, Is.EqualTo(10));
            Assert.That(crossed, Is.GreaterThan(0), "someone used a crossing in 4 minutes");
        }

        static double DistanceTo(Polyline l, Vec3d q)
        {
            double best = double.MaxValue; var pts = l.Points;
            for (int i = 0; i < pts.Count - 1; i++)
            {
                double ex = pts[i + 1].x - pts[i].x, ez = pts[i + 1].z - pts[i].z, len2 = ex * ex + ez * ez;
                double t = len2 > 0 ? Math.Max(0, Math.Min(1, ((q.x - pts[i].x) * ex + (q.z - pts[i].z) * ez) / len2)) : 0;
                double dx = q.x - pts[i].x - ex * t, dz = q.z - pts[i].z - ez * t;
                best = Math.Min(best, Math.Sqrt(dx * dx + dz * dz));
            }
            return best;
        }

        [Test]
        public void OnASignalledCrossingPedestriansStartOnlyOnGreen()
        {
            var run = new TrafficRun(SignalCross(), Quiet());
            var peds = run.Director.Pedestrians;
            var id = peds.PlaceAtCrossing(North);
            string group = run.World.crossings.First(c => c.id == North).signalGroupId;
            Assert.That(group, Is.Not.Null.And.Not.Empty);
            var before = PedestrianPhase.Waiting; bool started = false;
            run.Run(90, r =>
            {
                var p = peds.Find(id);
                if (before == PedestrianPhase.Waiting && p.Phase == PedestrianPhase.Crossing)
                {
                    started = true;
                    Assert.That(r.Director.AspectOf(group), Is.EqualTo(SignalAspect.Green), "started at " + r.Time.ToString("F1"));
                }
                before = p.Phase;
            });
            Assert.That(started, Is.True, "crossed within 90 s");
        }

        [Test]
        public void OnAnUnsignalledCrossingThePedestrianLetsAFastCloseCarPass()
        {
            var run = new TrafficRun(EqualCross(), Quiet());
            var route = RouteNorthToSouth(run.Director.Graph);
            run.Director.AddVehicle(route.Skip(1), 8, 12);           // north0/f, ~11 m before the crossing at 43 km/h
            var id = run.Director.Pedestrians.PlaceAtCrossing(North);
            run.Run(0.5);
            Assert.That(run.Director.Pedestrians.Find(id).Phase, Is.EqualTo(PedestrianPhase.Waiting), "must not step in front of the car");
            run.Run(10);
            Assert.That(run.Director.Pedestrians.Find(id).Phase, Is.Not.EqualTo(PedestrianPhase.Waiting), "crosses once the car has gone");
        }

        [Test]
        public void ACarStopsBeforeAnOccupiedCrossingAndGoesOnAfterwards()
        {
            var run = new TrafficRun(EqualCross(), Quiet());
            var g = run.Director.Graph;
            var route = RouteNorthToSouth(g);
            var carId = run.Director.AddVehicle(route, 0, 10);        // ~44 m before the crossing: the pedestrian may go
            run.Director.Pedestrians.PlaceAtCrossing(North, walkSpeedMps: 0.8);   // slow: on the crossing when the car arrives
            var crossing = run.World.crossings.First(c => c.id == North);
            string conn = crossing.laneIds.First(route.Contains);
            double sc = TrafficDirector.CrossingPoint(g.Path(conn).Line, crossing);
            bool waited = false, passed = false;
            run.Run(40, r =>
            {
                TrafficRun.AssertNoOverlaps(r.Director.Snapshot);
                if (!r.Director.Snapshot.Participants.Any(x => x.Id == carId)) { passed = true; return; }   // left the district
                var car = r.Director.Vehicle(carId);
                double front = car.DistanceAlongRoute(conn, sc);        // centre → crossing centre line; +inf once past
                bool occupied = r.Director.Pedestrians.IsCrossingOccupied(North);
                if (occupied && !double.IsPositiveInfinity(front))
                    Assert.That(front - car.Profile.LengthM / 2, Is.GreaterThan(crossing.widthM / 2), "car front on the occupied crossing at " + r.Time.ToString("F1"));
                if (occupied && car.CurrentSpeedMps < 0.2) waited = true;
                if (double.IsPositiveInfinity(front) || car.Finished) passed = true;
            });
            Assert.That(waited, Is.True, "the car stopped for the pedestrian");
            Assert.That(passed, Is.True, "and drove on afterwards");
        }

        [Test]
        public void AKnockedDownPedestrianLiesStillAndCarsStopForThemUntilRemoved()
        {
            var run = new TrafficRun(EqualCross(), new TrafficProfile { MaxVehicles = 0, DownedPedestrianSeconds = 30 });
            var peds = run.Director.Pedestrians;
            var id = peds.PlaceAtCrossing(North, walkSpeedMps: 1.0);
            run.Run(3);                                                  // on the crossing now
            Assert.That(peds.Find(id).Phase, Is.EqualTo(PedestrianPhase.Crossing));
            run.Director.KnockPedestrian(id, TrafficDirector.PlayerId);
            var lying = peds.Find(id).Position;
            run.Step();
            Assert.That(run.Director.Snapshot.Events.Any(e => e.Kind == "pedestrian-hit" && e.ParticipantId == id), Is.True);
            var car = run.Director.AddVehicle(RouteNorthToSouth(run.Director.Graph), 0, 10);
            run.Run(20);
            Assert.That(Polyline.Distance2D(peds.Find(id).Position, lying), Is.LessThan(1e-6), "does not walk on");
            Assert.That(peds.IsCrossingOccupied(North), Is.True);
            Assert.That(run.Director.Vehicle(car).CurrentSpeedMps, Is.LessThan(0.2f), "the car waits before the person");
            run.Run(15);                                                  // removed after 30 s
            Assert.That(peds.Find(id), Is.Null);
            run.Run(10);
            Assert.That(run.Director.Snapshot.Participants.Any(p => p.Id == car && p.SpeedMps > 1) || !run.Director.Snapshot.Participants.Any(p => p.Id == car), Is.True, "and drives on");
        }

        [Test]
        public void SameSeedSamePedestrians()
        {
            string Run()
            {
                var run = new TrafficRun(EqualCross(), Quiet(6), seed: 9);
                run.Run(60);
                return string.Join(";", run.Director.Pedestrians.People.Select(p => p.Id + ":" + p.Position.x.ToString("F3") + "," + p.Position.z.ToString("F3") + ":" + p.Phase));
            }
            Assert.That(Run(), Is.EqualTo(Run()));
        }
    }
}
