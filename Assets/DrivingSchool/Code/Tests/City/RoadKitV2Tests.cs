using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DrivingSchool.Contracts;
using DrivingSchool.Simulation.RoadGraph;
using DrivingSchool.Simulation.Traffic;
using NUnit.Framework;

namespace DrivingSchool.Tests
{
    /// <summary>T55/T56: Road Kit v2 semantics — 2+2 lanes, lane changes, roundabout, level crossing.</summary>
    public sealed class RoadKitV2Tests
    {
        static readonly RoadKitTemplatesV2 V2 = new RoadKitTemplatesV2();
        static readonly RoadKitCatalog Kits = new RoadKitCatalog();
        static ModuleTemplate T(string id) { Kits.TryGet(id, out var t); return t; }

        [Test]
        public void TemplatesMatchTheBlenderKit()
        {
            var json = File.ReadAllText(Path.Combine(ModuleTemplateTests.RepoRoot(), "Assets/DrivingSchool/Art/RoadKitV2/catalog-v2.json"));
            Assert.That(Regex.Match(json, "\"sourceSha256\"\\s*:\\s*\"([0-9a-f]+)\"").Groups[1].Value, Is.EqualTo(RoadKitTemplatesV2.SourceSha256),
                "Road Kit v2 was rebuilt: check RoadKitTemplatesV2 against tools/build_road_kit_v2.py and update the hash");
            foreach (var t in V2.All.Where(x => x.CatalogId != RoadKitTemplatesV2.LaneChange4))
            {
                var block = Regex.Match(json, "\"catalogId\"\\s*:\\s*\"" + t.CatalogId + "\".*?\"sockets\"\\s*:\\s*\\{(.*?)\\}", RegexOptions.Singleline);
                Assert.That(block.Success, t.CatalogId + " missing from catalog-v2.json");
                foreach (var s in t.Sockets)
                {
                    // Blender suffixes repeated object names (".001"); the importer matches sockets by prefix too.
                    var m = Regex.Match(block.Groups[1].Value, "\"" + s.Name + "(\\.\\d{3})?\"\\s*:\\s*\\[([^\\]]*)\\]");
                    Assert.That(m.Success, t.CatalogId + ":" + s.Name + " not in the Blender kit");
                    var v = m.Groups[2].Value.Split(',').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                    // Unity (x, z) = Blender (x, y).
                    Assert.That(s.Position.x, Is.EqualTo(v[0]).Within(0.01), t.CatalogId + ":" + s.Name);
                    Assert.That(s.Position.z, Is.EqualTo(v[1]).Within(0.01), t.CatalogId + ":" + s.Name);
                }
            }
        }

        [Test]
        public void EveryV2TemplateIsAValidGraphFragment()
        {
            foreach (var t in V2.All)
            {
                var f = t.Fragment;
                // Approach priorities need signs, which only a layout gives; the rest must stand on its own.
                var w = new WorldDocumentV2
                {
                    id = "tpl-" + t.CatalogId, nodes = f.nodes, lanes = f.lanes, junctions = f.junctions, connections = f.connections, conflictZones = f.conflictZones,
                    stopLines = f.stopLines, crossings = f.crossings, boundaries = f.boundaries, sidewalks = f.sidewalks, signalGroups = f.signalGroups,
                };
                Assert.DoesNotThrow(() => WorldValidatorV2.Validate(w, SignCatalog.Codes), t.CatalogId);
            }
        }

