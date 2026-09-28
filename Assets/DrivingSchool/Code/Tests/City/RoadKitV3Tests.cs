using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DrivingSchool.Contracts;
using DrivingSchool.Simulation.RoadGraph;
using NUnit.Framework;

namespace DrivingSchool.Tests
{
    /// <summary>T66: Road Kit v3 — plain and T junctions, the 2+2 curve and zebra, straights of any length.</summary>
    public sealed class RoadKitV3Tests
    {
        static readonly RoadKitCatalog Kits = new RoadKitCatalog();
        static ModuleTemplate T(string id) { Assert.That(Kits.TryGet(id, out var t), id); return t; }
        static readonly string[] V3 = { RoadKitTemplatesV2.CrossPlain, RoadKitTemplatesV2.Tee, RoadKitTemplatesV2.Curve4, RoadKitTemplatesV2.Crosswalk4 };

        [Test]
        public void TemplatesMatchTheBlenderKit()
        {
            var json = File.ReadAllText(Path.Combine(ModuleTemplateTests.RepoRoot(), "Assets/DrivingSchool/Art/RoadKitV3/catalog-v3.json"));
            Assert.That(Regex.Match(json, "\"sourceSha256\"\\s*:\\s*\"([0-9a-f]+)\"").Groups[1].Value, Is.EqualTo(RoadKitTemplatesV2.SourceSha256V3),
                "Road Kit v3 was rebuilt: check the v3 templates against tools/build_road_kit_v3.py and update the hash");
            foreach (var id in V3)
            {
                var t = T(id);
                var block = Regex.Match(json, "\"catalogId\"\\s*:\\s*\"" + id + "\".*?\"sockets\"\\s*:\\s*\\{(.*?)\\}", RegexOptions.Singleline);
                Assert.That(block.Success, id + " missing from catalog-v3.json");
                Assert.That(Regex.Matches(block.Groups[1].Value, "\"Socket_").Count, Is.EqualTo(t.Sockets.Length), id + ": socket count");
                foreach (var s in t.Sockets)
                {
                    var m = Regex.Match(block.Groups[1].Value, "\"" + s.Name + "(\\.\\d{3})?\"\\s*:\\s*\\[([^\\]]*)\\]");
                    Assert.That(m.Success, id + ":" + s.Name + " not in the Blender kit");
                    var v = m.Groups[2].Value.Split(',').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                    Assert.That(s.Position.x, Is.EqualTo(v[0]).Within(0.01), id + ":" + s.Name);
                    Assert.That(s.Position.z, Is.EqualTo(v[1]).Within(0.01), id + ":" + s.Name);
                }
            }
        }

        [Test]
        public void EveryV3TemplateIsAValidGraphFragment()
        {
            foreach (var id in V3.Concat(new[] { RoadKitTemplates.ZebraPlainId, RoadKitTemplates.Stretched(RoadKitTemplatesV2.Straight4, 34), RoadKitTemplates.Stretched(RoadKitTemplates.StraightId, 6.5) }))
            {
                var f = T(id).Fragment;
                var w = new WorldDocumentV2
                {
                    id = "tpl-" + id, nodes = f.nodes, lanes = f.lanes, junctions = f.junctions, connections = f.connections, conflictZones = f.conflictZones,
                    stopLines = f.stopLines, crossings = f.crossings, boundaries = f.boundaries, sidewalks = f.sidewalks, signalGroups = f.signalGroups,
                };
                Assert.DoesNotThrow(() => WorldValidatorV2.Validate(w, SignCatalog.Codes), id);
            }
        }

        [Test]
        public void JunctionsWithoutZebrasAndTheTHasNoSouthernArm()
        {
            Assert.That(T(RoadKitTemplatesV2.CrossPlain).Fragment.crossings, Is.Empty);
            var tee = T(RoadKitTemplatesV2.Tee);
            Assert.That(tee.Fragment.crossings, Is.Empty);
            Assert.That(tee.Sockets.Select(s => s.Name), Is.EquivalentTo(new[] { "Socket_North", "Socket_East", "Socket_West" }));
            // From the side street: left and right; along the main road: straight and one turn into the side street.
            var c = tee.Fragment.connections;
            Assert.That(c.Where(x => x.fromLaneId == "North.in1").Select(x => x.maneuver), Is.EquivalentTo(new[] { LaneManeuver.Left, LaneManeuver.Right }));
            Assert.That(c.Where(x => x.fromLaneId.StartsWith("West.in")).Select(x => x.maneuver).Distinct(), Is.EquivalentTo(new[] { LaneManeuver.Straight, LaneManeuver.Left }));
            Assert.That(c.Where(x => x.fromLaneId.StartsWith("East.in")).Select(x => x.maneuver).Distinct(), Is.EquivalentTo(new[] { LaneManeuver.Straight, LaneManeuver.Right }));
            Assert.That(c.Where(x => x.maneuver == LaneManeuver.Right).All(x => x.fromLaneId.EndsWith("in2") || x.fromLaneId == "North.in1"), "right turns from the outer lane only");
        }

