using System.Linq;
using DrivingSchool.Contracts;
using DrivingSchool.Editor;
using DrivingSchool.Simulation.RoadGraph;
using DrivingSchool.Simulation.Traffic;
using NUnit.Framework;
using UnityEngine;

namespace DrivingSchool.Tests
{
    /// <summary>T51, T55/T56, T65, T66: the town district of the test range compiles, is closed into loops and its traffic keeps moving.</summary>
    public sealed class TestRangeDistrictTests
    {
        static WorldDocumentV2 World() => DistrictCompiler.Compile(DistrictBuilder.Layout(Vector3.zero), new RoadKitCatalog());

        [Test]
        public void TheLayoutCompilesWithSignalsSignsAndCrossings()
        {
            var w = World();
            // X1…X7 and X10, four roundabout junctions, the level crossing, two lane-change stretches (T66).
            Assert.That(w.junctions.Length, Is.EqualTo(15));
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
            Assert.That(w.spawnPoints.Count(s => s.role == SpawnRole.Vehicle), Is.EqualTo(1), "the only open road end is the entrance from road A (T66)");
            // T66: no zebras at the unregulated junctions; the streets have mid-block ones instead.
            foreach (var x in new[] { "X2/", "X4/", "X5/", "X6/", "X7/", "X10/" })
                Assert.That(w.crossings.Count(c => c.id.StartsWith(x)), Is.Zero, x + " has no zebra");
            Assert.That(w.crossings.Count(c => c.id.EndsWith("/crossing") && !c.id.StartsWith("X") && !c.id.StartsWith("R/")), Is.GreaterThanOrEqualTo(7), "mid-block zebras");
        }

        [Test]
        public void FiveMinutesOfTrafficAndPedestriansWithoutOverlapsOrGridlock()
        {
            var run = new TrafficRun(World(), new TrafficProfile { MaxVehicles = 12, MaxPedestrians = 16 }, seed: 7);
            var stoppedSince = new System.Collections.Generic.Dictionary<string, double>();
            int vehiclesSeen = 0; var seen = new System.Collections.Generic.HashSet<string>();
            var travelled = new System.Collections.Generic.Dictionary<string, double>();
            run.Run(300, r =>
            {
                TrafficRun.AssertNoOverlaps(r.Director.Snapshot);
                foreach (var p in r.Director.Snapshot.Participants)
                {
                    if (p.Kind != ParticipantKind.Vehicle) continue;
                    if (seen.Add(p.Id)) { vehiclesSeen++; travelled[p.Id] = 0; }
                    travelled[p.Id] += p.SpeedMps * TrafficRun.Dt;
                    if (p.SpeedMps > 0.1) stoppedSince.Remove(p.Id);
                    else if (!stoppedSince.ContainsKey(p.Id)) stoppedSince[p.Id] = r.Time;
                    else Assert.That(r.Time - stoppedSince[p.Id], Is.LessThan(90), p.Id + " stuck: " + p.Decision);
                }
            });
            Assert.That(vehiclesSeen, Is.GreaterThanOrEqualTo(12), "cars come in mid-block too, not only from road A (T66)");
            Assert.That(travelled.Values.Count(m => m > 300), Is.GreaterThanOrEqualTo(10), "traffic goes round the loops: " + string.Join(", ", travelled.Select(kv => kv.Key + " " + kv.Value.ToString("F0"))));
            Assert.That(run.Director.Pedestrians.People.Count, Is.EqualTo(16));
        }

        [Test]
        public void TheTownIsClosedIntoLoops()
        {
            // T66: from any lane a car can get to any other one; only the road A arm leads in and out.
            var w = World();
            var next = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>();
            void Edge(string a, string b) { if (!next.TryGetValue(a, out var l)) next[a] = l = new System.Collections.Generic.List<string>(); l.Add(b); }
            foreach (var l in w.lanes) foreach (var s in l.successors) Edge(l.id, s);
            foreach (var c in w.connections) { Edge(c.fromLaneId, c.id); Edge(c.id, c.toLaneId); }
            // The road A arm: its lanes and the ring's east entry and exit with their connections.
            var roadA = new System.Collections.Generic.HashSet<string>(w.lanes.Select(l => l.id).Where(id => id.StartsWith("re0/") || id.StartsWith("re1/") || id == "R/East.in" || id == "R/East.out"));
            roadA.UnionWith(w.connections.Where(c => roadA.Contains(c.fromLaneId) || roadA.Contains(c.toLaneId)).Select(c => c.id).ToList());
            var nodes = w.lanes.Select(l => l.id).Concat(w.connections.Select(c => c.id)).Where(id => !roadA.Contains(id)).ToList();
            System.Collections.Generic.HashSet<string> Reach(string from, bool forward)
            {
                var back = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>();
                if (!forward) foreach (var kv in next) foreach (var b in kv.Value) { if (!back.TryGetValue(b, out var l)) back[b] = l = new System.Collections.Generic.List<string>(); l.Add(kv.Key); }
                var seen = new System.Collections.Generic.HashSet<string> { from }; var q = new System.Collections.Generic.Queue<string>(); q.Enqueue(from);
                while (q.Count > 0) { var x = q.Dequeue(); if ((forward ? next : back).TryGetValue(x, out var outs)) foreach (var y in outs) if (seen.Add(y)) q.Enqueue(y); }
                return seen;
            }
            var start = "X1/South.in1";
            var there = Reach(start, true); var back2 = Reach(start, false);
            var cut = nodes.Where(n => !there.Contains(n) || !back2.Contains(n)).ToList();
            Assert.That(cut, Is.Empty, "dead ends: " + string.Join(", ", cut.Take(12)));
            Assert.That(w.spawnPoints.Where(s => s.role == SpawnRole.Vehicle).Select(s => s.pathId), Is.EqualTo(new[] { DistrictBuilder.EntryLane }));
        }
    }
}
