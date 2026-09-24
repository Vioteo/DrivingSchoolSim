using System;
using System.Linq;
using DrivingSchool.Contracts;
using DrivingSchool.Simulation.RoadGraph;
using DrivingSchool.Simulation.Traffic;
using NUnit.Framework;

namespace DrivingSchool.Tests
{
    public sealed class LaneFollowerTests
    {
        const double Dt = 0.02;
        static RoadGraphIndex Chain(int n) => new RoadGraphIndex(CityLayouts.Compile(CityLayouts.StraightChain(n)));

        [Test]
        public void AcceleratesGentlyToTheSpeedLimitAndHoldsTheLaneCentre()
        {
            var index = Chain(40); // 800 m
            var agent = new LaneFollowerAgent("a", index, CityLayouts.ForwardRoute(40), 5, DriverProfile.Normal());
            double prev = 0, maxAcc = 0;
            for (int i = 0; i < 30 / Dt; i++)
            {
                agent.Update(Dt, double.PositiveInfinity, 0, double.PositiveInfinity);
                maxAcc = Math.Max(maxAcc, (agent.CurrentSpeedMps - prev) / Dt);
                prev = agent.CurrentSpeedMps;
                Assert.That(Math.Abs(agent.Position.x - RoadKitTemplates.LaneOffsetM), Is.LessThan(0.05));
            }
            Assert.That(maxAcc, Is.LessThanOrEqualTo(1.8 + 1e-6));
            Assert.That(agent.CurrentSpeedMps, Is.EqualTo(60 / 3.6).Within(0.3));
        }

        [Test]
        public void StopsTwoAndAHalfMetresBehindAStandingObstacle()
        {
            var index = Chain(10);
            var agent = new LaneFollowerAgent("a", index, CityLayouts.ForwardRoute(10), 10, DriverProfile.Normal(), 10);
            double obstacleRear = agent.CurrentDistanceAlongLane + agent.Profile.LengthM / 2 + 20; // 20 m gap
            for (int i = 0; i < 20 / Dt; i++)
            {
                double gap = obstacleRear - (Along(agent) + agent.Profile.LengthM / 2);
                agent.Update(Dt, gap, 0, double.PositiveInfinity);
                Assert.That(agent.TargetAccelerationMps2, Is.GreaterThanOrEqualTo(-5.0 - 1e-6));
            }
            double finalGap = obstacleRear - (Along(agent) + agent.Profile.LengthM / 2);
            Assert.That(agent.CurrentSpeedMps, Is.LessThan(0.05));
            Assert.That(finalGap, Is.EqualTo(2.5).Within(0.3));
        }

        [Test]
        public void StopsBeforeTheStopLineOnRed()
        {
            var w = CityLayouts.Compile(CityLayouts.CrossWithArms(3));
            var index = new RoadGraphIndex(w);
            var route = new[] { "south2/f", "south1/f", "south0/f", "c/South.in", "c/c:South.in>North.out", "c/North.out" };
            var agent = new LaneFollowerAgent("a", index, route, 2, DriverProfile.Normal(), 14);
            for (int i = 0; i < 30 / Dt; i++) agent.Update((float)Dt, float.PositiveInfinity, true);
            var stop = w.stopLines.Single(s => s.laneId == "c/South.in");
            var lineZ = index.Path("c/South.in").Line.PointAt(stop.s).z;
            double front = agent.Position.z + agent.Profile.LengthM / 2;
            Assert.That(agent.CurrentSpeedMps, Is.LessThan(0.05));
            Assert.That(front, Is.LessThanOrEqualTo(lineZ + 0.2), "overrun");
            Assert.That(front, Is.GreaterThan(lineZ - 1.5), "stopped far too early");
        }

        [Test]
        public void EndOfRouteIsAPlaceToStopNotACrash()
        {
            var index = Chain(3);
            var agent = new LaneFollowerAgent("a", index, CityLayouts.ForwardRoute(3), 0, DriverProfile.Normal(), 12);
            Assert.DoesNotThrow(() => { for (int i = 0; i < 30 / Dt; i++) agent.Update(Dt, double.PositiveInfinity, 0, double.PositiveInfinity); });
            Assert.That(agent.CurrentSpeedMps, Is.LessThan(0.05));
            Assert.That(agent.RemainingRouteM, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void HardBrakingLeaderIsNotRammed()
        {
            var index = Chain(20);
            var agent = new LaneFollowerAgent("a", index, CityLayouts.ForwardRoute(20), 10, DriverProfile.Normal(), 15);
            double leadRear = Along(agent) + agent.Profile.LengthM / 2 + 20, leadV = 15;
            double minGap = double.MaxValue;
            for (int i = 0; i < 15 / Dt; i++)
            {
                if (i * Dt > 1) leadV = Math.Max(0, leadV - 8 * Dt); // leader brakes at 8 m/s^2 after 1 s
                leadRear += leadV * Dt;
                double gap = leadRear - (Along(agent) + agent.Profile.LengthM / 2);
                minGap = Math.Min(minGap, gap);
                agent.Update(Dt, gap, leadV, double.PositiveInfinity);
                Assert.That(agent.TargetAccelerationMps2, Is.GreaterThanOrEqualTo(-5.0 - 1e-6));
            }
            Assert.That(minGap, Is.GreaterThan(0.5));
        }

        [Test]
        public void PassesThroughJunctionWithoutJumps()
        {
            var w = CityLayouts.Compile(CityLayouts.CrossWithArms(2));
            var index = new RoadGraphIndex(w);
            var route = new[] { "south1/f", "south0/f", "c/South.in", "c/c:South.in>West.out", "c/West.out", "west0/b", "west1/b" };
            var agent = new LaneFollowerAgent("a", index, route, 0, DriverProfile.Normal(), 8);
            var prev = agent.Position;
            for (int i = 0; i < 12 / Dt; i++)
            {
                agent.Update(Dt, double.PositiveInfinity, 0, double.PositiveInfinity);
                var p = agent.Position;
                Assert.That(Polyline.Distance2D(prev, p), Is.LessThanOrEqualTo(agent.CurrentSpeedMps * Dt + 0.05), agent.CurrentPathId);
                prev = p;
            }
            Assert.That(agent.RouteIndex, Is.GreaterThanOrEqualTo(4));
        }

        [Test]
        public void SpeedSignZoneIsRespected()
        {
            var layout = CityLayouts.StraightChain(20);
            layout.signs = new[] { new LayoutSign { id = "l40", code = "3.24", value = "40", instanceId = "s5", laneId = "f", atS = 0 } };
            var index = new RoadGraphIndex(CityLayouts.Compile(layout));
            var agent = new LaneFollowerAgent("a", index, CityLayouts.ForwardRoute(20), 0, DriverProfile.Normal(), 16);
            for (int i = 0; i < 20 / Dt; i++)
            {
                agent.Update(Dt, double.PositiveInfinity, 0, double.PositiveInfinity);
                if (agent.CurrentPathId == "s5/f" && agent.CurrentDistanceAlongLane > 2)
                    Assert.That(agent.CurrentSpeedMps * 3.6, Is.LessThanOrEqualTo(40.5));
            }
        }

        [Test]
        public void DiscontinuousRouteIsRejected()
        {
            var index = Chain(3);
            Assert.Throws<ArgumentException>(() => new LaneFollowerAgent("a", index, new[] { "s0/f", "s2/f" }, 0, DriverProfile.Normal()));
        }

        static double Along(LaneFollowerAgent a) => a.Position.z;
    }
}
