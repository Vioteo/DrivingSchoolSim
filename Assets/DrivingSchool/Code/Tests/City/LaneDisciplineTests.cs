using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DrivingSchool.Contracts;
using DrivingSchool.Simulation.RoadGraph;
using DrivingSchool.Simulation.Traffic;
using NUnit.Framework;

namespace DrivingSchool.Tests
{
    /// <summary>
    /// T65: turns from the extreme lanes (ПДД РФ 8.5) and signs 5.15.1 / 5.15.2 that change which movements a lane allows.
    /// </summary>
    public sealed class LaneDisciplineTests
    {
        static readonly RoadKitCatalog Kits = new RoadKitCatalog();
        static ModuleTemplate T(string id) { Kits.TryGet(id, out var t); return t; }

        /// <summary>A 2+2 cross "X" at the origin, two 2+2 straights on every arm, ends open; optional signs.</summary>
        internal static DistrictLayout Cross4(params LayoutSign[] signs)
        {
            var x = new ModuleInstance { id = "X", catalogId = RoadKitTemplatesV2.Cross4x4 };
            var list = new List<ModuleInstance> { x }; var joins = new List<SocketJoin>(); var open = new List<SocketRef>();
            foreach (var arm in new[] { "North", "South", "East", "West" })
            {
                ModuleInstance prev = x; string socket = "Socket_" + arm;
                for (int i = 0; i < 2; i++)
                {
                    var id = arm.Substring(0, 1).ToLowerInvariant() + i;
                    var m = DistrictCompiler.Dock(id, T(RoadKitTemplatesV2.Straight4), "Socket_End", prev, T(prev.catalogId), socket);
                    list.Add(m); joins.Add(new SocketJoin { instanceA = prev.id, socketA = socket, instanceB = id, socketB = "Socket_End" });
                    prev = m; socket = "Socket_Start";
                }
                open.Add(new SocketRef { instanceId = prev.id, socket = socket });
            }
            return new DistrictLayout { id = "cross4", instances = list.ToArray(), joins = joins.ToArray(), openSockets = open.ToArray(), signs = signs };
        }

        static LayoutSign Lanes(string id, string arm, string value) =>
            new LayoutSign { id = id, code = LaneDirectionSign.Lanes, value = value, instanceId = "X", laneId = arm + ".in2", atS = 0.5f };

        static IEnumerable<LaneConnection> From(WorldDocumentV2 w, string lane) => w.connections.Where(c => c.fromLaneId == lane);

        [Test]
        public void WithoutSignsRightTurnsLeaveFromTheOuterLaneAndLeftTurnsFromTheInner()
        {
            var w = DistrictCompiler.Compile(Cross4(), Kits);
            foreach (var arm in new[] { "North", "South", "East", "West" })
            {
                var inner = From(w, "X/" + arm + ".in1").Select(c => c.maneuver).Distinct().ToList();
                var outer = From(w, "X/" + arm + ".in2").Select(c => c.maneuver).Distinct().ToList();
                Assert.That(inner, Is.EquivalentTo(new[] { LaneManeuver.Straight, LaneManeuver.Left }), arm + " inner lane");
                Assert.That(outer, Is.EquivalentTo(new[] { LaneManeuver.Straight, LaneManeuver.Right }), arm + " outer lane");
                Assert.That(w.lanes.Single(l => l.id == "X/" + arm + ".in1").allowedManeuvers, Is.EqualTo(LaneManeuver.Straight | LaneManeuver.Left));
            }
        }

