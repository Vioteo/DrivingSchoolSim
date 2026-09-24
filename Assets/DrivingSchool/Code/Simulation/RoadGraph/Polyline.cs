using System;
using System.Collections.Generic;
using DrivingSchool.Contracts;

namespace DrivingSchool.Simulation.RoadGraph
{
    /// <summary>
    /// Arc-length parametrised polyline in the XZ plane (Y is carried along, not used for distances).
    /// Frenet convention: d > 0 is to the right of the travel direction (Unity: facing +Z, right is +X).
    /// </summary>
    public sealed class Polyline
    {
        readonly Vec3d[] points;
        readonly double[] cumulative;

        public Polyline(Vec3d[] points)
        {
            if (points == null || points.Length < 2) throw new ArgumentException("Polyline needs at least two points");
            this.points = points;
            cumulative = new double[points.Length];
            for (int i = 1; i < points.Length; i++)
                cumulative[i] = cumulative[i - 1] + Distance2D(points[i - 1], points[i]);
        }

        public double Length => cumulative[cumulative.Length - 1];
        public Vec3d Start => points[0];
        public Vec3d End => points[points.Length - 1];
        public IReadOnlyList<Vec3d> Points => points;

        public Vec3d PointAt(double s)
        {
            int i = SegmentIndex(s, out double t);
            return Lerp(points[i], points[i + 1], t);
        }

        /// <summary>Unit tangent (x, z) at arc length s.</summary>
        public void TangentAt(double s, out double tx, out double tz)
        {
            int i = SegmentIndex(s, out _);
            Direction(points[i], points[i + 1], out tx, out tz);
        }

        public double HeadingAt(double s)
        {
            TangentAt(s, out double tx, out double tz);
            return Math.Atan2(tx, tz); // 0 = +Z, positive towards +X (Unity yaw)
        }

        /// <summary>World position offset by d to the right of the tangent at s.</summary>
        public Vec3d OffsetPoint(double s, double d)
        {
            var p = PointAt(s);
            TangentAt(s, out double tx, out double tz);
            return new Vec3d(p.x + tz * d, p.y, p.z - tx * d);
        }

        /// <summary>Closest point projection. Returns s (clamped to [0, Length]) and signed lateral d (right positive).</summary>
        public void Project(double x, double z, out double s, out double d)
        {
            double best = double.MaxValue; s = 0; d = 0;
            for (int i = 0; i < points.Length - 1; i++)
            {
                var a = points[i]; var b = points[i + 1];
                double ex = b.x - a.x, ez = b.z - a.z, len2 = ex * ex + ez * ez;
                double t = len2 > 0 ? ((x - a.x) * ex + (z - a.z) * ez) / len2 : 0;
                t = Math.Max(0, Math.Min(1, t));
                double px = a.x + ex * t, pz = a.z + ez * t;
                double dist2 = (x - px) * (x - px) + (z - pz) * (z - pz);
                if (dist2 < best)
                {
                    best = dist2;
                    s = cumulative[i] + Math.Sqrt(len2) * t;
                    double len = Math.Sqrt(len2);
                    // cross(tangent, offset): right of travel is positive.
                    d = len > 0 ? ((x - px) * ez - (z - pz) * ex) / len : 0;
                }
            }
        }

        int SegmentIndex(double s, out double t)
        {
            if (s <= 0) { t = 0; return 0; }
            int last = points.Length - 2;
            if (s >= Length) { t = 1; return last; }
            int lo = 0, hi = cumulative.Length - 1;
            while (hi - lo > 1) { int mid = (lo + hi) / 2; if (cumulative[mid] <= s) lo = mid; else hi = mid; }
            double seg = cumulative[lo + 1] - cumulative[lo];
            t = seg > 0 ? (s - cumulative[lo]) / seg : 0;
            return Math.Min(lo, last);
        }

        public static double Distance2D(Vec3d a, Vec3d b)
        {
            double dx = b.x - a.x, dz = b.z - a.z;
            return Math.Sqrt(dx * dx + dz * dz);
        }

        public static Vec3d Lerp(Vec3d a, Vec3d b, double t) =>
            new Vec3d(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);

        public static void Direction(Vec3d a, Vec3d b, out double tx, out double tz)
        {
            double dx = b.x - a.x, dz = b.z - a.z, len = Math.Sqrt(dx * dx + dz * dz);
            if (len <= 0) { tx = 0; tz = 1; return; }
            tx = dx / len; tz = dz / len;
        }

        public static bool IsFinite(Vec3d p) => IsFinite(p.x) && IsFinite(p.y) && IsFinite(p.z);
        public static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        /// <summary>Samples a cubic Bezier so that consecutive points are at most maxStep apart (by chord).</summary>
        public static Vec3d[] SampleCubic(CubicCurve c, double maxStep)
        {
            double approx = Distance2D(c.p0, c.p1) + Distance2D(c.p1, c.p2) + Distance2D(c.p2, c.p3);
            int n = Math.Max(1, (int)Math.Ceiling(approx / maxStep));
            var result = new Vec3d[n + 1];
            for (int i = 0; i <= n; i++) result[i] = Cubic(c, (double)i / n);
            return result;
        }

        public static Vec3d Cubic(CubicCurve c, double t)
        {
            double u = 1 - t, a = u * u * u, b = 3 * u * u * t, cc = 3 * u * t * t, d = t * t * t;
            return new Vec3d(
                a * c.p0.x + b * c.p1.x + cc * c.p2.x + d * c.p3.x,
                a * c.p0.y + b * c.p1.y + cc * c.p2.y + d * c.p3.y,
                a * c.p0.z + b * c.p1.z + cc * c.p2.z + d * c.p3.z);
        }

        /// <summary>Offsets every vertex by d to the right, using the averaged tangent at interior vertices.</summary>
        public static Vec3d[] OffsetRight(Vec3d[] line, double d)
        {
            var result = new Vec3d[line.Length];
            for (int i = 0; i < line.Length; i++)
            {
                var prev = line[Math.Max(0, i - 1)]; var next = line[Math.Min(line.Length - 1, i + 1)];
                Direction(prev, next, out double tx, out double tz);
                result[i] = new Vec3d(line[i].x + tz * d, line[i].y, line[i].z - tx * d);
            }
            return result;
        }

        public static Vec3d[] Reversed(Vec3d[] line)
        {
            var r = (Vec3d[])line.Clone();
            Array.Reverse(r);
            return r;
        }
    }
}
