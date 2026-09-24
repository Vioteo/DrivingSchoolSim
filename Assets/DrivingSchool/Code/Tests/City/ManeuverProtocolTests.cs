using System;
using System.Collections.Generic;
using System.Linq;
using DrivingSchool.Contracts;
using DrivingSchool.Simulation.RoadGraph;
using DrivingSchool.Simulation.Traffic;
using NUnit.Framework;

namespace DrivingSchool.Tests
{
    public sealed class ManeuverProtocolTests
    {
        static readonly TrafficProfile NoSpawn = new TrafficProfile { MaxVehicles = 0 };

        // Routes into the cross from each arm with 2 straights (arm lanes run towards the cross as "f").
        static string[] Into(string arm, string turnTo) => new[]
        {
            arm + "1/f", arm + "0/f", "c/" + Cap(arm) + ".in", "c/c:" + Cap(arm) + ".in>" + Cap(turnTo) + ".out", "c/" + Cap(turnTo) + ".out", turnTo + "0/b", turnTo + "1/b",
        };
        static string Cap(string s) => char.ToUpperInvariant(s[0]) + s.Substring(1);

        static TrafficRun Cross(Action<DistrictLayout> tweak = null)
        {
            var layout = CityLayouts.CrossWithArms(2);
            tweak?.Invoke(layout);
            return new TrafficRun(CityLayouts.Compile(layout), NoSpawn);
        }

        static List<string> EntryOrder(TrafficRun run, double seconds)
        {
            var order = new List<string>();
            run.Run(seconds, r =>
            {
                foreach (var p in r.Director.Snapshot.Participants)
                    if (p.PathId != null && r.Director.Graph.Path(p.PathId).IsConnection && !order.Contains(p.Id)) order.Add(p.Id);
                TrafficRun.AssertNoOverlaps(r.Director.Snapshot);
            });
            return order;
        }

        [Test]
        public void SimultaneousRequestsForOneZoneGetExactlyOnePermit()
        {
            var run = Cross();
            run.Director.AddVehicle(Into("south", "north"), 10, 8, id: "s");
            run.Director.AddVehicle(Into("east", "west"), 10, 8, id: "e");
            bool checkedOnce = false;
            run.Run(10, r =>
            {
                var permits = r.Director.Snapshot.Permits;
                if (checkedOnce || permits.Count < 2) return;
                checkedOnce = true;
                Assert.That(permits.Count(p => p.Granted), Is.EqualTo(1));
                var denied = permits.Single(p => !p.Granted);
                Assert.That(denied.ConflictOwnerId, Is.Not.Null);
            });
            Assert.That(checkedOnce, Is.True, "both cars asked in the same decision round");
        }

        [Test]
        public void EqualJunctionTrafficFromTheRightGoesFirst()
        {
            var run = Cross();
            run.Director.AddVehicle(Into("south", "north"), 10, 8, id: "a-south");
            run.Director.AddVehicle(Into("east", "west"), 10, 8, id: "b-east"); // on the right of the southern car
            var order = EntryOrder(run, 20);
            Assert.That(order.Take(2), Is.EqualTo(new[] { "b-east", "a-south" }));
        }

        [Test]
        public void LeftTurnYieldsToOncomingStraight()
        {
            var run = Cross();
            run.Director.AddVehicle(Into("south", "west"), 10, 8, id: "a-left");
            run.Director.AddVehicle(Into("north", "south"), 10, 8, id: "b-oncoming");
            var order = EntryOrder(run, 20);
            Assert.That(order.Take(2), Is.EqualTo(new[] { "b-oncoming", "a-left" }));
        }

        [Test]
        public void MainRoadGoesBeforeTrafficFromTheRight()
        {
            // North-south is the main road; the east approach has "yield". East is on the right of the southern car.
            var run = Cross(layout =>
            {
                layout.signs = new[]
                {
                    new LayoutSign { id = "main-s", code = "2.1", instanceId = "south1", laneId = "f", atS = 5 },
                    new LayoutSign { id = "yield-e", code = "2.4", instanceId = "east0", laneId = "f", atS = 5 },
                };
                layout.approaches = new[]
                {
                    new LayoutApproach { instanceId = "c", socket = "Socket_South", priority = ApproachPriority.Main, signIds = new[] { "main-s" } },
                    new LayoutApproach { instanceId = "c", socket = "Socket_East", priority = ApproachPriority.Secondary, signIds = new[] { "yield-e" } },
                };
            });
            run.Director.AddVehicle(Into("south", "north"), 10, 8, id: "a-main");
            run.Director.AddVehicle(Into("east", "west"), 10, 8, id: "b-secondary");
            var order = EntryOrder(run, 20);
            Assert.That(order.Take(2), Is.EqualTo(new[] { "a-main", "b-secondary" }));
        }