        [Test]
        public void LaneDirectionSignsChangeWhatEachLaneMayDo()
        {
            var w = DistrictCompiler.Compile(Cross4(Lanes("east", "East", "L|SR"), Lanes("west", "West", "L|LSR")), Kits);
            Assert.That(From(w, "X/East.in1").Select(c => c.maneuver).Distinct(), Is.EquivalentTo(new[] { LaneManeuver.Left }), "east inner: left only");
            Assert.That(From(w, "X/East.in2").Select(c => c.maneuver).Distinct(), Is.EquivalentTo(new[] { LaneManeuver.Straight, LaneManeuver.Right }));
            Assert.That(From(w, "X/West.in2").Select(c => c.maneuver).Distinct(), Is.EquivalentTo(new[] { LaneManeuver.Left, LaneManeuver.Straight, LaneManeuver.Right }),
                "west outer: left too");
            // Two lanes turn left together: each into its own lane of the road they turn into.
            var lefts = w.connections.Where(c => c.fromLaneId.StartsWith("X/West.in") && c.maneuver == LaneManeuver.Left).Select(c => c.toLaneId).ToList();
            Assert.That(lefts, Is.EquivalentTo(new[] { "X/North.out1", "X/North.out2" }));
            Assert.That(w.lanes.Single(l => l.id == "X/West.in2").allowedManeuvers, Is.EqualTo(LaneManeuver.Left | LaneManeuver.Straight | LaneManeuver.Right));
            // Untouched approaches keep the default discipline; the crossings still know the connections over them.
            Assert.That(From(w, "X/North.in2").Select(c => c.maneuver).Distinct(), Is.EquivalentTo(new[] { LaneManeuver.Straight, LaneManeuver.Right }));
            Assert.That(w.crossings.Single(c => c.id == "X/crossing.North").laneIds, Does.Contain(w.connections.Single(c => c.fromLaneId == "X/West.in2" && c.toLaneId == "X/North.out2").id));
            Assert.That(w.signs.Single(s => s.id == "east").catalogId, Is.EqualTo("DS_Sign_LaneDir_L_SR"));
        }

        [Test]
        public void LaneDirectionValuesAreChecked()
        {
            var lanes = new[] { "East.in1", "East.in2" };
            Assert.That(LaneDirectionSign.Parse("5.15.1", "L|SR", "East.in2", lanes)["East.in1"], Is.EqualTo(LaneManeuver.Left));
            Assert.That(LaneDirectionSign.Parse("5.15.2", "LS", "East.in1", lanes)["East.in1"], Is.EqualTo(LaneManeuver.Left | LaneManeuver.Straight));
            Assert.Throws<FormatException>(() => LaneDirectionSign.Parse("5.15.1", "L", "East.in2", lanes), "one lane given for two");
            Assert.Throws<FormatException>(() => LaneDirectionSign.Parse("5.15.1", "L|SX", "East.in2", lanes));
            Assert.Throws<FormatException>(() => LaneDirectionSign.Parse("5.15.2", "LS", "North.in1", lanes));
            Assert.That(LaneDirectionSign.Format(LaneManeuver.Left | LaneManeuver.Right), Is.EqualTo("LR"));
            // A sign on a lane that is not an incoming lane of the junction is rejected by the compiler.
            var bad = Cross4(new LayoutSign { id = "bad", code = LaneDirectionSign.Lanes, value = "L|SR", instanceId = "X", laneId = "East.out2", atS = 0.5f });
            Assert.Throws<InvalidDataException>(() => DistrictCompiler.Compile(bad, Kits));
        }

        [Test]
        public void BotsOnlyMakeTheMovementsTheirLaneAllows()
        {
            var w = DistrictCompiler.Compile(Cross4(Lanes("east", "East", "L|SR"), Lanes("west", "West", "L|LSR")), Kits);
            var index = new RoadGraphIndex(w);
            var run = new TrafficRun(w, new TrafficProfile { MaxVehicles = 10 }, seed: 3);
            int turns = 0; var checkedIds = new HashSet<string>();
            run.Run(180, r =>
            {
                TrafficRun.AssertNoOverlaps(r.Director.Snapshot);
                foreach (var p in r.Director.Snapshot.Participants.Where(x => x.Kind == ParticipantKind.Vehicle))
                {
                    var path = index.Path(p.PathId);
                    if (!path.IsConnection || !index.IsIntersection(path.Connection.junctionId) || !checkedIds.Add(p.Id + path.Id)) continue;
                    var allowed = index.Path(path.Connection.fromLaneId).Lane.allowedManeuvers;
                    Assert.That(allowed == LaneManeuver.None || (allowed & path.Connection.maneuver) != 0, p.Id + " made " + path.Connection.maneuver + " from " + path.Connection.fromLaneId);
                    if (path.Connection.maneuver != LaneManeuver.Straight) turns++;
                }
            });
            Assert.That(turns, Is.GreaterThan(5), "bots turned at the junction");
        }
    }
}
