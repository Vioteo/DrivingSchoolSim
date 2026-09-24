using System;
using System.Collections.Generic;
using System.Linq;
using DrivingSchool.Contracts;
using DrivingSchool.Simulation.RoadGraph;

namespace DrivingSchool.Simulation.Traffic
{
    /// <summary>
    /// Driving style. All values are game parameters (calibration), not legal norms.
    /// </summary>
    public sealed class DriverProfile
    {
        public string Id = "normal";
        public double DesiredSpeedFactor = 1.0;   // of the speed limit
        public double MaxAccelerationMps2 = 1.8;
        public double ComfortDecelerationMps2 = 2.0;
        public double EmergencyDecelerationMps2 = 5.0;
        public double TimeHeadwaySeconds = 1.2;
        public double MinGapM = 2.5;              // standstill distance to a vehicle or obstacle ahead
        public double StopLineGapM = 0.3;         // standstill distance before a stop line
        public double MaxLateralSpeedMps = 1.0;
        public double ReactionDelaySeconds = 0.6; // jam start-up delay (T34)
        public double Politeness = 0.3;           // MOBIL (T34)
        public double WheelbaseM = 2.6;
        public double LengthM = 4.4;
        public double WidthM = 1.8;

        public static DriverProfile Normal() => new DriverProfile();
    }

    /// <summary>
    /// Kinematic car on a route of graph paths (T16, ADR-012). State is (path, s, d, v, a). Longitudinal control is the
    /// Intelligent Driver Model; the lateral offset d follows a target given by a behaviour layer (T34), 0 by default.
    /// The agent never decides who has right of way: stop requests come from the caller (director, T33/T35).
    /// </summary>
    public sealed class LaneFollowerAgent
    {
        const double Delta = 4; // IDM acceleration exponent
        readonly RoadGraphIndex index;
        readonly List<string> route;
        int routeIndex;
        double s, d, v, a, targetD;

        public readonly string Id;
        public DriverProfile Profile;

        public LaneFollowerAgent(string id, RoadGraphIndex index, IEnumerable<string> route, double startS, DriverProfile profile, double startSpeedMps = 0)
        {
            Id = id;
            this.index = index ?? throw new ArgumentNullException(nameof(index));
            this.route = route?.ToList() ?? throw new ArgumentNullException(nameof(route));
            if (this.route.Count == 0) throw new ArgumentException("Empty route");
            for (int i = 0; i + 1 < this.route.Count; i++)
                if (!index.Path(this.route[i]).Next.Contains(this.route[i + 1]))
                    throw new ArgumentException("Route is not continuous at " + this.route[i] + " -> " + this.route[i + 1]);
            Profile = profile ?? DriverProfile.Normal();
            s = Math.Max(0, Math.Min(startS, CurrentPath.Length));
            v = Math.Max(0, startSpeedMps);
        }

        public PathInfo CurrentPath => index.Path(route[routeIndex]);
        public string CurrentPathId => route[routeIndex];
        public IReadOnlyList<string> Route => route;
        public int RouteIndex => routeIndex;
        public double CurrentDistanceAlongLane => s;
        public double LateralOffset => d;
        public double TargetLateralOffset => targetD;
        public float CurrentSpeedMps => (float)v;
        public float TargetAccelerationMps2 => (float)a;
        public float TargetSteeringAngleRad { get; private set; }
        public bool Finished { get; private set; }
        /// <summary>The route ends at the district edge: drive off it instead of stopping before the end.</summary>
        public bool ExitAtRouteEnd;

        public Vec3d Position => CurrentPath.Line.OffsetPoint(s, d);
        public double HeadingRad => CurrentPath.Line.HeadingAt(s);

        /// <summary>Distance left on the route from the current point.</summary>
        public double RemainingRouteM
        {
            get
            {
                double total = CurrentPath.Length - s;
                for (int i = routeIndex + 1; i < route.Count; i++) total += index.Path(route[i]).Length;
                return total;
            }
        }

        public void SetTargetLateralOffset(double offset) => targetD = offset;

