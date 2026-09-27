using System;
using System.Collections.Generic;
using System.Linq;
using DrivingSchool.Contracts;
using DrivingSchool.Simulation.RoadGraph;
using DrivingSchool.Simulation.Traffic;
using NUnit.Framework;

namespace DrivingSchool.Tests
{
    /// <summary>T52: AI cars move like cars (front axle on the path, rear trails, wheels steer) and recover from a light knock.</summary>
    public sealed class TrafficBodyTests
    {
        /// <summary>Straight s0 → right-hand curve R14 → straight s1, both ends open.</summary>
        static WorldDocumentV2 Bend()
        {
            var st = CityLayouts.T(CityLayouts.Straight); var cv = CityLayouts.T(CityLayouts.Curve);
            var s0 = new ModuleInstance { id = "s0", catalogId = CityLayouts.Straight };
            var c = DistrictCompiler.Dock("c", cv, "Socket_Start", s0, st, "Socket_End");
            var s1 = DistrictCompiler.Dock("s1", st, "Socket_Start", c, cv, "Socket_End");
            return CityLayouts.Compile(new DistrictLayout
            {
                id = "bend", instances = new[] { s0, c, s1 },
                joins = new[] { new SocketJoin { instanceA = "s0", socketA = "Socket_End", instanceB = "c", socketB = "Socket_Start" },
                                new SocketJoin { instanceA = "c", socketA = "Socket_End", instanceB = "s1", socketB = "Socket_Start" } },
                openSockets = new[] { new SocketRef { instanceId = "s0", socket = "Socket_Start" }, new SocketRef { instanceId = "s1", socket = "Socket_End" } },
            });
        }

        [Test]
        public void InATurnTheFrontWheelsSteerAndTheBodyCutsInside()
        {
            var run = new TrafficRun(Bend(), new TrafficProfile { MaxVehicles = 0 });
            var id = run.Director.AddVehicle(new[] { "s0/f", "c/f", "s1/f" }, 0, 6);
            double maxSteer = 0; double insideCut = 0;
            var lane = run.Director.Graph.Path("c/f");
            run.Run(8, r =>
            {
                if (!r.Director.Snapshot.Participants.Any(p => p.Id == id)) return;   // left the graph at the end of the route
                var car = r.Director.Vehicle(id);
                if (car.CurrentPathId != "c/f" || car.CurrentDistanceAlongLane < 6 || car.CurrentDistanceAlongLane > lane.Length - 6) return;
                maxSteer = Math.Max(maxSteer, car.WheelSteerRad);
                lane.Line.Project(car.BodyPosition.x, car.BodyPosition.z, out _, out double d);
                insideCut = Math.Max(insideCut, d);    // right turn: the inside of the bend is to the right (d > 0)
                Assert.That(Math.Abs(Wrap(car.BodyHeadingRad - car.HeadingRad)), Is.LessThan(0.35), "body yaw stays close to the path");
            });
            double radius = RoadKitTemplates.CurveRadiusM - RoadKitTemplates.LaneOffsetM;
            double expected = Math.Atan(DriverProfile.Normal().WheelbaseM / radius);
            Assert.That(maxSteer, Is.EqualTo(expected).Within(expected * 0.35), "steady-state wheel angle ≈ atan(L/R)");
            Assert.That(insideCut, Is.GreaterThan(0.05), "the body centre runs off the lane centre line in the turn");
        }

        [Test]
        public void OnAStraightTheWheelsPointAhead()
        {
            var run = new TrafficRun(CityLayouts.Compile(CityLayouts.StraightChain(4)), new TrafficProfile { MaxVehicles = 0 });
            var id = run.Director.AddVehicle(CityLayouts.ForwardRoute(4), 5, 10);
            run.Run(3);
            var car = run.Director.Vehicle(id);
            Assert.That(Math.Abs(car.WheelSteerRad), Is.LessThan(0.01));
            Assert.That(Math.Abs(Wrap(car.BodyHeadingRad - car.HeadingRad)), Is.LessThan(0.01));
        }

        [Test]
        public void AfterALightKnockTheCarWaitsWithHazardsThenDrivesOn()
        {
            var run = new TrafficRun(CityLayouts.Compile(CityLayouts.StraightChain(20)), new TrafficProfile { MaxVehicles = 0, HazardHoldSeconds = 5 });
            var id = run.Director.AddVehicle(CityLayouts.ForwardRoute(20), 0, 10);
            run.Run(1);
            run.Director.ReportContact(id, "player");
            run.Run(3);
            var p = run.Director.Snapshot.Participants.Single(x => x.Id == id);
            Assert.That(p.Hazard, Is.True); Assert.That(p.SpeedMps, Is.LessThan(0.5));
            run.Run(6);
            p = run.Director.Snapshot.Participants.Single(x => x.Id == id);
            Assert.That(p.Hazard, Is.False, "hazard is switched off after the hold time");
            Assert.That(p.SpeedMps, Is.GreaterThan(1), "and the car drives on");
        }

        [Test]
        public void AContactThatGoesOnKeepsTheCarStanding()
        {
            var run = new TrafficRun(CityLayouts.Compile(CityLayouts.StraightChain(20)), new TrafficProfile { MaxVehicles = 0, HazardHoldSeconds = 5 });
            var id = run.Director.AddVehicle(CityLayouts.ForwardRoute(20), 0, 10);
            run.Run(1);
            for (int i = 0; i < 16; i++) { run.Director.ReportContact(id, "player"); run.Run(0.5); }   // pressed for 8 s
            Assert.That(run.Director.Snapshot.Participants.Single(x => x.Id == id).Hazard, Is.True);
        }

        static double Wrap(double x) { while (x > Math.PI) x -= 2 * Math.PI; while (x <= -Math.PI) x += 2 * Math.PI; return x; }
    }
}
