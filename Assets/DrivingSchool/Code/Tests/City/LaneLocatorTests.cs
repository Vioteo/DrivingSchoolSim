using System.Diagnostics;
using System.Linq;
using DrivingSchool.Simulation.RoadGraph;
using NUnit.Framework;

namespace DrivingSchool.Tests
{
    public sealed class LaneLocatorTests
    {
        [Test]
        public void CarRightOfLaneCentreIsOnItsLane()
        {
            var index = new RoadGraphIndex(CityLayouts.Compile(CityLayouts.StraightChain(3)));
            var locator = new LaneLocator(index);
            // Forward lanes run +Z at x = +1.825; 0.2 m to the right is x = 2.025.
            var p = locator.Locate(2.025, 10, 0, default);
            Assert.That(p.PathId, Is.EqualTo("s0/f"));
            Assert.That(p.D, Is.EqualTo(0.2).Within(1e-6));
            var q = locator.Locate(2.025, 30, 0, p);
            Assert.That(q.PathId, Is.EqualTo("s1/f"));
            Assert.That(q.S, Is.EqualTo(10).Within(1e-6));
        }

        [Test]
        public void SwayingOnTheBoundaryDoesNotFlipTheLane()
        {
            var w = CityTestWorlds.ExampleV2();                 // 2 + 2 lanes, 3.5 m, south arm runs +Z at x = 1.75 and 5.25
            var locator = new LaneLocator(new RoadGraphIndex(w));
            var pos = locator.Locate(1.75, -100, 0, default);
            Assert.That(pos.PathId, Is.EqualTo("south-centre:0:0"));
            for (int i = 0; i < 50; i++)
            {
                double x = 3.5 + (i % 2 == 0 ? 0.1 : -0.1);    // boundary between the two lanes
                pos = locator.Locate(x, -100 + i, 0, pos);
                Assert.That(pos.PathId, Is.EqualTo("south-centre:0:0"), "step " + i);
            }
            // Clearly in the right lane: the locator follows.
            pos = locator.Locate(5.25, -40, 0, pos);
            Assert.That(pos.PathId, Is.EqualTo("south-centre:0:1"));
        }

        [Test]
        public void LeftTurnThroughJunctionFollowsTheTurnConnection()
        {
            var w = CityLayouts.Compile(CityLayouts.CrossWithArms(1));
            var index = new RoadGraphIndex(w);
            var locator = new LaneLocator(index);
            var turn = index.Path("c/c:South.in>West.out");
            var pos = locator.Locate(1.825, -11, 0, default);
            Assert.That(pos.PathId, Is.EqualTo("c/South.in"));
            string last = pos.PathId;
            for (double s = 0; s <= turn.Length; s += 0.5)
            {
                var p = turn.Line.PointAt(s);
                pos = locator.Locate(p.x, p.z, turn.Line.HeadingAt(s), pos);
                Assert.That(pos.PathId, Is.EqualTo(turn.Id).Or.EqualTo("c/West.out"), "s=" + s);
                last = pos.PathId;
            }
            pos = locator.Locate(-11, 1.825, -System.Math.PI / 2, pos); // heading -X keeps right: +Z side
            Assert.That(pos.PathId, Is.EqualTo("c/West.out"));
        }

        [Test]
        public void SidewalkIsOffRoad()
        {
            var locator = new LaneLocator(new RoadGraphIndex(CityLayouts.Compile(CityLayouts.StraightChain(2))));
            Assert.That(locator.Locate(5.2, 10, 0, default).OffRoad, Is.True);
            Assert.That(locator.Locate(40, 10, 0, default).OffRoad, Is.True);
        }

        [Test]
        public void DrivingAgainstTheLaneIsFlagged()
        {
            var locator = new LaneLocator(new RoadGraphIndex(CityLayouts.Compile(CityLayouts.StraightChain(2))));
            // On the backward lane (x = -1.825, runs -Z) but facing +Z.
            var p = locator.Locate(-1.825, 10, 0, default);
            Assert.That(p.PathId, Is.EqualTo("s0/b"));
            Assert.That(p.AgainstDirection, Is.True);
        }

        [Test]
        public void HundredParticipantsThousandTicksFitTheBudget()
        {
            var index = new RoadGraphIndex(CityLayouts.Compile(CityLayouts.CrossWithArms(3)));
            var locator = new LaneLocator(index);
            var paths = index.Paths.ToArray();
            var state = new LanePosition[100];
            var watch = Stopwatch.StartNew();
            for (int tick = 0; tick < 1000; tick++)
                for (int i = 0; i < 100; i++)
                {
                    var p = paths[i % paths.Length];
                    double s = (tick * 0.1 + i) % p.Length;
                    var pt = p.Line.PointAt(s);
                    state[i] = locator.Locate(pt.x, pt.z, p.Line.HeadingAt(s), state[i]);
                }
            watch.Stop();
            TestContext.WriteLine("100 x 1000 Locate: " + watch.ElapsedMilliseconds + " ms");
            Assert.That(watch.ElapsedMilliseconds, Is.LessThan(5000), "budget recorded after the first measurement");
        }
    }
}
