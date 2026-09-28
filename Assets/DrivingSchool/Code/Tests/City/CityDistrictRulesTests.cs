using System.Collections.Generic;
using System.Linq;
using DrivingSchool.Contracts;
using DrivingSchool.Editor;
using DrivingSchool.Simulation.RoadGraph;
using DrivingSchool.Simulation.Traffic;
using NUnit.Framework;
using UnityEngine;

namespace DrivingSchool.Tests
{
    /// <summary>
    /// T65 on the town district of the test range: bots signal the movement they really make, speed zones by signs
    /// (3.24 to the next intersection or 3.25, on every lane of the direction), bots keep the limits.
    /// </summary>
    public sealed class CityDistrictRulesTests
    {
        static WorldDocumentV2 world;
        static WorldDocumentV2 World() => world ?? (world = DistrictCompiler.Compile(DistrictBuilder.Layout(Vector3.zero), new RoadKitCatalog()));

        [Test]
        public void LaneChangesAreMarkedAndStraightArcsAreNot()
        {
            var w = World();
            var changes = w.connections.Where(c => c.laneChange).ToList();
            Assert.That(changes.Count, Is.EqualTo(4 * 4), "four lane-change stretches (n, e, s, a), two directions, two moves each");
            Assert.That(changes.All(c => c.maneuver == LaneManeuver.Straight && c.junctionId.EndsWith("/lc")));
            // Ring sections and straight on from 1+1 into 2+2 shift sideways but are no lane change.
            Assert.That(w.connections.Where(c => c.junctionId.StartsWith("R/")).Any(c => c.laneChange), Is.False);
            Assert.That(w.lanes.Count(l => l.roundabout), Is.EqualTo(4), "the ring lanes are marked");
        }

        [Test]
        public void BotsSignalTheMovementTheyMake()
        {
            var w = World();
            var index = new RoadGraphIndex(w);
            var run = new TrafficRun(w, new TrafficProfile { MaxVehicles = 14 }, seed: 11);
            var previous = new Dictionary<string, ParticipantState>();
            int turnsChecked = 0, straightTicks = 0, ringTicks = 0, laneChanges = 0;
            run.Run(300, r =>
            {
                var now = r.Director.Snapshot.Participants.Where(p => p.Kind == ParticipantKind.Vehicle).ToList();
                foreach (var p in now)
                {
                    if (p.Hazard) { previous.Remove(p.Id); continue; }
                    var path = index.Path(p.PathId);
                    previous.TryGetValue(p.Id, out var before);
                    if (path.IsConnection && path.Connection.laneChange)
                    {
                        double shift = RoadKitTemplatesV2.LateralShift(path.Connection.centerline);
                        Assert.That(shift < 0 ? p.LeftIndicator && !p.RightIndicator : p.RightIndicator && !p.LeftIndicator, Is.True,
                            $"{p.Id} changes lanes on {p.PathId} (shift {shift:F1}) with L={p.LeftIndicator} R={p.RightIndicator}");
                        if (before != null && before.PathId != p.PathId) laneChanges++;
                    }
                    else if (path.IsConnection && index.IsIntersection(path.Connection.junctionId))
                    {
                        var m = path.Connection.maneuver;
                        if (m == LaneManeuver.Straight)
                        {
                            Assert.That(p.LeftIndicator || p.RightIndicator, Is.False, $"{p.Id} goes straight through {p.PathId} but signals L={p.LeftIndicator} R={p.RightIndicator}");
                            straightTicks++;
                        }
                        else if (before != null && before.PathId != p.PathId && !index.Path(before.PathId).IsConnection)
                        {
                            // Just drove in: the signal of this side was already on, the other one off.
                            bool left = m == LaneManeuver.Left || m == LaneManeuver.UTurn;
                            Assert.That(left ? before.LeftIndicator && !before.RightIndicator : before.RightIndicator && !before.LeftIndicator, Is.True,
                                $"{p.Id} turns {m} into {p.PathId} after signalling L={before.LeftIndicator} R={before.RightIndicator}");
                            turnsChecked++;
                        }
                    }
                    else if (path.Lane != null && path.Lane.roundabout)
                    {
                        // On the ring: nothing, unless the next movement is the exit (right).
                        var car = r.Director.Vehicle(p.Id);
                        var next = car.Route.Skip(car.RouteIndex + 1).Select(index.Path).FirstOrDefault(x => x.IsConnection);
                        if (next != null && next.Connection.maneuver == LaneManeuver.Straight)
                        {
                            Assert.That(p.LeftIndicator || p.RightIndicator, Is.False, $"{p.Id} goes on round the ring but signals");
                            ringTicks++;
                        }
                        if (next != null && next.Connection.maneuver == LaneManeuver.Right) Assert.That(p.LeftIndicator, Is.False, $"{p.Id} leaves the ring with the left signal");
                    }
                }
                previous = now.ToDictionary(p => p.Id);
            });
            Assert.That(turnsChecked, Is.GreaterThan(20), "turns seen");
            Assert.That(straightTicks, Is.GreaterThan(100));
            Assert.That(ringTicks, Is.GreaterThan(0), "a bot went round the ring past an exit");
            Assert.That(laneChanges, Is.GreaterThan(0), "a bot changed lanes");
        }

