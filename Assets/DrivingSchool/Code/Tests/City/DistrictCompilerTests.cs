using System.IO;
using System.Linq;
using DrivingSchool.Contracts;
using DrivingSchool.Simulation.RoadGraph;
using NUnit.Framework;

namespace DrivingSchool.Tests
{
    public sealed class DistrictCompilerTests
    {
        [Test]
        public void CrossWithArmsCompilesAndStitchesLanes()
        {
            var w = CityLayouts.Compile(CityLayouts.CrossWithArms(2));
            // north0 is docked to the cross by its end socket: its forward lane enters the cross from the north.
            var into = w.lanes.Single(l => l.id == "north0/f");
            Assert.That(into.successors, Is.EqualTo(new[] { "c/North.in" }));
            Assert.That(w.connections.Count(c => c.fromLaneId == "c/North.in"), Is.EqualTo(3));
            Assert.That(w.lanes.Single(l => l.id == "c/South.out").successors, Is.EqualTo(new[] { "south0/b" }));
            // 4 open arm ends: one entering lane each.
            Assert.That(w.spawnPoints.Count(s => s.role == SpawnRole.Vehicle), Is.EqualTo(4));
            Assert.That(w.approaches.Length, Is.EqualTo(4));
            Assert.That(w.approaches.All(a => a.priority == ApproachPriority.Equal), Is.True);
        }

        [Test]
        public void SidewalksAreLinkedAcrossJoins()
        {
            var w = CityLayouts.Compile(CityLayouts.CrossWithArms(2));
            var inner = w.sidewalks.Single(s => s.id == "north0/walk.L");
            Assert.That(inner.linkedIds.Any(id => id.StartsWith("north1/")), Is.True);
        }

        [Test]
        public void SpeedSignZoneEndsAtTheNextStopLine()
        {
            var layout = CityLayouts.CrossWithArms(2);
            layout.signs = new[] { new LayoutSign { id = "limit40", code = "3.24", value = "40", instanceId = "north1", laneId = "f", atS = 5, untilNextJunction = true } };
            var w = CityLayouts.Compile(layout);
            var sign = w.signs.Single();
            Assert.That(sign.catalogId, Is.EqualTo("DS_Sign_Speed40"));
            Assert.That(sign.zoneEndLaneId, Is.EqualTo("c/North.in"));
            Assert.That(sign.zoneEndS, Is.EqualTo(w.stopLines.Single(s => s.laneId == "c/North.in").s));
        }

        [Test]
        public void CompilationIsDeterministic()
        {
            var a = GraphFingerprint.Compute(CityLayouts.Compile(CityLayouts.CrossWithArms(2)));
            var b = GraphFingerprint.Compute(CityLayouts.Compile(CityLayouts.CrossWithArms(2)));
            Assert.That(a, Is.EqualTo(b));
            var c = GraphFingerprint.Compute(CityLayouts.Compile(CityLayouts.CrossWithArms(3)));
            Assert.That(c, Is.Not.EqualTo(a));
        }

        [Test]
        public void MisplacedModuleReportsBothInstancesAndGap()
        {
            var layout = CityLayouts.CrossWithArms(1);
            layout.instances.Single(i => i.id == "north0").z += 0.5;
            var ex = Assert.Throws<InvalidDataException>(() => CityLayouts.Compile(layout));
            StringAssert.Contains("c:Socket_North", ex.Message);
            StringAssert.Contains("north0:Socket_End", ex.Message);
            StringAssert.Contains("gap 0.50 m", ex.Message);
        }

        [Test]
        public void DanglingSocketIsRejected()
        {
            var layout = CityLayouts.CrossWithArms(1);
            layout.openSockets = layout.openSockets.Skip(1).ToArray();
            StringAssert.Contains("Dangling socket", Assert.Throws<InvalidDataException>(() => CityLayouts.Compile(layout)).Message);
        }

        [Test]
        public void DifferentProfilesCannotBeJoined()
        {
            var kit = new RoadKitTemplates();
            var source = new WideStraightSource(kit);
            var layout = CityLayouts.CrossWithArms(1);
            layout.instances.Single(i => i.id == "north0").catalogId = "WIDE";
            StringAssert.Contains("profiles differ", Assert.Throws<InvalidDataException>(() => DistrictCompiler.Compile(layout, source)).Message);
        }