        [Test]
        public void FourWayStandoffIsResolved()
        {
            var run = Cross();
            foreach (var (arm, to) in new[] { ("south", "north"), ("north", "south"), ("east", "west"), ("west", "east") })
                run.Director.AddVehicle(Into(arm, to), 10, 8, id: "car-" + arm);
            var order = EntryOrder(run, 60);
            Assert.That(order.Count, Is.EqualTo(4), "everyone got through: " + string.Join(",", order));
        }

        [Test]
        public void ReservationsNeverOverlapUnderRandomTraffic()
        {
            for (int seed = 1; seed <= 40; seed++)
            {
                var run = new TrafficRun(CityLayouts.Compile(CityLayouts.CrossWithArms(3)), new TrafficProfile { MaxVehicles = 10, SpawnIntervalSeconds = 1 }, seed);
                run.Run(60, r =>
                {
                    r.Director.Reservations.AssertConsistent();
                    TrafficRun.AssertNoOverlaps(r.Director.Snapshot);
                });
            }
        }

        [Test]
        public void ExpiredPermitIsReleasedAndTheOtherCarGoes()
        {
            var run = Cross();
            run.Director.AddVehicle(Into("east", "west"), 16, 3, id: "a-stuck");
            run.Director.AddVehicle(Into("south", "north"), 5, 6, id: "b-waiting");
            // a gets its permit, then freezes before the line.
            string granted = null;
            run.Run(3, r => { granted = granted ?? r.Director.Snapshot.Permits.FirstOrDefault(p => p.Granted)?.AgentId; });
            Assert.That(granted, Is.EqualTo("a-stuck"));
            run.Director.Freeze("a-stuck", true);
            bool cancelled = false; bool bEntered = false;
            run.Run(40, r =>
            {
                cancelled |= r.Director.Snapshot.Notices.Any(n => n.ToId == "a-stuck" && n.Kind == NoticeKind.Cancel);
                bEntered |= r.Director.Snapshot.Participants.Any(p => p.Id == "b-waiting" && p.PathId != null && r.Director.Graph.Path(p.PathId).IsConnection);
            });
            Assert.That(cancelled, Is.True, "lease expired -> cancel notice");
            Assert.That(bEntered, Is.True, "the waiting car got the junction");
        }

        [Test]
        public void PlayersVisibleClaimIsRespected()
        {
            var run = Cross();
            var index = run.Director.Graph;
            var playerConn = index.Path("c/c:South.in>North.out");
            var p = playerConn.Line.PointAt(4);
            run.Director.SetPlayer(new PlayerSample { Present = true, X = p.x, Z = p.z, HeadingRad = 0, SpeedMps = 5, LengthM = 4.4, WidthM = 1.8 });
            run.Director.AddVehicle(Into("east", "west"), 16, 6, id: "ai");
            ManeuverPermit first = null;
            run.Run(2, r => { first = first ?? r.Director.Snapshot.Permits.FirstOrDefault(x => x.AgentId == "ai"); });
            Assert.That(first, Is.Not.Null);
            Assert.That(first.Granted, Is.False);
            Assert.That(first.ConflictOwnerId, Is.EqualTo(TrafficDirector.PlayerId));
            Assert.That(run.Director.Reservations.Entries.Any(e => e.OwnerId == TrafficDirector.PlayerId), Is.True);
        }

        [Test]
        public void SideKeysConflictOnlyAcrossSides()
        {
            Assert.That(ReservationTable.KeysConflict("cz:1#A", "cz:1#B"), Is.True);
            Assert.That(ReservationTable.KeysConflict("cz:1#A", "cz:1#A"), Is.False, "followers on one connection");
            Assert.That(ReservationTable.KeysConflict("cz:1#A", "cz:2#B"), Is.False);
            Assert.That(ReservationTable.KeysConflict("x:crossing", "x:crossing"), Is.True);
        }
    }
}
