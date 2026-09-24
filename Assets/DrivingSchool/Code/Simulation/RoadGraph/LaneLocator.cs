using System;
using System.Collections.Generic;
using System.Linq;

namespace DrivingSchool.Simulation.RoadGraph
{
    /// <summary>Where a participant is on the graph (T31).</summary>
    public struct LanePosition
    {
        public string PathId;        // lane or connection; null when off road
        public double S, D;          // along the path; lateral, right of travel positive
        public double HeadingError;  // participant heading minus path heading, radians in (-pi, pi]
        public bool OffRoad, AgainstDirection;
        public bool IsValid => !OffRoad && PathId != null;
    }

    /// <summary>
    /// Map matching for any participant (player, AI, pedestrian on a road). Uses the previous result as hysteresis:
    /// the participant stays on its path until it is clearly outside it, and inside a junction it only switches
    /// between connections that leave the same lane.
    /// </summary>
    public sealed class LaneLocator
    {
        /// <summary>Game parameter: how far beyond the lane edge the centre may go before the lane changes.</summary>
        public double HysteresisM = 0.3;
        /// <summary>Game parameter: how far beyond the lane edge a point still counts as on that lane when nothing better exists.</summary>
        public double EdgeToleranceM = 0.6;
        const double EndToleranceM = 0.05;

        readonly RoadGraphIndex index;

        public LaneLocator(RoadGraphIndex index) { this.index = index ?? throw new ArgumentNullException(nameof(index)); }

        public LanePosition Locate(double x, double z, double headingRad, LanePosition previous)
        {
            // 1. Keep the previous path while the participant is clearly on it; follow it onto its continuation at the end.
            if (previous.IsValid && index.TryPath(previous.PathId, out var prev))
            {
                if (TryFit(prev, x, z, headingRad, HysteresisM, out var kept) && kept.S < prev.Length - EndToleranceM)
                {
                    // Inside a junction, a sibling connection (same origin lane) may fit clearly better.
                    if (prev.IsConnection && BetterSibling(prev, x, z, headingRad, kept, out var sibling)) return sibling;
                    return kept;
                }
                var follow = Best(prev.Next.Select(index.Path), x, z, headingRad, EdgeToleranceM);
                if (follow.IsValid) return follow;
                if (TryFit(prev, x, z, headingRad, HysteresisM, out kept)) return kept; // standing at the very end
            }
            // 2. Fresh search: best aligned path near the point. Entering a junction from nowhere prefers lanes.
            var candidates = index.Candidates(x, z, 4).Select(index.Path).ToList();
            var best = Best(candidates.Where(p => !p.IsConnection), x, z, headingRad, EdgeToleranceM);
            if (!best.IsValid) best = Best(candidates, x, z, headingRad, EdgeToleranceM);
            if (!best.IsValid) return new LanePosition { OffRoad = true };
            return best;
        }

        bool BetterSibling(PathInfo current, double x, double z, double heading, LanePosition kept, out LanePosition result)
        {
            result = kept;
            var from = current.Connection.fromLaneId;
            var siblings = index.Path(from).Next.Select(index.Path).Where(p => p.IsConnection && p.Id != current.Id);
            var best = Best(siblings, x, z, heading, 0);
            if (best.IsValid && Score(best) + HysteresisM < Score(kept)) { result = best; return true; }
            return false;
        }

        LanePosition Best(IEnumerable<PathInfo> paths, double x, double z, double heading, double tolerance)
        {
            var best = new LanePosition(); double bestScore = double.MaxValue;
            foreach (var p in paths)
            {
                if (!TryFit(p, x, z, heading, tolerance, out var pos)) continue;
                double score = Score(pos);
                // Deterministic tie-break by id so equal fits never depend on dictionary order.
                if (score < bestScore - 1e-9 || (Math.Abs(score - bestScore) <= 1e-9 && string.CompareOrdinal(p.Id, best.PathId) < 0))
                {
                    best = pos; bestScore = score;
                }
            }
            return best;
        }

        // Lateral distance plus a penalty for driving against the path; reversing along a lane is still "on" it.
        static double Score(LanePosition p) => Math.Abs(p.D) + (p.AgainstDirection ? 1.5 : 0) + (1 - Math.Abs(Math.Cos(p.HeadingError)));

        static bool TryFit(PathInfo p, double x, double z, double heading, double tolerance, out LanePosition pos)
        {
            p.Line.Project(x, z, out double s, out double d);
            pos = default;
            if (Math.Abs(d) > p.WidthM / 2 + tolerance) return false;
            // Projection clamped to an end: the point lies beyond the path, not on it.
            var at = p.Line.PointAt(s);
            double along = Math.Sqrt(Math.Max(0, (x - at.x) * (x - at.x) + (z - at.z) * (z - at.z) - d * d));
            if (along > EndToleranceM * 20) return false;
            double err = Wrap(heading - p.Line.HeadingAt(s));
            pos = new LanePosition { PathId = p.Id, S = s, D = d, HeadingError = err, AgainstDirection = Math.Cos(err) < 0 };
            return true;
        }

        static double Wrap(double a)
        {
            while (a > Math.PI) a -= 2 * Math.PI;
            while (a <= -Math.PI) a += 2 * Math.PI;
            return a;
        }
    }
}
