using System.Linq;
using DrivingSchool.Contracts;
using DrivingSchool.Editor;
using DrivingSchool.Simulation.RoadGraph;
using DrivingSchool.Simulation.Traffic;
using NUnit.Framework;
using UnityEngine;

namespace DrivingSchool.Tests
{
    /// <summary>T51, T55/T56, T65: the town district of the test range compiles and its traffic keeps moving.</summary>
    public sealed class TestRangeDistrictTests
    {
        static WorldDocumentV2 World() => DistrictCompiler.Compile(DistrictBuilder.Layout(Vector3.zero), new RoadKitCatalog());

        [Test]
        public void TheLayoutCompilesWithSignalsSignsAndCrossings()
        {
            var w = World();
            // X1…X5, four roundabout junctions, the level crossing, four lane-change stretches (west part since T65).
            Assert.That(w.junctions.Length, Is.EqualTo(14));
            Assert.That(w.signalPlans.Length, Is.EqualTo(2), "X1 and X3 have traffic lights");
            Assert.That(w.signals.Count(s => s.catalogId == "DS_Signal_Pedestrian"), Is.EqualTo(16), "a pedestrian head at both ends of 4 crossings, two junctions");
            Assert.That(w.signals.Count(s => s.catalogId == "DS_Signal_Vehicle"), Is.EqualTo(8));
            Assert.That(w.lanes.Count(l => l.allowedManeuvers == (LaneManeuver.Straight | LaneManeuver.Left)), Is.GreaterThanOrEqualTo(6), "inner lanes of 2+2 approaches");
            var codes = w.signs.Select(s => s.code).Distinct().ToList();
            Assert.That(codes, Is.SupersetOf(new[] { "1.1", "1.17", "2.1", "2.4", "2.5", "3.24", "3.25", "3.27", "3.28", "4.3", "5.15.1", "5.19.1", "5.20", "5.23.1", "5.24.1", "6.4" }));
            Assert.That(w.signs.Where(s => s.code == "3.24").Select(s => s.value).Distinct(), Is.SupersetOf(new[] { "20", "30", "40", "60" }), "several speed limits");
            Assert.That(w.crossings.Count(c => c.id.StartsWith("b4/")), Is.EqualTo(1), "a zebra away from junctions on the bump street");
            Assert.That(w.approaches.Where(a => a.junctionId.StartsWith("X2/")).Select(a => a.priority).Distinct(), Is.EquivalentTo(new[] { ApproachPriority.Main, ApproachPriority.Secondary }));
            Assert.That(w.approaches.Where(a => a.junctionId.StartsWith("R/")).Count(a => a.priority == ApproachPriority.Main), Is.EqualTo(4), "the ring goes first at every entry");
            Assert.That(w.signalGroups.Count(g => g.junctionId.StartsWith(DistrictBuilder.RailInstance + "/")), Is.EqualTo(3), "level crossing: two lanes and the walkways");
            Assert.That(w.lanes.Any(l => l.id == DistrictBuilder.EntryLane), "entrance from road A");
            Assert.That(w.spawnPoints.Count(s => s.role == SpawnRole.Vehicle), Is.EqualTo(18), "open road ends: six 2+2 ends with two lanes, six 1+1");
        }

        [Test]
        public void FiveMinutesOfTrafficAndPedestriansWithoutOverlapsOrGridlock()
        {
            var run = new TrafficRun(World(), new TrafficProfile { MaxVehicles = 12, MaxPedestrians = 16 }, seed: 7);
            var stoppedSince = new System.Collections.Generic.Dictionary<string, double>();
            int vehiclesSeen = 0; var seen = new System.Collections.Generic.HashSet<string>();
            run.Run(300, r =>
            {
                TrafficRun.AssertNoOverlaps(r.Director.Snapshot);
                foreach (var p in r.Director.Snapshot.Participants)
                {
                    if (p.Kind != ParticipantKind.Vehicle) continue;
                    if (seen.Add(p.Id)) vehiclesSeen++;
                    if (p.SpeedMps > 0.1) stoppedSince.Remove(p.Id);
                    else if (!stoppedSince.ContainsKey(p.Id)) stoppedSince[p.Id] = r.Time;
                    else Assert.That(r.Time - stoppedSince[p.Id], Is.LessThan(90), p.Id + " stuck: " + p.Decision);
                }
            });
            Assert.That(vehiclesSeen, Is.GreaterThan(18), "traffic flows through the district");
            Assert.That(run.Director.Pedestrians.People.Count, Is.EqualTo(16));
        }
    }
}