        [Test]
        public void SpeedZonesRunToTheEndSignOnEveryLaneOfTheDirection()
        {
            var index = new RoadGraphIndex(World());
            // Bump street (T65): 20 from the sign 45 m before the zebra to 3.25 after it, then the town limit again.
            Assert.That(index.SpeedLimitAt("b2/b", 4), Is.EqualTo(60), "before the sign");
            Assert.That(index.SpeedLimitAt("b2/b", 6), Is.EqualTo(20));
            Assert.That(index.SpeedLimitAt("b4/b", 10), Is.EqualTo(20), "over the zebra");
            Assert.That(index.SpeedLimitAt("b5/b", 7), Is.EqualTo(20));
            Assert.That(index.SpeedLimitAt("b5/b", 9), Is.EqualTo(60), "after 3.25");
            Assert.That(index.SpeedLimitAt("b4/f", 10), Is.EqualTo(20), "the other way too");
            Assert.That(index.SpeedLimitAt("b3/f", 9), Is.EqualTo(60));
            // Avenue: the sign stands at the outer lane, the zone covers the inner one as well, to the district edge.
            Assert.That(index.SpeedLimitAt("a1/b1", 5), Is.EqualTo(40));
            Assert.That(index.SpeedLimitAt("a4/b2", 15), Is.EqualTo(40));
            // Towards X3 through the lane-change stretch (not an intersection) up to the junction, not beyond it.
            Assert.That(index.SpeedLimitAt("a0/f1a", 3), Is.EqualTo(40));
            Assert.That(index.SpeedLimitAt("X3/West.in1", 1), Is.EqualTo(40));
            Assert.That(index.SpeedLimitAt("X3/East.out1", 1), Is.EqualTo(60), "the zone ends at the intersection");
            // East entrance (older sign, now a real zone): 40 from road A to the roundabout.
            Assert.That(index.SpeedLimitAt("re0/f", 10), Is.EqualTo(40));
        }

        [Test]
        public void BotsKeepTheSpeedLimits()
        {
            var w = World();
            var index = new RoadGraphIndex(w);
            var run = new TrafficRun(w, new TrafficProfile { MaxVehicles = 14 }, seed: 5);
            double worst = 0; string where = null; int onBumpStreet = 0;
            run.Run(300, r =>
            {
                foreach (var p in r.Director.Snapshot.Participants.Where(x => x.Kind == ParticipantKind.Vehicle && x.Lod == SimulationLod.Near))
                {
                    double over = p.SpeedMps * 3.6 - index.SpeedLimitAt(p.PathId, p.S);
                    if (over > worst) { worst = over; where = p.Id + " on " + p.PathId + " at " + p.S.ToString("F1"); }
                    if (p.PathId.StartsWith("b4/")) onBumpStreet++;
                }
            });
            Assert.That(worst, Is.LessThan(2.0), "km/h over the limit: " + where);
            Assert.That(onBumpStreet, Is.GreaterThan(0), "bots drove over the zebra with the bumps");
        }
    }
}
