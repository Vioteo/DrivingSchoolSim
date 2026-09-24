using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DrivingSchool.Contracts;
using DrivingSchool.Simulation.RoadGraph;

namespace DrivingSchool.Simulation.Traffic
{
    public enum SignalMode { Normal, FlashingAmber, Off }

    /// <summary>
    /// Fixed-time signal plan of one junction (T32). The aspect is a pure function of simulation time, so a junction
    /// that was not ticked (unloaded area, ADR-014) shows the right phase as soon as it is queried again.
    /// Stage i: its green groups show Green (the last greenFlashSeconds GreenFlashing), then Amber, then Red for
    /// all-red. Vehicle groups that turn green in the next stage show RedAmber for the last redAmberSeconds of the
    /// stage. A group green in two consecutive stages stays Green. Pedestrian groups never show amber.
    /// </summary>
    public sealed class SignalController
    {
        readonly SignalPlan plan;
        readonly Dictionary<string, SignalGroupKind> kinds;
        readonly double[] stageStart;
        readonly double cycle;
        double now;

        public SignalMode Mode { get; set; } = SignalMode.Normal;
        public string JunctionId => plan.junctionId;
        public IEnumerable<string> GroupIds => kinds.Keys;
        public double CycleSeconds => cycle;

        public SignalController(SignalPlan plan, IEnumerable<SignalGroup> groups)
        {
            this.plan = plan ?? throw new ArgumentNullException(nameof(plan));
            kinds = groups.Where(g => g.junctionId == plan.junctionId).ToDictionary(g => g.id, g => g.kind);
            if (plan.stages.Length == 0) throw new InvalidDataException("Signal plan without stages: " + plan.id);
            stageStart = new double[plan.stages.Length];
            double t = 0;
            for (int i = 0; i < plan.stages.Length; i++) { stageStart[i] = t; t += StageLength(plan.stages[i]); }
            cycle = t;
            if (cycle <= 0) throw new InvalidDataException("Signal plan with zero cycle: " + plan.id);
        }

        public void Tick(double simSeconds) => now = simSeconds;

        public SignalAspect GetAspect(string groupId) => AspectAt(groupId, now);

        public SignalAspect AspectAt(string groupId, double simSeconds)
        {
            if (!kinds.TryGetValue(groupId, out var kind)) throw new KeyNotFoundException("Unknown signal group " + groupId);
            bool pedestrian = kind == SignalGroupKind.Pedestrian;
            if (Mode == SignalMode.Off) return SignalAspect.Off;
            if (Mode == SignalMode.FlashingAmber) return pedestrian ? SignalAspect.Off : SignalAspect.AmberFlashing;

            Locate(simSeconds, out int i, out double t);
            var stage = plan.stages[i];
            var nextStage = plan.stages[(i + 1) % plan.stages.Length];
            bool green = stage.greenGroupIds.Contains(groupId);
            bool greenNext = nextStage.greenGroupIds.Contains(groupId);
            if (green)
            {
                if (t < stage.greenSeconds - stage.greenFlashSeconds) return SignalAspect.Green;
                if (greenNext) return SignalAspect.Green; // continues into the next stage
                if (t < stage.greenSeconds) return SignalAspect.GreenFlashing;
                if (pedestrian) return SignalAspect.Red;
                if (t < stage.greenSeconds + stage.amberSeconds) return SignalAspect.Amber;
                return SignalAspect.Red;
            }
            if (!pedestrian && greenNext && t >= StageLength(stage) - nextStage.redAmberSeconds) return SignalAspect.RedAmber;
            return SignalAspect.Red;
        }

        /// <summary>Seconds until the aspect of the group changes (for agents deciding whether to stop on amber).</summary>
        public double TimeToChange(string groupId)
        {
            var current = GetAspect(groupId);
            if (Mode != SignalMode.Normal) return double.PositiveInfinity;
            const double step = 0.05;
            for (double dt = step; dt <= cycle + step; dt += step)
                if (AspectAt(groupId, now + dt) != current) return dt;
            return double.PositiveInfinity;
        }

        void Locate(double simSeconds, out int stage, out double t)
        {
            double c = (simSeconds + plan.offsetSeconds) % cycle;
            if (c < 0) c += cycle;
            stage = plan.stages.Length - 1;
            for (int i = 0; i < stageStart.Length - 1; i++) if (c < stageStart[i + 1]) { stage = i; break; }
            t = c - stageStart[stage];
        }

        static double StageLength(SignalStage s) => s.greenSeconds + s.amberSeconds + s.allRedSeconds;

        /// <summary>
        /// A plan must not give green at the same time to through movements whose paths cross, or to a through
        /// movement and a pedestrian crossing it. Permissive turns (left, right) may share green: they yield by the
        /// rules (ПДД РФ 13.1, 13.4 — редакцию сверить). Throws <see cref="InvalidDataException"/>.
        /// </summary>
        public static void ValidatePlan(SignalPlan plan, WorldDocumentV2 world)
        {
            var connections = world.connections.ToDictionary(c => c.id);
            var groups = world.signalGroups.ToDictionary(g => g.id);
            foreach (var stage in plan.stages)
            {
                var green = stage.greenGroupIds.Select(id => groups[id]).ToList();
                var straight = new HashSet<string>(green.Where(g => g.kind != SignalGroupKind.Pedestrian)
                    .SelectMany(g => g.connectionIds).Where(id => connections[id].maneuver == LaneManeuver.Straight));
                foreach (var z in world.conflictZones)
                    if (!z.merge && straight.Contains(z.connectionA) && straight.Contains(z.connectionB))
                        throw new InvalidDataException("Plan " + plan.id + " gives green to crossing through movements: " + z.connectionA + " / " + z.connectionB);
                foreach (var g in green.Where(g => g.kind == SignalGroupKind.Pedestrian))
                    foreach (var crossingId in g.crossingIds)
                    {
                        var crossing = world.crossings.First(c => c.id == crossingId);
                        var hit = crossing.laneIds.FirstOrDefault(straight.Contains);
                        if (hit != null)
                            throw new InvalidDataException("Plan " + plan.id + " gives green to pedestrians on " + crossingId + " and to through movement " + hit);
                    }
            }
        }
    }
}
