using System;
using System.Collections.Generic;
using DrivingSchool.Contracts;

namespace DrivingSchool.Rules
{
    /// <summary>
    /// Нарушения водителя в городе по фактам графа дорог (T65, <see cref="DriverRoadFacts"/>): превышение скорости по
    /// знакам, выезд на встречную, поворотник при начале движения, перестроении, повороте и на кольце, поворот не из той
    /// полосы, дистанция, остановка на переходе и в неположенном месте. Пункты — ПДД РФ (постановление Правительства РФ
    /// № 1090), редакция не сверена (CLAUDE.md, правило 5). Все пороги ниже — игровые, не правовые нормы. Каждое нарушение
    /// засчитывается один раз за эпизод (пока машина снова не поедет правильно). Баллов нет до таблицы T37.
    /// </summary>
    public sealed class CityRuleMonitor
    {
        public const string RuleSpeed = "PDD_10.1_SPEED_LIMIT";
        public const string RuleOncoming = "PDD_9.1.1_ONCOMING";
        public const string RuleStartNoSignal = "PDD_8.1_START_NO_SIGNAL";
        public const string RuleLaneChangeNoSignal = "PDD_8.1_LANE_CHANGE_NO_SIGNAL";
        public const string RuleTurnNoSignal = "PDD_8.1_TURN_NO_SIGNAL";
        public const string RuleRoundaboutEntryNoSignal = "PDD_8.1_ROUNDABOUT_ENTRY_NO_SIGNAL";
        public const string RuleRoundaboutExitNoSignal = "PDD_8.1_ROUNDABOUT_EXIT_NO_SIGNAL";
        public const string RuleWrongLane = "PDD_8.5_WRONG_LANE";
        public const string RuleDistance = "PDD_9.10_DISTANCE";
        public const string RuleStopOnCrosswalk = "PDD_12.4_STOP_ON_CROSSWALK";
        public const string RuleStopNearCrosswalk = "PDD_12.4_STOP_NEAR_CROSSWALK";
        public const string RuleNoStoppingZone = "PDD_3.27_NO_STOPPING";
        public const string RuleStopNotAtRightEdge = "PDD_12.1_STOP_NOT_AT_RIGHT_EDGE";

        public static readonly string[] AllRules =
        {
            RuleSpeed, RuleOncoming, RuleStartNoSignal, RuleLaneChangeNoSignal, RuleTurnNoSignal, RuleRoundaboutEntryNoSignal,
            RuleRoundaboutExitNoSignal, RuleWrongLane, RuleDistance, RuleStopOnCrosswalk, RuleStopNearCrosswalk, RuleNoStoppingZone,
            RuleStopNotAtRightEdge,
        };

        // Игровые пороги (калибровка), не нормы.
        public const float SpeedToleranceKph = 3f;
        public const double SpeedDebounceSeconds = 1.5;
        public const float StandstillMps = 0.3f;
        public const float MovingOffMps = 1.0f;
        public const double ParkedSeconds = 2.0;          // стоит у края столько — следующее трогание проверяется
        public const float RoadsideOffsetM = 0.3f;        // правее центра полосы — «у края»
        public const double SignalBeforeSeconds = 5.0;    // поворотник должен гореть в этом окне до манёвра...
        public const double SignalAfterSeconds = 1.0;     // ...или включиться сразу после начала
        public const double StartSignalSeconds = 3.0;
        public const float FollowMinSpeedMps = 5f;        // ~18 км/ч
        public const float MinHeadwaySeconds = 1.0f, MinGapM = 4f, HeadwayRecoverSeconds = 1.5f;
        public const double DistanceDebounceSeconds = 2.0;
        public const double CrosswalkStopSeconds = 1.0;
        public const double StopSeconds = 3.0;
        public const double OncomingClearSeconds = 1.0;

