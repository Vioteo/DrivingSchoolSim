using System.Collections.Generic;
using System.Linq;
using DrivingSchool.Contracts;
using DrivingSchool.Rules;
using NUnit.Framework;

namespace DrivingSchool.Tests
{
    /// <summary>T65: city rules from road facts — each rule fires on the wrong behaviour and stays quiet on the right one.</summary>
    public sealed class CityRuleMonitorTests
    {
        const double Dt = 0.1;

        static DriverRoadFacts Road(double t, float speed, float limit = 60) => new DriverRoadFacts
        {
            Seconds = t, SpeedMps = speed, OnRoad = true, SpeedLimitKph = limit, LanesInDirection = 1, RightmostLane = true,
            LeadGapM = float.PositiveInfinity, JunctionFromAllowedLane = true,
        };

        static List<string> Run(IEnumerable<DriverRoadFacts> facts)
        {
            var m = new CityRuleMonitor();
            return facts.SelectMany(f => m.Update(f)).Select(e => e.ruleId).ToList();
        }

        static IEnumerable<DriverRoadFacts> Seconds(double from, double to, System.Func<double, DriverRoadFacts> make)
        {
            for (double t = from; t < to - 1e-9; t += Dt) yield return make(t);
        }

        [Test]
        public void SpeedOverTheZoneLimitIsReportedOnceAfterTheDebounce()
        {
            var over = Run(Seconds(0, 5, t => Road(t, 30f / 3.6f + 2, limit: 20)));   // ~37 км/ч в зоне 20
            Assert.That(over.Count(r => r == CityRuleMonitor.RuleSpeed), Is.EqualTo(1));
            Assert.That(Run(Seconds(0, 5, t => Road(t, 22f / 3.6f, limit: 20))), Is.Empty, "within the tolerance");
            Assert.That(Run(Seconds(0, 1, t => Road(t, 40f / 3.6f, limit: 20))), Is.Empty, "a moment over is not yet a violation");
        }

        [Test]
        public void CrossingASolidAxisOrDrivingAgainstOnAFourLaneRoadIsOncoming()
        {
            DriverRoadFacts Over(double t, MarkingType axis) { var f = Road(t, 10); f.LeftSideOverAxis = true; f.AxisMarking = axis; return f; }
            Assert.That(Run(Seconds(0, 3, t => Over(t, MarkingType.Solid))), Is.EqualTo(new[] { CityRuleMonitor.RuleOncoming }));
            Assert.That(Run(Seconds(0, 3, t => Over(t, MarkingType.Dashed))), Is.Empty, "over a dashed axis (overtaking) is allowed");
            var against = Run(Seconds(0, 3, t => { var f = Road(t, 10); f.AgainstDirection = true; f.LanesInDirection = 2; f.AxisMarking = MarkingType.DoubleSolid; return f; }));
            Assert.That(against, Is.EqualTo(new[] { CityRuleMonitor.RuleOncoming }));
        }

        [Test]
        public void MovingOffFromTheRoadsideNeedsTheLeftSignal()
        {
            IEnumerable<DriverRoadFacts> Start(bool signal) =>
                Seconds(0, 3, t => { var f = Road(t, 0); f.LateralOffsetM = 0.8f; f.LeftIndicator = signal && t > 2; return f; })
                .Concat(Seconds(3, 5, t => { var f = Road(t, 3); f.LateralOffsetM = 0.6f; f.LeftIndicator = signal; return f; }));
            Assert.That(Run(Start(false)), Is.EqualTo(new[] { CityRuleMonitor.RuleStartNoSignal }));
            Assert.That(Run(Start(true)), Is.Empty);
            // Standing in a queue (waiting for traffic) is no parking: moving on needs no signal.
            var queue = Seconds(0, 3, t => { var f = Road(t, 0); f.LateralOffsetM = 0.8f; f.WaitingForTraffic = true; return f; })
                .Concat(Seconds(3, 5, t => Road(t, 3)));
            Assert.That(Run(queue), Is.Empty);
        }