        [Test]
        public void LoopOfCurrentKitDoesNotCloseByFourMetres()
        {
            // Two crosses 124 m apart (24 + 5 x 20 m inside); outer loop: curve, 5 straights, curve.
            var kit = CityLayouts.Kit;
            var a = new ModuleInstance { id = "a", catalogId = CityLayouts.Cross };
            var b = new ModuleInstance { id = "b", catalogId = CityLayouts.Cross, z = 124 };
            var inst = new System.Collections.Generic.List<ModuleInstance> { a, b };
            var joins = new System.Collections.Generic.List<SocketJoin>();
            ModuleInstance prev = a; string prevSocket = "Socket_North";
            for (int i = 0; i < 5; i++)
            {
                var s = DistrictCompiler.Dock("in" + i, CityLayouts.T(CityLayouts.Straight), "Socket_Start", prev, CityLayouts.T(prev.catalogId), prevSocket);
                inst.Add(s); joins.Add(new SocketJoin { instanceA = prev.id, socketA = prevSocket, instanceB = s.id, socketB = "Socket_Start" });
                prev = s; prevSocket = "Socket_End";
            }
            joins.Add(new SocketJoin { instanceA = prev.id, socketA = prevSocket, instanceB = "b", socketB = "Socket_South" });

            // Leaving a to the west, the curve turns right (north).
            var c1 = DistrictCompiler.Dock("c1", CityLayouts.T(CityLayouts.Curve), "Socket_Start", a, CityLayouts.T(CityLayouts.Cross), "Socket_West");
            inst.Add(c1); joins.Add(new SocketJoin { instanceA = "a", socketA = "Socket_West", instanceB = "c1", socketB = "Socket_Start" });
            prev = c1; prevSocket = "Socket_End";
            for (int i = 0; i < 5; i++)
            {
                var s = DistrictCompiler.Dock("out" + i, CityLayouts.T(CityLayouts.Straight), "Socket_Start", prev, CityLayouts.T(prev.catalogId), prevSocket);
                inst.Add(s); joins.Add(new SocketJoin { instanceA = prev.id, socketA = prevSocket, instanceB = s.id, socketB = "Socket_Start" });
                prev = s; prevSocket = "Socket_End";
            }
            // The second curve turns right again (east) and should land on b's west socket.
            var c2 = DistrictCompiler.Dock("c2", CityLayouts.T(CityLayouts.Curve), "Socket_Start", prev, CityLayouts.T(CityLayouts.Straight), prevSocket);
            inst.Add(c2); joins.Add(new SocketJoin { instanceA = prev.id, socketA = prevSocket, instanceB = "c2", socketB = "Socket_Start" });
            joins.Add(new SocketJoin { instanceA = "c2", socketA = "Socket_End", instanceB = "b", socketB = "Socket_West" });

            var layout = new DistrictLayout { id = "loop", instances = inst.ToArray(), joins = joins.ToArray() };
            var ex = Assert.Throws<InvalidDataException>(() => DistrictCompiler.Compile(layout, kit));
            StringAssert.Contains("gap 4.00 m", ex.Message);
        }

        [Test]
        public void SignalPlanCreatesGroupsHeadsAndSignalizedApproaches()
        {
            var layout = CityLayouts.CrossWithArms(1);
            layout.signalPlans = new[] { CityLayouts.TwoPhasePlan() };
            var w = CityLayouts.Compile(layout);
            Assert.That(w.signalPlans.Single().stages.Length, Is.EqualTo(2));
            Assert.That(w.junctions.Single().signalPlanId, Is.EqualTo("c/plan"));
            Assert.That(w.connections.All(c => c.signalGroupId == "c/sg." + "Socket_" + c.fromLaneId.Split('/')[1].Split('.')[0]), Is.True);
            Assert.That(w.crossings.All(c => c.signalGroupId != null), Is.True);
            Assert.That(w.signals.Count(s => s.catalogId == "DS_Signal_Vehicle"), Is.EqualTo(4));
            Assert.That(w.signals.Count(s => s.catalogId == "DS_Signal_Pedestrian"), Is.EqualTo(8));
            Assert.That(w.approaches.All(a => a.priority == ApproachPriority.Signalized), Is.True);
        }

        [Test]
        public void PlanMustServeEveryApproach()
        {
            var layout = CityLayouts.CrossWithArms(1);
            var plan = CityLayouts.TwoPhasePlan();
            plan.stages = plan.stages.Take(1).ToArray();
            layout.signalPlans = new[] { plan };
            StringAssert.Contains("never gives green", Assert.Throws<InvalidDataException>(() => CityLayouts.Compile(layout)).Message);
        }

        [Test]
        public void SecondaryApproachNeedsYieldSign()
        {
            var layout = CityLayouts.CrossWithArms(2);
            layout.approaches = new[] { new LayoutApproach { instanceId = "c", socket = "Socket_East", priority = ApproachPriority.Secondary } };
            StringAssert.Contains("Secondary approach", Assert.Throws<InvalidDataException>(() => CityLayouts.Compile(layout)).Message);

            layout.signs = new[] { new LayoutSign { id = "yield-e", code = "2.4", instanceId = "east0", laneId = "f", atS = 15 } };
            layout.approaches[0].signIds = new[] { "yield-e" };
            var w = CityLayouts.Compile(layout);
            Assert.That(w.approaches.Single(a => a.laneId == "c/East.in").priority, Is.EqualTo(ApproachPriority.Secondary));
        }

        sealed class WideStraightSource : IModuleTemplateSource
        {
            readonly RoadKitTemplates kit;
            public WideStraightSource(RoadKitTemplates kit) { this.kit = kit; }
            public bool TryGet(string id, out ModuleTemplate t)
            {
                if (id != "WIDE") return kit.TryGet(id, out t);
                kit.TryGet(CityLayouts.Straight, out var s);
                t = new ModuleTemplate
                {
                    CatalogId = "WIDE", SourceSha256 = s.SourceSha256, Fragment = s.Fragment,
                    Sockets = s.Sockets.Select(x => new TemplateSocket { Name = x.Name, Position = x.Position, OutHeadingDeg = x.OutHeadingDeg, InLaneIds = x.InLaneIds, OutLaneIds = x.OutLaneIds.Concat(new[] { "extra" }).ToArray(), LaneWidthM = x.LaneWidthM }).ToArray(),
                };
                return true;
            }
        }
    }
}