        [Test]
        public void OnTwoLaneApproachesTheInnerLaneTurnsLeftAndTheOuterRight()
        {
            foreach (var id in new[] { RoadKitTemplatesV2.Cross4x4, RoadKitTemplatesV2.Cross4x2 })
            {
                var f = T(id).Fragment;
                var lanes = f.lanes.ToDictionary(l => l.id);
                foreach (var c in f.connections)
                {
                    var from = lanes[c.fromLaneId]; var to = lanes[c.toLaneId];
                    Assert.That(c.maneuver, Is.Not.EqualTo(LaneManeuver.UTurn), c.id);
                    bool fromTwo = from.leftNeighborId != null || from.rightNeighborId != null;
                    bool toTwo = to.leftNeighborId != null || to.rightNeighborId != null;
                    if (fromTwo && c.maneuver == LaneManeuver.Left) Assert.That(from.index, Is.EqualTo(1), c.id + ": left turns only from the inner lane");
                    if (fromTwo && c.maneuver == LaneManeuver.Right) Assert.That(from.index, Is.EqualTo(2), c.id + ": right turns only from the outer lane");
                    if (toTwo && c.maneuver == LaneManeuver.Left) Assert.That(Math.Abs(to.index), Is.EqualTo(1), c.id + ": a left turn ends in the nearest lane");
                    if (toTwo && c.maneuver == LaneManeuver.Right) Assert.That(Math.Abs(to.index), Is.EqualTo(2), c.id + ": a right turn ends in the nearest lane");
                }
                var south = f.connections.Where(c => c.fromLaneId.StartsWith("South.in")).ToList();
                Assert.That(south.Select(c => c.maneuver).Distinct(), Is.EquivalentTo(new[] { LaneManeuver.Left, LaneManeuver.Straight, LaneManeuver.Right }), id);
            }
        }

        [Test]
        public void TheRoundaboutGoesAnticlockwiseAndTheRingHasPriority()
        {
            var f = T(RoadKitTemplatesV2.Roundabout).Fragment;
            Assert.That(f.junctions.Length, Is.EqualTo(4));
            foreach (var ring in f.lanes.Where(l => l.id.StartsWith("ring.")))
            {
                var line = new Polyline(ring.centerline);
                var p = line.PointAt(line.Length / 2);
                line.TangentAt(line.Length / 2, out double tx, out double tz);
                // Map view (x east, z north): anticlockwise means the angle atan2(z, x) grows along the lane.
                Assert.That(p.x * tz - p.z * tx, Is.GreaterThan(0), ring.id + " must run anticlockwise seen from above");
                Assert.That(Math.Sqrt(p.x * p.x + p.z * p.z), Is.EqualTo(RoadKitTemplatesV2.RingRadiusM).Within(0.05));
            }
            Assert.That(f.approaches.Where(a => a.laneId.StartsWith("ring.")).All(a => a.priority == ApproachPriority.Main));
            Assert.That(f.approaches.Where(a => a.laneId.EndsWith(".in")).All(a => a.priority == ApproachPriority.Secondary));
            // Entering and circulating traffic merge into the same ring lane: a conflict zone guards every entry.
            foreach (var j in f.junctions)
                Assert.That(f.conflictZones.Any(z => z.junctionId == j.id && z.merge), j.id);
        }

        // ------------------------------------------------------------------ traffic

        static DistrictLayout RoundaboutLayout()
        {
            var list = new List<ModuleInstance> { new ModuleInstance { id = "R", catalogId = RoadKitTemplatesV2.Roundabout } };
            var joins = new List<SocketJoin>(); var open = new List<SocketRef>(); var signs = new List<LayoutSign>(); var approaches = new List<LayoutApproach>();
            foreach (var arm in new[] { "East", "North", "West", "South" })
            {
                var m = DistrictCompiler.Dock("a" + arm, T(CityLayouts.Straight), "Socket_End", list[0], T(RoadKitTemplatesV2.Roundabout), "Socket_" + arm);
                list.Add(m);
                joins.Add(new SocketJoin { instanceA = "R", socketA = "Socket_" + arm, instanceB = m.id, socketB = "Socket_End" });
                open.Add(new SocketRef { instanceId = m.id, socket = "Socket_Start" });
                signs.Add(new LayoutSign { id = "y" + arm, code = "2.4", instanceId = "R", laneId = arm + ".in", atS = 2 });
                signs.Add(new LayoutSign { id = "r" + arm, code = "4.3", instanceId = "R", laneId = arm + ".in", atS = 2.4f });
                approaches.Add(new LayoutApproach { instanceId = "R", socket = "Socket_" + arm, priority = ApproachPriority.Secondary, signIds = new[] { "y" + arm, "r" + arm } });
            }
            return new DistrictLayout { id = "ring", instances = list.ToArray(), joins = joins.ToArray(), openSockets = open.ToArray(), signs = signs.ToArray(), approaches = approaches.ToArray() };
        }

