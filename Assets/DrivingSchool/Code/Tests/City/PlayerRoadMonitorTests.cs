using System;
using System.Collections.Generic;
using System.Linq;
using DrivingSchool.Contracts;
using DrivingSchool.Rules;
using DrivingSchool.Simulation.RoadGraph;
using DrivingSchool.Simulation.Traffic;
using NUnit.Framework;

namespace DrivingSchool.Tests
{
    /// <summary>T65: facts about the player's car from the graph — lane change, movement and its lane, axis, stops at a zebra.</summary>
    public sealed class PlayerRoadMonitorTests
    {
        const double Dt = 0.05;

        /// <summary>Drives the player along <paramref name="path"/> at <paramref name="speed"/>; returns the facts of every tick.</summary>
        static List<DriverRoadFacts> Drive(WorldDocumentV2 w, IReadOnlyList<Vec3d> path, double speed, Func<double, (bool left, bool right)> signals = null)
        {
            var director = new TrafficDirector(w, new TrafficProfile { MaxVehicles = 0 }, 1);
            var monitor = new PlayerRoadMonitor(director);
            var line = new Polyline(path.ToArray());
            var facts = new List<DriverRoadFacts>();
            long tick = 0;
            for (double s = 0, t = 0; s <= line.Length; s += speed * Dt, t += Dt)
            {
                var p = line.PointAt(s);
                var sig = signals?.Invoke(t) ?? (false, false);
                director.SetPlayer(new PlayerSample
                {
                    Present = true, X = p.x, Y = p.y, Z = p.z, HeadingRad = line.HeadingAt(s), SpeedMps = speed,
                    LeftIndicator = sig.left, RightIndicator = sig.right, LengthM = 4.4, WidthM = 1.8,
                });
                director.Tick(tick++, t);
                facts.Add(monitor.Update());
            }
            return facts;
        }

        static IReadOnlyList<Vec3d> Lane(WorldDocumentV2 w, string id) => w.lanes.Single(l => l.id == id).centerline;
        static IReadOnlyList<Vec3d> Connection(WorldDocumentV2 w, string from, string to) => w.connections.Single(c => c.fromLaneId == from && c.toLaneId == to).centerline;
        static List<Vec3d> Join(params IReadOnlyList<Vec3d>[] parts)
        {
            var all = new List<Vec3d>();
            foreach (var part in parts) foreach (var p in part) if (all.Count == 0 || Polyline.Distance2D(all[all.Count - 1], p) > 1e-3) all.Add(p);
            return all;
        }

        static List<string> Rules(IEnumerable<DriverRoadFacts> facts) { var m = new CityRuleMonitor(); return facts.SelectMany(f => m.Update(f)).Select(e => e.ruleId).ToList(); }

        [Test]
        public void MovingToTheInnerLaneIsOneLaneChangeToTheLeft()
        {
            var w = DistrictCompiler.Compile(LaneDisciplineTests.Cross4(), new RoadKitCatalog());
            // From the outer lane of s1 to the inner lane of s0, drifting over 20 m.
            var outer = new Polyline(Lane(w, "s1/f2").ToArray()); var inner = new Polyline(Lane(w, "s0/f1").ToArray());
            var path = new List<Vec3d> { outer.PointAt(0), outer.PointAt(8), inner.PointAt(8), inner.PointAt(18) };
            var facts = Drive(w, path, 10);
            Assert.That(facts.Where(f => f.LaneChange != 0).Select(f => f.LaneChange), Is.EqualTo(new[] { -1 }));
            Assert.That(facts.All(f => f.LanesInDirection == 2 || !f.OnRoad), Is.True);
            Assert.That(Rules(facts), Does.Contain(CityRuleMonitor.RuleLaneChangeNoSignal));
            Assert.That(Rules(Drive(w, path, 10, t => (true, false))), Does.Not.Contain(CityRuleMonitor.RuleLaneChangeNoSignal));
        }

        [Test]
        public void ARightTurnIsJudgedByTheLaneItWasMadeFrom()
        {
            var w = DistrictCompiler.Compile(LaneDisciplineTests.Cross4(), new RoadKitCatalog());
            var right = Connection(w, "X/South.in2", "X/East.out2");
            // Correct: from the outer lane into the outer lane, right signal on.
            var good = Drive(w, Join(Lane(w, "s0/f2"), Lane(w, "X/South.in2"), right, Lane(w, "X/East.out2"), Lane(w, "e0/b2")), 6, t => (false, true));
            var turn = good.Single(f => f.JunctionManeuver != LaneManeuver.None);
            Assert.That(turn.JunctionManeuver, Is.EqualTo(LaneManeuver.Right));
            Assert.That(turn.JunctionFromAllowedLane, Is.True);
            Assert.That(Rules(good), Is.Empty);
            // Wrong: from the inner lane, the same arc 3.5 m further out, into the inner lane; no signal either.
            var wide = Polyline.OffsetRight(right.ToArray(), -RoadKitTemplatesV2.Lane4WidthM);
            var bad = Drive(w, Join(Lane(w, "s0/f1"), Lane(w, "X/South.in1"), wide, Lane(w, "X/East.out1"), Lane(w, "e0/b1")), 6);
            var badTurn = bad.Single(f => f.JunctionManeuver != LaneManeuver.None);
            Assert.That(badTurn.JunctionManeuver, Is.EqualTo(LaneManeuver.Right));
            Assert.That(badTurn.JunctionFromAllowedLane, Is.False);
            Assert.That(badTurn.JunctionLaneAllowed, Is.EqualTo(LaneManeuver.Straight | LaneManeuver.Left));
            Assert.That(Rules(bad), Is.EquivalentTo(new[] { CityRuleMonitor.RuleTurnNoSignal, CityRuleMonitor.RuleWrongLane }));
        }