        [Test]
        public void AStretchedStraightHasItsLengthAndTheZebra4CrossesAllLanes()
        {
            var s = T(RoadKitTemplates.Stretched(RoadKitTemplatesV2.Straight4, 34));
            Assert.That(s.Socket("Socket_End").Position.z, Is.EqualTo(34).Within(1e-6));
            Assert.That(s.Fragment.lanes.All(l => System.Math.Abs(new Polyline(l.centerline).Length - 34) < 1e-6));
            Assert.That(Kits.TryGet(RoadKitTemplatesV2.Straight4 + "@1", out _), Is.False, "shorter than a car");
            Assert.That(Kits.TryGet(RoadKitTemplatesV2.Straight4 + "@abc", out _), Is.False);
            Assert.That(RoadKitTemplatesV2.MeshPieces(RoadKitTemplates.Stretched(RoadKitTemplatesV2.Straight4, 34)).Select(m => (m.z, m.scaleZ)),
                Is.EqualTo(new[] { (0.0, 1.0), (20.0, 0.7) }), "34 m: a whole mesh and one squeezed to 14 m");
            Assert.That(RoadKitTemplatesV2.MeshPieces(RoadKitTemplates.Stretched(RoadKitTemplatesV2.Straight4, 88)).Select(m => (m.z, m.scaleZ)),
                Is.EqualTo(new[] { (0.0, 1.0), (20.0, 1.0), (40.0, 1.0), (60.0, 1.4) }));
            var zebra = T(RoadKitTemplatesV2.Crosswalk4).Fragment.crossings.Single();
            Assert.That(zebra.laneIds, Is.EquivalentTo(new[] { "f1", "f2", "b1", "b2" }));
        }

        [Test]
        public void TheCurveJoinsStraightsLaneToLane()
        {
            // S4 – curve (right) – S4 – curve taken the other way (left) – S4: every lane end meets the next lane start.
            var kit = Kits;
            var a = new ModuleInstance { id = "a", catalogId = RoadKitTemplatesV2.Straight4 };
            var c1 = DistrictCompiler.Dock("c1", T(RoadKitTemplatesV2.Curve4), "Socket_Start", a, T(a.catalogId), "Socket_End");
            var b = DistrictCompiler.Dock("b", T(RoadKitTemplatesV2.Straight4), "Socket_Start", c1, T(c1.catalogId), "Socket_End");
            var c2 = DistrictCompiler.Dock("c2", T(RoadKitTemplatesV2.Curve4), "Socket_End", b, T(b.catalogId), "Socket_End");
            var d = DistrictCompiler.Dock("d", T(RoadKitTemplatesV2.Straight4), "Socket_Start", c2, T(c2.catalogId), "Socket_Start");
            var layout = new DistrictLayout
            {
                id = "curves", instances = new[] { a, c1, b, c2, d },
                joins = new[]
                {
                    new SocketJoin { instanceA = "a", socketA = "Socket_End", instanceB = "c1", socketB = "Socket_Start" },
                    new SocketJoin { instanceA = "c1", socketA = "Socket_End", instanceB = "b", socketB = "Socket_Start" },
                    new SocketJoin { instanceA = "b", socketA = "Socket_End", instanceB = "c2", socketB = "Socket_End" },
                    new SocketJoin { instanceA = "c2", socketA = "Socket_Start", instanceB = "d", socketB = "Socket_Start" },
                },
                openSockets = new[] { new SocketRef { instanceId = "a", socket = "Socket_Start" }, new SocketRef { instanceId = "d", socket = "Socket_End" } },
            };
            var w = DistrictCompiler.Compile(layout, kit);
            Assert.That(w.lanes.Single(l => l.id == "a/f2").successors, Is.EqualTo(new[] { "c1/f2" }));
            Assert.That(d.yawDeg, Is.EqualTo(0).Within(1e-3), "right then left: heading as at the start");
        }
    }
}