        /// <summary>Extends the route (the director appends the next leg before the agent runs out).</summary>
        public void AppendRoute(IEnumerable<string> more)
        {
            foreach (var id in more)
            {
                if (!index.Path(route[route.Count - 1]).Next.Contains(id)) throw new ArgumentException("Route is not continuous at " + id);
                route.Add(id);
            }
            Finished = false;
        }

        /// <summary>Card T16 contract: gap to the obstacle ahead (+inf when free) and whether the next stop line must be respected.</summary>
        public void Update(float dtSeconds, float distanceToLeadObstacleM, bool isStopLineActive) =>
            Update(dtSeconds, distanceToLeadObstacleM, 0, isStopLineActive ? DistanceToNextStopLine() : double.PositiveInfinity);

        /// <param name="leadGapM">Bumper-to-bumper gap to the participant ahead on the route, +inf if none.</param>
        /// <param name="leadSpeedMps">Its speed along the route.</param>
        /// <param name="stopGapM">Distance from the front bumper to a point where the agent must stop (stop line, zone entry), +inf if none.</param>
        public void Update(double dtSeconds, double leadGapM, double leadSpeedMps, double stopGapM)
        {
            if (dtSeconds <= 0 || Finished) return;
            a = Acceleration(leadGapM, leadSpeedMps, stopGapM);
            double vNext = Math.Max(0, v + a * dtSeconds);
            double ds = (v + vNext) / 2 * dtSeconds;
            // Never pass the point we must stop at (discrete-time guard for the IDM).
            double hard = Math.Min(stopGapM - Profile.StopLineGapM * 0.5, leadGapM - 0.5);
            if (ds > Math.Max(0, hard)) { ds = Math.Max(0, hard); vNext = Math.Min(vNext, ds / dtSeconds); }
            v = vNext;

            double headingBefore = HeadingRad;
            double dMax = Profile.MaxLateralSpeedMps * dtSeconds;
            d += Math.Max(-dMax, Math.Min(dMax, targetD - d));
            Advance(ds);
            double curvature = ds > 1e-6 ? Wrap(HeadingRad - headingBefore) / ds : 0;
            TargetSteeringAngleRad = (float)Math.Atan(Profile.WheelbaseM * curvature);
        }

        public double Acceleration(double leadGapM, double leadSpeedMps, double stopGapM)
        {
            double v0 = Math.Max(0.1, DesiredSpeed());
            double free = 1 - Math.Pow(v / v0, Delta);
            double interaction = 0;
            // End of route without continuation behaves like a stop point.
            double endGap = ExitAtRouteEnd ? double.PositiveInfinity : RemainingRouteM - Profile.LengthM / 2;
            double stop = Math.Min(stopGapM, endGap + Profile.StopLineGapM);
            interaction = Math.Max(interaction, Term(leadGapM, leadSpeedMps, Profile.MinGapM));
            interaction = Math.Max(interaction, Term(stop, 0, Profile.StopLineGapM));
            double acc = Profile.MaxAccelerationMps2 * (free - interaction);
            // IDM brakes softly above the desired speed; a lower limit ahead must be met at its start.
            acc = Math.Min(acc, LimitDeceleration());
            return Math.Max(-Profile.EmergencyDecelerationMps2, Math.Min(Profile.MaxAccelerationMps2, acc));
        }

        /// <summary>Deceleration needed to be at or below every speed limit ahead by the time it starts (+inf if none binds).</summary>
        double LimitDeceleration()
        {
            double result = double.PositiveInfinity;
            double here = index.SpeedLimitAt(CurrentPathId, s) / 3.6 * Profile.DesiredSpeedFactor;
            if (v > here + 0.3) result = -Profile.ComfortDecelerationMps2;
            double front = Profile.LengthM / 2;
            foreach (var (limit, dist) in LimitsAhead(150))
            {
                double target = limit * Profile.DesiredSpeedFactor, gap = dist - front - 1.0;
                if (v <= target) continue;
                double need = gap > 0.5 ? (target * target - v * v) / (2 * gap) : -Profile.ComfortDecelerationMps2;
                // Start braking once the needed deceleration reaches ~70 % of comfortable (smooth, not last-moment).
                if (need <= -0.7 * Profile.ComfortDecelerationMps2) result = Math.Min(result, need);
            }
            return result;
        }