        [Test]
        public void ARoundaboutWithoutSignsDoesNotCompile()
        {
            var layout = RoundaboutLayout();
            layout.signs = layout.signs.Where(s => s.code != "4.3").ToArray();
            foreach (var a in layout.approaches) a.signIds = a.signIds.Where(x => x.StartsWith("y")).ToArray();
            Assert.Throws<InvalidDataException>(() => DistrictCompiler.Compile(layout, Kits), "the ring has priority only with 4.3 at the entries");
        }

        [Test]
        public void ThreeMinutesOnTheRoundaboutWithoutOverlapsAndEveryArmUsed()
        {
            var world = DistrictCompiler.Compile(RoundaboutLayout(), Kits);
            var run = new TrafficRun(world, new TrafficProfile { MaxVehicles = 10 }, seed: 3);
            var exits = new HashSet<string>(); var stopped = new Dictionary<string, double>();
            run.Run(180, r =>
            {
                TrafficRun.AssertNoOverlaps(r.Director.Snapshot);
                foreach (var p in r.Director.Snapshot.Participants.Where(x => x.Kind == ParticipantKind.Vehicle))
                {
                    if (p.PathId != null && p.PathId.StartsWith("R/c:ring.") && p.PathId.Contains(".out")) exits.Add(p.PathId.Substring(p.PathId.IndexOf('>') + 1));
                    if (p.SpeedMps > 0.1) stopped.Remove(p.Id);
                    else if (!stopped.ContainsKey(p.Id)) stopped[p.Id] = r.Time;
                    else Assert.That(r.Time - stopped[p.Id], Is.LessThan(60), p.Id + " stuck: " + p.Decision);
                }
            });
            Assert.That(exits.Count, Is.EqualTo(4), "cars leave the ring through every arm: " + string.Join(", ", exits));
        }

        [Test]
        public void OnATwoPlusTwoStretchSomeCarsChangeLanesWithTheirIndicatorOn()
        {
            var list = new List<ModuleInstance> { new ModuleInstance { id = "a", catalogId = RoadKitTemplatesV2.Straight4 } };
            var b = DistrictCompiler.Dock("b", T(RoadKitTemplatesV2.LaneChange4), "Socket_Start", list[0], T(RoadKitTemplatesV2.Straight4), "Socket_End"); list.Add(b);
            var c = DistrictCompiler.Dock("c", T(RoadKitTemplatesV2.Straight4), "Socket_Start", b, T(RoadKitTemplatesV2.LaneChange4), "Socket_End"); list.Add(c);
            var layout = new DistrictLayout
            {
                id = "lc", instances = list.ToArray(),
                joins = new[] { new SocketJoin { instanceA = "a", socketA = "Socket_End", instanceB = "b", socketB = "Socket_Start" }, new SocketJoin { instanceA = "b", socketA = "Socket_End", instanceB = "c", socketB = "Socket_Start" } },
                openSockets = new[] { new SocketRef { instanceId = "a", socket = "Socket_Start" }, new SocketRef { instanceId = "c", socket = "Socket_End" } },
            };
            var world = DistrictCompiler.Compile(layout, Kits);
            Assert.That(world.connections.Count(x => Math.Abs(RoadKitTemplatesV2.LateralShift(x.centerline)) > 3), Is.EqualTo(4), "two lane changes per direction");
            var run = new TrafficRun(world, new TrafficProfile { MaxVehicles = 8 }, seed: 5);
            int changes = 0, signalled = 0; var seen = new HashSet<string>();
            run.Run(120, r =>
            {
                TrafficRun.AssertNoOverlaps(r.Director.Snapshot);
                foreach (var p in r.Director.Snapshot.Participants.Where(x => x.Kind == ParticipantKind.Vehicle && x.PathId != null && x.PathId.StartsWith("b/c:")))
                {
                    var conn = world.connections.First(x => x.id == p.PathId);
                    double shift = RoadKitTemplatesV2.LateralShift(conn.centerline);
                    if (Math.Abs(shift) < 1.5 || !seen.Add(p.Id + p.PathId)) continue;
                    changes++;
                    if (shift > 0 ? p.RightIndicator : p.LeftIndicator) signalled++;
                }
            });
            Assert.That(changes, Is.GreaterThan(3), "cars change lanes");
            Assert.That(signalled, Is.EqualTo(changes), "every lane change is signalled");
        }

