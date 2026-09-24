using System.IO;
using System.Linq;
using DrivingSchool.Contracts;
using DrivingSchool.Simulation.Traffic;
using NUnit.Framework;

namespace DrivingSchool.Tests
{
    public sealed class SignalControllerTests
    {
        static (WorldDocumentV2 w, SignalController c) Signalized()
        {
            var layout = CityLayouts.CrossWithArms(1);
            layout.signalPlans = new[] { CityLayouts.TwoPhasePlan() };
            var w = CityLayouts.Compile(layout);
            return (w, new SignalController(w.signalPlans[0], w.signalGroups));
        }

        static bool Moving(SignalAspect a) => a == SignalAspect.Green || a == SignalAspect.GreenFlashing || a == SignalAspect.Amber;

        [Test]
        public void CrossingApproachesAreNeverGreenTogether()
        {
            var (_, c) = Signalized();
            for (double t = 0; t < c.CycleSeconds * 2; t += 0.1)
            {
                c.Tick(t);
                bool ns = Moving(c.GetAspect("c/sg.Socket_North")) || Moving(c.GetAspect("c/sg.Socket_South"));
                bool ew = Moving(c.GetAspect("c/sg.Socket_East")) || Moving(c.GetAspect("c/sg.Socket_West"));
                Assert.That(ns && ew, Is.False, "t=" + t);
            }
        }

        [Test]
        public void GreenFlashesThenAmberThenRedAmberForTheOthers()
        {
            var (_, c) = Signalized();
            Assert.That(c.AspectAt("c/sg.Socket_North", 1), Is.EqualTo(SignalAspect.Green));
            Assert.That(c.AspectAt("c/sg.Socket_North", 16), Is.EqualTo(SignalAspect.GreenFlashing));
            Assert.That(c.AspectAt("c/sg.Socket_North", 19), Is.EqualTo(SignalAspect.Amber));
            Assert.That(c.AspectAt("c/sg.Socket_North", 21.5), Is.EqualTo(SignalAspect.Red));
            Assert.That(c.AspectAt("c/sg.Socket_East", 22.5), Is.EqualTo(SignalAspect.RedAmber));
            Assert.That(c.AspectAt("c/sg.Socket_East", 23.5), Is.EqualTo(SignalAspect.Green));
            Assert.That(c.CycleSeconds, Is.EqualTo(46));
        }

        [Test]
        public void PedestriansNeverWalkAcrossGreenThroughTraffic()
        {
            var (w, c) = Signalized();
            var straight = w.connections.Where(x => x.maneuver == LaneManeuver.Straight).ToList();
            for (double t = 0; t < c.CycleSeconds; t += 0.25)
                foreach (var g in w.signalGroups.Where(g => g.kind == SignalGroupKind.Pedestrian))
                {
                    var walk = c.AspectAt(g.id, t);
                    Assert.That(walk, Is.Not.EqualTo(SignalAspect.Amber).And.Not.EqualTo(SignalAspect.RedAmber));
                    if (walk != SignalAspect.Green && walk != SignalAspect.GreenFlashing) continue;
                    var crossed = w.crossings.Single(x => x.id == g.crossingIds[0]).laneIds;
                    foreach (var conn in straight.Where(x => crossed.Contains(x.id)))
                        Assert.That(Moving(c.AspectAt(conn.signalGroupId, t)), Is.False, g.id + " vs " + conn.id + " at " + t);
                }
        }

        [Test]
        public void AspectDependsOnTimeOnlyNotOnTickRate()
        {
            var (_, a) = Signalized(); var (_, b) = Signalized();
            double ta = 0, tb = 0;
            for (int i = 0; i < 30 * 40; i++) { ta += 1.0 / 30; a.Tick(ta); }
            for (int i = 0; i < 120 * 40; i++) { tb += 1.0 / 120; b.Tick(tb); }
            foreach (var g in a.GroupIds) Assert.That(a.GetAspect(g), Is.EqualTo(b.GetAspect(g)), g);
        }

        [Test]
        public void FlashingAmberModeForUnregulatedJunction()
        {
            var (_, c) = Signalized();
            c.Mode = SignalMode.FlashingAmber;
            Assert.That(c.GetAspect("c/sg.Socket_North"), Is.EqualTo(SignalAspect.AmberFlashing));
            Assert.That(c.GetAspect("c/pg.Socket_East"), Is.EqualTo(SignalAspect.Off));
        }

        [Test]
        public void PlanWithCrossingThroughMovementsIsRejected()
        {
            var (w, _) = Signalized();
            var plan = w.signalPlans[0];
            plan.stages[0].greenGroupIds = plan.stages[0].greenGroupIds.Concat(new[] { "c/sg.Socket_East" }).ToArray();
            Assert.Throws<InvalidDataException>(() => SignalController.ValidatePlan(plan, w));
            SignalController.ValidatePlan(Signalized().w.signalPlans[0], Signalized().w);
        }

        [Test]
        public void TimeToChangeCountsDownToAmber()
        {
            var (_, c) = Signalized();
            c.Tick(10);
            Assert.That(c.TimeToChange("c/sg.Socket_North"), Is.EqualTo(5).Within(0.1)); // flashing starts at 15 s
        }
    }
}
