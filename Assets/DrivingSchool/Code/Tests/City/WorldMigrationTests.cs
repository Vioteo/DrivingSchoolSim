using System;
using System.IO;
using System.Linq;
using DrivingSchool.Contracts;
using DrivingSchool.Simulation.RoadGraph;
using NUnit.Framework;

namespace DrivingSchool.Tests
{
    public sealed class WorldMigrationTests
    {
        [Test]
        public void ExampleWorldMigratesWithoutLosingNodesLanesOrTopology()
        {
            var v1 = CityTestWorlds.ExampleV1();
            var v2 = WorldMigration.MigrateV1ToV2(v1);

            Assert.That(v2.schemaVersion, Is.EqualTo(2));
            Assert.That(v2.nodes.Select(n => n.id), Is.EquivalentTo(v1.nodes.Select(n => n.id)));
            Assert.That(v2.segments.Select(s => s.id), Is.EquivalentTo(v1.segments.Select(s => s.id)));
            Assert.That(v2.lanes.Select(l => l.id), Is.EquivalentTo(v1.lanes.Select(l => l.id)));
            Assert.That(v2.junctions.Select(j => j.nodeId), Is.EqualTo(new[] { "centre" }));

            // Every v1 successor survives, either as a direct successor or as a junction connection.
            int links = 0;
            foreach (var l in v1.lanes)
                foreach (var next in l.successors)
                {
                    links++;
                    var lane = v2.lanes.Single(x => x.id == l.id);
                    bool kept = lane.successors.Contains(next) || v2.connections.Any(c => c.fromLaneId == l.id && c.toLaneId == next);
                    Assert.That(kept, Is.True, l.id + " -> " + next);
                }
            Assert.That(v2.connections.Length, Is.EqualTo(links));
            WorldValidatorV2.Validate(v2);
        }

        [Test]
        public void LaneCenterlinesAreSampledAtMostTwoMetresApart()
        {
            var v2 = CityTestWorlds.ExampleV2();
            foreach (var lane in v2.lanes)
                for (int i = 1; i < lane.centerline.Length; i++)
                    Assert.That(Polyline.Distance2D(lane.centerline[i - 1], lane.centerline[i]), Is.LessThanOrEqualTo(2.0 + 1e-9), lane.id);
        }

        [Test]
        public void StraightSegmentGetsThirdPointHandlesAndRightHandLanes()
        {
            var v2 = WorldMigration.MigrateV1ToV2(CityTestWorlds.SingleStraightV1(100));
            var c = v2.segments[0].curve;
            Assert.That(c.p1.z, Is.EqualTo(33.333).Within(0.01));
            Assert.That(c.p2.z, Is.EqualTo(66.667).Within(0.01));

            var forward = v2.lanes.Single(l => l.id == "ab:f");
            var backward = v2.lanes.Single(l => l.id == "ab:b");
            Assert.That(forward.index, Is.EqualTo(1));
            Assert.That(backward.index, Is.EqualTo(-1));
            // Right-hand traffic: travelling +Z the lane lies at +X, travelling -Z at -X.
            Assert.That(forward.centerline[0].x, Is.EqualTo(1.75).Within(1e-6));
            Assert.That(backward.centerline[0].x, Is.EqualTo(-1.75).Within(1e-6));
            Assert.That(forward.oncomingLaneId, Is.EqualTo("ab:b"));
        }

        [Test]
        public void TurnsAreClassifiedForRightHandTraffic()
        {
            var v2 = CityTestWorlds.ExampleV2();
            // From the south arm the car travels +Z (north): east is a right turn, west is a left turn.
            Assert.That(CityTestWorlds.Connection(v2, "south-centre:0:0", "east-centre:1:0").maneuver, Is.EqualTo(LaneManeuver.Right));
            Assert.That(CityTestWorlds.Connection(v2, "south-centre:0:0", "west-centre:1:0").maneuver, Is.EqualTo(LaneManeuver.Left));
            Assert.That(CityTestWorlds.Connection(v2, "south-centre:0:0", "north-centre:1:0").maneuver, Is.EqualTo(LaneManeuver.Straight));
        }

        [Test]
        public void LeftTurnConflictsWithOncomingStraight()
        {
            var v2 = CityTestWorlds.ExampleV2();
            var left = CityTestWorlds.Connection(v2, "south-centre:0:0", "west-centre:1:0").id;
            var oncoming = CityTestWorlds.Connection(v2, "north-centre:0:0", "south-centre:1:0").id;
            Assert.That(v2.conflictZones.Any(z => (z.connectionA == left && z.connectionB == oncoming) || (z.connectionA == oncoming && z.connectionB == left)), Is.True);
        }

        [Test]
        public void UnsupportedVersionIsRejected()
        {
            Assert.That(WorldMigration.CanMigrate(99), Is.False);
            var w = CityTestWorlds.ExampleV1(); w.schemaVersion = 99;
            var ex = Assert.Throws<NotSupportedException>(() => WorldMigration.MigrateV1ToV2(w));
            StringAssert.Contains("99", ex.Message);
        }

        [Test]
        public void BrokenSuccessorIsRejectedBeforeMigration()
        {
            var w = CityTestWorlds.ExampleV1();
            w.lanes[0].successors = new[] { "missing-lane" };
            Assert.Throws<InvalidDataException>(() => WorldMigration.MigrateV1ToV2(w));
        }

        [Test]
        public void ZeroLengthSegmentIsRejected()
        {
            var sameIds = CityTestWorlds.SingleStraightV1();
            sameIds.segments[0].toNode = "a";
            Assert.Throws<InvalidDataException>(() => WorldMigration.MigrateV1ToV2(sameIds));

            var samePoint = CityTestWorlds.SingleStraightV1();
            samePoint.nodes[1].z = 0;
            Assert.Throws<InvalidDataException>(() => WorldMigration.MigrateV1ToV2(samePoint));
        }
    }
}