        readonly List<(double t, bool left, bool right)> signals = new List<(double, bool, bool)>();
        double overSince = -1; bool speedReported;
        bool oncomingReported; double oncomingClearSince = -1;
        double stoppedSince = -1; bool parked, startChecked = true;
        double closeSince = -1; bool distanceReported;
        bool crosswalkReported, stopReported; double idleSince = -1;
        int seq;

        public List<RuleEvent> Update(DriverRoadFacts f)
        {
            var result = new List<RuleEvent>();
            signals.Add((f.Seconds, f.LeftIndicator && !f.Hazard, f.RightIndicator && !f.Hazard));
            while (signals.Count > 0 && signals[0].t < f.Seconds - 12) signals.RemoveAt(0);

            // Скорость (ПДД 10.1, 10.2, знак 3.24).
            float kph = f.SpeedMps * 3.6f;
            if (f.OnRoad && f.SpeedLimitKph > 0 && kph > f.SpeedLimitKph + SpeedToleranceKph)
            {
                if (overSince < 0) overSince = f.Seconds;
                if (!speedReported && f.Seconds - overSince >= SpeedDebounceSeconds) { speedReported = true; result.Add(Make(RuleSpeed, f, $"{kph:0}/{f.SpeedLimitKph:0}")); }
            }
            else if (!f.OnRoad || kph <= f.SpeedLimitKph) { overSince = -1; speedReported = false; }

            // Встречная: сплошная по оси или дорога с 4 и более полосами (ПДД 9.1(1), 9.2; разметка 1.1, 1.3).
            bool forbiddenAxis = f.AxisMarking == MarkingType.Solid || f.AxisMarking == MarkingType.DoubleSolid;
            bool oncoming = f.OnRoad && !f.InIntersection &&
                            (f.LeftSideOverAxis && forbiddenAxis || f.AgainstDirection && (forbiddenAxis || f.LanesInDirection >= 2));
            if (oncoming)
            {
                oncomingClearSince = -1;
                if (!oncomingReported) { oncomingReported = true; result.Add(Make(RuleOncoming, f)); }
            }
            else if (oncomingReported)
            {
                if (oncomingClearSince < 0) oncomingClearSince = f.Seconds;
                if (f.Seconds - oncomingClearSince >= OncomingClearSeconds) oncomingReported = false;
            }

            // Стоянка у края и трогание (ПДД 8.1, 8.2).
            if (f.SpeedMps < StandstillMps)
            {
                if (stoppedSince < 0) { stoppedSince = f.Seconds; crosswalkReported = false; stopReported = false; idleSince = -1; }
                double stood = f.Seconds - stoppedSince;
                bool atRoadside = !f.OnRoad || f.RightmostLane && f.LateralOffsetM >= RoadsideOffsetM && !f.InIntersection;
                if (stood >= ParkedSeconds && atRoadside && !f.WaitingForTraffic) { parked = true; startChecked = false; }

                // Остановка на переходе (ПДД 12.4) — даже в заторе.
                if (!crosswalkReported && f.StopPlace == RoadStopPlace.Crosswalk && stood >= CrosswalkStopSeconds)
                { crosswalkReported = true; result.Add(Make(RuleStopOnCrosswalk, f)); }
                // Остановка в неположенном месте — если стоять незачем всё это время (причина — сигнал, очередь, переход).
                bool reason = f.WaitingForTraffic || f.InIntersection;
                if (reason) idleSince = -1; else if (idleSince < 0) idleSince = f.Seconds;
                if (!stopReported && !reason && f.Seconds - idleSince >= StopSeconds && f.OnRoad)
                {
                    string rule = null;
                    if (f.StopPlace == RoadStopPlace.BeforeCrosswalk && !f.CrosswalkSignalled) rule = RuleStopNearCrosswalk;
                    else if (f.StopPlace == RoadStopPlace.NoStoppingZone) rule = RuleNoStoppingZone;
                    else if (f.AgainstDirection || !f.RightmostLane) rule = RuleStopNotAtRightEdge;
                    if (rule != null) { stopReported = true; result.Add(Make(rule, f)); }
                }
            }
            else
            {
                if (f.SpeedMps >= MovingOffMps && parked && !startChecked)
                {
                    startChecked = true; parked = false;
                    if (!SignalWithin(-1, f.Seconds - StartSignalSeconds, f.Seconds)) result.Add(Make(RuleStartNoSignal, f));
                }
                if (f.SpeedMps >= MovingOffMps) stoppedSince = -1;
            }

            // Сигналы: светофор перекрёстка (ПДД 6.2, 6.13) и закрытый переезд (ПДД 15.3) — те же правила, что на полигоне.
            if (f.EnteredOnRed) result.Add(Make(DriveRuleMonitor.RuleRedLight, f));
            if (f.EnteredClosedRailway) result.Add(Make(DriveRuleMonitor.RuleRailwayClosed, f));

            // Перестроение (ПДД 8.1, 8.4).
            if (f.LaneChange != 0 && !f.InIntersection && !SignalWithin(f.LaneChange, f.Seconds - SignalBeforeSeconds, f.Seconds))
                result.Add(Make(RuleLaneChangeNoSignal, f, f.LaneChange < 0 ? "left" : "right"));

            // Перекрёсток пройден: поворотник и полоса (ПДД 8.1, 8.2, 8.5; знаки 5.15.1, 5.15.2).
            if (f.JunctionManeuver != LaneManeuver.None)
            {
                int side = f.JunctionManeuver == LaneManeuver.Right ? 1 : f.JunctionManeuver == LaneManeuver.Left || f.JunctionManeuver == LaneManeuver.UTurn ? -1 : 0;
                if (side != 0 && !SignalWithin(side, f.JunctionEnteredSeconds - SignalBeforeSeconds, f.JunctionEnteredSeconds + SignalAfterSeconds))
                    result.Add(Make(f.EnteredRoundabout ? RuleRoundaboutEntryNoSignal : f.LeftRoundabout ? RuleRoundaboutExitNoSignal : RuleTurnNoSignal, f, f.JunctionManeuver.ToString()));
                if (!f.JunctionFromAllowedLane) result.Add(Make(RuleWrongLane, f, f.JunctionManeuver + " from " + f.JunctionLaneAllowed));
            }

            // Дистанция до впереди идущего (ПДД 9.10).
            if (f.SpeedMps > FollowMinSpeedMps && f.LeadGapM < Math.Max(MinGapM, f.SpeedMps * MinHeadwaySeconds))
            {
                if (closeSince < 0) closeSince = f.Seconds;
                if (!distanceReported && f.Seconds - closeSince >= DistanceDebounceSeconds) { distanceReported = true; result.Add(Make(RuleDistance, f, $"{f.LeadGapM:0.0}m@{kph:0}")); }
            }
            else
            {
                closeSince = -1;
                if (f.SpeedMps < 3f || f.LeadGapM >= f.SpeedMps * HeadwayRecoverSeconds) distanceReported = false;
            }
            return result;
        }

        /// <summary>The indicator of <paramref name="side"/> (−1 left, +1 right) was on at some moment in [from, to].</summary>
        bool SignalWithin(int side, double from, double to)
        {
            foreach (var s in signals)
                if (s.t >= from - 1e-6 && s.t <= to + 1e-6 && (side < 0 ? s.left : s.right)) return true;
            return false;
        }

        RuleEvent Make(string ruleId, DriverRoadFacts f, string evidence = null) => new RuleEvent
        {
            id = $"city-{++seq}", ruleId = ruleId, ruleRevision = "не сверена", participantId = "player", evidenceId = evidence,
            explanationKey = ruleId, simulationSeconds = f.Seconds, x = f.X, z = f.Z, penalty = 0,
        };
    }
}