        [Test]
        public void LaneChangeAndTurnsNeedTheSignalOfTheirSide()
        {
            DriverRoadFacts Change(double t, int side, bool left, bool right) { var f = Road(t, 12); f.LeftIndicator = left; f.RightIndicator = right; if (t > 2.95 && t < 3.05) f.LaneChange = side; return f; }
            Assert.That(Run(Seconds(0, 4, t => Change(t, 1, false, false))), Is.EqualTo(new[] { CityRuleMonitor.RuleLaneChangeNoSignal }));
            Assert.That(Run(Seconds(0, 4, t => Change(t, 1, true, false))), Is.EqualTo(new[] { CityRuleMonitor.RuleLaneChangeNoSignal }), "wrong side");
            Assert.That(Run(Seconds(0, 4, t => Change(t, 1, false, t > 1 && t < 2.5))), Is.Empty, "signalled before");

            DriverRoadFacts Turn(double t, LaneManeuver m, bool roundaboutIn, bool signalRight) { var f = Road(t, 6); f.RightIndicator = signalRight && t < 2; if (t > 4.95 && t < 5.05) { f.JunctionManeuver = m; f.JunctionEnteredSeconds = 2; f.EnteredRoundabout = roundaboutIn; } return f; }
            Assert.That(Run(Seconds(0, 6, t => Turn(t, LaneManeuver.Right, false, false))), Is.EqualTo(new[] { CityRuleMonitor.RuleTurnNoSignal }));
            Assert.That(Run(Seconds(0, 6, t => Turn(t, LaneManeuver.Right, true, false))), Is.EqualTo(new[] { CityRuleMonitor.RuleRoundaboutEntryNoSignal }));
            Assert.That(Run(Seconds(0, 6, t => Turn(t, LaneManeuver.Right, true, true))), Is.Empty, "right signal before entering the ring");
            Assert.That(Run(Seconds(0, 6, t => Turn(t, LaneManeuver.Straight, false, false))), Is.Empty);
        }

        [Test]
        public void TurningFromALaneThatDoesNotAllowItIsReported()
        {
            var facts = Seconds(0, 3, t =>
            {
                var f = Road(t, 6); f.RightIndicator = true;
                if (t > 1.95 && t < 2.05) { f.JunctionManeuver = LaneManeuver.Right; f.JunctionEnteredSeconds = 1; f.JunctionFromAllowedLane = false; f.JunctionLaneAllowed = LaneManeuver.Straight | LaneManeuver.Left; }
                return f;
            });
            Assert.That(Run(facts), Is.EqualTo(new[] { CityRuleMonitor.RuleWrongLane }));
        }

        [Test]
        public void FollowingTooCloseForTwoSecondsBreaksTheDistance()
        {
            Assert.That(Run(Seconds(0, 3, t => { var f = Road(t, 14); f.LeadGapM = 8; return f; })), Is.EqualTo(new[] { CityRuleMonitor.RuleDistance }));
            Assert.That(Run(Seconds(0, 3, t => { var f = Road(t, 14); f.LeadGapM = 20; return f; })), Is.Empty, "1.4 s is enough for the game threshold");
            Assert.That(Run(Seconds(0, 3, t => { var f = Road(t, 3); f.LeadGapM = 3; return f; })), Is.Empty, "crawling in a queue");
        }

        [Test]
        public void StoppingOnACrosswalkBeforeItOrInANoStoppingZone()
        {
            List<string> Stop(RoadStopPlace place, bool waiting = false, bool signalled = false, bool rightmost = true) =>
                Run(Seconds(0, 5, t => { var f = Road(t, 0); f.StopPlace = place; f.WaitingForTraffic = waiting; f.CrosswalkSignalled = signalled; f.RightmostLane = rightmost; return f; }));
            Assert.That(Stop(RoadStopPlace.Crosswalk), Is.EqualTo(new[] { CityRuleMonitor.RuleStopOnCrosswalk }));
            Assert.That(Stop(RoadStopPlace.Crosswalk, waiting: true), Is.EqualTo(new[] { CityRuleMonitor.RuleStopOnCrosswalk }), "not even in a queue");
            Assert.That(Stop(RoadStopPlace.BeforeCrosswalk), Is.EqualTo(new[] { CityRuleMonitor.RuleStopNearCrosswalk }));
            Assert.That(Stop(RoadStopPlace.BeforeCrosswalk, waiting: true), Is.Empty, "letting pedestrians cross");
            Assert.That(Stop(RoadStopPlace.BeforeCrosswalk, signalled: true), Is.Empty, "the stop line of a light-controlled crossing");
            Assert.That(Stop(RoadStopPlace.NoStoppingZone), Is.EqualTo(new[] { CityRuleMonitor.RuleNoStoppingZone }));
            Assert.That(Stop(RoadStopPlace.Lane, rightmost: false), Is.EqualTo(new[] { CityRuleMonitor.RuleStopNotAtRightEdge }));
            Assert.That(Stop(RoadStopPlace.Lane), Is.Empty, "at the right edge");
        }

        [Test]
        public void RedLightAndClosedCrossingInTown()
        {
            var facts = new[] { Road(0, 8), Road(0.1, 8) };
            facts[1].EnteredOnRed = true;
            Assert.That(Run(facts), Is.EqualTo(new[] { DriveRuleMonitor.RuleRedLight }));
            facts[1].EnteredOnRed = false; facts[1].EnteredClosedRailway = true;
            Assert.That(Run(facts), Is.EqualTo(new[] { DriveRuleMonitor.RuleRailwayClosed }));
        }
    }
}
