using System;
using System.Collections.Generic;
using System.Linq;
using DrivingSchool.Contracts;
using DrivingSchool.Simulation.RoadGraph;
using DrivingSchool.Simulation.Traffic;

namespace DrivingSchool.Tests
{
    /// <summary>Runs a director at a fixed step and checks physical invariants.</summary>
    sealed class TrafficRun
    {
        public const double Dt = 0.02;
        public readonly TrafficDirector Director;
        public readonly WorldDocumentV2 World;
        public long Tick;
        public double Time;

        public TrafficRun(WorldDocumentV2 world, TrafficProfile profile = null, int seed = 1)
        {
            World = world;
            Director = new TrafficDirector(world, profile ?? new TrafficProfile(), seed);
        }

        public void Step(Action<TrafficRun> check = null)
        {
            Director.Tick(Tick, Time);
            check?.Invoke(this);
            Tick++; Time += Dt;
        }

        public void Run(double seconds, Action<TrafficRun> check = null)
        {
            int n = (int)Math.Round(seconds / Dt);
            for (int i = 0; i < n; i++) Step(check);
        }

        public static string SnapshotHash(TrafficSnapshot s) =>
            GraphFingerprint.Compute(s.Participants.Select(p => new object[] { p.Id, p.PathId, Math.Round(p.S, 6), Math.Round(p.SpeedMps, 6) }).ToList());

        /// <summary>Oriented rectangles of two vehicles overlap (separating axis test in XZ).</summary>
        public static bool Overlap(ParticipantState a, ParticipantState b)
        {
            var ca = Corners(a); var cb = Corners(b);
            foreach (var axis in Axes(a).Concat(Axes(b)))
            {
                double aMin = ca.Min(c => c.x * axis.x + c.z * axis.z), aMax = ca.Max(c => c.x * axis.x + c.z * axis.z);
                double bMin = cb.Min(c => c.x * axis.x + c.z * axis.z), bMax = cb.Max(c => c.x * axis.x + c.z * axis.z);
                if (aMax < bMin || bMax < aMin) return false;
            }
            return true;
        }

        static IEnumerable<(double x, double z)> Axes(ParticipantState p)
        {
            double fx = Math.Sin(p.HeadingRad), fz = Math.Cos(p.HeadingRad);
            yield return (fx, fz); yield return (fz, -fx);
        }

        static (double x, double z)[] Corners(ParticipantState p)
        {
            double fx = Math.Sin(p.HeadingRad), fz = Math.Cos(p.HeadingRad), rx = fz, rz = -fx;
            double l = p.LengthM / 2, w = p.WidthM / 2;
            return new[] { (1, 1), (1, -1), (-1, -1), (-1, 1) }
                .Select(k => (p.Position.x + fx * l * k.Item1 + rx * w * k.Item2, p.Position.z + fz * l * k.Item1 + rz * w * k.Item2)).ToArray();
        }

        public static void AssertNoOverlaps(TrafficSnapshot s)
        {
            var list = s.Participants;
            for (int i = 0; i < list.Count; i++)
                for (int j = i + 1; j < list.Count; j++)
                    if (Overlap(list[i], list[j]))
                        throw new Exception($"Overlap at t={s.SimSeconds:F2}: {list[i].Id} ({list[i].PathId} {list[i].S:F1}) and {list[j].Id} ({list[j].PathId} {list[j].S:F1})");
        }
    }
}
