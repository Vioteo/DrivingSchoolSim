using System.Collections.Generic;
using System.Linq;
using DrivingSchool.Contracts;
using DrivingSchool.Editor;
using DrivingSchool.Simulation.RoadGraph;
using DrivingSchool.Simulation.Traffic;
using NUnit.Framework;
using UnityEngine;

namespace DrivingSchool.Tests
{
    /// <summary>T74 on the town district of the test range: the bus lane of the outer loop, routes 42 and 107, their stops.</summary>
    public sealed class DistrictTransitTests
    {
        static WorldDocumentV2 world;
        static WorldDocumentV2 World() => world ?? (world = DistrictCompiler.Compile(DistrictBuilder.Layout(Vector3.zero), new RoadKitCatalog()));

        [Test]
        public void TheOuterLoopHasABusLaneEachWay()
        {
            var w = World();
            var bus = w.lanes.Where(l => l.busLane).ToList();
            Assert.That(bus.Count, Is.EqualTo(16), "ol0..ol7, outer lane each way");
            Assert.That(bus.Where(l => l.busLaneEntry).Select(l => l.id), Is.EquivalentTo(new[] { "ol0/b2", "ol0/f2", "ol7/b2", "ol7/f2" }),
                "broken line on the pieces touching X7 and X10");
            Assert.That(w.signs.Count(s => s.code == "5.14"), Is.EqualTo(2));
            Assert.That(w.signs.Count(s => s.code == "5.14.1"), Is.EqualTo(2));
            Assert.That(w.signs.Count(s => s.code == "5.16"), Is.EqualTo(6));
        }

        [Test]
        public void RoutesGoRoundThroughTheirStops()
        {
            var w = World();
            var index = new RoadGraphIndex(w);
            Assert.That(w.transitRoutes.Select(r => r.name), Is.EquivalentTo(new[] { "42", "107" }));
            foreach (var r in w.transitRoutes)
            {
                for (int i = 0; i < r.pathIds.Length; i++)
                    Assert.That(index.Path(r.pathIds[i]).Next, Has.Member(r.pathIds[(i + 1) % r.pathIds.Length]), r.id + " at " + r.pathIds[i]);
                foreach (var sid in r.stopIds) Assert.That(r.pathIds, Has.Member(w.transitStops.Single(s => s.id == sid).laneId), r.id + " misses " + sid);
                // No U-turns, and a route vehicle never leaves the bus lane where it has one (it goes straight through the loop).
                Assert.That(r.pathIds.Select(index.Path).Where(p => p.IsConnection).All(p => p.Connection.maneuver != LaneManeuver.UTurn), r.id);
            }
            var r42 = w.transitRoutes.Single(r => r.name == "42");
            Assert.That(r42.pathIds, Has.Member("ol1/b2").And.Member("ol5/b2").And.Member("w1/f2").And.Member("e0/b2"));
            var r107 = w.transitRoutes.Single(r => r.name == "107");
            Assert.That(r107.pathIds, Has.Member("ol1/f2").And.Member("ol5/f2").And.Member("w1/b2"));
        }

        [Test]
        public void BusesAndMinibusesRunTheirRoutesAndCarsKeepOffTheBusLane()
        {
            var w = World();
            var index = new RoadGraphIndex(w);
            var run = new TrafficRun(w, new TrafficProfile { MaxVehicles = 12, TransitVehiclesPerRoute = 2 }, seed: 17);
            var served = new HashSet<string>(); var kinds = new HashSet<TransitKind>();
            run.Run(400, r =>
            {
                foreach (var p in r.Director.Snapshot.Participants.Where(x => x.Kind == ParticipantKind.Vehicle))
                {
                    var path = index.Path(p.PathId);
                    var lane = path.IsConnection ? index.Path(path.Connection.toLaneId).Lane : path.Lane;
                    if (p.RouteId == null)
                    {
                        Assert.That(lane.busLane && !lane.busLaneEntry, Is.False, $"{p.Id} drives on the bus lane {p.PathId}");
                        continue;
                    }
                    kinds.Add(p.RouteKind);
                    if (p.Transit == TransitPhase.Boarding) served.Add(p.StopId);
                }
            });
            Assert.That(kinds, Is.EquivalentTo(new[] { TransitKind.Bus, TransitKind.Minibus }));
            Assert.That(served.Count, Is.GreaterThanOrEqualTo(4), "stops served: " + string.Join(", ", served));
        }
    }
}