        static WorldDocumentV2 RailWorld()
        {
            var list = new List<ModuleInstance> { new ModuleInstance { id = "rc", catalogId = RoadKitTemplatesV2.RailCrossing } };
            var a = DistrictCompiler.Dock("a", T(CityLayouts.Straight), "Socket_End", list[0], T(RoadKitTemplatesV2.RailCrossing), "Socket_Start"); list.Add(a);
            var b = DistrictCompiler.Dock("b", T(CityLayouts.Straight), "Socket_Start", list[0], T(RoadKitTemplatesV2.RailCrossing), "Socket_End"); list.Add(b);
            return DistrictCompiler.Compile(new DistrictLayout
            {
                id = "rail", instances = list.ToArray(),
                joins = new[] { new SocketJoin { instanceA = "rc", socketA = "Socket_Start", instanceB = "a", socketB = "Socket_End" }, new SocketJoin { instanceA = "rc", socketA = "Socket_End", instanceB = "b", socketB = "Socket_Start" } },
                openSockets = new[] { new SocketRef { instanceId = "a", socket = "Socket_Start" }, new SocketRef { instanceId = "b", socket = "Socket_End" } },
            }, Kits);
        }

        [Test]
        public void AClosedLevelCrossingStopsCarsAtTheStopLineAndPeopleAtTheTracks()
        {
            var world = RailWorld();
            var run = new TrafficRun(world, new TrafficProfile { MaxVehicles = 0 }, seed: 1);
            Assert.That(run.Director.ExternalGroups, Is.EquivalentTo(new[] { "rc/rail.f", "rc/rail.b", "rc/rail.ped" }));
            foreach (var g in run.Director.ExternalGroups.ToList()) run.Director.SetExternalAspect(g, SignalAspect.Red);
            var car = run.Director.AddVehicle(new[] { "a/f", "rc/f.in", "rc/c:f.in>f.out", "rc/f.out", "b/f" }, 2, 11);
            var ped = run.Director.Pedestrians.PlaceAtCrossing("rc/rail.walk.R");
            run.Run(15);
            var p = run.Director.Snapshot.Participants.First(x => x.Id == car);
            var stop = world.stopLines.First(s => s.laneId == "rc/f.in");
            Assert.That(p.PathId == "a/f" || p.PathId == "rc/f.in", "the car did not enter the crossing: " + p.PathId);
            Assert.That(p.SpeedMps, Is.LessThan(0.1));
            if (p.PathId == "rc/f.in") Assert.That(p.S + p.LengthM / 2, Is.LessThanOrEqualTo(stop.s + 0.3));
            Assert.That(run.Director.Pedestrians.Find(ped).Phase, Is.EqualTo(PedestrianPhase.Waiting));
            // Open again: cars see no signal (drive by the rules), people may walk.
            run.Director.SetExternalAspect("rc/rail.f", SignalAspect.Off); run.Director.SetExternalAspect("rc/rail.b", SignalAspect.Off);
            run.Director.SetExternalAspect("rc/rail.ped", SignalAspect.Green);
            run.Run(15);
            var after = run.Director.Snapshot.Participants.FirstOrDefault(x => x.Id == car);
            Assert.That(after == null || after.PathId == "b/f" || after.PathId == null, "the car went over: " + after?.PathId);
            Assert.That(run.Director.Pedestrians.Find(ped).Phase, Is.Not.EqualTo(PedestrianPhase.Waiting));
        }
    }
}
