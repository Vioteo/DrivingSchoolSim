using System.Linq;
using DrivingSchool.Contracts;
using DrivingSchool.Editor;
using DrivingSchool.Simulation.RoadGraph;
using DrivingSchool.Simulation.Traffic;
using NUnit.Framework;
using UnityEngine;

namespace DrivingSchool.Tests
{
    /// <summary>T51, T55/T56: the town district of the test range compiles and its traffic keeps moving.</summary>
    public sealed class TestRangeDistrictTests
    {
        static WorldDocumentV2 World() => DistrictCompiler.Compile(DistrictBuilder.Layout(Vector3.zero), new RoadKitCatalog());

        [Test]
        public void TheLayoutCompilesWithSignalsSignsAndCrossings()
        {
            var w = World();
            // X1, X2, four roundabout junctions, the level crossing, three lane-change stretches.
            Assert.That(w.junctions.Length, Is.EqualTo(10));
            Assert.That(w.signalPlans.Length, Is.EqualTo(1), "X1 has traffic lights");
            Assert.That(w.signals.Count(s => s.catalogId == "DS_Signal_Pedestrian"), Is.EqualTo(8), "a pedestrian head at both ends of 4 crossings");
            Assert.That(w.signals.Count(s => s.catalogId == "DS_Signal_Vehicle"), Is.EqualTo(4));
            Assert.That(w.lanes.Count(l => l.allowedManeuvers == (LaneManeuver.Straight | LaneManeuver.Left)), Is.GreaterThanOrEqualTo(6), "inner lanes of 2+2 approaches");
            var codes = w.signs.Select(s => s.code).Distinct().ToList();
            Assert.That(codes, Is.SupersetOf(new[] { "1.1", "2.1", "2.4", "2.5", "3.24", "3.27", "3.28", "4.3", "5.19.1", "5.23.1", "5.24.1", "6.4" }));
            Assert.That(w.approaches.Where(a => a.junctionId.StartsWith("X2/")).Select(a => a.priority).Distinct(), Is.EquivalentTo(new[] { ApproachPriority.Main, ApproachPriority.Secondary }));
            Assert.That(w.approaches.Where(a => a.junctionId.StartsWith("R/")).Count(a => a.priority == ApproachPriority.Main), Is.EqualTo(4), "the ring goes first at every entry");
            Assert.That(w.signalGroups.Count(g => g.junctionId.StartsWith(DistrictBuilder.RailInstance + "/")), Is.EqualTo(3), "level crossing: two lanes and the walkways");
            Assert.That(w.lanes.Any(l => l.id == DistrictBuilder.EntryLane), "entrance from road A");
            Assert.That(w.spawnPoints.Count(s => s.role == SpawnRole.Vehicle), Is.EqualTo(12), "open road ends: four 2+2 ends with two lanes, four 1+1");
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