        [Test]
        public void TheLeftSideOverTheDoubleSolidAxisIsOncoming()
        {
            var w = DistrictCompiler.Compile(LaneDisciplineTests.Cross4(), new RoadKitCatalog());
            var lane = new Polyline(Lane(w, "s1/f1").ToArray());
            var shifted = Polyline.OffsetRight(Lane(w, "s1/f1").ToArray(), -1.4);   // 1.4 m left of the inner lane centre
            var facts = Drive(w, shifted, 8);
            Assert.That(facts.Any(f => f.LeftSideOverAxis && f.AxisMarking == MarkingType.DoubleSolid), Is.True);
            Assert.That(Rules(facts), Is.EqualTo(new[] { CityRuleMonitor.RuleOncoming }));
            Assert.That(Rules(Drive(w, Lane(w, "s1/f1"), 8)), Is.Empty, "in the middle of the lane");
            Assert.That(lane.Length, Is.GreaterThan(15));
        }

        /// <summary>Straight – zebra module – straight (1+1, Road Kit v1), open at both ends.</summary>
        static WorldDocumentV2 ZebraStreet()
        {
            var kit = new RoadKitTemplates();
            ModuleTemplate T(string id) { kit.TryGet(id, out var t); return t; }
            var s0 = new ModuleInstance { id = "s0", catalogId = RoadKitTemplates.StraightId };
            var z = DistrictCompiler.Dock("z", T(RoadKitTemplates.CrosswalkId), "Socket_Start", s0, T(RoadKitTemplates.StraightId), "Socket_End");
            var s2 = DistrictCompiler.Dock("s2", T(RoadKitTemplates.StraightId), "Socket_Start", z, T(RoadKitTemplates.CrosswalkId), "Socket_End");
            var layout = new DistrictLayout
            {
                id = "zebra", instances = new[] { s0, z, s2 },
                joins = new[] { new SocketJoin { instanceA = "s0", socketA = "Socket_End", instanceB = "z", socketB = "Socket_Start" }, new SocketJoin { instanceA = "z", socketA = "Socket_End", instanceB = "s2", socketB = "Socket_Start" } },
                openSockets = new[] { new SocketRef { instanceId = "s0", socket = "Socket_Start" }, new SocketRef { instanceId = "s2", socket = "Socket_End" } },
            };
            return DistrictCompiler.Compile(layout, kit);
        }

        /// <summary>Stands still for 4 s with the car centre at <paramref name="z"/> on the forward lane (x = 1.825).</summary>
        static List<DriverRoadFacts> StandAt(WorldDocumentV2 w, double z)
        {
            var director = new TrafficDirector(w, new TrafficProfile { MaxVehicles = 0 }, 1);
            var monitor = new PlayerRoadMonitor(director);
            var facts = new List<DriverRoadFacts>();
            for (int i = 0; i < 80; i++)
            {
                director.SetPlayer(new PlayerSample { Present = true, X = RoadKitTemplates.LaneOffsetM, Z = z, HeadingRad = 0, SpeedMps = 0, LengthM = 4.4, WidthM = 1.8 });
                director.Tick(i, i * Dt);
                facts.Add(monitor.Update());
            }
            return facts;
        }

        [Test]
        public void StandingOnTheZebraOrRightBeforeIt()
        {
            var w = ZebraStreet();
            Assert.That(w.crossings.Single().id, Is.EqualTo("z/crossing"));
            Assert.That(StandAt(w, 30).Last().StopPlace, Is.EqualTo(RoadStopPlace.Crosswalk), "the crossing is at z = 30");
            Assert.That(StandAt(w, 25).Last().StopPlace, Is.EqualTo(RoadStopPlace.BeforeCrosswalk), "front bumper 1.3 m before the stripes");
            Assert.That(StandAt(w, 12).Last().StopPlace, Is.EqualTo(RoadStopPlace.Lane));
            Assert.That(Rules(StandAt(w, 30)), Does.Contain(CityRuleMonitor.RuleStopOnCrosswalk));
            Assert.That(Rules(StandAt(w, 25)), Does.Contain(CityRuleMonitor.RuleStopNearCrosswalk));
            Assert.That(StandAt(w, 12).Last().SpeedLimitKph, Is.EqualTo(60));
        }
    }
}