        /// <summary>(limit m/s, distance from the car centre) where the speed limit drops along the route.</summary>
        IEnumerable<(double limit, double dist)> LimitsAhead(double horizon)
        {
            double current = index.SpeedLimitAt(CurrentPathId, s);
            double offset = -s;
            for (int i = routeIndex; i < route.Count && offset < horizon; i++)
            {
                var id = route[i];
                var path = index.Path(id);
                var points = new List<double> { 0 };
                foreach (var cov in index.CoverageOn(id)) points.Add(cov.FromS);
                foreach (var at in points.Distinct().OrderBy(x => x))
                {
                    if (i == routeIndex && at <= s) continue;
                    double limit = index.SpeedLimitAt(id, at + 1e-3);
                    if (limit < current - 1e-3) yield return (limit / 3.6, offset + at);
                    current = limit;
                }
                offset += path.Length;
            }
        }

        double Term(double gap, double otherSpeed, double s0)
        {
            if (double.IsPositiveInfinity(gap)) return 0;
            double dv = v - otherSpeed;
            double sStar = s0 + Math.Max(0, v * Profile.TimeHeadwaySeconds + v * dv / (2 * Math.Sqrt(Profile.MaxAccelerationMps2 * Profile.ComfortDecelerationMps2)));
            double g = Math.Max(0.05, gap);
            return (sStar / g) * (sStar / g);
        }

        /// <summary>Speed the driver wants here: the limit at the car times the profile factor.</summary>
        public double DesiredSpeed() => index.SpeedLimitAt(CurrentPathId, s) / 3.6 * Profile.DesiredSpeedFactor;

        /// <summary>Distance from the front bumper to the next stop line on the route, +inf if none ahead.</summary>
        public double DistanceToNextStopLine()
        {
            double front = s + Profile.LengthM / 2, offset = 0;
            for (int i = routeIndex; i < route.Count; i++)
            {
                var stop = index.StopLineOf(route[i]);
                if (stop != null && (i > routeIndex || stop.s >= front - 0.5)) return offset + stop.s - (i == routeIndex ? front : 0);
                offset += (i == routeIndex ? index.Path(route[i]).Length - front : index.Path(route[i]).Length);
            }
            return double.PositiveInfinity;
        }

        /// <summary>Distance along the route from the agent's centre to (pathId, pathS); +inf if not on the route ahead.</summary>
        public double DistanceAlongRoute(string pathId, double pathS)
        {
            double offset = 0;
            for (int i = routeIndex; i < route.Count; i++)
            {
                if (route[i] == pathId)
                {
                    double here = i == routeIndex ? s : 0;
                    if (pathS >= here - 1e-6) return offset + pathS - here;
                }
                offset += i == routeIndex ? index.Path(route[i]).Length - s : index.Path(route[i]).Length;
            }
            return double.PositiveInfinity;
        }

        void Advance(double ds)
        {
            s += ds;
            while (s > CurrentPath.Length)
            {
                if (routeIndex + 1 >= route.Count) { s = CurrentPath.Length; v = 0; Finished = true; return; }
                s -= CurrentPath.Length;
                routeIndex++;
            }
        }

        /// <summary>Moves the agent to a neighbouring lane at the same progress (T34 lane change completes here).</summary>
        public void SwitchToLane(LaneV2 newLane, IEnumerable<string> newRoute = null)
        {
            var target = index.Path(newLane.id);
            var p = Position;
            target.Line.Project(p.x, p.z, out double ns, out double nd);
            route.RemoveRange(routeIndex, route.Count - routeIndex);
            route.Add(target.Id);
            if (newRoute != null) route.AddRange(newRoute);
            s = ns; d = nd; targetD = 0;
        }

        static double Wrap(double x)
        {
            while (x > Math.PI) x -= 2 * Math.PI;
            while (x <= -Math.PI) x += 2 * Math.PI;
            return x;
        }
    }
}
