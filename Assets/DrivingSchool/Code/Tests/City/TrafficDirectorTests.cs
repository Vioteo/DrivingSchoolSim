using System;
using System.Collections.Generic;
using System.Linq;
using DrivingSchool.Contracts;
using DrivingSchool.Simulation.RoadGraph;
using DrivingSchool.Simulation.Traffic;
using NUnit.Framework;

namespace DrivingSchool.Tests
{
    public sealed class TrafficDirectorTests
    {
        static WorldDocumentV2 EqualCross() => CityLayouts.Compile(CityLayouts.CrossWithArms(3));

        static WorldDocumentV2 SignalCross()
        {
            var layout = CityLayouts.CrossWithArms(3);
            layout.signalPlans = new[] { CityLayouts.TwoPhasePlan() };
            return CityLayouts.Compile(layout);
        }

        [Test]
        public void TenMinutesOnAnEqualJunctionWithoutOverlapsOrGridlock()
        {
            var run = new TrafficRun(EqualCross());
            var seen = new HashSet<string>();
            var stoppedSince = new Dictionary<string, double>();
            run.Run(600, r =>
            {
                var s = r.Director.Snapshot;
                TrafficRun.AssertNoOverlaps(s);
                r.Director.Reservations.AssertConsistent();
                Assert.That(r.Director.VehicleCount, Is.LessThanOrEqualTo(8));
                foreach (var p in s.Participants)
                {
                    seen.Add(p.Id);
                    if (p.SpeedMps > 0.1) stoppedSince.Remove(p.Id);
                    else if (!stoppedSince.ContainsKey(p.Id)) stoppedSince[p.Id] = r.Time;
                    else Assert.That(r.Time - stoppedSince[p.Id], Is.LessThan(90), p.Id + " stuck: " + p.Decision);
                }
            });
            TestContext.WriteLine("vehicles seen in 10 min: " + seen.Count);
            Assert.That(seen.Count, Is.GreaterThan(20), "traffic keeps flowing");
        }

        [Test]
        public void NobodyEntersTheJunctionOnRed()
        {
            var run = new TrafficRun(SignalCross());
            var lastPath = new Dictionary<string, string>();
            run.Run(300, r =>
            {
                TrafficRun.AssertNoOverlaps(r.Director.Snapshot);
                foreach (var p in r.Director.Snapshot.Participants)
                {
                    lastPath.TryGetValue(p.Id, out var before);
                    lastPath[p.Id] = p.PathId;
                    if (before == null || before == p.PathId || !r.Director.Graph.Path(p.PathId).IsConnection) continue;
                    var aspect = r.Director.AspectOf(r.Director.Graph.Path(p.PathId).Connection.signalGroupId);
                    Assert.That(aspect, Is.Not.EqualTo(SignalAspect.Red).And.Not.EqualTo(SignalAspect.RedAmber), p.Id + " entered " + p.PathId + " at " + r.Time);
                }
            });
        }

        [Test]
        public void SameSeedSameHistoryAndVisitOrderDoesNotMatter()
        {
            string Run(bool reverse, int seed)
            {
                var run = new TrafficRun(EqualCross(), seed: seed);
                run.Director.ReverseProcessingOrder = reverse;
                var hashes = new List<string>();
                run.Run(120, r => { if (r.Tick % 250 == 0) hashes.Add(TrafficRun.SnapshotHash(r.Director.Snapshot)); });
                return string.Join(",", hashes);
            }
            var a = Run(false, 7);
            Assert.That(Run(false, 7), Is.EqualTo(a));
            Assert.That(Run(true, 7), Is.EqualTo(a));
            Assert.That(Run(false, 8), Is.Not.EqualTo(a));
        }

        [Test]
        public void VehiclesNeverAppearInFrontOfThePlayer()
        {
            var run = new TrafficRun(EqualCross());
            // Player parked on the south arm facing south, 42 m from the south edge: that edge is in view, the others are not.
            const double px = -1.825, pz = -30, heading = Math.PI;
            run.Director.SetPlayer(new PlayerSample { Present = true, X = px, Z = pz, HeadingRad = heading, LengthM = 4.4, WidthM = 1.8 });
            var profile = new TrafficProfile();
            var known = new HashSet<string>();
            run.Run(180, r =>
            {
                foreach (var p in r.Director.Snapshot.Participants.Where(p => p.Kind == ParticipantKind.Vehicle))
                {
                    if (!known.Add(p.Id)) continue;
                    double dx = p.Position.x - px, dz = p.Position.z - pz, dist = Math.Sqrt(dx * dx + dz * dz);
                    double rel = DistrictCompiler.NormalizeDeg((Math.Atan2(dx, dz) - heading) * 180 / Math.PI);
                    bool inCone = Math.Abs(rel) <= profile.ViewConeDeg / 2;
                    Assert.That(dist >= profile.HiddenSpawnDistanceM && (!inCone || dist > profile.MinSpawnDistanceM), Is.True, p.Id + " appeared visibly at " + dist.ToString("F0") + " m");
                }
            });
            Assert.That(known.Count, Is.GreaterThan(0));
        }

        [Test]
        public void NoVehiclesInUnloadedChunks()
        {
            var run = new TrafficRun(EqualCross());
            run.Director.OnChunkReady(0, 0); // only x >= 0, z >= 0: north and east arms
            run.Run(120, r =>
            {
                foreach (var p in r.Director.Snapshot.Participants)
                    Assert.That(p.Position.x >= -1e-6 && p.Position.z >= -1e-6, Is.True, p.Id + " at " + p.Position.x.ToString("F1") + "," + p.Position.z.ToString("F1"));
            });
        }

        [Test]
        public void ContactStopsTheCarWithHazardsAndWarnsTheOnesBehind()
        {
            var w = CityLayouts.Compile(CityLayouts.StraightChain(20));
            var run = new TrafficRun(w, new TrafficProfile { MaxVehicles = 0 });
            var a = run.Director.AddVehicle(CityLayouts.ForwardRoute(20), 0, 10, id: "a1");
            run.Director.AddVehicle(CityLayouts.ForwardRoute(20).Skip(4), 0, 10, id: "a0"); // 80 m ahead
            run.Run(1);
            run.Director.ReportContact("a0", "player");
            run.Step();
            Assert.That(run.Director.Snapshot.Events.Any(e => e.Kind == "contact" && e.ParticipantId == "a0"), Is.True);
            bool warned = false;
            // Notices appear in the snapshot of the tick they are delivered on.
            run.Run(2, r => warned |= r.Director.Snapshot.Notices.Any(n => n.ToId == a && n.Kind == NoticeKind.HazardAhead && n.FromId == "a0"));
            var hit = run.Director.Snapshot.Participants.Single(p => p.Id == "a0");
            Assert.That(hit.SpeedMps, Is.LessThan(0.01));
            Assert.That(hit.Hazard && hit.LeftIndicator && hit.RightIndicator, Is.True);
            Assert.That(warned, Is.True);
        }
    }
}
