using System.Collections.Generic;
using System.IO;
using System.Linq;
using DrivingSchool.Contracts;
using DrivingSchool.Simulation.RoadGraph;
using NUnit.Framework;

namespace DrivingSchool.Tests
{
    public sealed class WorldCitySemanticsTests
    {
        static readonly HashSet<string> Codes = new HashSet<string> { "2.1", "2.4", "2.5", "3.24", "5.19.1" };

        static WorldDocumentV2 WithSemantics()
        {
            var w = CityTestWorlds.ExampleV2();
            var south = w.lanes.Single(l => l.id == "south-centre:0:0");
            var len = (float)new Polyline(south.centerline).Length;
            w.boundaries = new[]
            {
                new LaneBoundary { id = "b1", laneId = south.id, side = BoundarySide.Left, type = MarkingType.Dashed, fromS = 0, toS = len - 20 },
                new LaneBoundary { id = "b2", laneId = south.id, side = BoundarySide.Left, type = MarkingType.Solid, fromS = len - 20, toS = len },
            };
            w.signs = new[]
            {
                new SignPlacement { id = "s-main", code = "2.1", laneIds = new[] { south.id }, atS = 10 },
                new SignPlacement { id = "s-limit", code = "3.24", value = "40", laneIds = new[] { south.id }, atS = 20, untilNextJunction = true },
            };
            w.stopLines = new[] { new StopLine { id = "stop-s", laneId = south.id, s = len - 1 } };
            w.approaches = new[] { new JunctionApproach { id = "ap-s", junctionId = "j:centre", laneId = south.id, stopLineId = "stop-s", priority = ApproachPriority.Main, sourceSignIds = new[] { "s-main" } } };
            // Walkway across the south arm, 40 m before the junction.
            w.crossings = new[] { new PedestrianCrossing { id = "x-s", widthM = 4, a = new Vec3d(-8, 0, -40), b = new Vec3d(8, 0, -40), laneIds = new[] { south.id, "south-centre:1:0" } } };
            w.zones = new[] { new Zone { id = "z1", laneId = south.id, kind = ZoneKind.NoStopping, fromS = 0, toS = 30 } };
            w.spawnPoints = new[] { new SpawnPoint { id = "sp1", pathId = south.id, role = SpawnRole.Vehicle, edge = SpawnEdge.DistrictEdge, s = 5 } };
            return w;
        }

        [Test]
        public void ValidSemanticsPass() => WorldValidatorV2.Validate(WithSemantics(), Codes);

        [Test]
        public void ConnectionForbiddenByLaneManeuversIsRejected()
        {
            var w = WithSemantics();
            w.lanes.Single(l => l.id == "south-centre:0:0").allowedManeuvers = LaneManeuver.Straight | LaneManeuver.Right;
            var ex = Assert.Throws<InvalidDataException>(() => WorldValidatorV2.Validate(w, Codes));
            StringAssert.Contains("not allowed by lane maneuvers", ex.Message);
        }

        [Test]
        public void UnknownReferencesAreRejected()
        {
            var w = WithSemantics(); w.boundaries[0].laneId = "nope";
            Assert.Throws<InvalidDataException>(() => WorldValidatorV2.Validate(w, Codes));
            w = WithSemantics(); w.signs[0].laneIds = new[] { "nope" };
            Assert.Throws<InvalidDataException>(() => WorldValidatorV2.Validate(w, Codes));
            w = WithSemantics(); w.approaches[0].sourceSignIds = new[] { "nope" };
            Assert.Throws<InvalidDataException>(() => WorldValidatorV2.Validate(w, Codes));
        }

        [Test]
        public void OverlappingOrOutOfLaneBoundariesAreRejected()
        {
            var w = WithSemantics(); w.boundaries[1].fromS -= 5;
            StringAssert.Contains("Overlapping", Assert.Throws<InvalidDataException>(() => WorldValidatorV2.Validate(w, Codes)).Message);
            w = WithSemantics(); w.boundaries[1].toS += 50;
            Assert.Throws<InvalidDataException>(() => WorldValidatorV2.Validate(w, Codes));
        }

        [Test]
        public void MainApproachNeedsPrioritySign()
        {
            var w = WithSemantics(); w.approaches[0].sourceSignIds = new string[0];
            StringAssert.Contains("Main approach", Assert.Throws<InvalidDataException>(() => WorldValidatorV2.Validate(w, Codes)).Message);
            w = WithSemantics(); w.approaches[0].priority = ApproachPriority.Secondary;
            StringAssert.Contains("Secondary approach", Assert.Throws<InvalidDataException>(() => WorldValidatorV2.Validate(w, Codes)).Message);
        }

        [Test]
        public void UnknownSignCodeIsRejectedWhenCatalogGiven()
        {
            var w = WithSemantics(); w.signs[1].code = "9.99";
            StringAssert.Contains("Unknown sign code", Assert.Throws<InvalidDataException>(() => WorldValidatorV2.Validate(w, Codes)).Message);
            WorldValidatorV2.Validate(w); // no catalog: code not checked
        }

        [Test]
        public void CrossingMustActuallyCrossItsLanes()
        {
            var w = WithSemantics(); w.crossings[0].laneIds = new[] { "west-centre:0:0" };
            StringAssert.Contains("does not cross", Assert.Throws<InvalidDataException>(() => WorldValidatorV2.Validate(w, Codes)).Message);
        }

        [Test]
        public void DuplicateIdsAcrossKindsAreRejected()
        {
            var w = WithSemantics(); w.zones[0].id = "b1";
            StringAssert.Contains("Duplicate", Assert.Throws<InvalidDataException>(() => WorldValidatorV2.Validate(w, Codes)).Message);
        }

        [Test]
        public void VehicleSpawnOnSidewalkIsRejected()
        {
            var w = WithSemantics();
            w.sidewalks = new[] { new SidewalkPath { id = "walk", widthM = 2, points = new[] { new Vec3d(10, 0, -100), new Vec3d(10, 0, -50) } } };
            w.spawnPoints[0].pathId = "walk";
            Assert.Throws<InvalidDataException>(() => WorldValidatorV2.Validate(w, Codes));
        }
    }
}
