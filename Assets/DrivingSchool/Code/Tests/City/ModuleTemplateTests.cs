using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DrivingSchool.Contracts;
using DrivingSchool.Simulation.RoadGraph;
using NUnit.Framework;

namespace DrivingSchool.Tests
{
    public sealed class ModuleTemplateTests
    {
        static WorldDocumentV2 AsWorld(ModuleTemplate t) { var f = t.Fragment; f.id = "tpl-" + t.CatalogId; return f; }

        [Test]
        public void TemplatesMatchRoadKitCatalogSource()
        {
            var root = RepoRoot();
            var catalog = File.ReadAllText(Path.Combine(root, "Assets/DrivingSchool/Art/RoadKit/catalog.json"));
            var sha = Regex.Match(catalog, "\"sourceSha256\"\\s*:\\s*\"([0-9a-f]+)\"").Groups[1].Value;
            Assert.That(sha, Is.Not.Empty);
            RoadKitTemplates.RequireSource(sha);
            Assert.Throws<InvalidDataException>(() => RoadKitTemplates.RequireSource(new string('0', 64)));
        }

        [Test]
        public void EveryTemplateIsAValidGraphFragment()
        {
            foreach (var t in CityLayouts.Kit.All)
                Assert.DoesNotThrow(() => WorldValidatorV2.Validate(AsWorld(t), SignCatalog.Codes), t.CatalogId);
        }

        [Test]
        public void CrossHasTwelveConnectionsStopLinesAndCrossings()
        {
            var f = CityLayouts.T(CityLayouts.Cross).Fragment;
            Assert.That(f.lanes.Length, Is.EqualTo(8));
            Assert.That(f.connections.Length, Is.EqualTo(12));
            Assert.That(f.connections.Count(c => c.maneuver == LaneManeuver.UTurn), Is.Zero);
            Assert.That(f.connections.Count(c => c.maneuver == LaneManeuver.Left), Is.EqualTo(4));
            Assert.That(f.connections.Count(c => c.maneuver == LaneManeuver.Right), Is.EqualTo(4));
            Assert.That(f.stopLines.Length, Is.EqualTo(4));
            Assert.That(f.crossings.Length, Is.EqualTo(4));
            Assert.That(f.crossings.All(c => c.laneIds.Length > 0), Is.True);
        }

        [Test]
        public void StopLineMatchesStopBarOfTheMesh()
        {
            // build_road_kit.py: south incoming stop bar box(x=1.9, y=-10, w=3.5, depth=.4) -> near edge at z=-10.2.
            var f = CityLayouts.T(CityLayouts.Cross).Fragment;
            var stop = f.stopLines.Single(s => s.id == "stop.South");
            var p = new Polyline(f.lanes.Single(l => l.id == stop.laneId).centerline).PointAt(stop.s);
            Assert.That(p.z, Is.EqualTo(-10.2).Within(0.05));
            Assert.That(p.x, Is.EqualTo(1.9).Within(0.1)); // lane centre 1.825 vs bar centre 1.9
        }

        [Test]
        public void LeftFromSouthConflictsWithStraightFromEast()
        {
            var f = CityLayouts.T(CityLayouts.Cross).Fragment;
            string left = "c:South.in>West.out", straight = "c:East.in>West.out";
            string straightEast = "c:East.in>South.out";
            Assert.That(f.connections.Any(c => c.id == left), Is.True);
            bool Zone(string a, string b) => f.conflictZones.Any(z => (z.connectionA == a && z.connectionB == b) || (z.connectionA == b && z.connectionB == a));
            Assert.That(Zone(left, straight), Is.True, "merge into west exit");
            Assert.That(Zone(left, "c:East.in>West.out") || Zone(left, straightEast), Is.True);
            Assert.That(Zone("c:South.in>North.out", "c:East.in>West.out"), Is.True, "crossing straights");
        }

        [Test]
        public void ShiftedConnectionIsRejected()
        {
            var f = new RoadKitTemplates().All.Single(t => t.CatalogId == CityLayouts.Cross);
            var c = f.Fragment.connections[0];
            c.centerline[0] = new Vec3d(c.centerline[0].x + 0.1, 0, c.centerline[0].z);
            StringAssert.Contains("does not start at lane end", Assert.Throws<InvalidDataException>(() => WorldValidatorV2.Validate(AsWorld(f))).Message);
        }

        [Test]
        public void CrossingConnectionsWithoutZoneAreRejected()
        {
            var f = new RoadKitTemplates().All.Single(t => t.CatalogId == CityLayouts.Cross);
            f.Fragment.conflictZones = f.Fragment.conflictZones.Where(z => !(z.connectionA.Contains("South.in>North") && z.connectionB.Contains("East.in>West") || z.connectionB.Contains("South.in>North") && z.connectionA.Contains("East.in>West"))).ToArray();
            StringAssert.Contains("without conflict zone", Assert.Throws<InvalidDataException>(() => WorldValidatorV2.Validate(AsWorld(f))).Message);
        }

        [Test]
        public void SignCatalogCoversKitSignsAndIsMarkedUnverified()
        {
            Assert.That(SignCatalog.Verified, Is.False, "codes must be checked against the standard before this flips");
            Assert.That(SignCatalog.PrefabFor("3.24", "40"), Is.EqualTo("DS_Sign_Speed40"));
            Assert.That(SignCatalog.PrefabFor("3.24", "50"), Is.Null, "no 50 km/h sign in the kit");
            Assert.That(SignCatalog.Entries.Select(e => e.Code).Distinct().Count(), Is.EqualTo(SignCatalog.Entries.Count));
        }

        internal static string RepoRoot()
        {
            foreach (var start in new[] { TestContext.CurrentContext.TestDirectory, Directory.GetCurrentDirectory() })
                for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
                    if (Directory.Exists(Path.Combine(dir.FullName, "Assets/DrivingSchool"))) return dir.FullName;
            throw new DirectoryNotFoundException("Project root not found");
        }
    }
}
